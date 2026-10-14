import MockiCore
import SwiftUI

enum ExamStyle: String, CaseIterable, Identifiable {
    case modern, classic
    var id: Self { self }
    var label: String { self == .modern ? "Modern" : "Classic testing center" }
}

/// Lets shared question views adapt to the classic look (plain radio rows, lettered options).
private struct ClassicLookKey: EnvironmentKey { static let defaultValue = false }
extension EnvironmentValues {
    var classicLook: Bool {
        get { self[ClassicLookKey.self] }
        set { self[ClassicLookKey.self] = newValue }
    }
}

/// Exam simulation: item screens in the chosen style, a review screen, timer warnings and notes.
struct ExamSession: View {
    @Binding var session: Session
    let style: ExamStyle
    let onFinish: () -> Void
    let onQuit: () -> Void

    @State private var now = Date.now
    @State private var reviewing = false
    /// Items being revisited from the review screen ("Review incomplete", "Review marked").
    @State private var queue: [Int]?
    @State private var confirmingEnd = false
    @State private var confirmingQuit = false
    @State private var warning: String?
    @State private var warned: Set<Int> = []
    @State private var notes = ""
    @State private var showNotes = false

    private var nav: ExamNavigation {
        ExamNavigation(
            next: goNext, previous: { session.previous() }, review: openReview,
            end: { confirmingEnd = true }, quit: { confirmingQuit = true },
            jump: { i in queue = nil; reviewing = false; session.jump(to: i) },
            nextLabel: nextLabel)
    }

