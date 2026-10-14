import Foundation

extension Question {
    /// Points earned as a fraction of `points`, 0–1. All-or-nothing unless `partialCredit`.
    public func score(_ response: Response?) -> Double {
        let f = fraction(response)
        return partialCredit ? f : (f >= 1 ? 1 : 0)
    }

    public func isCorrect(_ response: Response?) -> Bool { fraction(response) >= 1 }

    /// The share of the question answered correctly, ignoring the partial-credit setting.
    public func fraction(_ response: Response?) -> Double {
        switch (body, response) {
        case let (.single(_, a), .single(r)?):
            return a == r ? 1 : 0

        case let (.multiple(_, a), .multiple(r)?), let (.hotspot(_, _, a), .hotspot(r)?):
            let hits = r.intersection(a).count, wrong = r.subtracting(a).count
            return max(0, Double(hits - wrong)) / Double(a.count)

        case let (.ordering(_, a), .ordering(r)?):
            let inPlace = zip(a, r).count { $0 == $1 }
            return Double(inPlace) / Double(max(a.count, r.count))

        case let (.matching(items, _, a), .matching(r)?):
            let right = items.count { a[$0.id] == r[$0.id] }
            return Double(right) / Double(items.count)

        case let (.dropdowns(rows), .dropdowns(r)?):
            return Double(rows.count { r[$0.id] == $0.answer }) / Double(rows.count)

        case let (.fillIn(blanks), .fillIn(r)?):
            return Double(blanks.count { $0.accepts(r[$0.id] ?? "") }) / Double(blanks.count)

        default:
            return 0
        }
    }
}

extension Blank {
    public func accepts(_ input: String) -> Bool {
        let given = Self.normalize(input)
        guard !given.isEmpty else { return false }
        if accept.contains(where: { Self.normalize($0).compare(given, options: caseSensitive ? [] : .caseInsensitive) == .orderedSame }) {
            return true
        }
        guard let pattern, var regex = try? Regex(pattern) else { return false }
        if !caseSensitive { regex = regex.ignoresCase() }
        return (try? regex.wholeMatch(in: given)) != nil
    }

    /// Trims and collapses runs of whitespace, so "ip  route " matches "ip route".
    static func normalize(_ s: String) -> String {
        s.split(whereSeparator: \.isWhitespace).joined(separator: " ")
    }
}
