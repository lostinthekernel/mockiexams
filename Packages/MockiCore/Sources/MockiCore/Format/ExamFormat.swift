import Foundation

public struct Issue: Codable, Hashable, Sendable, CustomStringConvertible {
    public enum Severity: String, Codable, Sendable { case error, warning }
    public let severity: Severity
    /// JSON path such as `questions[3].choices[1].id`.
    public let path: String
    public let message: String

    public var description: String { "\(severity.rawValue): \(path): \(message)" }

    public init(severity: Severity, path: String, message: String) {
        self.severity = severity
        self.path = path
        self.message = message
    }
}

public struct ExamLoadError: LocalizedError, Sendable {
    public let issues: [Issue]
    public var errors: [Issue] { issues.filter { $0.severity == .error } }

    public var errorDescription: String? {
        let shown = errors.prefix(6).map(\.description)
        let more = errors.count > 6 ? ["…and \(errors.count - 6) more"] : []
        return (shown + more).joined(separator: "\n")
    }
}

public enum ExamFormat {
    public static let currentVersion = 1

    /// Parses and checks an exam. Pass `baseURL` (the folder holding the JSON) to check that
    /// referenced images exist; without it, asset paths are only checked for shape.
    public static func validate(_ data: Data, baseURL: URL? = nil) -> (exam: Exam?, issues: [Issue]) {
        var v = Validator(baseURL: baseURL)
        let exam = v.run(data)
        return (v.hasErrors ? nil : exam, v.issues)
    }

    public static func decode(_ data: Data, baseURL: URL? = nil) throws -> Exam {
        let (exam, issues) = validate(data, baseURL: baseURL)
        guard var exam else { throw ExamLoadError(issues: issues) }
        exam.assetBase = baseURL
        return exam
    }

    public static func encode(_ document: ExamDocument) throws -> Data {
        let encoder = JSONEncoder()
        encoder.outputFormatting = [.prettyPrinted, .withoutEscapingSlashes, .sortedKeys]
        return try encoder.encode(document)
    }
}

private struct Validator {
    let baseURL: URL?
    var issues: [Issue] = []
    var hasErrors: Bool { issues.contains { $0.severity == .error } }

    init(baseURL: URL?) { self.baseURL = baseURL }

    mutating func error(_ path: String, _ message: String) { issues.append(Issue(severity: .error, path: path, message: message)) }
    mutating func warn(_ path: String, _ message: String) { issues.append(Issue(severity: .warning, path: path, message: message)) }

    mutating func run(_ data: Data) -> Exam? {
        let doc: ExamDocument
        do {
            doc = try JSONDecoder().decode(ExamDocument.self, from: data)
        } catch let e as DecodingError {
            describe(e)
            return nil
        } catch {
            self.error("$", "Not valid JSON: \(error.localizedDescription)")
            return nil
        }
        if let raw = try? JSONSerialization.jsonObject(with: data) { checkKeys(raw) }
        return build(doc)
    }

    // MARK: Exam

