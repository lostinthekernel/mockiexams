import MockiCore
import SwiftData
import SwiftUI
import UniformTypeIdentifiers

struct LibraryView: View {
    @State private var library = ExamLibrary()
    @State private var selection: Exam.ID?
    @State private var run: RunRequest?
    @State private var importing = false
    @State private var importFailures: [String] = []
    @State private var columns = NavigationSplitViewVisibility.automatic
    @Environment(\.modelContext) private var modelContext
    @Environment(\.newDocument) private var newDocument
    @Environment(\.openDocument) private var openDocument

    private var selectedExam: Exam? { library.exams.first { $0.id == selection } }

    var body: some View {
        NavigationSplitView(columnVisibility: $columns) {
            List(selection: $selection) {
                ForEach(library.exams) { exam in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(exam.title)
                        Text("\(exam.examQuestions.count) questions")
                            .font(.caption)
                            .foregroundStyle(.secondary)
                    }
                    .tag(exam.id)
                    .contextMenu {
                        Button("Open in Designer") { openInDesigner(exam) }
                        Divider()
                        Button("Remove from Library", role: .destructive) { library.remove(exam) }
                    }
                }
                if !library.brokenFiles.isEmpty {
                    Section("Could not load") {
                        ForEach(library.brokenFiles, id: \.self) {
                            Text($0).font(.caption).foregroundStyle(.red)
                        }
                    }
                }
            }
            .overlay {
                if library.exams.isEmpty {
                    ContentUnavailableView {
                        Label("No exams yet", systemImage: "doc.text")
                    } description: {
                        Text("Import an exam file or folder, or create one in the Designer.")
                    } actions: {
                        Button("Import…") { importing = true }
                        Button("New Exam Project") { newDocument(ExamProjectDocument()) }
                    }
                }
            }
            .disabled(run != nil)
            .navigationSplitViewColumnWidth(min: 220, ideal: 260)
            .toolbar {
                Button { importing = true } label: { Label("Import Exam", systemImage: "square.and.arrow.down") }
                    .disabled(run != nil)
                Button { newDocument(ExamProjectDocument()) } label: { Label("New Exam Project", systemImage: "pencil.and.list.clipboard") }
                    .help("Open the Designer with a new exam project")
            }
        } detail: {
            if let run {
                RunView(request: run, onFinish: { modelContext.insert(Attempt(result: $0)) }, onRetry: { ids in
                    var options = run.options
                    options.questionIDs = Set(ids)
                    options.questionLimit = nil
                    options.seed = .random(in: .min ... .max)
                    self.run = RunRequest(exam: run.exam, options: options, fullScreen: run.fullScreen, style: run.style)
                }, onClose: { self.run = nil })
                .id(run.id)
            } else if let exam = selectedExam {
                ExamDetailView(exam: exam) { options, fullScreen, style in
                    run = RunRequest(exam: exam, options: options, fullScreen: fullScreen, style: style)
                }
                .id(exam.id)
            } else {
                ContentUnavailableView("Select an exam", systemImage: "checklist")
            }
        }
        .onChange(of: run?.id) { columns = run == nil ? .all : .detailOnly }
        .onAppear { columns = .all }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.json, .folder, .mockiExam], allowsMultipleSelection: true) { result in
            switch result {
            case let .success(urls): importFailures = library.importFiles(urls)
            case let .failure(error): importFailures = [error.localizedDescription]
            }
            if selection == nil { selection = library.exams.first?.id }
        }
        .alert("Some files were not imported", isPresented: .constant(!importFailures.isEmpty)) {
            Button("OK") { importFailures = [] }
        } message: {
            Text(importFailures.joined(separator: "\n\n"))
        }
        .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in library.reload() }
        .onReceive(NotificationCenter.default.publisher(for: .libraryChanged)) { note in
            library.reload()
            if let id = note.object as? String { selection = id }
        }
    }

    /// Copies the installed exam into a new project package and opens it in the Designer.
    private func openInDesigner(_ exam: Exam) {
        guard let base = exam.assetBase else { return }
        let dest = URL.temporaryDirectory.appending(path: "\(LibraryStore.folderName(for: exam.id))-\(UUID().uuidString.prefix(6)).mockiexam")
        do {
            try FileManager.default.copyItem(at: base, to: dest)
            Task { try? await openDocument(at: dest) }
        } catch {
            importFailures = [error.localizedDescription]
        }
    }
}

extension Notification.Name {
    /// Posted after the Designer installs an exam; `object` is the exam id.
    static let libraryChanged = Notification.Name("MockiExams.libraryChanged")
}

extension UTType {
    static let mockiExam = UTType(exportedAs: "app.mockiexams.project")
}
