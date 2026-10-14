import Foundation

/// The exam library on disk: one folder per exam, holding `exam.json` and the images it uses.
/// Shared by the app and the `mockiexam install` command.
public struct LibraryStore: Sendable {
    public static let bundleID = "app.mockiexams.MockiExams"
    public let directory: URL

    public init(directory: URL? = nil) {
        self.directory = directory ?? Self.defaultDirectory
        try? FileManager.default.createDirectory(at: self.directory, withIntermediateDirectories: true)
    }

    /// Inside the app this is its sandbox container; from the CLI it points at the same container.
    public static var defaultDirectory: URL {
        let sandboxed = ProcessInfo.processInfo.environment["APP_SANDBOX_CONTAINER_ID"] != nil
        let support = sandboxed
            ? URL.applicationSupportDirectory
            : URL.homeDirectory.appending(path: "Library/Containers/\(bundleID)/Data/Library/Application Support")
        return support.appending(path: "MockiExams/Library", directoryHint: .isDirectory)
    }

    public struct Contents: Sendable {
        public var exams: [Exam] = []
        /// Folders that no longer load, as "name: reason".
        public var broken: [String] = []
    }

    public func load() -> Contents {
        migrateFlatFiles()
        var contents = Contents()
        let entries = (try? FileManager.default.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.isDirectoryKey])) ?? []
        for entry in entries.sorted(by: { $0.lastPathComponent < $1.lastPathComponent }) {
            let file = entry.appending(path: "exam.json")
            guard FileManager.default.fileExists(atPath: file.path) else { continue }
            do {
                contents.exams.append(try ExamFormat.decode(Data(contentsOf: file), baseURL: entry))
            } catch {
                contents.broken.append("\(entry.lastPathComponent): \(error.localizedDescription)")
            }
        }
        contents.exams.sort { $0.title.localizedStandardCompare($1.title) == .orderedAscending }
        return contents
    }

    /// Validates and copies an exam in. `source` is an exam JSON file or a folder holding
    /// `exam.json`; referenced images are resolved relative to it. Replaces an exam with the same id.
    @discardableResult
    public func install(_ source: URL) throws -> Exam {
        let fm = FileManager.default
        var isDir: ObjCBool = false
        guard fm.fileExists(atPath: source.path, isDirectory: &isDir) else {
            throw ExamLoadError(issues: [Issue(severity: .error, path: "$", message: "No such file: \(source.path)")])
        }
        let file = isDir.boolValue ? try Self.examFile(in: source) : source
        let base = file.deletingLastPathComponent()
        let exam = try ExamFormat.decode(Data(contentsOf: file), baseURL: base)

        let staging = directory.appending(path: ".staging-\(UUID().uuidString)")
        defer { try? fm.removeItem(at: staging) }
        try fm.createDirectory(at: staging, withIntermediateDirectories: true)
        try fm.copyItem(at: file, to: staging.appending(path: "exam.json"))
        for path in exam.assetPaths {
            let dest = staging.appending(path: path)
            try fm.createDirectory(at: dest.deletingLastPathComponent(), withIntermediateDirectories: true)
            try fm.copyItem(at: base.appending(path: path), to: dest)
        }

        let target = directory.appending(path: Self.folderName(for: exam.id))
        if fm.fileExists(atPath: target.path) { try fm.removeItem(at: target) }
        try fm.moveItem(at: staging, to: target)
        var installed = exam
        installed.assetBase = target
        return installed
    }

    /// v1 stored exams as flat `<id>.exam.json` files; move each into its own folder.
    func migrateFlatFiles() {
        let fm = FileManager.default
        let files = ((try? fm.contentsOfDirectory(at: directory, includingPropertiesForKeys: nil)) ?? [])
            .filter { $0.pathExtension == "json" }
        for file in files {
            guard let exam = try? ExamFormat.decode(Data(contentsOf: file)) else { continue }
            let target = directory.appending(path: Self.folderName(for: exam.id))
            do {
                if fm.fileExists(atPath: target.appending(path: "exam.json").path) {
                    try fm.removeItem(at: file)
                } else {
                    try fm.createDirectory(at: target, withIntermediateDirectories: true)
                    try fm.moveItem(at: file, to: target.appending(path: "exam.json"))
                }
            } catch {
                continue
            }
        }
    }

    public func remove(examID: String) throws {
        let target = directory.appending(path: Self.folderName(for: examID))
        if FileManager.default.fileExists(atPath: target.path) { try FileManager.default.removeItem(at: target) }
    }

    /// `exam.json` in a folder, or its only `.json` file.
    static func examFile(in folder: URL) throws -> URL {
        let named = folder.appending(path: "exam.json")
        if FileManager.default.fileExists(atPath: named.path) { return named }
        let jsons = ((try? FileManager.default.contentsOfDirectory(at: folder, includingPropertiesForKeys: nil)) ?? [])
            .filter { $0.pathExtension == "json" }
        guard jsons.count == 1 else {
            throw ExamLoadError(issues: [Issue(severity: .error, path: "$", message:
                "Folder \(folder.lastPathComponent) needs an exam.json (found \(jsons.count) .json files).")])
        }
        return jsons[0]
    }

    public static func folderName(for id: String) -> String {
        String(id.unicodeScalars.map { CharacterSet.alphanumerics.contains($0) || $0 == "-" ? Character($0) : "_" })
    }
}