    mutating func build(_ doc: ExamDocument) -> Exam? {
        if let v = doc.formatVersion, v > ExamFormat.currentVersion {
            error("formatVersion", "File uses format \(v); this build reads up to \(ExamFormat.currentVersion).")
        }
        let id = required(doc.id, "id")
        let title = required(doc.title, "title")
        let pass = doc.passPercent ?? 70
        if !(0...100).contains(pass) { error("passPercent", "Must be 0–100, got \(pass).") }
        if let s = doc.scale, !(s.min < s.max && (s.min...s.max).contains(s.pass)) {
            error("scale", "Needs min < max and min ≤ pass ≤ max.")
        }
        if let t = doc.timeLimitMinutes, t <= 0 { error("timeLimitMinutes", "Must be positive.") }
        if let weights = doc.topicWeights {
            for (topic, w) in weights.sorted(by: { $0.key < $1.key }) where !(0...100).contains(w) {
                error("topicWeights.\(topic)", "Must be 0–100, got \(w).")
            }
            let sum = weights.values.reduce(0, +)
            if !weights.isEmpty, abs(sum - 100) > 0.5 { warn("topicWeights", "Weights add up to \(sum.formatted()), not 100.") }
            let used = Set((doc.questions ?? []).map { $0.topic?.trimmingCharacters(in: .whitespaces).nonEmpty ?? "General" })
            for topic in weights.keys.sorted() where !used.contains(topic) {
                warn("topicWeights.\(topic)", "No question has this topic.")
            }
        }

        var caseIDs = Set<String>()
        let caseStudies = (doc.caseStudies ?? []).enumerated().compactMap { i, c -> CaseStudy? in
            let p = "caseStudies[\(i)]"
            guard let cid = required(c.id, "\(p).id") else { return nil }
            if !caseIDs.insert(cid).inserted { error("\(p).id", "Duplicate case study id \"\(cid)\".") }
            return CaseStudy(
                id: cid,
                title: c.title ?? cid,
                scenario: required(c.scenario, "\(p).scenario") ?? "",
                exhibits: exhibits(c.exhibits, "\(p).exhibits"))
        }

        guard let docs = doc.questions, !docs.isEmpty else {
            error("questions", "Exam has no questions.")
            return nil
        }
        var questionIDs = Set<String>()
        var prompts: [String: String] = [:]
        let questions = docs.enumerated().compactMap { i, q -> Question? in
            let p = "questions[\(i)]"
            if let qid = q.id, !questionIDs.insert(qid).inserted { error("\(p).id", "Duplicate question id \"\(qid)\".") }
            if let c = q.caseStudy, !caseIDs.contains(c) { error("\(p).caseStudy", "No case study with id \"\(c)\".") }
            if let text = q.prompt?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty {
                if let first = prompts[text] { warn("\(p).prompt", "Same prompt as \(first); likely a duplicate question.") }
                else { prompts[text] = p }
            }
            return question(q, p, partialDefault: doc.partialCredit ?? false)
        }

        let sections = sections(doc.sections, questionIDs: docs.compactMap(\.id))

        guard let id, let title, !hasErrors else { return nil }
        return Exam(
            id: id, title: title, summary: doc.description, passPercent: pass, scale: doc.scale,
            timeLimit: doc.timeLimitMinutes.map { $0 * 60 }, allowBack: doc.allowBack ?? true,
            topicWeights: doc.topicWeights ?? [:],
            caseStudies: caseStudies, sections: sections, questions: questions, assetBase: nil)
    }

    mutating func sections(_ docs: [SectionDocument]?, questionIDs: [String]) -> [ExamSection] {
        guard let docs, !docs.isEmpty else { return [] }
        let known = Set(questionIDs)
        var sectionIDs = Set<String>(), placed: [String: String] = [:]
        let sections = docs.enumerated().compactMap { i, s -> ExamSection? in
            let p = "sections[\(i)]"
            guard let sid = required(s.id, "\(p).id") else { return nil }
            if !sectionIDs.insert(sid).inserted { error("\(p).id", "Duplicate section id \"\(sid)\".") }
            let ids = s.questions ?? []
            if ids.isEmpty { error("\(p).questions", "Section has no questions.") }
            for (j, q) in ids.enumerated() {
                if !known.contains(q) { error("\(p).questions[\(j)]", "No question with id \"\(q)\".") }
                else if let other = placed[q] { error("\(p).questions[\(j)]", "\"\(q)\" is already in section \"\(other)\".") }
                else { placed[q] = sid }
            }
            return ExamSection(id: sid, title: s.title?.nonEmpty ?? sid, questionIDs: ids, shuffle: s.shuffle ?? true)
        }
        let unplaced = questionIDs.filter { placed[$0] == nil }
        if !unplaced.isEmpty {
            warn("sections", "\(unplaced.count) question(s) are in the bank but no section, so they won't be asked: \(unplaced.prefix(8).joined(separator: ", "))\(unplaced.count > 8 ? ", …" : "").")
        }
        if placed.isEmpty { error("sections", "No section contains a question.") }
        return sections
    }

    // MARK: Questions

    mutating func question(_ q: QuestionDocument, _ p: String, partialDefault: Bool) -> Question? {
        let id = required(q.id, "\(p).id")
        let prompt = required(q.prompt, "\(p).prompt")
        let points = q.points ?? 1
        if points <= 0 { error("\(p).points", "Must be positive.") }
        guard let kindName = required(q.kind, "\(p).kind") else { return nil }
        guard let kind = QuestionKind(rawValue: kindName) else {
            error("\(p).kind", "Unknown kind \"\(kindName)\". Use one of: \(QuestionKind.allCases.map(\.rawValue).joined(separator: ", ")).")
            return nil
        }
        let ex = exhibits(q.exhibits, "\(p).exhibits")
        guard let body = body(kind, q, p), let id, let prompt else { return nil }
        return Question(
            id: id, kind: kind, prompt: prompt,
            topic: q.topic?.trimmingCharacters(in: .whitespaces).nonEmpty ?? "General",
            explanation: q.explanation?.nonEmpty, exhibits: ex, caseStudyID: q.caseStudy,
            points: points, partialCredit: q.partialCredit ?? partialDefault, body: body)
    }

