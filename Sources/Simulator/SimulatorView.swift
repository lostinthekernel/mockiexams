import AppKit
import MockiCore
import SwiftUI

/// One run of an exam: answering, then results. Used by the player and by the Designer preview.
struct RunRequest: Identifiable {
    let id = UUID()
    let exam: Exam
    let options: SessionOptions
    var fullScreen = false
    var style: ExamStyle = .modern
    /// Label for leaving the results: the Library, or closing a Designer preview.
    var doneLabel = "Back to Library"
    var allowsRetry = true
}

struct RunView: View {
    let request: RunRequest
    /// Called with the graded result; the player records history here, previews don't.
    let onFinish: (SessionResult) -> Void
    let onRetry: ([String]) -> Void
    let onClose: () -> Void

    @State private var session: Session
    @State private var result: SessionResult?
    @State private var window: NSWindow?
    @State private var enteredFullScreen = false

    init(request: RunRequest, onFinish: @escaping (SessionResult) -> Void = { _ in },
         onRetry: @escaping ([String]) -> Void, onClose: @escaping () -> Void) {
        self.request = request
        self.onFinish = onFinish
        self.onRetry = onRetry
        self.onClose = onClose
        _session = State(initialValue: Session(exam: request.exam, options: request.options))
    }

    var body: some View {
        Group {
            if let result {
                ResultsView(result: result, onRetry: request.allowsRetry ? { onRetry(result.missedIDs) } : nil, onDone: close, doneLabel: request.doneLabel)
            } else if request.options.mode == .exam {
                ExamSession(session: $session, style: request.style, onFinish: finish, onQuit: close)
            } else {
                SimulatorView(session: $session, onFinish: finish, onQuit: close)
            }
        }
        .background(WindowAccessor(window: $window))
        .onChange(of: window) { _, w in
            guard request.fullScreen, let w, !w.styleMask.contains(.fullScreen) else { return }
            enteredFullScreen = true
            w.toggleFullScreen(nil)
        }
    }

    private func finish() {
        guard result == nil else { return }
        let graded = session.grade()
        onFinish(graded)
        result = graded
        leaveFullScreen()
    }

    private func close() {
        leaveFullScreen()
        onClose()
    }

    private func leaveFullScreen() {
        if enteredFullScreen, let window, window.styleMask.contains(.fullScreen) { window.toggleFullScreen(nil) }
        enteredFullScreen = false
    }
}

struct SimulatorView: View {
    @Binding var session: Session
    let onFinish: () -> Void
    let onQuit: () -> Void

    @State private var now = Date.now
    @State private var confirmingFinish = false
    @State private var showCase = true

    private var q: Question { session.currentQuestion }
    private var isPractice: Bool { session.options.mode == .practice }
    private var isLast: Bool { session.current == session.questions.count - 1 }
    private var caseStudy: CaseStudy? { session.exam.caseStudy(q.caseStudyID) }
    /// Bare-key shortcuts would steal typing from text fields.
    private var typing: Bool { if case .fillIn = q.body { true } else { false } }

    var body: some View {
        HStack(spacing: 0) {
            QuestionGrid(session: $session)
                .frame(width: 168)
            Divider()
            VStack(spacing: 0) {
                header
                Divider()
                ScrollView {
                    VStack(alignment: .leading, spacing: 16) {
                        RichText(source: q.prompt, font: .title3)
                        ExhibitList(exam: session.exam, exhibits: q.exhibits)
                        Text(instruction(for: q)).font(.callout).foregroundStyle(.secondary)
                        QuestionBodyView(session: $session, question: q)
                        if session.isLocked(q) { feedback }
                    }
                    .frame(maxWidth: 900, alignment: .leading)
                    .padding(24)
                    .frame(maxWidth: .infinity, alignment: .leading)
                }
                .id(q.id)
                Divider()
                footer
            }
        }
        .inspector(isPresented: Binding(get: { caseStudy != nil && showCase }, set: { showCase = $0 })) {
            if let caseStudy {
                CaseStudyPanel(exam: session.exam, caseStudy: caseStudy)
                    .inspectorColumnWidth(min: 280, ideal: 380, max: 640)
            }
        }
        .task {
            while !Task.isCancelled {
                now = .now
                if session.isExpired(at: now) { onFinish(); return }
                try? await Task.sleep(for: .milliseconds(500))
            }
        }
        .confirmationDialog("Finish the exam?", isPresented: $confirmingFinish) {
            Button("Finish and Grade") { onFinish() }
            Button("Keep Going", role: .cancel) {}
        } message: {
            let n = session.unansweredCount
            Text(n == 0 ? "All questions are answered." : "\(n) question\(n == 1 ? " is" : "s are") unanswered and will be marked wrong.")
        }
    }

    // MARK: Header / footer

