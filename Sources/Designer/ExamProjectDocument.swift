import Foundation
import MockiCore
import SwiftUI
import UniformTypeIdentifiers

/// A `.mockiexam` package: `exam.json` plus the images it references, held in memory while editing.
/// The JSON is kept as the raw `ExamDocument` so a half-finished project still opens.
struct ExamProjectDocument: FileDocument {
    static var readableContentTypes: [UTType] { [.mockiExam] }

    var exam: ExamDocument
    /// Relative path (e.g. `images/topology.png`) → file data.
    var assets: [String: Data]

    init() {
        exam = ExamDocument(id: "new-exam", title: "Untitled Exam", questions: [])
        exam.passPercent = 70
        exam.timeLimitMinutes = 60
        assets = [:]
    }

    init(configuration: ReadConfiguration) throws {
        guard let files = configuration.file.fileWrappers, let json = files["exam.json"]?.regularFileContents else {
            throw CocoaError(.fileReadCorruptFile)
        }
        exam = try JSONDecoder().decode(ExamDocument.self, from: json)
        assets = [:]
        for (name, wrapper) in files where name != "exam.json" {
            Self.collect(wrapper, path: name, into: &assets)
        }
    }

    private static func collect(_ wrapper: FileWrapper, path: String, into assets: inout [String: Data]) {
        if wrapper.isDirectory {
            for (name, child) in wrapper.fileWrappers ?? [:] { collect(child, path: "\(path)/\(name)", into: &assets) }
        } else if let data = wrapper.regularFileContents, !path.hasSuffix(".DS_Store") {
            assets[path] = data
        }
    }

    func fileWrapper(configuration: WriteConfiguration) throws -> FileWrapper {
        let root = FileWrapper(directoryWithFileWrappers: [:])
        root.addRegularFile(withContents: try ExamFormat.encode(exam), preferredFilename: "exam.json")
        // Rebuild the folder tree for assets.
        var folders: [String: FileWrapper] = ["": root]
        func folder(_ path: String) -> FileWrapper {
            if let f = folders[path] { return f }
            let parent = folder((path as NSString).deletingLastPathComponent)
            let f = FileWrapper(directoryWithFileWrappers: [:])
            f.preferredFilename = (path as NSString).lastPathComponent
            parent.addFileWrapper(f)
            folders[path] = f
            return f
        }
        for (path, data) in assets.sorted(by: { $0.key < $1.key }) {
            folder((path as NSString).deletingLastPathComponent)
                .addRegularFile(withContents: data, preferredFilename: (path as NSString).lastPathComponent)
        }
        return root
    }

    /// Writes the project as a plain folder, for preview, validation and install.
    func write(to folder: URL) throws {
        let fm = FileManager.default
        try? fm.removeItem(at: folder)
        try fm.createDirectory(at: folder, withIntermediateDirectories: true)
        try ExamFormat.encode(exam).write(to: folder.appending(path: "exam.json"))
        for (path, data) in assets {
            let url = folder.appending(path: path)
            try fm.createDirectory(at: url.deletingLastPathComponent(), withIntermediateDirectories: true)
            try data.write(to: url)
        }
    }

