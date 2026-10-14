import Foundation

public enum SourceFormat: String, CaseIterable, Sendable {
    case aiken, gift, csv

    /// Guesses from the extension, then from content.
    public static func detect(fileName: String, text: String) -> SourceFormat? {
        switch (fileName as NSString).pathExtension.lowercased() {
        case "gift": return .gift
        case "csv", "tsv": return .csv
        case "aiken": return .aiken
        default: break
        }
        if text.range(of: #"(?m)^ANSWER:\s*[A-Z]"#, options: .regularExpression) != nil { return .aiken }
        if text.range(of: #"\{[^}]*[=~][^}]*\}"#, options: .regularExpression) != nil { return .gift }
        if let first = text.split(separator: "\n").first, first.contains(","), first.lowercased().contains("question") { return .csv }
        return nil
    }
}

public struct Conversion: Sendable {
    public var document: ExamDocument
    /// Source entries that could not be converted, with the reason.
    public var skipped: [Issue]
}

public enum Converter {
    public static func convert(_ text: String, format: SourceFormat, id: String, title: String, topic: String? = nil) -> Conversion {
        var c: Conversion = switch format {
        case .aiken: Aiken.convert(text)
        case .gift: Gift.convert(text)
        case .csv: CSV.convert(text)
        }
        c.document.id = id
        c.document.title = title
        if let topic {
            c.document.questions = c.document.questions?.map { var q = $0; q.topic = q.topic ?? topic; return q }
        }
        return c
    }

    static func letterID(_ i: Int) -> String { String(UnicodeScalar(UInt8(97 + i % 26))) + (i >= 26 ? "\(i / 26)" : "") }

    public static func slug(_ s: String) -> String {
        let lowered = s.lowercased().unicodeScalars.map { CharacterSet.alphanumerics.contains($0) ? Character($0) : "-" }
        return String(lowered).split(separator: "-").joined(separator: "-")
    }
}

// MARK: - Aiken

/// Aiken: question lines, then `A. text` / `A) text` options, then `ANSWER: B` (or `ANSWER: A, C`).
enum Aiken {
    static func convert(_ text: String) -> Conversion {
        var questions: [QuestionDocument] = [], skipped: [Issue] = []
        var prompt: [String] = [], options: [(String, String)] = []
        var startLine = 1

        func reset() { prompt = []; options = [] }

        for (n, raw) in text.components(separatedBy: .newlines).enumerated() {
            let line = raw.trimmingCharacters(in: .whitespaces)
            if line.isEmpty, options.isEmpty { continue }
            if prompt.isEmpty && options.isEmpty { startLine = n + 1 }
            if let m = line.firstMatch(of: /^([A-Za-z])[\.\)]\s+(.+)$/), !prompt.isEmpty {
                options.append((String(m.1).uppercased(), String(m.2)))
            } else if let m = line.firstMatch(of: /^ANSWER:\s*(.+)$/.ignoresCase()) {
                let letters = m.1.split(whereSeparator: { ",; ".contains($0) }).map { $0.uppercased() }
                let index = Dictionary(uniqueKeysWithValues: options.enumerated().map { ($1.0, $0) })
                let ids = letters.compactMap { index[$0] }.map(Converter.letterID)
                if prompt.isEmpty || options.count < 2 || ids.count != letters.count || ids.isEmpty {
                    skipped.append(Issue(severity: .warning, path: "line \(startLine)", message: "Skipped: needs a question, 2+ options and an ANSWER naming them."))
                } else {
                    var q = QuestionDocument(id: "q\(questions.count + 1)", kind: ids.count == 1 ? .single : .multiple, prompt: prompt.joined(separator: "\n"))
                    q.choices = options.enumerated().map { ChoiceDocument(id: Converter.letterID($0), text: $1.1) }
                    q.answer = ids.count == 1 ? .string(ids[0]) : .strings(ids)
                    questions.append(q)
                }
                reset()
            } else if options.isEmpty {
                prompt.append(line)
            } else {
                // Continuation of the previous option.
                options[options.count - 1].1 += " " + line
            }
        }
        if !prompt.isEmpty {
            skipped.append(Issue(severity: .warning, path: "line \(startLine)", message: "Skipped: question has no ANSWER line."))
        }
        return Conversion(document: ExamDocument(id: "", title: "", questions: questions), skipped: skipped)
    }
}

// MARK: - GIFT

/// Moodle GIFT: single/multiple choice (incl. `~%50%` weights), true/false, short answer,
/// matching, simple numeric, `$CATEGORY:` topics and `####` general feedback.
enum Gift {
    static func convert(_ text: String) -> Conversion {
        var questions: [QuestionDocument] = [], skipped: [Issue] = []
        var topic: String?
        var block: [String] = [], blockLine = 1

        func flush() {
            defer { block = [] }
            let source = block.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !source.isEmpty else { return }
            switch parse(source, index: questions.count + 1) {
            case var .success(q):
                q.topic = topic
                questions.append(q)
            case let .failure(reason):
                skipped.append(Issue(severity: .warning, path: "line \(blockLine)", message: "Skipped: \(reason.message)"))
            }
        }

        for (n, line) in text.components(separatedBy: .newlines).enumerated() {
            let t = line.trimmingCharacters(in: .whitespaces)
            if t.hasPrefix("//") { continue }
            if t.hasPrefix("$CATEGORY:") {
                flush()
                topic = t.dropFirst("$CATEGORY:".count).split(separator: "/").last.map { $0.trimmingCharacters(in: .whitespaces) }
                    .flatMap { $0.hasPrefix("$") ? nil : $0 }
                continue
            }
            if t.isEmpty { flush(); continue }
            if block.isEmpty { blockLine = n + 1 }
            block.append(line)
        }
        flush()
        return Conversion(document: ExamDocument(id: "", title: "", questions: questions), skipped: skipped)
    }

