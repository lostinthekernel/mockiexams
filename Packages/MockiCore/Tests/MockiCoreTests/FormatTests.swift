import Foundation
import XCTest
@testable import MockiCore

final class FormatTests: XCTestCase {
    func testSamplesValidate() throws {
        let v1 = try decode(Data(contentsOf: Repo.url("samples/sample.exam.json")))
        XCTAssertEqual(v1.questions.count, 6)
        XCTAssertEqual(v1.timeLimit, 600)

        let base = Repo.url("samples/networking-sim")
        let (exam, issues) = ExamFormat.validate(try Data(contentsOf: base.appending(path: "exam.json")), baseURL: base)
        XCTAssertEqual(issues, [])
        let sim = try XCTUnwrap(exam)
        XCTAssertEqual(Set(sim.questions.map(\.kind)), Set(QuestionKind.allCases))
        XCTAssertEqual(sim.assetPaths.sorted(), ["images/server-rear.png", "images/switch-panel.png", "images/terminal.png", "images/topology.png"])
        XCTAssertEqual(sim.scale, ScoreScale(min: 100, max: 900, pass: 720))
    }

    func testEveryTemplateIsValid() throws {
        let exam = try decode(Data(Templates.exam.utf8))
        XCTAssertEqual(exam.questions.count, QuestionKind.allCases.count - 1)
        for kind in QuestionKind.allCases where kind != .hotspot {
            _ = try decode(examJSON([Templates.question(kind)]))
        }
        // The hotspot template references an image, so check shape without a base folder.
        _ = try decode(examJSON([Templates.question(.hotspot)]))
    }

    func testSchemaKindsMatchModel() throws {
        let schema = try XCTUnwrap(JSONSerialization.jsonObject(with: Data(Templates.schema.utf8)) as? [String: Any])
        let defs = try XCTUnwrap(schema["$defs"] as? [String: Any])
        let question = try XCTUnwrap(defs["question"] as? [String: Any])
        let kind = try XCTUnwrap((question["properties"] as? [String: Any])?["kind"] as? [String: Any])
        XCTAssertEqual(Set(kind["enum"] as? [String] ?? []), Set(QuestionKind.allCases.map(\.rawValue)))
        let questionKeys = Set(((question["properties"] as? [String: Any]) ?? [:]).keys)
        XCTAssertEqual(questionKeys, KnownKeys.question)
        XCTAssertEqual(Set(((schema["properties"] as? [String: Any]) ?? [:]).keys), KnownKeys.exam)
    }