    /// Validates against a scratch copy on disk, so image references are checked too.
    func validate() -> (exam: Exam?, issues: [Issue]) {
        let folder = URL.temporaryDirectory.appending(path: "mocki-validate-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: folder) }
        do {
            try write(to: folder)
            var (exam, issues) = ExamFormat.validate(try Data(contentsOf: folder.appending(path: "exam.json")), baseURL: folder)
            for path in assets.keys.sorted() where usage(of: path) == 0 {
                issues.append(Issue(severity: .warning, path: "assets", message: "\(path) isn't used by any question."))
            }
            exam?.assetBase = nil
            return (exam, issues)
        } catch {
            return (nil, [Issue(severity: .error, path: "$", message: error.localizedDescription)])
        }
    }

    /// A playable copy on disk (for preview); nil with issues if the project doesn't validate.
    func materialize() -> (exam: Exam?, issues: [Issue]) {
        let folder = URL.temporaryDirectory.appending(path: "mocki-preview-\(UUID().uuidString)")
        do {
            try write(to: folder)
            let exam = try ExamFormat.decode(Data(contentsOf: folder.appending(path: "exam.json")), baseURL: folder)
            return (exam, [])
        } catch let e as ExamLoadError {
            return (nil, e.issues)
        } catch {
            return (nil, [Issue(severity: .error, path: "$", message: error.localizedDescription)])
        }
    }

    // MARK: Editing helpers

    var questions: [QuestionDocument] {
        get { exam.questions ?? [] }
        set { exam.questions = newValue }
    }

    func uniqueQuestionID(_ base: String) -> String {
        let used = Set(questions.compactMap(\.id))
        if !used.contains(base) { return base }
        return (2...).lazy.map { "\(base)-\($0)" }.first { !used.contains($0) }!
    }

    /// A new question of a kind, seeded from the format templates, not yet in the bank.
    func makeQuestion(_ kind: QuestionKind) -> QuestionDocument {
        var q = (try? JSONDecoder().decode(QuestionDocument.self, from: Data(Templates.question(kind).utf8)))
            ?? QuestionDocument(id: "q", kind: kind, prompt: "")
        q.id = uniqueQuestionID("q\(questions.count + 1)")
        q.topic = nil
        q.explanation = nil
        if kind == .hotspot {
            q.image = assets.keys.sorted().first { !$0.hasSuffix(".pdf") }
            q.regions = []
            q.answer = .strings([])
        }
        return q
    }

    /// The questions the exam asks: section order when there are sections, otherwise the bank.
    var askedQuestions: [QuestionDocument] {
        guard let sections = exam.sections, !sections.isEmpty else { return questions }
        let byID = Dictionary(questions.compactMap { q in q.id.map { ($0, q) } }, uniquingKeysWith: { a, _ in a })
        return sections.flatMap { ($0.questions ?? []).compactMap { byID[$0] } }
    }

    var topics: [String] {
        var seen = Set<String>()
        let fromQuestions = questions.map { $0.topic?.nonEmpty ?? "General" }
        return (fromQuestions + (exam.topicWeights ?? [:]).keys.sorted()).filter { seen.insert($0).inserted }
    }

    /// Appends converted or copied questions, renaming ids that clash with the bank.
    mutating func append(_ incoming: [QuestionDocument]) -> Int {
        for var q in incoming {
            q.id = uniqueQuestionID(q.id?.nonEmpty ?? "q\(questions.count + 1)")
            questions.append(q)
        }
        return incoming.count
    }

    mutating func duplicateQuestion(_ id: String) -> String? {
        guard let i = questions.firstIndex(where: { $0.id == id }) else { return nil }
        var copy = questions[i]
        copy.id = uniqueQuestionID(id + "-copy")
        questions.insert(copy, at: i + 1)
        return copy.id
    }

    mutating func deleteQuestion(_ id: String) {
        questions.removeAll { $0.id == id }
        exam.sections = exam.sections?.map { s in var s = s; s.questions?.removeAll { $0 == id }; return s }
    }

    /// Renames a question and updates every section that lists it.
    mutating func renameQuestion(_ old: String, to new: String) {
        guard let i = questions.firstIndex(where: { $0.id == old }) else { return }
        questions[i].id = new
        exam.sections = exam.sections?.map { s in
            var s = s
            s.questions = s.questions?.map { $0 == old ? new : $0 }
            return s
        }
    }

    func usage(of asset: String) -> Int {
        let exhibits = (exam.caseStudies ?? []).flatMap { $0.exhibits ?? [] } + questions.flatMap { $0.exhibits ?? [] }
        return exhibits.count { $0.image == asset } + questions.count { $0.image == asset }
    }

    mutating func addAsset(_ data: Data, named name: String) -> String {
        let ext = (name as NSString).pathExtension
        let stem = Converter.slug((name as NSString).deletingPathExtension).nonEmptyOr("image")
        var path = "images/\(stem).\(ext.lowercased())", n = 2
        while assets[path] != nil && assets[path] != data {
            path = "images/\(stem)-\(n).\(ext.lowercased())"
            n += 1
        }
        assets[path] = data
        return path
    }
}

extension String {
    func nonEmptyOr(_ fallback: String) -> String { isEmpty ? fallback : self }
    var nonEmpty: String? { trimmingCharacters(in: .whitespaces).isEmpty ? nil : self }
}
