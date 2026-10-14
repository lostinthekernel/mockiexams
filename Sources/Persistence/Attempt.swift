import Foundation
import MockiCore
import SwiftData

@Model
final class Attempt {
    var examID: String
    var examTitle: String
    var date: Date
    var duration: TimeInterval
    var mode: String
    var percent: Int
    var scaledScore: Int?
    var passed: Bool
    var questionCount: Int
    var missedQuestionIDs: [String]
    var topicData: Data

    init(result: SessionResult) {
        examID = result.exam.id
        examTitle = result.exam.title
        date = result.finishedAt
        duration = result.duration
        mode = result.mode.rawValue
        percent = result.percent
        scaledScore = result.scaledScore
        passed = result.passed
        questionCount = result.items.count
        missedQuestionIDs = result.missedIDs
        topicData = (try? JSONEncoder().encode(result.topics)) ?? Data()
    }

    /// Reads both the current shape and v1's `{topic, correct, total}`.
    var topics: [TopicScore] {
        if let current = try? JSONDecoder().decode([TopicScore].self, from: topicData) { return current }
        struct Legacy: Decodable { let topic: String; let correct: Int; let total: Int }
        let legacy = (try? JSONDecoder().decode([Legacy].self, from: topicData)) ?? []
        return legacy.map { TopicScore(topic: $0.topic, earned: Double($0.correct), possible: Double($0.total)) }
    }

    var sessionMode: SessionMode { SessionMode(rawValue: mode) ?? .practice }
}

extension Sequence<Attempt> {
    /// Topic accuracy pooled over every attempt, weakest first.
    func weakTopics() -> [TopicScore] {
        var pooled: [String: TopicScore] = [:]
        for score in flatMap(\.topics) {
            pooled[score.topic, default: TopicScore(topic: score.topic)].earned += score.earned
            pooled[score.topic, default: TopicScore(topic: score.topic)].possible += score.possible
        }
        return pooled.values.sorted { ($0.fraction, $0.topic) < ($1.fraction, $1.topic) }
    }
}