    func testDefaultsAndAnswerForms() throws {
        let exam = try decode(examJSON([choiceQ(answer: #"["a"]"#), choiceQ("q2", answer: #""b""#)]))
        XCTAssertEqual(exam.passPercent, 70)
        XCTAssertNil(exam.timeLimit)
        XCTAssertTrue(exam.allowBack)
        XCTAssertEqual(exam.questions[0].topic, "General")
        XCTAssertEqual(exam.questions[0].points, 1)
        XCTAssertFalse(exam.questions[0].partialCredit)
    }

    func testReportsAllErrorsWithPaths() {
        let data = examJSON([
            choiceQ("q1", answer: #""z""#),
            choiceQ("q1", kind: "multiple", answer: "[]"),
            #"{"id":"q3","kind":"essay","prompt":"P"}"#,
        ], extra: #","passPercent":150"#)
        let (_, issues) = ExamFormat.validate(data)
        let paths = Set(issues.filter { $0.severity == .error }.map(\.path))
        XCTAssertEqual(paths, ["passPercent", "questions[0].answer", "questions[1].id", "questions[1].answer", "questions[2].kind"])
    }

    func testRejectsMalformedQuestions() {
        assertIssue(Data("not json".utf8), "$", containing: "Not valid JSON")
        assertIssue(examJSON([]), "questions", containing: "no questions")
        assertIssue(examJSON([choiceQ(answer: #"["a","b"]"#)]), "questions[0].answer", containing: "exactly one")
        assertIssue(examJSON([choiceQ(kind: "ordering", answer: #"["a"]"#)]), "questions[0].answer", containing: "two or more")
        assertIssue(examJSON([#"{"id":"q","kind":"single","prompt":"P","choices":[{"id":"a","text":"A"}],"answer":"a"}"#]), "questions[0].choices", containing: "at least 2")
        assertIssue(examJSON([#"{"id":"q","kind":"trueFalse","prompt":"P","answer":"maybe"}"#]), "questions[0].answer", containing: "true or false")
        assertIssue(examJSON([#"{"id":"q","kind":"yesNo","prompt":"P","statements":[{"id":"s","text":"T"}]}"#]), "questions[0].statements[0].answer", containing: "Yes")
        assertIssue(examJSON([#"{"id":"q","kind":"matching","prompt":"P","items":[{"id":"i","text":"I"}],"targets":[{"id":"t","text":"T"}],"answer":{"i":"nope"}}"#]), "questions[0].answer", containing: "Unknown target")
        assertIssue(examJSON([#"{"id":"q","kind":"dropdowns","prompt":"P","rows":[{"id":"r","label":"L","options":["x","y"],"answer":"z"}]}"#]), "questions[0].rows[0].answer", containing: "one of")
        assertIssue(examJSON([#"{"id":"q","kind":"fillIn","prompt":"P","blanks":[{"id":"b","pattern":"("}]}"#]), "questions[0].blanks[0].pattern", containing: "regular expression")
        assertIssue(examJSON([#"{"id":"q","kind":"fillIn","prompt":"P","blanks":[{"id":"b"}]}"#]), "questions[0].blanks[0]", containing: "accept")
        assertIssue(examJSON([#"{"id":"q","kind":"hotspot","prompt":"P","image":"../x.png","regions":[{"id":"r","x":0,"y":0,"width":0.5,"height":0.5}],"answer":["r"]}"#]), "questions[0].image", containing: "relative path")
        assertIssue(examJSON([#"{"id":"q","kind":"hotspot","prompt":"P","image":"x.png","regions":[{"id":"r","x":0.8,"y":0,"width":0.5,"height":0.5}],"answer":["r"]}"#]), "questions[0].regions[0]", containing: "inside the image")
        assertIssue(examJSON([choiceQ(extra: #","caseStudy":"nope""#)]), "questions[0].caseStudy", containing: "No case study")
        assertIssue(examJSON([choiceQ(extra: #","exhibits":[{"title":"x"}]"#)]), "questions[0].exhibits[0]", containing: "exactly one")
        assertIssue(examJSON([choiceQ()], extra: #","formatVersion":2"#), "formatVersion", containing: "format 2")
    }

    func testMissingImageFileIsAnErrorOnlyWithABaseFolder() {
        let q = #"{"id":"q","kind":"single","prompt":"P","exhibits":[{"image":"images/none.png"}],"choices":[{"id":"a","text":"A"},{"id":"b","text":"B"}],"answer":"a"}"#
        XCTAssertNotNil(ExamFormat.validate(examJSON([q])).exam)
        let (exam, issues) = ExamFormat.validate(examJSON([q]), baseURL: tempDir())
        XCTAssertNil(exam)
        XCTAssertTrue(issues.contains { $0.message.contains("File not found") })
    }

    func testUnknownKeysAndDuplicatePromptsAreWarnings() throws {
        let data = examJSON([choiceQ("q1", extra: #","answers":["a"]"#), choiceQ("q2").replacingOccurrences(of: "P q2", with: "P q1")], extra: #","titel":"x""#)
        let (exam, issues) = ExamFormat.validate(data)
        XCTAssertNotNil(exam)
        XCTAssertEqual(Set(issues.map(\.path)), ["titel", "questions[0].answers", "questions[1].prompt"])
        XCTAssertTrue(issues.allSatisfy { $0.severity == .warning })
    }

    func testSections() throws {
        let qs = [choiceQ("q1"), choiceQ("q2"), choiceQ("q3")]
        let exam = try decode(examJSON(qs, extra: #","sections":[{"id":"s2","title":"Two","questions":["q3"]},{"id":"s1","questions":["q1"]}]"#))
        XCTAssertEqual(exam.examQuestions.map(\.id), ["q3", "q1"])
        XCTAssertEqual(exam.questions.count, 3, "bank keeps unplaced questions")
        XCTAssertEqual(exam.sections.map(\.title), ["Two", "s1"])
        let (_, issues) = ExamFormat.validate(examJSON(qs, extra: #","sections":[{"id":"s","questions":["q1"]}]"#))
        XCTAssertTrue(issues.contains { $0.severity == .warning && $0.message.contains("won't be asked") })

        assertIssue(examJSON(qs, extra: #","sections":[{"id":"s","questions":["q1","zz"]}]"#), "sections[0].questions[1]", containing: "No question")
        assertIssue(examJSON(qs, extra: #","sections":[{"id":"a","questions":["q1"]},{"id":"b","questions":["q1"]}]"#), "sections[1].questions[0]", containing: "already in section")
        assertIssue(examJSON(qs, extra: #","sections":[{"id":"a","questions":[]}]"#), "sections[0].questions", containing: "no questions")
    }

    func testEncodeRoundTrips() throws {
        let base = Repo.url("samples/networking-sim")
        let original = try JSONDecoder().decode(ExamDocument.self, from: Data(contentsOf: base.appending(path: "exam.json")))
        let again = try ExamFormat.decode(ExamFormat.encode(original), baseURL: base)
        let first = try ExamFormat.decode(Data(contentsOf: base.appending(path: "exam.json")), baseURL: base)
        XCTAssertEqual(again, first)
    }
}

final class BlueprintTests: XCTestCase {
    func testTopicWeights() throws {
        let qs = [choiceQ("q1", extra: #","topic":"A""#), choiceQ("q2", extra: #","topic":"B","points":3"#)]
        let exam = try decode(examJSON(qs, extra: #","topicWeights":{"A":40,"B":60}"#))
        XCTAssertEqual(exam.weight(of: "A"), 40)
        let plain = try decode(examJSON(qs))
        XCTAssertEqual(plain.weight(of: "B"), 75, "without a blueprint, weight is the share of points")

        let (_, issues) = ExamFormat.validate(examJSON(qs, extra: #","topicWeights":{"A":40,"C":10}"#))
        XCTAssertTrue(issues.contains { $0.path == "topicWeights" && $0.message.contains("not 100") })
        XCTAssertTrue(issues.contains { $0.path == "topicWeights.C" && $0.severity == .warning })
        assertIssue(examJSON(qs, extra: #","topicWeights":{"A":140}"#), "topicWeights.A", containing: "0–100")
    }
}
