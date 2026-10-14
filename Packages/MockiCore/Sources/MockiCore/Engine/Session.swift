import Foundation

public enum SessionMode: String, CaseIterable, Identifiable, Sendable {
    case practice, exam
    public var id: Self { self }
    public var label: String { self == .practice ? "Practice" : "Exam simulation" }
}

public struct SessionOptions: Hashable, Sendable {
    public var mode: SessionMode
    public var shuffleQuestions: Bool
    public var shuffleChoices: Bool
    /// Only these questions (e.g. "retry missed"); nil means all. Stale ids fall back to all.
    public var questionIDs: Set<String>?
    /// Only these topics; nil means all.
    public var topics: Set<String>?
    /// Draw this many questions at random from the pool (case studies stay whole).
    public var questionLimit: Int?
    public var seed: UInt64

    public init(mode: SessionMode = .practice, shuffleQuestions: Bool = true, shuffleChoices: Bool = true,
                questionIDs: Set<String>? = nil, topics: Set<String>? = nil, questionLimit: Int? = nil,
                seed: UInt64 = .random(in: .min ... .max)) {
        self.mode = mode
        self.shuffleQuestions = shuffleQuestions
        self.shuffleChoices = shuffleChoices
        self.questionIDs = questionIDs
        self.topics = topics
        self.questionLimit = questionLimit
        self.seed = seed
    }
}

public struct Session: Sendable {
    public let exam: Exam
    public let options: SessionOptions
    /// Questions in display order, each with choices in display order.
    public let questions: [Question]
    public let startedAt: Date

    public private(set) var responses: [String: Response] = [:]
    public private(set) var flagged: Set<String> = []
    /// Practice mode: questions whose answer has been checked and locked.
    public private(set) var revealed: Set<String> = []
    public private(set) var current = 0

    public init(exam: Exam, options: SessionOptions, now: Date = .now) {
        var rng = SplitMix64(seed: options.seed)
        let asked = exam.examQuestions
        var pool = asked.filter {
            (options.questionIDs?.contains($0.id) ?? true) && (options.topics?.contains($0.topic) ?? true)
        }
        if pool.isEmpty { pool = asked }

        // Units are what moves when shuffling: a case study's questions stay together, in order,
        // and nothing leaves its section.
        struct Unit { var section: Int; var shuffles: Bool; var questions: [Question] }
        let sectionIndex = Dictionary(exam.sections.enumerated().flatMap { i, s in s.questionIDs.map { ($0, i) } }, uniquingKeysWith: { a, _ in a })
        var units: [Unit] = []
        var unitOfCase: [String: Int] = [:]
        for q in pool {
            let s = sectionIndex[q.id] ?? 0
            let key = q.caseStudyID.map { "\(s)/\($0)" }
            if let key, let i = unitOfCase[key] { units[i].questions.append(q); continue }
            if let key { unitOfCase[key] = units.count }
            units.append(Unit(section: s, shuffles: exam.sections.isEmpty || exam.sections[s].shuffle, questions: [q]))
        }

        let fileOrder = Dictionary(uniqueKeysWithValues: asked.enumerated().map { ($1.id, $0) })
        if let limit = options.questionLimit, limit > 0, limit < pool.count {
            units.shuffle(using: &rng)
            var taken: [Unit] = [], count = 0
            for u in units where count < limit {
                taken.append(u)
                count += u.questions.count
            }
            units = taken.sorted { fileOrder[$0.questions[0].id]! < fileOrder[$1.questions[0].id]! }
        }
        if options.shuffleQuestions {
            // Shuffle within each section, leaving non-shuffling sections in file order.
            var shuffled: [Unit] = []
            for s in Set(units.map(\.section)).sorted() {
                var part = units.filter { $0.section == s }
                if part.first?.shuffles == true { part.shuffle(using: &rng) }
                shuffled += part
            }
            units = shuffled
        } else {
            units.sort { ($0.section, fileOrder[$0.questions[0].id]!) < ($1.section, fileOrder[$1.questions[0].id]!) }
        }
        self.questions = units.flatMap(\.questions).map { Self.arrange($0, shuffle: options.shuffleChoices, rng: &rng) }
        self.exam = exam
        self.options = options
        self.startedAt = now
    }

    private static func arrange(_ q: Question, shuffle: Bool, rng: inout SplitMix64) -> Question {
        var q = q
        switch q.body {
        case let .single(c, a) where shuffle && q.kind != .trueFalse:
            q.body = .single(choices: c.shuffled(using: &rng), answer: a)
        case let .multiple(c, a) where shuffle:
            q.body = .multiple(choices: c.shuffled(using: &rng), answer: a)
        case let .ordering(c, a):
            // The pool is always shuffled; an unshuffled pool would give the order away.
            q.body = .ordering(choices: c.shuffled(using: &rng), answer: a)
        case let .matching(items, targets, a) where shuffle:
            q.body = .matching(items: items.shuffled(using: &rng), targets: targets, answer: a)
        default:
            break
        }
        return q
    }

    // MARK: State

    public var currentQuestion: Question { questions[current] }

    /// Title of the section starting at `index`, if a new section begins there.
    public func sectionStart(at index: Int) -> String? {
        guard !exam.sections.isEmpty, questions.indices.contains(index) else { return nil }
        let s = exam.section(of: questions[index].id)
        if index > 0, exam.section(of: questions[index - 1].id)?.id == s?.id { return nil }
        return s?.title
    }
    public var timeLimit: TimeInterval? { options.mode == .exam ? exam.timeLimit : nil }
    public var canGoBack: Bool { options.mode == .practice || exam.allowBack }

