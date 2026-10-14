import Foundation
import XCTest
@testable import MockiCore

final class ScoringTests: XCTestCase {
    private func question(_ kind: QuestionKind, partial: Bool = false) throws -> Question {
        var json = Templates.question(kind)
        if partial { json = json.replacingOccurrences(of: #""id":"#, with: #""partialCredit": true, "id":"#, options: [], range: json.range(of: #""id":"#)) }
        return try decode(examJSON([json])).questions[0]
    }

    func testFullMarksForEachKind() throws {
        let cases: [(QuestionKind, Response)] = [
            (.single, .single("a")),
            (.multiple, .multiple(["a", "c"])),
            (.trueFalse, .single("false")),
            (.yesNo, .dropdowns(["s1": "Yes", "s2": "No"])),
            (.ordering, .ordering(["d", "o", "r", "a"])),
            (.matching, .matching(["ssh": "22", "rdp": "3389", "ftp": "21"])),
            (.dropdowns, .dropdowns(["action": "permit", "port": "443"])),
            (.fillIn, .fillIn(["cmd": "  SH   ip route "])),
            (.hotspot, .hotspot(["router"])),
        ]
        for (kind, response) in cases {
            let q = try question(kind)
            XCTAssertTrue(q.isCorrect(response), "\(kind)")
            XCTAssertEqual(q.score(response), 1, "\(kind)")
            XCTAssertFalse(q.isCorrect(nil), "\(kind) unanswered")
            XCTAssertNotEqual(q.describe(response), "(unanswered)", "\(kind)")
            XCTAssertFalse(q.describeAnswer.isEmpty, "\(kind)")
        }
    }

    func testAllOrNothingByDefault() throws {
        let q = try question(.multiple)
        XCTAssertEqual(q.score(.multiple(["a"])), 0)
        XCTAssertEqual(q.fraction(.multiple(["a"])), 0.5)
    }

    func testPartialCredit() throws {
        XCTAssertEqual(try question(.multiple, partial: true).score(.multiple(["a", "b"])), 0, "a wrong pick cancels a right one")
        XCTAssertEqual(try question(.multiple, partial: true).score(.multiple(["a"])), 0.5)
        XCTAssertEqual(try question(.ordering, partial: true).score(.ordering(["d", "o", "a", "r"])), 0.5)
        XCTAssertEqual(try question(.ordering, partial: true).score(.ordering(["d", "o", "r", "a", "x"])), 0.8, "extra distractor costs")
        XCTAssertEqual(try question(.matching, partial: true).score(.matching(["ssh": "22", "rdp": "21"])), 1.0 / 3, accuracy: 1e-9)
        XCTAssertEqual(try question(.yesNo, partial: true).score(.dropdowns(["s1": "Yes"])), 0.5)
    }

    func testMatchingDistractorsMustStayUnplaced() throws {
        let q = try decode(examJSON([#"{"id":"m","kind":"matching","prompt":"P","items":[{"id":"a","text":"A"},{"id":"x","text":"X"}],"targets":[{"id":"t","text":"T"}],"answer":{"a":"t"}}"#])).questions[0]
        XCTAssertTrue(q.isCorrect(.matching(["a": "t"])))
        XCTAssertFalse(q.isCorrect(.matching(["a": "t", "x": "t"])))
    }

    func testFillInMatching() throws {
        let blank = Blank(id: "b", label: nil, accept: ["show ip route"], caseSensitive: false, pattern: "sh(o(w)?)? ip ro(u(te?)?)?")
        XCTAssertTrue(blank.accepts("SHOW IP ROUTE"))
        XCTAssertTrue(blank.accepts("sh ip ro"))
        XCTAssertFalse(blank.accepts("show ip"))
        XCTAssertFalse(blank.accepts("   "))
        let strict = Blank(id: "b", label: nil, accept: ["Admin"], caseSensitive: true, pattern: nil)
        XCTAssertFalse(strict.accepts("admin"))
        XCTAssertTrue(strict.accepts(" Admin "))
    }

    func testScaledScoreAndPoints() throws {
        let exam = try decode(examJSON([choiceQ("q1", extra: #","points":3"#), choiceQ("q2")], extra: #","scale":{"min":100,"max":900,"pass":700}"#))
        var s = Session(exam: exam, options: SessionOptions(shuffleQuestions: false, seed: 1))
        s.jump(to: s.questions.firstIndex { $0.id == "q1" }!)
        s.choose("a")
        let r = s.grade()
        XCTAssertEqual(r.earned, 3)
        XCTAssertEqual(r.possible, 4)
        XCTAssertEqual(r.percent, 75)
        XCTAssertEqual(r.scaledScore, 700)
        XCTAssertTrue(r.passed)
    }
}