    struct Failure: Error { let message: String }

    static func parse(_ source: String, index: Int) -> Result<QuestionDocument, Failure> {
        var s = Substring(source)
        var id = "q\(index)"
        if s.hasPrefix("::"), let end = s.dropFirst(2).range(of: "::") {
            let title = unescape(String(s[s.index(s.startIndex, offsetBy: 2)..<end.lowerBound]))
            id = Converter.slug(title).nonEmpty ?? id
            s = s[end.upperBound...]
        }
        if s.hasPrefix("["), let close = s.firstIndex(of: "]") { s = s[s.index(after: close)...] }

        guard let open = unescapedIndex(of: "{", in: s), let close = unescapedIndex(of: "}", in: s[open...]) else {
            return .failure(Failure(message: "no {answer} block"))
        }
        let before = unescape(String(s[..<open])).trimmingCharacters(in: .whitespacesAndNewlines)
        let after = unescape(String(s[s.index(after: close)...])).trimmingCharacters(in: .whitespacesAndNewlines)
        let prompt = after.isEmpty ? before : "\(before) _____ \(after)"
        var body = String(s[s.index(after: open)..<close]).trimmingCharacters(in: .whitespacesAndNewlines)

        var explanation: String?
        if let r = body.range(of: "####") {
            explanation = unescape(String(body[r.upperBound...])).trimmingCharacters(in: .whitespacesAndNewlines)
            body = String(body[..<r.lowerBound])
        }

        func make(_ kind: QuestionKind) -> QuestionDocument {
            var q = QuestionDocument(id: id, kind: kind, prompt: prompt)
            q.explanation = explanation?.nonEmpty
            return q
        }

        let upper = stripFeedback(body).trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if ["T", "TRUE", "F", "FALSE"].contains(upper) {
            var q = make(.trueFalse)
            q.answer = .bool(upper.hasPrefix("T"))
            return .success(q)
        }
        if body.isEmpty { return .failure(Failure(message: "essay questions have no answer key")) }
        if body.hasPrefix("#") {
            let value = stripFeedback(String(body.dropFirst())).trimmingCharacters(in: .whitespaces)
            guard value.range(of: #"^-?\d+(\.\d+)?$"#, options: .regularExpression) != nil else {
                return .failure(Failure(message: "numeric ranges and tolerances aren't supported; use an exact number"))
            }
            var q = make(.fillIn)
            q.blanks = [BlankDocument(id: "answer", accept: [value])]
            return .success(q)
        }

        let answers = tokens(body)
        if answers.contains(where: { $0.text.contains("->") }) {
            var items: [ChoiceDocument] = [], targets: [String] = [], map: [String: String] = [:]
            for (i, a) in answers.enumerated() {
                let parts = a.text.components(separatedBy: "->")
                let left = parts[0].trimmingCharacters(in: .whitespaces), right = parts.dropFirst().joined(separator: "->").trimmingCharacters(in: .whitespaces)
                if !targets.contains(right) { targets.append(right) }
                let itemID = "i\(i + 1)"
                if !left.isEmpty {
                    items.append(ChoiceDocument(id: itemID, text: left))
                    map[itemID] = "t\(targets.firstIndex(of: right)! + 1)"
                }
            }
            var q = make(.matching)
            q.items = items
            q.targets = targets.enumerated().map { ChoiceDocument(id: "t\($0 + 1)", text: $1) }
            q.answer = .map(map)
            return .success(q)
        }

        if !answers.contains(where: { $0.marker == "~" }) {
            var q = make(.fillIn)
            q.blanks = [BlankDocument(id: "answer", accept: answers.map(\.text))]
            return .success(q)
        }

        let correct = answers.enumerated().filter { $1.marker == "=" || ($1.weight ?? 0) > 0 }.map { Converter.letterID($0.offset) }
        guard !correct.isEmpty else { return .failure(Failure(message: "no correct answer marked")) }
        let weighted = answers.contains { $0.weight != nil }
        var q = make(weighted || correct.count > 1 ? .multiple : .single)
        q.choices = answers.enumerated().map { ChoiceDocument(id: Converter.letterID($0), text: $1.text) }
        q.answer = q.kind == "single" ? .string(correct[0]) : .strings(correct)
        return .success(q)
    }

