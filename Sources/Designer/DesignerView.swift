import AppKit
import MockiCore
import SwiftUI
import UniformTypeIdentifiers

enum DesignerPage: Hashable {
    case overview, bank, sections, caseStudies, assets, settings

    var title: String {
        switch self {
        case .overview: "Overview"
        case .bank: "Question bank"
        case .sections: "Sections"
        case .caseStudies: "Case studies"
        case .assets: "Assets"
        case .settings: "Exam settings"
        }
    }
}

/// The designer window: a left navigation of pages, a page header with actions, and an issues panel.
struct DesignerView: View {
    @Binding var document: ExamProjectDocument

    @State private var page: DesignerPage? = .overview
    @State private var bankSelection: Set<Int> = []
    @State private var search = ""
    @State private var issues: [Issue] = []
    @State private var showIssues = false
    @State private var creating = false
    @State private var message: String?

    private var errorCount: Int { issues.count { $0.severity == .error } }
    private var warningCount: Int { issues.count - errorCount }

    var body: some View {
        NavigationSplitView {
            sidebar.navigationSplitViewColumnWidth(min: 210, ideal: 230, max: 280)
        } detail: {
            detail
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Palette.consolePage)
        }
        .inspector(isPresented: $showIssues) {
            IssuesView(issues: issues) { select($0) }
                .inspectorColumnWidth(min: 260, ideal: 320)
        }
        .searchable(text: $search, placement: .toolbar, prompt: "Search questions, topics, ids")
        .onChange(of: search) { if !search.isEmpty { page = .bank } }
        .toolbar { toolbar }
        .navigationTitle(document.exam.title ?? "Untitled Exam")
        .task(id: document.exam) {
            try? await Task.sleep(for: .milliseconds(250))
            issues = document.validate().issues
        }
        .task(id: document.assets.count) { issues = document.validate().issues }
        .sheet(isPresented: $creating) {
            CreateQuestionFlow(document: $document) { index in
                page = .bank
                bankSelection = [index]
            }
        }
        .alert(message ?? "", isPresented: Binding(get: { message != nil }, set: { if !$0 { message = nil } })) {
            Button("OK") { message = nil }
        }
    }

    // MARK: Navigation

    private var sidebar: some View {
        List(selection: $page) {
            Section("Project") {
                navRow(.overview, "square.grid.2x2")
                navRow(.bank, "list.bullet.rectangle", count: document.questions.count)
                navRow(.sections, "list.number", count: document.exam.sections?.count ?? 0)
                navRow(.caseStudies, "briefcase", count: document.exam.caseStudies?.count ?? 0)
                navRow(.assets, "photo.on.rectangle", count: document.assets.count)
                navRow(.settings, "gearshape")
            }
            Section("Publish") {
                Menu {
                    Button("Practice") { preview(mode: .practice) }
                    Button("Exam simulation") { preview(mode: .exam) }
                    Button("Exam simulation, classic screen") { preview(mode: .exam, style: .classic) }
                    Button("Exam simulation, full screen") { preview(mode: .exam, fullScreen: true) }
                } label: {
                    Label("Preview exam", systemImage: "play.rectangle")
                }
                .menuStyle(.button).buttonStyle(.plain)
                Button { install() } label: { Label("Install to Library", systemImage: "tray.and.arrow.down") }
                    .buttonStyle(.plain)
            }
        }
    }

    private func navRow(_ p: DesignerPage, _ icon: String, count: Int? = nil) -> some View {
        Label {
            HStack {
                Text(p.title)
                Spacer()
                if let count { Text("\(count)").foregroundStyle(.secondary).monospacedDigit() }
            }
        } icon: {
            Image(systemName: icon)
        }
        .tag(p)
    }

    @ViewBuilder
    private var detail: some View {
        switch page ?? .overview {
        case .overview:
            OverviewPage(document: $document, issues: issues, showIssues: { showIssues = true }, go: { page = $0 },
                         preview: { preview(mode: .exam) }, install: install)
        case .bank:
            QuestionBankPage(document: $document, selection: $bankSelection, search: $search, issues: issues,
                             create: { creating = true }, preview: { preview(only: $0) }, message: { message = $0 })
        case .sections:
            ConsolePage(crumbs: [projectName], title: "Sections", info: "Sections set which bank questions the exam asks, and in what order.") {
                EmptyView()
            } content: {
                SectionsEditor(document: $document)
            }
        case .caseStudies:
            CaseStudiesPage(document: $document, projectName: projectName)
        case .assets:
            ConsolePage(crumbs: [projectName], title: "Assets (\(document.assets.count))", info: "Images and PDFs used as exhibits and hotspots.") {
                EmptyView()
            } content: {
                AssetsView(document: $document)
            }
        case .settings:
            ConsolePage(crumbs: [projectName], title: "Exam settings") {
                EmptyView()
            } content: {
                ExamSettingsView(document: $document)
            }
        }
    }

    private var projectName: String { document.exam.title ?? "Untitled Exam" }

    // MARK: Toolbar

    @ToolbarContentBuilder
    private var toolbar: some ToolbarContent {
        ToolbarItemGroup {
            Button { creating = true } label: { Label("Create Question", systemImage: "plus") }
                .help("Create a question step by step")
            Button { preview(mode: .exam) } label: { Label("Preview Exam", systemImage: "play.rectangle") }
                .help("Run the whole exam in the simulator")
            Button { install() } label: { Label("Install to Library", systemImage: "tray.and.arrow.down") }
                .help("Validate and copy this exam into the player library")
            Button { showIssues.toggle() } label: {
                Label(errorCount == 0 ? "No Errors" : "\(errorCount) Error\(errorCount == 1 ? "" : "s")",
                      systemImage: errorCount == 0 ? "checkmark.seal" : "exclamationmark.triangle.fill")
            }
            .labelStyle(.titleAndIcon)
            .foregroundStyle(errorCount == 0 ? Palette.ok : Palette.warn)
            .help("Show validation issues")
        }
    }

    // MARK: Actions

    private func preview(only index: Int? = nil, mode: SessionMode = .practice, style: ExamStyle = .modern, fullScreen: Bool = false) {
        let (exam, issues) = document.materialize()
        guard let exam else {
            self.issues = issues
            showIssues = true
            message = "Fix the errors in the Issues panel before previewing."
            return
        }
        var options = SessionOptions(mode: index == nil ? mode : .practice, shuffleQuestions: index == nil, shuffleChoices: true)
        var title = "Preview: \(exam.title)"
        if let index, let id = document.questions[safe: index]?.id {
            options.questionIDs = [id]
            title = "Preview: \(id)"
        }
        PreviewWindows.open(RunRequest(exam: exam, options: options, fullScreen: fullScreen, style: style, doneLabel: "Close Preview", allowsRetry: false), title: title)
    }

    private func install() {
        let (exam, issues) = document.materialize()
        guard let exam, let folder = exam.assetBase else {
            self.issues = issues
            showIssues = true
            message = "Fix the errors in the Issues panel before installing."
            return
        }
        do {
            let installed = try LibraryStore().install(folder)
            NotificationCenter.default.post(name: .libraryChanged, object: installed.id)
            message = "Installed \"\(installed.title)\" (\(installed.examQuestions.count) questions) in the Library."
        } catch {
            message = error.localizedDescription
        }
    }

    /// Jumps to whatever an issue path points at.
    private func select(_ issue: Issue) {
        if let m = issue.path.firstMatch(of: /^questions\[(\d+)\]/), let i = Int(m.1), document.questions.indices.contains(i) {
            search = ""
            page = .bank
            bankSelection = [i]
        } else if issue.path.hasPrefix("caseStudies") {
            page = .caseStudies
        } else if issue.path.hasPrefix("sections") {
            page = .sections
        } else if issue.path == "assets" {
            page = .assets
        } else if issue.path.hasPrefix("topicWeights") {
            page = .overview
        } else {
            page = .settings
        }
    }
}

