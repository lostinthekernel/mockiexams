import Foundation
import XCTest
@testable import MockiCore

final class ConverterTests: XCTestCase {
    private func convert(_ file: String, _ format: SourceFormat) throws -> (Exam, Conversion) {
        let text = try String(contentsOf: Repo.url("samples/converters/\(file)"), encoding: .utf8)
        XCTAssertEqual(SourceFormat.detect(fileName: file, text: text), format)
        let c = Converter.convert(text, format: format, id: "conv", title: "Converted")
        return (try decode(ExamFormat.encode(c.document)), c)
    }

    func testAiken() throws {
        let (exam, c) = try convert("sample.aiken.txt", .aiken)
        XCTAssertEqual(exam.questions.map(\.kind), [.single, .multiple])
        XCTAssertTrue(exam.questions[0].isCorrect(.single("b")))
        XCTAssertTrue(exam.questions[1].isCorrect(.multiple(["a", "c"])))
        XCTAssertEqual(c.skipped.count, 1)
    }

    func testGift() throws {
        let (exam, c) = try convert("sample.gift", .gift)
        XCTAssertEqual(exam.questions.map(\.id), ["dns-port", "rfc1918", "udp-reliable", "show-run", "match-ports", "hosts-26"])
        XCTAssertEqual(exam.questions.map(\.kind), [.single, .multiple, .trueFalse, .fillIn, .matching, .fillIn])
        XCTAssertEqual(exam.questions.map(\.topic), ["Ports", "Ports", "Concepts", "Concepts", "Concepts", "Concepts"])
        XCTAssertEqual(exam.questions[0].explanation, "DNS uses 53/UDP, and TCP for large responses.")
        XCTAssertTrue(exam.questions[0].isCorrect(.single("a")))
        XCTAssertTrue(exam.questions[1].isCorrect(.multiple(["a", "b"])))
        XCTAssertTrue(exam.questions[2].isCorrect(.single("false")))
        XCTAssertTrue(exam.questions[3].isCorrect(.fillIn(["answer": "show run"])))
        XCTAssertTrue(exam.questions[4].isCorrect(.matching(["i1": "t1", "i2": "t2", "i3": "t3"])))
        XCTAssertTrue(exam.questions[5].isCorrect(.fillIn(["answer": "62"])))
        XCTAssertEqual(c.skipped.map(\.message), ["Skipped: essay questions have no answer key"])
    }

    func testCSV() throws {
        let (exam, c) = try convert("sample.csv", .csv)
        XCTAssertEqual(exam.questions.map(\.id), ["ssh-port", "private", "tcp-reliable", "show-ver", "by-text"])
        XCTAssertEqual(exam.questions.map(\.kind), [.single, .multiple, .trueFalse, .fillIn, .single])
        XCTAssertTrue(exam.questions[0].isCorrect(.single("b")))
        XCTAssertTrue(exam.questions[1].isCorrect(.multiple(["a", "c"])))
        XCTAssertTrue(exam.questions[3].isCorrect(.fillIn(["answer": "sh ver"])))
        XCTAssertTrue(exam.questions[4].isCorrect(.single("b")))
        XCTAssertEqual(exam.questions[1].explanation, "10/8, 172.16/12 and 192.168/16.")
        XCTAssertEqual(c.skipped.count, 1)
        XCTAssertTrue(c.skipped[0].path == "line 7")
    }

    func testCSVQuotingAndDelimiters() {
        XCTAssertEqual(CSV.parse("a;b\n\"x;1\";\"say \"\"hi\"\"\nthere\"\n"), [["a", "b"], ["x;1", "say \"hi\"\nthere"]])
    }
}

final class LibraryTests: XCTestCase {
    func testInstallFolderWithImagesAndReplaceByID() throws {
        let store = LibraryStore(directory: tempDir())
        let exam = try store.install(Repo.url("samples/networking-sim"))
        let folder = store.directory.appending(path: "sample-networking-sim")
        XCTAssertEqual(exam.assetBase, folder)
        for path in exam.assetPaths { XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appending(path: path).path), path) }

        try store.install(Repo.url("samples/networking-sim/exam.json"))
        let contents = store.load()
        XCTAssertEqual(contents.exams.map(\.id), ["sample-networking-sim"])
        XCTAssertEqual(contents.broken, [])

        try store.remove(examID: exam.id)
        XCTAssertTrue(store.load().exams.isEmpty)
    }

    func testInstallRejectsInvalidAndMissingImages() throws {
        let store = LibraryStore(directory: tempDir())
        let src = tempDir()
        try Data(#"{"id":"x","title":"X","questions":[{"id":"q","kind":"hotspot","prompt":"P","image":"img/a.png","regions":[{"id":"r","x":0,"y":0,"width":1,"height":1}],"answer":["r"]}]}"#.utf8)
            .write(to: src.appending(path: "exam.json"))
        XCTAssertThrowsError(try store.install(src)) { XCTAssertTrue($0.localizedDescription.contains("File not found")) }
        XCTAssertTrue(store.load().exams.isEmpty)
    }

    func testMigratesFlatV1Files() throws {
        let store = LibraryStore(directory: tempDir())
        try FileManager.default.copyItem(at: Repo.url("samples/sample.exam.json"), to: store.directory.appending(path: "sample-networking-basics.exam.json"))
        let contents = store.load()
        XCTAssertEqual(contents.exams.map(\.id), ["sample-networking-basics"])
        let names = try FileManager.default.contentsOfDirectory(atPath: store.directory.path)
        XCTAssertEqual(names, ["sample-networking-basics"])
        XCTAssertEqual(store.load().exams.count, 1, "migration is idempotent")
    }
}
