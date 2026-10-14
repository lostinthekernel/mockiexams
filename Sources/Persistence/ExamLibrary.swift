import Foundation
import MockiCore
import Observation

/// Observable view of the on-disk library. All file work lives in `LibraryStore`.
@MainActor
@Observable
final class ExamLibrary {
    private(set) var exams: [Exam] = []
    /// Exams that no longer load, as "name: reason".
    private(set) var brokenFiles: [String] = []
    let store: LibraryStore

    @ObservationIgnored private var watcher: DispatchSourceFileSystemObject?

    init(store: LibraryStore = LibraryStore()) {
        self.store = store
        reload()
        watch()
    }

    /// Reloads when anything is added to or removed from the library folder (e.g. `mockiexam install`).
    private func watch() {
        let fd = open(store.directory.path, O_EVTONLY)
        guard fd >= 0 else { return }
        let source = DispatchSource.makeFileSystemObjectSource(fileDescriptor: fd, eventMask: [.write, .rename, .delete], queue: .main)
        source.setEventHandler { [weak self] in
            MainActor.assumeIsolated { self?.reload() }
        }
        source.setCancelHandler { close(fd) }
        source.resume()
        watcher = source
    }

    func reload() {
        let contents = store.load()
        exams = contents.exams
        brokenFiles = contents.broken
    }

    /// Validates each exam (JSON file, folder, or .mockiexam package) and copies it in;
    /// returns one message per item that was rejected.
    func importFiles(_ urls: [URL]) -> [String] {
        var failures: [String] = []
        for url in urls {
            let scoped = url.startAccessingSecurityScopedResource()
            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
            do {
                try store.install(url)
            } catch {
                var message = error.localizedDescription
                if message.contains("File not found"), url.pathExtension == "json" {
                    message += "\n\nThis exam uses images. Import the folder that contains exam.json and its images instead."
                }
                failures.append("\(url.lastPathComponent): \(message)")
            }
        }
        reload()
        return failures
    }

    func remove(_ exam: Exam) {
        try? store.remove(examID: exam.id)
        reload()
    }
}