    mutating func body(_ kind: QuestionKind, _ q: QuestionDocument, _ p: String) -> QuestionBody? {
        switch kind {
        case .single, .multiple, .ordering:
            let choices = choiceList(q.choices, "\(p).choices", min: 2)
            guard let list = answerList(q.answer, p, ids: Set(choices.map(\.id))) else { return nil }
            switch kind {
            case .single:
                guard list.count == 1 else { error("\(p).answer", "single needs exactly one choice id."); return nil }
                return .single(choices: choices, answer: list[0])
            case .multiple:
                guard !list.isEmpty, Set(list).count == list.count else {
                    error("\(p).answer", "multiple needs one or more distinct choice ids."); return nil
                }
                return .multiple(choices: choices, answer: Set(list))
            default:
                guard list.count >= 2, Set(list).count == list.count else {
                    error("\(p).answer", "ordering needs two or more distinct choice ids in order."); return nil
                }
                return .ordering(choices: choices, answer: list)
            }

        case .trueFalse:
            let value: Bool? = switch q.answer {
            case let .bool(b)?: b
            case let .string(s)? where ["true", "false"].contains(s.lowercased()): s.lowercased() == "true"
            default: nil
            }
            guard let value else { error("\(p).answer", "trueFalse needs answer true or false."); return nil }
            return .single(choices: [Choice(id: "true", text: "True"), Choice(id: "false", text: "False")],
                           answer: value ? "true" : "false")

        case .yesNo:
            guard let statements = q.statements, !statements.isEmpty else {
                error("\(p).statements", "yesNo needs a non-empty statements list."); return nil
            }
            var ids = Set<String>()
            let rows = statements.enumerated().compactMap { i, s -> DropdownRow? in
                let sp = "\(p).statements[\(i)]"
                guard let sid = required(s.id, "\(sp).id"), let text = required(s.text, "\(sp).text") else { return nil }
                if !ids.insert(sid).inserted { error("\(sp).id", "Duplicate statement id \"\(sid)\".") }
                guard let a = s.answer else { error("\(sp).answer", "Needs true (Yes) or false (No)."); return nil }
                return DropdownRow(id: sid, label: text, options: ["Yes", "No"], answer: a ? "Yes" : "No")
            }
            return rows.count == statements.count ? .dropdowns(rows: rows) : nil

        case .matching:
            let items = choiceList(q.items, "\(p).items", min: 1)
            let targets = choiceList(q.targets, "\(p).targets", min: 1)
            guard case let .map(map)? = q.answer else {
                error("\(p).answer", "matching needs an object mapping item ids to target ids."); return nil
            }
            let itemIDs = Set(items.map(\.id)), targetIDs = Set(targets.map(\.id))
            for (item, target) in map.sorted(by: { $0.key < $1.key }) {
                if !itemIDs.contains(item) { error("\(p).answer", "Unknown item id \"\(item)\".") }
                if !targetIDs.contains(target) { error("\(p).answer", "Unknown target id \"\(target)\".") }
            }
            if map.isEmpty { error("\(p).answer", "matching needs at least one placement.") }
            return .matching(items: items, targets: targets, answer: map)

        case .dropdowns:
            guard let docs = q.rows, !docs.isEmpty else { error("\(p).rows", "dropdowns needs a non-empty rows list."); return nil }
            var ids = Set<String>()
            let rows = docs.enumerated().compactMap { i, r -> DropdownRow? in
                let rp = "\(p).rows[\(i)]"
                guard let rid = required(r.id, "\(rp).id"), let label = required(r.label, "\(rp).label") else { return nil }
                if !ids.insert(rid).inserted { error("\(rp).id", "Duplicate row id \"\(rid)\".") }
                let options = r.options ?? []
                guard options.count >= 2, Set(options).count == options.count else {
                    error("\(rp).options", "Needs two or more distinct options."); return nil
                }
                guard let a = r.answer, options.contains(a) else {
                    error("\(rp).answer", "Must be exactly one of the row's options."); return nil
                }
                return DropdownRow(id: rid, label: label, options: options, answer: a)
            }
            return rows.count == docs.count ? .dropdowns(rows: rows) : nil

        case .fillIn:
            guard let docs = q.blanks, !docs.isEmpty else { error("\(p).blanks", "fillIn needs a non-empty blanks list."); return nil }
            var ids = Set<String>()
            let blanks = docs.enumerated().compactMap { i, b -> Blank? in
                let bp = "\(p).blanks[\(i)]"
                guard let bid = required(b.id, "\(bp).id") else { return nil }
                if !ids.insert(bid).inserted { error("\(bp).id", "Duplicate blank id \"\(bid)\".") }
                let accept = (b.accept ?? []).filter { !$0.trimmingCharacters(in: .whitespaces).isEmpty }
                if accept.isEmpty && b.pattern == nil { error(bp, "Needs accept (non-empty list) or pattern."); return nil }
                if let pattern = b.pattern, (try? Regex(pattern)) == nil {
                    error("\(bp).pattern", "Not a valid regular expression."); return nil
                }
                return Blank(id: bid, label: b.label, accept: accept, caseSensitive: b.caseSensitive ?? false, pattern: b.pattern)
            }
            return blanks.count == docs.count ? .fillIn(blanks: blanks) : nil

        case .hotspot:
            guard let image = required(q.image, "\(p).image") else { return nil }
            assetPath(image, "\(p).image")
            guard let docs = q.regions, !docs.isEmpty else { error("\(p).regions", "hotspot needs a non-empty regions list."); return nil }
            var ids = Set<String>()
            let regions = docs.enumerated().compactMap { i, r -> Region? in
                let rp = "\(p).regions[\(i)]"
                guard let rid = required(r.id, "\(rp).id") else { return nil }
                if !ids.insert(rid).inserted { error("\(rp).id", "Duplicate region id \"\(rid)\".") }
                guard let x = r.x, let y = r.y, let w = r.width, let h = r.height else {
                    error(rp, "Needs x, y, width and height (fractions 0–1 of the image)."); return nil
                }
                guard x >= 0, y >= 0, w > 0, h > 0, x + w <= 1.0001, y + h <= 1.0001 else {
                    error(rp, "Must lie inside the image: 0 ≤ x, y and x + width, y + height ≤ 1."); return nil
                }
                return Region(id: rid, label: r.label, x: x, y: y, width: w, height: h)
            }
            guard let list = answerList(q.answer, p, ids: ids), !list.isEmpty else {
                if q.answer != nil { error("\(p).answer", "hotspot needs one or more region ids.") }
                return nil
            }
            guard regions.count == docs.count else { return nil }
            return .hotspot(image: image, regions: regions, answer: Set(list))
        }
    }