    public func remaining(at now: Date) -> TimeInterval? {
        timeLimit.map { max(0, $0 - now.timeIntervalSince(startedAt)) }
    }

    public func isExpired(at now: Date) -> Bool { remaining(at: now) == 0 }
    public func response(for q: Question) -> Response? { responses[q.id] }
    public func isLocked(_ q: Question) -> Bool { revealed.contains(q.id) }
    public var unansweredCount: Int { questions.count(where: { responses[$0.id] == nil }) }

    // MARK: Answering

    /// Stores a response for a question; empty responses clear it. Locked questions ignore this.
    public mutating func respond(_ response: Response?, to q: Question) {
        guard !isLocked(q) else { return }
        responses[q.id] = (response?.isEmpty ?? true) ? nil : response
    }

    /// Picks a choice on the current question: replaces for single, toggles for multiple.
    public mutating func choose(_ choiceID: String) {
        let q = currentQuestion
        switch q.body {
        case let .single(choices, _) where choices.contains { $0.id == choiceID }:
            respond(.single(choiceID), to: q)
        case let .multiple(choices, _) where choices.contains { $0.id == choiceID }:
            var set: Set<String> = if case let .multiple(s)? = responses[q.id] { s } else { [] }
            if set.remove(choiceID) == nil { set.insert(choiceID) }
            respond(.multiple(set), to: q)
        default:
            break
        }
    }

    public mutating func toggleRegion(_ regionID: String) {
        let q = currentQuestion
        guard case let .hotspot(_, _, answer) = q.body else { return }
        var set: Set<String> = if case let .hotspot(s)? = responses[q.id] { s } else { [] }
        if set.remove(regionID) == nil {
            // Single-target hotspots behave like radio buttons.
            if answer.count == 1 { set = [] }
            set.insert(regionID)
        }
        respond(.hotspot(set), to: q)
    }

    public mutating func toggleFlag() {
        let id = currentQuestion.id
        if flagged.remove(id) == nil { flagged.insert(id) }
    }

    public mutating func reveal() {
        guard options.mode == .practice else { return }
        revealed.insert(currentQuestion.id)
    }

    // MARK: Navigation

    public func canJump(to index: Int) -> Bool {
        questions.indices.contains(index) && (canGoBack || index >= current)
    }

    public mutating func jump(to index: Int) { if canJump(to: index) { current = index } }
    public mutating func next() { jump(to: min(current + 1, questions.count - 1)) }
    public mutating func previous() { jump(to: max(current - 1, 0)) }

    // MARK: Grading

    public func grade(at now: Date = .now) -> SessionResult {
        let items = questions.map { q in
            let r = responses[q.id]
            return ResultItem(question: q, response: r, score: q.score(r), flagged: flagged.contains(q.id))
        }
        var byTopic: [String: TopicScore] = [:]
        for item in items {
            byTopic[item.question.topic, default: TopicScore(topic: item.question.topic)].add(item)
        }
        let order = exam.topics
        return SessionResult(
            exam: exam, mode: options.mode, startedAt: startedAt, finishedAt: now, items: items,
            topics: byTopic.values.sorted { (order.firstIndex(of: $0.topic) ?? .max) < (order.firstIndex(of: $1.topic) ?? .max) })
    }
}

public struct ResultItem: Identifiable, Sendable {
    public let question: Question
    public let response: Response?
    /// 0–1 share of the question's points earned.
    public let score: Double
    public let flagged: Bool
    public var id: String { question.id }
    public var isCorrect: Bool { score >= 1 }
    public var earned: Double { score * question.points }
}

public struct TopicScore: Identifiable, Codable, Hashable, Sendable {
    public let topic: String
    public var earned: Double
    public var possible: Double
    public var id: String { topic }
    public var fraction: Double { possible == 0 ? 0 : earned / possible }

    public init(topic: String, earned: Double = 0, possible: Double = 0) {
        self.topic = topic
        self.earned = earned
        self.possible = possible
    }

    mutating func add(_ item: ResultItem) {
        earned += item.earned
        possible += item.question.points
    }
}

public struct SessionResult: Sendable {
    public let exam: Exam
    public let mode: SessionMode
    public let startedAt: Date
    public let finishedAt: Date
    public let items: [ResultItem]
    public let topics: [TopicScore]

    public var earned: Double { items.reduce(0) { $0 + $1.earned } }
    public var possible: Double { items.reduce(0) { $0 + $1.question.points } }
    public var fraction: Double { possible == 0 ? 0 : earned / possible }
    public var percent: Int { Int((fraction * 100).rounded()) }
    public var correctCount: Int { items.count(where: \.isCorrect) }
    public var scaledScore: Int? { exam.scale?.scaled(fraction) }
    public var passed: Bool {
        if let scale = exam.scale, let s = scaledScore { return s >= scale.pass }
        return percent >= exam.passPercent
    }
    public var duration: TimeInterval { finishedAt.timeIntervalSince(startedAt) }
    public var missedIDs: [String] { items.filter { !$0.isCorrect }.map(\.id) }
}

/// Deterministic RNG so a seed reproduces a run's ordering.
public struct SplitMix64: RandomNumberGenerator, Sendable {
    private var state: UInt64
    public init(seed: UInt64) { state = seed }
    public mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}
