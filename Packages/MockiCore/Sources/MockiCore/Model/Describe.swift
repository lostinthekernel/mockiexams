import Foundation

/// Plain-text renderings of answers, shared by the results review and the CLI.
extension Question {
    public func describe(_ response: Response?) -> String {
        guard let response, !response.isEmpty else { return "(unanswered)" }
        switch (body, response) {
        case let (.single(choices, _), .single(id)):
            return texts(choices, [id])
        case let (.multiple(choices, _), .multiple(ids)):
            return texts(choices, choices.map(\.id).filter(ids.contains))
        case let (.ordering(choices, _), .ordering(ids)):
            return texts(choices, ids, separator: " → ")
        case let (.matching(items, targets, _), .matching(map)):
            return items.compactMap { item in
                map[item.id].map { "\(item.text) → \(texts(targets, [$0]))" }
            }.joined(separator: "; ")
        case let (.dropdowns(rows), .dropdowns(map)):
            return rows.map { "\($0.label): \(map[$0.id] ?? "—")" }.joined(separator: "; ")
        case let (.fillIn(blanks), .fillIn(map)):
            return blanks.map { "\($0.label ?? $0.id): \(map[$0.id].map(Blank.normalize) ?? "")" }.joined(separator: "; ")
        case let (.hotspot(_, regions, _), .hotspot(ids)):
            return regions.filter { ids.contains($0.id) }.map { $0.label ?? $0.id }.joined(separator: ", ")
        default:
            return "(unanswered)"
        }
    }

    public var describeAnswer: String {
        switch body {
        case let .single(choices, a):
            texts(choices, [a])
        case let .multiple(choices, a):
            texts(choices, choices.map(\.id).filter(a.contains))
        case let .ordering(choices, a):
            texts(choices, a, separator: " → ")
        case let .matching(items, targets, a):
            items.compactMap { item in a[item.id].map { "\(item.text) → \(texts(targets, [$0]))" } }.joined(separator: "; ")
        case let .dropdowns(rows):
            rows.map { "\($0.label): \($0.answer)" }.joined(separator: "; ")
        case let .fillIn(blanks):
            blanks.map { "\($0.label ?? $0.id): \($0.accept.first ?? $0.pattern.map { "/\($0)/" } ?? "")" }.joined(separator: "; ")
        case let .hotspot(_, regions, a):
            regions.filter { a.contains($0.id) }.map { $0.label ?? $0.id }.joined(separator: ", ")
        }
    }

    private func texts(_ choices: [Choice], _ ids: [String], separator: String = ", ") -> String {
        ids.compactMap { id in choices.first { $0.id == id }?.text }.joined(separator: separator)
    }
}