    // MARK: Pieces

    mutating func choiceList(_ docs: [ChoiceDocument]?, _ p: String, min: Int) -> [Choice] {
        let docs = docs ?? []
        if docs.count < min { error(p, "Needs at least \(min) entr\(min == 1 ? "y" : "ies").") }
        var ids = Set<String>()
        return docs.enumerated().compactMap { i, c in
            guard let id = required(c.id, "\(p)[\(i)].id"), let text = required(c.text, "\(p)[\(i)].text") else { return nil }
            if !ids.insert(id).inserted { error("\(p)[\(i)].id", "Duplicate id \"\(id)\".") }
            return Choice(id: id, text: text)
        }
    }

    /// Answer as a list of ids, each of which must be in `ids`.
    mutating func answerList(_ answer: AnswerValue?, _ p: String, ids: Set<String>) -> [String]? {
        guard let answer else { error("\(p).answer", "Missing answer."); return nil }
        guard let list = answer.asList else { error("\(p).answer", "Must be an id or a list of ids."); return nil }
        let unknown = list.filter { !ids.contains($0) }
        if !unknown.isEmpty {
            error("\(p).answer", "Unknown id\(unknown.count == 1 ? "" : "s") \(unknown.map { "\"\($0)\"" }.joined(separator: ", ")). Known: \(ids.sorted().joined(separator: ", ")).")
            return nil
        }
        return list
    }

    mutating func exhibits(_ docs: [ExhibitDocument]?, _ p: String) -> [Exhibit] {
        (docs ?? []).enumerated().compactMap { i, e in
            let ep = "\(p)[\(i)]"
            let title = e.title ?? "Exhibit \(i + 1)"
            switch (e.text, e.image) {
            case let (text?, nil): return Exhibit(title: title, content: .text(text))
            case let (nil, image?):
                assetPath(image, "\(ep).image")
                return Exhibit(title: title, content: .image(image))
            default:
                error(ep, "Needs exactly one of text or image.")
                return nil
            }
        }
    }

    mutating func assetPath(_ path: String, _ p: String) {
        let parts = path.split(separator: "/")
        guard !path.hasPrefix("/"), !path.contains("://"), !parts.contains("..") else {
            error(p, "Must be a relative path inside the exam folder, like \"images/topology.png\".")
            return
        }
        let ext = (path as NSString).pathExtension.lowercased()
        if !["png", "jpg", "jpeg", "gif", "heic", "tiff", "tif", "bmp", "webp", "pdf"].contains(ext) {
            warn(p, "Unusual image extension \".\(ext)\".")
        }
        if let baseURL, !FileManager.default.fileExists(atPath: baseURL.appending(path: path).path) {
            error(p, "File not found: \(path) (relative to \(baseURL.path)).")
        }
    }

    mutating func required(_ value: String?, _ p: String) -> String? {
        guard let value, !value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            error(p, "Missing or empty.")
            return nil
        }
        return value
    }

    // MARK: Raw JSON checks

    mutating func checkKeys(_ raw: Any) {
        guard let exam = raw as? [String: Any] else { return }
        unknown(exam, KnownKeys.exam, "$")
        each(exam["sections"], KnownKeys.section, "sections")
        if let s = exam["scale"] as? [String: Any] { unknown(s, KnownKeys.scale, "scale") }
        for (i, c) in ((exam["caseStudies"] as? [[String: Any]]) ?? []).enumerated() {
            unknown(c, KnownKeys.caseStudy, "caseStudies[\(i)]")
            each(c["exhibits"], KnownKeys.exhibit, "caseStudies[\(i)].exhibits")
        }
        for (i, q) in ((exam["questions"] as? [[String: Any]]) ?? []).enumerated() {
            let p = "questions[\(i)]"
            unknown(q, KnownKeys.question, p)
            each(q["exhibits"], KnownKeys.exhibit, "\(p).exhibits")
            for key in ["choices", "items", "targets"] { each(q[key], KnownKeys.choice, "\(p).\(key)") }
            each(q["rows"], KnownKeys.row, "\(p).rows")
            each(q["statements"], KnownKeys.statement, "\(p).statements")
            each(q["blanks"], KnownKeys.blank, "\(p).blanks")
            each(q["regions"], KnownKeys.region, "\(p).regions")
        }
    }

    mutating func each(_ value: Any?, _ known: Set<String>, _ p: String) {
        for (i, d) in ((value as? [[String: Any]]) ?? []).enumerated() { unknown(d, known, "\(p)[\(i)]") }
    }

    mutating func unknown(_ dict: [String: Any], _ known: Set<String>, _ p: String) {
        for key in dict.keys.sorted() where !known.contains(key) {
            warn(p == "$" ? key : "\(p).\(key)", "Unknown key; it is ignored. Check spelling against the schema.")
        }
    }

    mutating func describe(_ e: DecodingError) {
        func path(_ keys: [CodingKey]) -> String {
            let s = keys.map { $0.intValue.map { "[\($0)]" } ?? ".\($0.stringValue)" }.joined()
            return s.isEmpty ? "$" : String(s.drop { $0 == "." })
        }
        switch e {
        case let .typeMismatch(type, ctx): error(path(ctx.codingPath), "Wrong type (expected \(type)). \(ctx.debugDescription)")
        case let .valueNotFound(_, ctx): error(path(ctx.codingPath), "Null value.")
        case let .keyNotFound(key, ctx): error(path(ctx.codingPath + [key]), "Missing.")
        case let .dataCorrupted(ctx): error(path(ctx.codingPath), "Not valid JSON: \(ctx.debugDescription) \((ctx.underlyingError as NSError?)?.userInfo["NSDebugDescription"] ?? "")")
        @unknown default: error("$", e.localizedDescription)
        }
    }
}

extension String {
    var nonEmpty: String? { isEmpty ? nil : self }
}