extension Array {
    subscript(safe i: Int) -> Element? { indices.contains(i) ? self[i] : nil }
}

/// Breadcrumbs, a title with optional info text and actions, then the page content.
struct ConsolePage<Actions: View, Content: View>: View {
    let crumbs: [String]
    let title: String
    var info: String? = nil
    @ViewBuilder let actions: Actions
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            VStack(alignment: .leading, spacing: 6) {
                Text((crumbs + [title]).joined(separator: "  ›  "))
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
                HStack(alignment: .firstTextBaseline, spacing: 12) {
                    Text(title).font(.title2.weight(.semibold))
                    Spacer()
                    actions
                }
                if let info { Text(info).font(.callout).foregroundStyle(.secondary) }
            }
            .padding(.horizontal, 22).padding(.top, 16).padding(.bottom, 12)
            content
        }
    }
}

/// A bordered panel on the console background.
struct ConsoleCard<Content: View>: View {
    var title: String? = nil
    var trailing: AnyView? = nil
    @ViewBuilder let content: Content

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            if title != nil || trailing != nil {
                HStack {
                    if let title { Text(title).font(.headline) }
                    Spacer()
                    trailing
                }
            }
            content
        }
        .padding(14)
        .frame(maxWidth: .infinity, alignment: .topLeading)
        .background(Palette.consolePanel, in: RoundedRectangle(cornerRadius: 8))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Palette.consoleLine))
    }
}

