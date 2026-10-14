import Foundation

/// The on-disk JSON shape, field for field. Everything is optional here so the validator can
/// report every problem at once; `ExamFormat` turns a document into a checked `Exam`.
public struct ExamDocument: Codable, Sendable, Equatable {
    public var formatVersion: Int?
    public var id: String?
    public var title: String?
    public var description: String?
    public var passPercent: Int?
    public var scale: ScoreScale?
    public var timeLimitMinutes: Double?
    public var allowBack: Bool?
    public var partialCredit: Bool?
    /// Target share of the exam per topic, in percent (the exam blueprint).
    public var topicWeights: [String: Double]?
    public var caseStudies: [CaseStudyDocument]?
    /// Assembles the exam from the bank; questions in no section are not asked.
    public var sections: [SectionDocument]?
    public var questions: [QuestionDocument]?

    public init(id: String, title: String, questions: [QuestionDocument]) {
        self.formatVersion = ExamFormat.currentVersion
        self.id = id
        self.title = title
        self.questions = questions
    }
}

public struct SectionDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var title: String?
    public var questions: [String]?
    public var shuffle: Bool?

    public init(id: String, title: String, questions: [String] = []) {
        self.id = id
        self.title = title
        self.questions = questions
    }
}

public struct CaseStudyDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var title: String?
    public var scenario: String?
    public var exhibits: [ExhibitDocument]?
    public init(id: String, title: String? = nil, scenario: String = "", exhibits: [ExhibitDocument]? = nil) {
        self.id = id; self.title = title; self.scenario = scenario; self.exhibits = exhibits
    }
}

public struct ExhibitDocument: Codable, Sendable, Equatable {
    public var title: String?
    public var text: String?
    public var image: String?
    public init(title: String? = nil, text: String? = nil, image: String? = nil) {
        self.title = title; self.text = text; self.image = image
    }
}

public struct QuestionDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var kind: String?
    public var topic: String?
    public var prompt: String?
    public var exhibits: [ExhibitDocument]?
    public var caseStudy: String?
    public var points: Double?
    public var partialCredit: Bool?
    public var choices: [ChoiceDocument]?
    public var items: [ChoiceDocument]?
    public var targets: [ChoiceDocument]?
    public var rows: [RowDocument]?
    public var statements: [StatementDocument]?
    public var blanks: [BlankDocument]?
    public var image: String?
    public var regions: [RegionDocument]?
    public var answer: AnswerValue?
    public var explanation: String?

    public init(id: String, kind: QuestionKind, prompt: String) {
        self.id = id
        self.kind = kind.rawValue
        self.prompt = prompt
    }
}

public struct ChoiceDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var text: String?
    public init(id: String, text: String) { self.id = id; self.text = text }
}

public struct RowDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var label: String?
    public var options: [String]?
    public var answer: String?
    public init(id: String, label: String, options: [String], answer: String?) {
        self.id = id; self.label = label; self.options = options; self.answer = answer
    }
}

public struct StatementDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var text: String?
    public var answer: Bool?
    public init(id: String, text: String, answer: Bool) {
        self.id = id; self.text = text; self.answer = answer
    }
}

public struct BlankDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var label: String?
    public var accept: [String]?
    public var caseSensitive: Bool?
    public var pattern: String?
    public init(id: String, accept: [String]) { self.id = id; self.accept = accept }
}

public struct RegionDocument: Codable, Sendable, Equatable {
    public var id: String?
    public var label: String?
    public var x: Double?
    public var y: Double?
    public var width: Double?
    public var height: Double?
    public init(id: String, label: String? = nil, x: Double, y: Double, width: Double, height: Double) {
        self.id = id; self.label = label; self.x = x; self.y = y; self.width = width; self.height = height
    }
}

/// `answer` is a string, a list of strings, a boolean, or an object, depending on the kind.
public enum AnswerValue: Codable, Sendable, Equatable {
    case string(String)
    case strings([String])
    case bool(Bool)
    case map([String: String])

    public init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if let v = try? c.decode(String.self) { self = .string(v) }
        else if let v = try? c.decode([String].self) { self = .strings(v) }
        else if let v = try? c.decode(Bool.self) { self = .bool(v) }
        else if let v = try? c.decode([String: String].self) { self = .map(v) }
        else {
            throw DecodingError.typeMismatch(AnswerValue.self, .init(
                codingPath: decoder.codingPath,
                debugDescription: "answer must be a string, an array of strings, a boolean, or an object of strings"))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.singleValueContainer()
        switch self {
        case let .string(v): try c.encode(v)
        case let .strings(v): try c.encode(v)
        case let .bool(v): try c.encode(v)
        case let .map(v): try c.encode(v)
        }
    }

    var asList: [String]? {
        switch self {
        case let .string(s): [s]
        case let .strings(l): l
        default: nil
        }
    }
}

/// Keys allowed at each level; anything else is reported as a likely typo.
enum KnownKeys {
    static let exam: Set = ["formatVersion", "id", "title", "description", "passPercent", "scale", "timeLimitMinutes", "allowBack", "partialCredit", "topicWeights", "caseStudies", "sections", "questions", "$schema"]
    static let section: Set = ["id", "title", "questions", "shuffle"]
    static let caseStudy: Set = ["id", "title", "scenario", "exhibits"]
    static let exhibit: Set = ["title", "text", "image"]
    static let question: Set = ["id", "kind", "topic", "prompt", "exhibits", "caseStudy", "points", "partialCredit", "choices", "items", "targets", "rows", "statements", "blanks", "image", "regions", "answer", "explanation"]
    static let choice: Set = ["id", "text"]
    static let row: Set = ["id", "label", "options", "answer"]
    static let statement: Set = ["id", "text", "answer"]
    static let blank: Set = ["id", "label", "accept", "caseSensitive", "pattern"]
    static let region: Set = ["id", "label", "x", "y", "width", "height"]
    static let scale: Set = ["min", "max", "pass"]
}