    struct Token { var marker: Character; var text: String; var weight: Double? }

    /// Splits `=a ~b ~%50%c#feedback` into answers, honoring `\=`/`\~` escapes.
    static func tokens(_ body: String) -> [Token] {
        var out: [Token] = [], current: Token?, escaped = false
        for ch in body {
            if escaped { current?.text.append(ch); escaped = false; continue }
            if ch == "\\" { current?.text.append(ch); escaped = true; continue }
            if ch == "=" || ch == "~" {
                // "->" inside a matching pair is not a marker.
                if let c = current { out.append(c) }
                current = Token(marker: ch, text: "")
            } else {
                current?.text.append(ch)
            }
        }
        if let c = current { out.append(c) }
        return out.map { t in
            var t = t
            var text = stripFeedback(t.text).trimmingCharacters(in: .whitespacesAndNewlines)
            if let m = text.firstMatch(of: /^%(-?\d+(?:\.\d+)?)%/) {
                t.weight = Double(m.1)
                text = String(text[m.range.upperBound...]).trimmingCharacters(in: .whitespaces)
            }
            t.text = unescape(text)
            return t
        }
    }

    static func stripFeedback(_ s: String) -> String {
        var escaped = false
        for (i, ch) in s.enumerated() {
            if escaped { escaped = false; continue }
            if ch == "\\" { escaped = true; continue }
            if ch == "#" { return String(s.prefix(i)) }
        }
        return s
    }

    static func unescapedIndex(of target: Character, in s: Substring) -> Substring.Index? {
        var escaped = false
        for i in s.indices {
            if escaped { escaped = false; continue }
            if s[i] == "\\" { escaped = true; continue }
            if s[i] == target { return i }
        }
        return nil
    }

