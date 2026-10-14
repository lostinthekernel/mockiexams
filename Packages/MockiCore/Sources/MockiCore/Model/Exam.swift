import Foundation

public struct Exam: Identifiable, Hashable, Sendable {
    public let id: String
    public let title: String
    public let summary: String?
    public let passPercent: Int
    public let scale: ScoreScale?
    public let timeLimit: TimeInterval?
    /// Exam mode only: false means no going back to earlier questions.
    public let allowBack: Bool
    /// Blueprint target per topic, in percent. Empty when the exam doesn't define one.
    public let topicWeights: [String: Double]
    public let caseStudies: [CaseStudy]
    public let sections: [ExamSection]
    /// The whole question bank, in file order.
    public let questions: [Question]
    /// Folder that relative asset paths resolve against.
    public var assetBase: URL?

    /// The questions the exam asks: section order when there are sections, otherwise the bank.
    public var examQuestions: [Question] {
        guard !sections.isEmpty else { return questions }
        let byID = Dictionary(uniqueKeysWithValues: questions.map { ($0.id, $0) })
        return sections.flatMap { $0.questionIDs.compactMap { byID[$0] } }
    }

    public func section(of questionID: String) -> ExamSection? {
        sections.first { $0.questionIDs.contains(questionID) }
    }

    public var topics: [String] {
        var seen = Set<String>()
        return examQuestions.map(\.topic).filter { seen.insert($0).inserted }
    }

    public var totalPoints: Double { examQuestions.reduce(0) { $0 + $1.points } }

    /// The topic's blueprint weight, or its share of the exam's points when no blueprint is set.
    public func weight(of topic: String) -> Double {
        if let w = topicWeights[topic] { return w }
        let total = totalPoints
        guard total > 0 else { return 0 }
        return examQuestions.filter { $0.topic == topic }.reduce(0) { $0 + $1.points } / total * 100
    }

    public func caseStudy(_ id: String?) -> CaseStudy? {
        id.flatMap { id in caseStudies.first { $0.id == id } }
    }

    public func assetURL(_ path: String) -> URL? { assetBase?.appending(path: path) }

    /// Every relative file path the exam references, for copying and validation.
    public var assetPaths: [String] {
        let exhibits = (caseStudies.flatMap(\.exhibits) + questions.flatMap(\.exhibits)).compactMap(\.imagePath)
        let hotspots = questions.compactMap { q -> String? in
            if case let .hotspot(image, _, _) = q.body { image } else { nil }
        }
        var seen = Set<String>()
        return (exhibits + hotspots).filter { seen.insert($0).inserted }
    }
}

/// Linear map from percent to a vendor-style scaled score. Real vendors don't scale linearly.
public struct ScoreScale: Hashable, Sendable, Codable {
    public let min: Int
    public let max: Int
    public let pass: Int

    public init(min: Int, max: Int, pass: Int) {
        self.min = min
        self.max = max
        self.pass = pass
    }

    public func scaled(_ fraction: Double) -> Int {
        min + Int((Double(max - min) * fraction).rounded())
    }
}

public struct ExamSection: Identifiable, Hashable, Sendable {
    public let id: String
    public let title: String
    public let questionIDs: [String]
    /// Whether questions may be shuffled within the section (when the run shuffles).
    public let shuffle: Bool
}

public struct CaseStudy: Identifiable, Hashable, Sendable {
    public let id: String
    public let title: String
    public let scenario: String
    public let exhibits: [Exhibit]
}

public struct Exhibit: Hashable, Sendable {
    public enum Content: Hashable, Sendable {
        case text(String)
        case image(String)
    }

    public let title: String
    public let content: Content

    public var imagePath: String? { if case let .image(p) = content { p } else { nil } }
}

public enum QuestionKind: String, CaseIterable, Codable, Sendable {
    case single, multiple, trueFalse, yesNo, ordering, matching, dropdowns, fillIn, hotspot

    public var label: String {
        switch self {
        case .single: "Single choice"
        case .multiple: "Multiple choice"
        case .trueFalse: "True / false"
        case .yesNo: "Yes / no series"
        case .ordering: "Ordering"
        case .matching: "Matching"
        case .dropdowns: "Dropdowns"
        case .fillIn: "Fill in"
        case .hotspot: "Hotspot"
        }
    }
}

public struct Question: Identifiable, Hashable, Sendable {
    public let id: String
    /// The kind as written in the file; `trueFalse` and `yesNo` are stored as other bodies.
    public let kind: QuestionKind
    public let prompt: String
    public let topic: String
    public let explanation: String?
    public let exhibits: [Exhibit]
    public let caseStudyID: String?
    public let points: Double
    public let partialCredit: Bool
    public internal(set) var body: QuestionBody
}

public enum QuestionBody: Hashable, Sendable {
    case single(choices: [Choice], answer: String)
    case multiple(choices: [Choice], answer: Set<String>)
    /// `answer` may be a subset of `choices`; the rest are distractors.
    case ordering(choices: [Choice], answer: [String])
    /// Items missing from `answer` are distractors and should stay unplaced.
    case matching(items: [Choice], targets: [Choice], answer: [String: String])
    case dropdowns(rows: [DropdownRow])
    case fillIn(blanks: [Blank])
    case hotspot(image: String, regions: [Region], answer: Set<String>)
}

public struct Choice: Identifiable, Hashable, Sendable {
    public let id: String
    public let text: String
}

public struct DropdownRow: Identifiable, Hashable, Sendable {
    public let id: String
    public let label: String
    public let options: [String]
    public let answer: String
}

public struct Blank: Identifiable, Hashable, Sendable {
    public let id: String
    public let label: String?
    public let accept: [String]
    public let caseSensitive: Bool
    /// Regular expression the whole trimmed answer must match, as an alternative to `accept`.
    public let pattern: String?
}

/// A clickable rectangle on a hotspot image, in fractions (0–1) of the image size.
public struct Region: Identifiable, Hashable, Sendable {
    public let id: String
    public let label: String?
    public let x: Double
    public let y: Double
    public let width: Double
    public let height: Double

    public func contains(x px: Double, y py: Double) -> Bool {
        px >= x && px <= x + width && py >= y && py <= y + height
    }
}

/// What the user entered; one case per `QuestionBody` case.
public enum Response: Hashable, Sendable {
    case single(String)
    case multiple(Set<String>)
    case ordering([String])
    case matching([String: String])
    case dropdowns([String: String])
    case fillIn([String: String])
    case hotspot(Set<String>)

    public var isEmpty: Bool {
        switch self {
        case let .single(s): s.isEmpty
        case let .multiple(s), let .hotspot(s): s.isEmpty
        case let .ordering(o): o.isEmpty
        case let .matching(m), let .dropdowns(m): m.isEmpty
        case let .fillIn(m): m.values.allSatisfy { $0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        }
    }
}
