import Foundation
import XCTest
@testable import MockiCore

enum Repo {
    static let root = URL(filePath: #filePath).deletingLastPathComponent()
        .deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent()
    static func url(_ path: String) -> URL { root.appending(path: path) }
}

/// Builds exam JSON from question JSON fragments.
func examJSON(_ questions: [String], extra: String = "") -> Data {
    Data(#"{"id":"t","title":"T"\#(extra),"questions":[\#(questions.joined(separator: ","))]}"#.utf8)
}

func choiceQ(_ id: String = "q1", kind: String = "single", answer: String = #""a""#, extra: String = "") -> String {
    #"{"id":"\#(id)","kind":"\#(kind)","prompt":"P \#(id)","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"}],"answer":\#(answer)\#(extra)}"#
}

func decode(_ data: Data, file: StaticString = #filePath, line: UInt = #line) throws -> Exam {
    do { return try ExamFormat.decode(data) } catch let e as ExamLoadError {
        XCTFail("unexpected issues:\n\(e.issues.map(\.description).joined(separator: "\n"))", file: file, line: line)
        throw e
    }
}

func assertIssue(_ data: Data, _ path: String, containing text: String, file: StaticString = #filePath, line: UInt = #line) {
    let (exam, issues) = ExamFormat.validate(data)
    XCTAssertNil(exam, "expected invalid", file: file, line: line)
    XCTAssertTrue(issues.contains { $0.severity == .error && $0.path == path && $0.message.contains(text) },
                  "no error at \(path) containing \"\(text)\". Got:\n\(issues.map(\.description).joined(separator: "\n"))", file: file, line: line)
}

func tempDir() -> URL {
    let url = FileManager.default.temporaryDirectory.appending(path: "mocki-\(UUID().uuidString)")
    try! FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
    return url
}