    private var header: some View {
        HStack(spacing: 12) {
            Text("Question \(session.current + 1) of \(session.questions.count)").font(.headline)
                .lineLimit(1).fixedSize()
            Text(q.topic)
                .font(.caption)
                .lineLimit(1)
                .truncationMode(.tail)
                .padding(.horizontal, 8).padding(.vertical, 3)
                .background(.quaternary, in: Capsule())
            if q.points != 1 {
                Text("\(q.points.formatted()) pts").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if let remaining = session.remaining(at: now) {
                Label(formatDuration(remaining), systemImage: "timer")
                    .monospacedDigit()
                    .foregroundStyle(remaining < 60 ? .red : .primary)
                    .accessibilityLabel("Time remaining \(formatDuration(remaining))")
            } else {
                Label(formatDuration(now.timeIntervalSince(session.startedAt)), systemImage: "clock")
                    .monospacedDigit()
                    .foregroundStyle(.secondary)
            }
            if caseStudy != nil {
                Toggle(isOn: $showCase) { Label("Case Study", systemImage: "briefcase") }
                    .toggleStyle(.button)
                    .fixedSize()
                    .help("Show or hide the case study")
            }
            Button {
                session.toggleFlag()
            } label: {
                Label(session.flagged.contains(q.id) ? "Flagged" : "Flag",
                      systemImage: session.flagged.contains(q.id) ? "flag.fill" : "flag")
            }
            .keyboardShortcut(typing ? nil : "f")
            .help("Flag for review (F)")
            Button("Quit", role: .cancel, action: onQuit)
        }
        .padding(.horizontal, 20).padding(.vertical, 12)
    }

    private var footer: some View {
        HStack {
            if session.canGoBack {
                Button { session.previous() } label: { Label("Previous", systemImage: "chevron.left") }
                    .keyboardShortcut(typing ? nil : .leftArrow)
                    .disabled(session.current == 0)
            } else {
                Label("Review is not allowed in this exam", systemImage: "lock")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if !isLast {
                Button("Skip") { session.next() }
                    .keyboardShortcut(typing ? nil : .rightArrow)
            }
            Spacer()
            if !isLast { Button("Finish…") { confirmingFinish = true } }
            Button(primaryLabel) { primaryAction() }
                .buttonStyle(.borderedProminent)
                .keyboardShortcut(.defaultAction)
                .disabled(primaryLabel == "Check" && session.response(for: q) == nil)
        }
        .padding(.horizontal, 20).padding(.vertical, 12)
    }

    private var primaryLabel: String {
        if isPractice && !session.isLocked(q) { return "Check" }
        return isLast ? "Finish…" : "Next"
    }

    private func primaryAction() {
        switch primaryLabel {
        case "Check": if session.response(for: q) != nil { session.reveal() }
        case "Next": session.next()
        default: confirmingFinish = true
        }
    }

    // MARK: Practice feedback

    private var feedback: some View {
        let r = session.response(for: q)
        let right = q.isCorrect(r)
        let fraction = q.fraction(r)
        return VStack(alignment: .leading, spacing: 8) {
            Label(right ? "Correct" : fraction > 0 ? "Partly correct (\(Int((fraction * 100).rounded()))%)" : "Incorrect",
                  systemImage: right ? "checkmark.circle.fill" : "xmark.circle.fill")
                .font(.headline)
                .foregroundStyle(right ? .green : fraction > 0 ? .orange : .red)
            if !right {
                switch q.body {
                case .single, .multiple: EmptyView()
                default: Text(markdown("Correct answer: \(q.describeAnswer)"))
                }
            }
            if let e = q.explanation { RichText(source: e) }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background((right ? Color.green : fraction > 0 ? Color.orange : Color.red).opacity(0.1), in: RoundedRectangle(cornerRadius: 10))
    }
}

struct QuestionGrid: View {
    @Binding var session: Session

    private struct Group: Identifiable { let id: Int; let title: String?; let indices: [Int] }

    private var groups: [Group] {
        var out: [Group] = []
        for i in session.questions.indices {
            if let title = session.sectionStart(at: i) { out.append(Group(id: i, title: title, indices: [i])) }
            else if out.isEmpty { out.append(Group(id: i, title: nil, indices: [i])) }
            else { out[out.count - 1] = Group(id: out[out.count - 1].id, title: out[out.count - 1].title, indices: out[out.count - 1].indices + [i]) }
        }
        return out
    }

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 10) {
                ForEach(groups) { group in
                    if let title = group.title {
                        Text(title).font(.caption.bold()).foregroundStyle(.secondary).lineLimit(2)
                    }
                    LazyVGrid(columns: Array(repeating: GridItem(.fixed(36), spacing: 6), count: 4), spacing: 6) {
                        ForEach(group.indices, id: \.self) { cell($0) }
                    }
                }
            }
            .padding(12)
        }
    }

    private func cell(_ index: Int) -> some View {
        let q = session.questions[index]
        return Button { session.jump(to: index) } label: {
            Text("\(index + 1)")
                .font(.caption.monospacedDigit())
                .frame(width: 36, height: 30)
                .background(fill(q), in: RoundedRectangle(cornerRadius: 6))
                .overlay(RoundedRectangle(cornerRadius: 6)
                    .strokeBorder(index == session.current ? Color.accentColor : .clear, lineWidth: 2))
                .overlay(alignment: .topTrailing) {
                    if session.flagged.contains(q.id) {
                        Image(systemName: "flag.fill").font(.system(size: 7)).foregroundStyle(.orange).padding(2)
                    }
                }
        }
        .buttonStyle(.plain)
        .disabled(!session.canJump(to: index))
        .accessibilityLabel("Question \(index + 1)\(session.response(for: q) == nil ? ", unanswered" : ", answered")")
    }

    private func fill(_ q: Question) -> Color {
        if session.isLocked(q) { return q.isCorrect(session.response(for: q)) ? .green.opacity(0.3) : .red.opacity(0.3) }
        return session.response(for: q) == nil ? Color(nsColor: .quaternaryLabelColor) : .accentColor.opacity(0.35)
    }
}

/// Exposes the hosting NSWindow (for full screen).
struct WindowAccessor: NSViewRepresentable {
    @Binding var window: NSWindow?
    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { window = v.window }
        return v
    }
    func updateNSView(_ v: NSView, context: Context) {
        if v.window !== window { DispatchQueue.main.async { window = v.window } }
    }
}

func formatDuration(_ seconds: TimeInterval) -> String {
    let s = Int(seconds.rounded(.down))
    return s >= 3600
        ? String(format: "%d:%02d:%02d", s / 3600, s % 3600 / 60, s % 60)
        : String(format: "%d:%02d", s / 60, s % 60)
}