    static func unescape(_ s: String) -> String {
        var out = "", escaped = false
        for ch in s {
            if escaped {
                out.append(ch == "n" ? "\n" : ch)
                escaped = false
            } else if ch == "\\" {
                escaped = true
            } else {
                out.append(ch)
            }
        }
        return out
    }
}

// MARK: - CSV

/// One question per row. Columns (header names, any case): `question` or `prompt`; options in
/// `A`…`J` (or `option a`, `option1`…); `answer` as letters (`B`, `A,C`) or option text;
/// optional `id`, `topic`, `explanation`, `kind`. With no options, `answer` is a fill-in
/// (alternatives split by `|`), or `true`/`false` for a true/false question.
enum CSV {
    static func convert(_ text: String) -> Conversion {
        let rows = parse(text)
        guard let header = rows.first else {
            return Conversion(document: ExamDocument(id: "", title: "", questions: []), skipped: [Issue(severity: .error, path: "line 1", message: "Empty file.")])
        }
        let names = header.map { $0.trimmingCharacters(in: .whitespaces).lowercased() }
        func column(_ candidates: String...) -> Int? { names.firstIndex { candidates.contains($0) } }
        let qCol = column("question", "prompt", "question text")
        let aCol = column("answer", "answers", "correct", "correct answer", "key")
        let idCol = column("id"), topicCol = column("topic", "category", "domain", "objective")
        let exCol = column("explanation", "rationale", "feedback"), kindCol = column("kind", "type")
        var optionCols: [Int] = []
        for i in 0..<10 {
            let letter = String(UnicodeScalar(UInt8(97 + i)))
            if let c = column(letter, "option \(letter)", "choice \(letter)", "option\(i + 1)", "option \(i + 1)", "choice\(i + 1)") { optionCols.append(c) }
        }

        var questions: [QuestionDocument] = [], skipped: [Issue] = []
        guard let qCol, let aCol else {
            skipped.append(Issue(severity: .error, path: "line 1", message: "Header needs a question (or prompt) column and an answer column. Found: \(names.joined(separator: ", "))."))
            return Conversion(document: ExamDocument(id: "", title: "", questions: []), skipped: skipped)
        }

        for (n, row) in rows.dropFirst().enumerated() {
            func cell(_ i: Int?) -> String? { i.flatMap { $0 < row.count ? row[$0].trimmingCharacters(in: .whitespacesAndNewlines) : nil }?.nonEmpty }
            let line = "line \(n + 2)"
            guard let prompt = cell(qCol) else {
                if row.contains(where: { !$0.trimmingCharacters(in: .whitespaces).isEmpty }) {
                    skipped.append(Issue(severity: .warning, path: line, message: "Skipped: empty question."))
                }
                continue
            }
            guard let answer = cell(aCol) else {
                skipped.append(Issue(severity: .warning, path: line, message: "Skipped: no answer."))
                continue
            }
            let id = cell(idCol) ?? "q\(n + 1)"
            let options = optionCols.compactMap { cell($0) }
            var q: QuestionDocument

            if options.isEmpty {
                if ["true", "false"].contains(answer.lowercased()) {
                    q = QuestionDocument(id: id, kind: .trueFalse, prompt: prompt)
                    q.answer = .bool(answer.lowercased() == "true")
                } else {
                    q = QuestionDocument(id: id, kind: .fillIn, prompt: prompt)
                    q.blanks = [BlankDocument(id: "answer", accept: answer.components(separatedBy: "|").map { $0.trimmingCharacters(in: .whitespaces) })]
                }
            } else {
                var ids: [String] = []
                if let exact = options.firstIndex(where: { $0.caseInsensitiveCompare(answer) == .orderedSame }) {
                    ids = [Converter.letterID(exact)]
                } else {
                    let parts = answer.contains(where: { ",;| ".contains($0) })
                        ? answer.split(whereSeparator: { ",;| ".contains($0) }).map(String.init)
                        : answer.count <= options.count && answer.allSatisfy(\.isLetter) ? answer.map(String.init) : [answer]
                    for p in parts {
                        if p.count == 1, let a = p.uppercased().unicodeScalars.first, a.value >= 65, Int(a.value - 65) < options.count {
                            ids.append(Converter.letterID(Int(a.value - 65)))
                        } else if let i = options.firstIndex(where: { $0.caseInsensitiveCompare(p) == .orderedSame }) {
                            ids.append(Converter.letterID(i))
                        } else {
                            ids = []
                            break
                        }
                    }
                }
                guard !ids.isEmpty else {
                    skipped.append(Issue(severity: .warning, path: line, message: "Skipped: answer \"\(answer)\" doesn't name an option."))
                    continue
                }
                let declared = cell(kindCol).flatMap { QuestionKind(rawValue: $0) }
                let kind = declared == .ordering ? QuestionKind.ordering : (ids.count == 1 ? .single : .multiple)
                q = QuestionDocument(id: id, kind: kind, prompt: prompt)
                q.choices = options.enumerated().map { ChoiceDocument(id: Converter.letterID($0), text: $1) }
                q.answer = kind == .single ? .string(ids[0]) : .strings(ids)
            }
            q.topic = cell(topicCol)
            q.explanation = cell(exCol)
            questions.append(q)
        }
        return Conversion(document: ExamDocument(id: "", title: "", questions: questions), skipped: skipped)
    }

    /// RFC 4180 with quoted fields; delimiter is whichever of `,` `;` tab is most common in line 1.
    static func parse(_ text: String) -> [[String]] {
        let firstLine = text.prefix { $0 != "\n" }
        let delimiter: Character = [",", ";", "\t"].max { a, b in firstLine.count { $0 == a } < firstLine.count { $0 == b } } ?? ","
        var rows: [[String]] = [], row: [String] = [], field = "", quoted = false
        var chars = text.makeIterator(), pending: Character? = nil
        func next() -> Character? { if let p = pending { pending = nil; return p }; return chars.next() }
        while let ch = next() {
            if quoted {
                if ch == "\"" {
                    let after = next()
                    if after == "\"" { field.append("\"") } else { quoted = false; pending = after }
                } else {
                    field.append(ch)
                }
            } else if ch == "\"" && field.isEmpty {
                quoted = true
            } else if ch == delimiter {
                row.append(field); field = ""
            } else if ch == "\n" || ch == "\r\n" {
                row.append(field); rows.append(row); row = []; field = ""
            } else if ch != "\r" {
                field.append(ch)
            }
        }
        if !field.isEmpty || !row.isEmpty { row.append(field); rows.append(row) }
        return rows
    }
}