    var body: some View {
        Group {
            if reviewing {
                ExamReviewScreen(session: session, style: style, now: now, nav: nav, review: startReview)
            } else {
                switch style {
                case .modern: ModernItemScreen(session: $session, now: now, nav: nav, notes: $notes, showNotes: $showNotes, inQueue: queue != nil)
                case .classic: ClassicItemScreen(session: $session, now: now, nav: nav, inQueue: queue != nil)
                }
            }
        }
        .task {
            while !Task.isCancelled {
                now = .now
                if session.isExpired(at: now) { onFinish(); return }
                checkWarnings()
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
        .alert(warning ?? "", isPresented: Binding(get: { warning != nil }, set: { if !$0 { warning = nil } })) {
            Button("Continue") { warning = nil }
        }
        .confirmationDialog("End the exam?", isPresented: $confirmingEnd) {
            Button("End Exam") { onFinish() }
            Button("Return to Exam", role: .cancel) {}
        } message: {
            let incomplete = session.unansweredCount, marked = session.flagged.count
            Text(incomplete == 0 && marked == 0 ? "All items are complete. You can't change answers after ending."
                 : "\(incomplete) incomplete and \(marked) marked item\(marked == 1 ? "" : "s"). Incomplete items are scored as wrong.")
        }
        .confirmationDialog("Leave without a score?", isPresented: $confirmingQuit) {
            Button("Leave Exam", role: .destructive, action: onQuit)
            Button("Return to Exam", role: .cancel) {}
        } message: {
            Text("Your answers won't be graded or saved to history.")
        }
    }

    private var isLast: Bool { session.current == session.questions.count - 1 }

    private var nextLabel: String {
        if let queue {
            return queue.contains(where: { $0 > session.current }) ? "Next" : "Review Screen"
        }
        if !isLast { return "Next" }
        return session.canGoBack ? "Review Screen" : "End Exam"
    }

    private func goNext() {
        if let q = queue {
            if let n = q.first(where: { $0 > session.current }) { session.jump(to: n) } else { openReview() }
        } else if !isLast {
            session.next()
        } else if session.canGoBack {
            openReview()
        } else {
            confirmingEnd = true
        }
    }

    private func openReview() {
        guard session.canGoBack else { confirmingEnd = true; return }
        queue = nil
        reviewing = true
    }

    private func startReview(_ filter: ReviewFilter) {
        let indices = session.questions.indices.filter { i in
            let q = session.questions[i]
            return switch filter {
            case .all: true
            case .incomplete: session.response(for: q) == nil
            case .marked: session.flagged.contains(q.id)
            }
        }
        guard let first = indices.first else { return }
        queue = indices
        reviewing = false
        session.jump(to: first)
    }

    private func checkWarnings() {
        guard let limit = session.timeLimit, let remaining = session.remaining(at: now) else { return }
        for minutes in [15, 5] where Double(minutes * 60) < limit && remaining <= Double(minutes * 60) && !warned.contains(minutes) {
            warned.insert(minutes)
            warning = "\(minutes) minutes remaining."
        }
    }
}

enum ReviewFilter { case all, incomplete, marked }

struct ExamNavigation {
    let next: () -> Void
    let previous: () -> Void
    let review: () -> Void
    let end: () -> Void
    let quit: () -> Void
    let jump: (Int) -> Void
    let nextLabel: String
}

extension Session {
    /// Fill-in questions need bare keys for typing, so letter and arrow shortcuts are off there.
    var isTyping: Bool { if case .fillIn = currentQuestion.body { true } else { false } }
    var answeredCount: Int { questions.count - unansweredCount }
}

// MARK: - Modern

struct ModernItemScreen: View {
    @Binding var session: Session
    let now: Date
    let nav: ExamNavigation
    @Binding var notes: String
    @Binding var showNotes: Bool
    let inQueue: Bool

    @State private var caseTab = 0
    private var q: Question { session.currentQuestion }
    private var caseStudy: CaseStudy? { session.exam.caseStudy(q.caseStudyID) }

    var body: some View {
        VStack(spacing: 0) {
            header
            ProgressStrip(session: $session, jump: nav.jump)
            Divider()
            Group {
                if let caseStudy {
                    HSplitView {
                        casePane(caseStudy).frame(minWidth: 300, idealWidth: 460)
                        item.frame(minWidth: 420)
                    }
                } else {
                    item
                }
            }
            .frame(maxHeight: .infinity)
            Divider()
            footer
        }
        .background(Palette.examPage)
        .foregroundStyle(Palette.examInk)
        .inspector(isPresented: $showNotes) {
            VStack(alignment: .leading, spacing: 8) {
                Text("Notes").font(.headline)
                Text("Scratch space for this exam. Not saved or graded.").font(.caption).foregroundStyle(.secondary)
                TextEditor(text: $notes)
                    .font(.system(.body, design: .monospaced))
                    .scrollContentBackground(.hidden)
                    .padding(6)
                    .background(Palette.examSurface, in: RoundedRectangle(cornerRadius: 6))
                    .accessibilityLabel("Notes")
            }
            .padding(14)
            .inspectorColumnWidth(min: 240, ideal: 300, max: 420)
        }
    }

    private var header: some View {
        HStack(spacing: 14) {
            Text(session.exam.title).font(.headline).lineLimit(1)
            if let caseStudy {
                Text(caseStudy.title).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
            Toggle(isOn: $showNotes) { Label("Notes", systemImage: "square.and.pencil") }
                .toggleStyle(.button)
            Button { session.toggleFlag() } label: {
                Label(session.flagged.contains(q.id) ? "Flagged" : "Flag", systemImage: session.flagged.contains(q.id) ? "flag.fill" : "flag")
            }
            .keyboardShortcut(session.isTyping ? nil : "f")
            .tint(Palette.flagged)
            TimerPill(remaining: session.remaining(at: now), elapsed: now.timeIntervalSince(session.startedAt))
            Button("Quit", action: nav.quit)
        }
        .padding(.horizontal, 18).padding(.vertical, 10)
        .background(Palette.examSurface)
    }

    private func casePane(_ c: CaseStudy) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            Picker("Case study", selection: $caseTab) {
                Text("Scenario").tag(0)
                if !c.exhibits.isEmpty { Text("Exhibits (\(c.exhibits.count))").tag(1) }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .fixedSize()
            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    if caseTab == 0 {
                        Text(c.title).font(.title3.bold())
                        RichText(source: c.scenario)
                    } else {
                        ExhibitList(exam: session.exam, exhibits: c.exhibits)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(16)
        .background(Palette.examSurface)
    }

    private var item: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 16) {
                Text("Question \(session.current + 1) of \(session.questions.count)\(inQueue ? " · reviewing" : "")")
                    .font(.callout).foregroundStyle(.secondary)
                RichText(source: q.prompt, font: .title3)
                ExhibitList(exam: session.exam, exhibits: q.exhibits)
                Text(instruction(for: q)).font(.callout).foregroundStyle(.secondary)
                QuestionBodyView(session: $session, question: q)
            }
            .frame(maxWidth: 860, alignment: .leading)
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .id(q.id)
    }

    private var footer: some View {
        HStack(spacing: 10) {
            if session.canGoBack {
                Button { nav.previous() } label: { Label("Previous", systemImage: "chevron.left") }
                    .keyboardShortcut(session.isTyping ? nil : .leftArrow)
                    .disabled(session.current == 0)
            } else {
                Label("You can't return to earlier questions in this exam", systemImage: "lock").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            Text("\(session.answeredCount) answered · \(session.flagged.count) flagged")
                .font(.callout).foregroundStyle(.secondary).monospacedDigit()
            if session.canGoBack { Button("Review", action: nav.review) }
            Button(nav.nextLabel) { nav.next() }
                .buttonStyle(.borderedProminent)
                .tint(Palette.examInk)
                .keyboardShortcut(.defaultAction)
        }
        .padding(.horizontal, 18).padding(.vertical, 10)
        .background(Palette.examSurface)
    }
}

/// One segment per question: answered, flagged, current, or not yet answered. Click to jump.
struct ProgressStrip: View {
    @Binding var session: Session
    let jump: (Int) -> Void

    var body: some View {
        HStack(spacing: 2) {
            ForEach(session.questions.indices, id: \.self) { i in
                let q = session.questions[i]
                Rectangle()
                    .fill(color(i, q))
                    .frame(height: i == session.current ? 6 : 4)
                    .clipShape(RoundedRectangle(cornerRadius: 2))
                    .contentShape(Rectangle().inset(by: -4))
                    .onTapGesture { if session.canJump(to: i) { jump(i) } }
                    .help("Question \(i + 1)")
            }
        }
        .frame(height: 10)
        .padding(.horizontal, 18).padding(.bottom, 6)
        .background(Palette.examSurface)
        .accessibilityElement()
        .accessibilityLabel("Progress: \(session.answeredCount) of \(session.questions.count) answered, \(session.flagged.count) flagged")
    }

    private func color(_ i: Int, _ q: Question) -> Color {
        if i == session.current { return Palette.examInk }
        if session.flagged.contains(q.id) { return Palette.flagged }
        return session.response(for: q) == nil ? Palette.unanswered : Palette.answered
    }
}

struct TimerPill: View {
    let remaining: TimeInterval?
    let elapsed: TimeInterval

    var body: some View {
        let low = (remaining ?? .infinity) < 600, critical = (remaining ?? .infinity) < 300
        Label(remaining.map { "\(formatDuration($0)) left" } ?? formatDuration(elapsed), systemImage: remaining == nil ? "clock" : "timer")
            .monospacedDigit()
            .font(.callout.weight(.semibold))
            .padding(.horizontal, 10).padding(.vertical, 4)
            .foregroundStyle(critical ? Palette.err : low ? Palette.warn : Palette.examInk)
            .background(low ? Palette.timerSoft : Color.clear, in: Capsule())
            .overlay(Capsule().strokeBorder(low ? Palette.warn : Palette.examLine))
            .accessibilityLabel(remaining.map { "Time remaining \(formatDuration($0))" } ?? "Elapsed \(formatDuration(elapsed))")
    }
}

// MARK: - Classic

struct ClassicItemScreen: View {
    @Binding var session: Session
    let now: Date
    let nav: ExamNavigation
    let inQueue: Bool

    @State private var openExhibit: Exhibit?
    @State private var showCase = false
    private var q: Question { session.currentQuestion }
    private var caseStudy: CaseStudy? { session.exam.caseStudy(q.caseStudyID) }

    var body: some View {
        VStack(spacing: 0) {
            ClassicTitleBar(title: session.exam.title, trailing: "Exam simulation", quit: nav.quit)
            HStack(spacing: 24) {
                Text(session.remaining(at: now).map { "Time Remaining \(clock($0))" } ?? "Time Elapsed \(clock(now.timeIntervalSince(session.startedAt)))")
                    .font(.classic(13, weight: .bold)).monospacedDigit()
                Text("Item \(session.current + 1) of \(session.questions.count)").font(.classic(13))
                if inQueue { Text("Reviewing").font(.classic(13)).foregroundStyle(.secondary) }
                Spacer()
                Toggle("Mark for Review", isOn: Binding(get: { session.flagged.contains(q.id) }, set: { _ in session.toggleFlag() }))
                    .toggleStyle(.checkbox)
                    .font(.classic(13))
                    .keyboardShortcut(session.isTyping ? nil : "f")
            }
            .padding(.horizontal, 14).padding(.vertical, 6)
            .background(Palette.classicBar)
            .overlay(alignment: .bottom) { Rectangle().fill(Palette.classicLine).frame(height: 1) }

            ScrollView {
                VStack(alignment: .leading, spacing: 16) {
                    RichText(source: q.prompt, font: .classic(15))
                    let exhibits = (caseStudy?.exhibits ?? []) + q.exhibits
                    if caseStudy != nil || !exhibits.isEmpty {
                        HStack(spacing: 8) {
                            if caseStudy != nil {
                                Button("Case Study") { showCase = true }.buttonStyle(ClassicButtonStyle())
                            }
                            ForEach(Array(exhibits.enumerated()), id: \.offset) { i, e in
                                Button(exhibits.count == 1 ? "Exhibit" : "Exhibit \(i + 1)") { openExhibit = e }
                                    .buttonStyle(ClassicButtonStyle())
                                    .help(e.title)
                            }
                        }
                    }
                    QuestionBodyView(session: $session, question: q)
                        .environment(\.classicLook, true)
                        .font(.classic(14))
                }
                .frame(maxWidth: 900, alignment: .leading)
                .padding(.horizontal, 26).padding(.vertical, 22)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .id(q.id)
            .background(Palette.classicStage)
            .overlay(Rectangle().strokeBorder(Palette.classicLine))
            .padding(12)

            HStack(spacing: 8) {
                if session.canGoBack {
                    Button("◀ Previous") { nav.previous() }
                        .buttonStyle(ClassicButtonStyle())
                        .keyboardShortcut(session.isTyping ? nil : .leftArrow)
                        .disabled(session.current == 0)
                }
                Button(nav.nextLabel == "Next" ? "Next ▶" : nav.nextLabel) { nav.next() }
                    .buttonStyle(ClassicButtonStyle(strong: nav.nextLabel != "Next"))
                    .keyboardShortcut(.defaultAction)
                Spacer()
                Text("Answers are saved automatically.").font(.classic(12)).foregroundStyle(.secondary)
                if session.canGoBack { Button("Review", action: nav.review).buttonStyle(ClassicButtonStyle()) }
            }
            .padding(.horizontal, 14).padding(.bottom, 12)
        }
        .background(Palette.classicPage)
        .sheet(item: $openExhibit) { ExhibitViewer(exam: session.exam, exhibit: $0) }
        .sheet(isPresented: $showCase) {
            if let caseStudy {
                VStack(spacing: 0) {
                    CaseStudyPanel(exam: session.exam, caseStudy: caseStudy)
                    Divider()
                    HStack { Spacer(); Button("Close") { showCase = false }.keyboardShortcut(.cancelAction) }.padding(12)
                }
                .frame(minWidth: 720, idealWidth: 860, minHeight: 520, idealHeight: 680)
            }
        }
    }
}

struct ClassicTitleBar: View {
    let title: String
    let trailing: String
    var quit: (() -> Void)? = nil

    var body: some View {
        HStack(spacing: 16) {
            Text(title).font(.classic(13, weight: .bold))
            Spacer()
            Text(trailing).font(.classic(13)).opacity(0.8)
            if let quit {
                Button("Quit", action: quit).buttonStyle(.plain).font(.classic(12)).underline().opacity(0.85)
            }
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 14).padding(.vertical, 7)
        .background(Palette.classicTitle)
    }
}

/// Square, gradient buttons in the style of testing-center software.
struct ClassicButtonStyle: ButtonStyle {
    var strong = false
    @Environment(\.isEnabled) private var enabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.classic(13))
            .padding(.horizontal, 16).padding(.vertical, 6)
            .foregroundStyle(strong ? Color.white : Color.primary)
            .background(
                RoundedRectangle(cornerRadius: 3).fill(strong
                    ? AnyShapeStyle(LinearGradient(colors: [Color(red: 0.25, green: 0.47, blue: 0.73), Color(red: 0.16, green: 0.37, blue: 0.62)], startPoint: .top, endPoint: .bottom))
                    : AnyShapeStyle(Palette.classicStage)))
            .overlay(RoundedRectangle(cornerRadius: 3).strokeBorder(strong ? Color(red: 0.11, green: 0.31, blue: 0.53) : Palette.classicLine.opacity(1)))
            .brightness(configuration.isPressed ? -0.08 : 0)
            .opacity(enabled ? 1 : 0.45)
    }
}

private func clock(_ seconds: TimeInterval) -> String {
    let s = Int(seconds.rounded(.down))
    return String(format: "%02d:%02d:%02d", s / 3600, s % 3600 / 60, s % 60)
}

/// The instruction line under a prompt, shared by both exam styles and practice mode.
func instruction(for q: Question) -> String {
    switch q.body {
    case let .single(choices, _): "Choose one. Keys 1–\(min(choices.count, 9)) select."
    case let .multiple(choices, a): "Choose \(a.count). Keys 1–\(min(choices.count, 9)) toggle."
    case let .ordering(choices, a):
        a.count < choices.count ? "Drag the correct options to the answer area and put them in order. Not all options are used." : "Drag every option to the answer area in the correct order."
    case .matching: "Drag each item onto a target. Targets may be used once, more than once, or not at all."
    case .dropdowns: q.kind == .yesNo ? "Select Yes or No for each statement." : "Select the correct option in each list."
    case let .fillIn(blanks): blanks.count == 1 ? "Type your answer." : "Fill in each field."
    case let .hotspot(_, _, a): a.count == 1 ? "Click the correct area of the image." : "Click the \(a.count) correct areas of the image."
    }
}

// MARK: - Review screen

struct ExamReviewScreen: View {
    let session: Session
    let style: ExamStyle
    let now: Date
    let nav: ExamNavigation
    let review: (ReviewFilter) -> Void

    private var incomplete: Int { session.unansweredCount }
    private var marked: Int { session.flagged.count }

    var body: some View {
        VStack(spacing: 0) {
            if style == .classic {
                ClassicTitleBar(title: session.exam.title, trailing: "Review Screen", quit: nav.quit)
                HStack(spacing: 22) {
                    Text(session.remaining(at: now).map { "Time Remaining \(clock($0))" } ?? "Time Elapsed \(clock(now.timeIntervalSince(session.startedAt)))")
                        .font(.classic(13, weight: .bold)).monospacedDigit()
                    Text(summary).font(.classic(13))
                    Spacer()
                }
                .padding(.horizontal, 14).padding(.vertical, 6)
                .background(Palette.classicBar)
            } else {
                HStack(spacing: 14) {
                    Text("Review your answers").font(.title3.bold())
                    Text(summary).foregroundStyle(.secondary)
                    Spacer()
                    TimerPill(remaining: session.remaining(at: now), elapsed: now.timeIntervalSince(session.startedAt))
                    Button("Quit", action: nav.quit)
                }
                .padding(.horizontal, 18).padding(.vertical, 12)
                .background(Palette.examSurface)
                Divider()
            }

            ScrollView {
                VStack(alignment: .leading, spacing: 12) {
                    Text("Click an item to go to it, or use the buttons below. Marked and incomplete items can still be changed until you end the exam.")
                        .font(style == .classic ? .classic(13) : .callout)
                        .foregroundStyle(.secondary)
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 0), count: 5), spacing: 0) {
                        ForEach(session.questions.indices, id: \.self) { i in cell(i) }
                    }
                    .overlay(Rectangle().strokeBorder(style == .classic ? Palette.classicLine : Palette.examLine))
                }
                .padding(18)
            }
            .background(style == .classic ? Palette.classicStage : Palette.examPage)

            Divider()
            HStack(spacing: 8) {
                reviewButton("Review All") { review(.all) }
                reviewButton("Review Incomplete (\(incomplete))") { review(.incomplete) }.disabled(incomplete == 0)
                reviewButton("Review Marked (\(marked))") { review(.marked) }.disabled(marked == 0)
                Spacer()
                if style == .classic {
                    Button("End Exam", action: nav.end).buttonStyle(ClassicButtonStyle(strong: true))
                } else {
                    Button("End Exam", action: nav.end).buttonStyle(.borderedProminent).tint(Palette.examInk)
                }
            }
            .padding(.horizontal, 18).padding(.vertical, 10)
            .background(style == .classic ? Palette.classicPage : Palette.examSurface)
        }
        .foregroundStyle(style == .classic ? Color.primary : Palette.examInk)
    }

    private var summary: String {
        "\(session.questions.count) items · \(session.answeredCount) complete · \(incomplete) incomplete · \(marked) marked"
    }

    @ViewBuilder
    private func reviewButton(_ title: String, action: @escaping () -> Void) -> some View {
        if style == .classic { Button(title, action: action).buttonStyle(ClassicButtonStyle()) }
        else { Button(title, action: action) }
    }

    private func cell(_ i: Int) -> some View {
        let q = session.questions[i]
        let isMarked = session.flagged.contains(q.id), isIncomplete = session.response(for: q) == nil
        let status = isMarked ? "Marked" : isIncomplete ? "Incomplete" : "Complete"
        return Button { nav.jump(i) } label: {
            HStack {
                Text("\(i + 1)").monospacedDigit()
                if let section = session.sectionStart(at: i) { Text(section).font(.caption2).foregroundStyle(.secondary).lineLimit(1) }
                Spacer()
                if isMarked { Image(systemName: "flag.fill").foregroundStyle(Palette.flagged).font(.caption) }
                Text(status)
                    .font(.caption.weight(isMarked || isIncomplete ? .bold : .regular))
                    .foregroundStyle(isIncomplete ? Palette.err : isMarked ? Palette.flagged : .secondary)
            }
            .font(style == .classic ? .classic(13) : .callout)
            .padding(.horizontal, 10).padding(.vertical, 8)
            .contentShape(Rectangle())
            .overlay(Rectangle().strokeBorder((style == .classic ? Palette.classicLine : Palette.examLine).opacity(0.6), lineWidth: 0.5))
        }
        .buttonStyle(.plain)
        .accessibilityLabel("Item \(i + 1), \(status)")
    }
}
