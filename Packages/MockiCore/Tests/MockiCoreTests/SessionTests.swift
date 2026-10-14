import Foundation
import XCTest
@testable import MockiCore

final class SessionTests: XCTestCase {
    private let start = Date(timeIntervalSince1970: 0)

    private func exam(extra: String = "", questions: [String]? = nil) throws -> Exam {
        try decode(examJSON(questions ?? [
            choiceQ("s", answer: #""b""#, extra: #","topic":"One""#),
            choiceQ("m", kind: "multiple", answer: #"["a","c"]"#, extra: #","topic":"One""#),
            #"{"id":"o","kind":"ordering","topic":"Two","prompt":"P","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"},{"id":"d","text":"D"}],"answer":["a","b","c","d"]}"#,
        ], extra: #","passPercent":60,"timeLimitMinutes":2"# + extra))
    }

    private func session(_ e: Exam, _ mode: SessionMode = .exam, shuffle: Bool = false, seed: UInt64 = 1,
                         only: Set<String>? = nil, topics: Set<String>? = nil, limit: Int? = nil) -> Session {
        Session(exam: e, options: .init(mode: mode, shuffleQuestions: shuffle, shuffleChoices: shuffle,
                                        questionIDs: only, topics: topics, questionLimit: limit, seed: seed), now: start)
    }

    private func go(_ s: inout Session, to id: String) { s.jump(to: s.questions.firstIndex { $0.id == id }!) }

    func testGradesAndTopics() throws {
        var s = session(try exam())
        go(&s, to: "s"); s.choose("a"); s.choose("b")
        go(&s, to: "m"); s.choose("a"); s.choose("b"); s.choose("c"); s.choose("b")
        go(&s, to: "o"); s.respond(.ordering(["a", "b", "c", "d"]), to: s.currentQuestion)
        let r = s.grade(at: start.addingTimeInterval(30))
        XCTAssertEqual(r.correctCount, 3)
        XCTAssertEqual(r.percent, 100)
        XCTAssertTrue(r.passed)
        XCTAssertEqual(r.duration, 30)
        XCTAssertEqual(r.topics, [TopicScore(topic: "One", earned: 2, possible: 2), TopicScore(topic: "Two", earned: 1, possible: 1)])
    }

    func testUnansweredAndEmptyResponses() throws {
        var s = session(try exam())
        go(&s, to: "m"); s.choose("a"); s.choose("a")
        XCTAssertNil(s.response(for: s.currentQuestion), "deselecting the last choice clears the response")
        s.respond(.fillIn(["x": "  "]), to: s.currentQuestion)
        XCTAssertNil(s.response(for: s.currentQuestion))
        XCTAssertEqual(s.unansweredCount, 3)
        XCTAssertEqual(Set(s.grade().missedIDs), ["s", "m", "o"])
    }

    func testShuffleIsSeededAndKeepsAnswers() throws {
        let e = try exam()
        let a = session(e, shuffle: true, seed: 42), b = session(e, shuffle: true, seed: 42)
        XCTAssertEqual(a.questions, b.questions)
        let orders = Set((UInt64(0)..<20).map { session(e, shuffle: true, seed: $0).questions.first { $0.id == "s" }!.body })
        XCTAssertGreaterThan(orders.count, 1)
        for seed in UInt64(0)..<20 {
            var s = session(e, shuffle: true, seed: seed)
            go(&s, to: "s"); s.choose("b")
            XCTAssertTrue(s.currentQuestion.isCorrect(s.response(for: s.currentQuestion)))
        }
    }

    func testOrderingPoolAlwaysShuffledButTrueFalseNever() throws {
        let e = try exam(questions: [
            #"{"id":"o","kind":"ordering","prompt":"P","choices":[{"id":"a","text":"A"},{"id":"b","text":"B"},{"id":"c","text":"C"},{"id":"d","text":"D"}],"answer":["a","b","c","d"]}"#,
            #"{"id":"tf","kind":"trueFalse","prompt":"P","answer":true}"#,
        ])
        let pools = Set((UInt64(0)..<10).map { seed -> [String] in
            guard case let .ordering(c, _) = session(e, shuffle: false, seed: seed).questions[0].body else { return [] }
            return c.map(\.id)
        })
        XCTAssertGreaterThan(pools.count, 1)
        for seed in UInt64(0)..<10 {
            guard case let .single(c, _) = session(e, shuffle: true, seed: seed).questions.first(where: { $0.id == "tf" })!.body else { return XCTFail() }
            XCTAssertEqual(c.map(\.id), ["true", "false"])
        }
    }

    func testTimerOnlyInExamMode() throws {
        let e = try exam()
        let s = session(e, .exam)
        XCTAssertEqual(s.remaining(at: start.addingTimeInterval(30)), 90)
        XCTAssertFalse(s.isExpired(at: start.addingTimeInterval(119)))
        XCTAssertTrue(s.isExpired(at: start.addingTimeInterval(121)))
        XCTAssertNil(session(e, .practice).remaining(at: start.addingTimeInterval(1000)))
    }

    func testPracticeRevealLocks() throws {
        let e = try exam()
        var s = session(e, .practice)
        go(&s, to: "s"); s.choose("a"); s.reveal(); s.choose("b")
        XCTAssertEqual(s.response(for: s.currentQuestion), .single("a"))
        var x = session(e, .exam)
        go(&x, to: "s"); x.choose("a"); x.reveal(); x.choose("b")
        XCTAssertEqual(x.response(for: x.currentQuestion), .single("b"))
    }

    func testFilters() throws {
        let e = try exam()
        XCTAssertEqual(session(e, only: ["m"]).questions.map(\.id), ["m"])
        XCTAssertEqual(session(e, only: ["gone"]).questions.count, 3, "stale ids fall back to the full exam")
        XCTAssertEqual(session(e, topics: ["Two"]).questions.map(\.id), ["o"])
        let limited = session(e, seed: 7, limit: 2)
        XCTAssertEqual(limited.questions.count, 2)
        let order = ["s", "m", "o"]
        XCTAssertEqual(limited.questions.map(\.id), limited.questions.map(\.id).sorted { order.firstIndex(of: $0)! < order.firstIndex(of: $1)! },
                       "unshuffled draws keep file order")
    }

    func testNoBackNavigation() throws {
        var s = session(try exam(extra: #","allowBack":false"#))
        s.next(); s.next()
        XCTAssertEqual(s.current, 2)
        s.previous(); s.jump(to: 0)
        XCTAssertEqual(s.current, 2)
        XCTAssertFalse(s.canJump(to: 1))
        var p = session(try exam(extra: #","allowBack":false"#), .practice)
        p.next(); p.previous()
        XCTAssertEqual(p.current, 0, "practice mode can always go back")
    }

    func testCaseStudiesStayTogether() throws {
        let qs = (1...6).map { choiceQ("q\($0)", extra: $0 >= 3 && $0 <= 5 ? #","caseStudy":"cs""# : "") }
        let e = try exam(extra: #","caseStudies":[{"id":"cs","scenario":"S"}]"#, questions: qs)
        for seed in UInt64(0)..<30 {
            let ids = session(e, shuffle: true, seed: seed).questions.map(\.id)
            let i = ids.firstIndex(of: "q3")!
            XCTAssertEqual(Array(ids[i...(i + 2)]), ["q3", "q4", "q5"], "seed \(seed): \(ids)")
        }
    }

    func testSectionsOrderAndShuffle() throws {
        let qs = (1...6).map { choiceQ("q\($0)") }
        let e = try exam(extra: #","sections":[{"id":"b","title":"Second","questions":["q4","q5","q6"],"shuffle":false},{"id":"a","title":"First","questions":["q1","q2","q3"]}]"#, questions: qs)
        for seed in UInt64(0)..<20 {
            let s = session(e, shuffle: true, seed: seed)
            let ids = s.questions.map(\.id)
            XCTAssertEqual(Array(ids.prefix(3)), ["q4", "q5", "q6"], "unshuffled section keeps order")
            XCTAssertEqual(Set(ids.suffix(3)), ["q1", "q2", "q3"])
            XCTAssertEqual(s.sectionStart(at: 0), "Second")
            XCTAssertNil(s.sectionStart(at: 1))
            XCTAssertEqual(s.sectionStart(at: 3), "First")
        }
    }

    func testHotspotSingleTargetActsLikeRadio() throws {
        let e = try decode(examJSON([Templates.question(.hotspot)]))
        var s = session(e)
        s.toggleRegion("switch"); s.toggleRegion("router")
        XCTAssertEqual(s.response(for: s.currentQuestion), .hotspot(["router"]))
        s.toggleRegion("router")
        XCTAssertNil(s.response(for: s.currentQuestion))
    }
}