enum KindIcon {
    static func name(_ kind: QuestionKind) -> String {
        switch kind {
        case .single: "circle.inset.filled"
        case .multiple: "checklist"
        case .trueFalse: "checkmark.circle.badge.xmark"
        case .yesNo: "list.bullet.rectangle"
        case .ordering: "list.number"
        case .matching: "arrow.left.arrow.right"
        case .dropdowns: "filemenu.and.selection"
        case .fillIn: "character.cursor.ibeam"
        case .hotspot: "scope"
        }
    }

    static func blurb(_ kind: QuestionKind) -> String {
        switch kind {
        case .single: "One correct option."
        case .multiple: "Choose two or more correct options."
        case .trueFalse: "A statement that is true or false."
        case .yesNo: "Several statements, Yes or No for each."
        case .ordering: "Drag steps into order, with optional distractors."
        case .matching: "Drag items onto categories or pairs."
        case .dropdowns: "Pick from a list in each row, like a hot area."
        case .fillIn: "Type a command, number or word."
        case .hotspot: "Click the right area of an image."
        }
    }
}

struct IssuesView: View {
    let issues: [Issue]
    let onSelect: (Issue) -> Void

    var body: some View {
        List {
            if issues.isEmpty {
                Label("No issues. Ready to preview and install.", systemImage: "checkmark.seal.fill").foregroundStyle(.green)
            }
            ForEach(Array(issues.enumerated()), id: \.offset) { _, issue in
                Button { onSelect(issue) } label: {
                    HStack(alignment: .firstTextBaseline) {
                        Image(systemName: issue.severity == .error ? "xmark.octagon.fill" : "exclamationmark.triangle.fill")
                            .foregroundStyle(issue.severity == .error ? .red : .yellow)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(issue.message).fixedSize(horizontal: false, vertical: true)
                            Text(issue.path).font(.caption.monospaced()).foregroundStyle(.secondary)
                        }
                    }
                }
                .buttonStyle(.plain)
            }
        }
        .navigationTitle("Issues")
    }
}

/// Preview runs in their own windows so the project stays editable alongside.
@MainActor
enum PreviewWindows {
    private static var open: [NSWindow] = []

    static func open(_ request: RunRequest, title: String) {
        let visible = NSScreen.main?.visibleFrame.size ?? CGSize(width: 1280, height: 800)
        let size = CGSize(width: min(1200, visible.width - 80), height: min(800, visible.height - 60))
        let window = NSWindow(contentRect: NSRect(origin: .zero, size: size),
                              styleMask: [.titled, .closable, .resizable, .miniaturizable], backing: .buffered, defer: false)
        window.title = title
        window.isReleasedWhenClosed = false
        window.collectionBehavior.insert(.fullScreenPrimary)
        let root = RunView(request: request, onRetry: { _ in }, onClose: { [weak window] in window?.close() })
            .frame(minWidth: 820, minHeight: 560)
            .appAppearance()
        window.contentViewController = NSHostingController(rootView: root)
        window.center()
        window.makeKeyAndOrderFront(nil)
        open.append(window)
        let id = ObjectIdentifier(window)
        NotificationCenter.default.addObserver(forName: NSWindow.willCloseNotification, object: window, queue: .main) { _ in
            MainActor.assumeIsolated { open.removeAll { ObjectIdentifier($0) == id } }
        }
    }
}
