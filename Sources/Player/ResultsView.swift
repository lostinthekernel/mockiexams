import MockiCore
import SwiftUI

struct ResultsView: View {
    let result: SessionResult
    let onRetry: (() -> Void)?
    let onDone: () -> Void
    var doneLabel = "Back to Library"

    enum Filter: String, CaseIterable, Identifiable {
        case all = "All", incorrect = "Incorrect", flagged = "Flagged"
        var id: Self { self }
    }

    @State private var filter: Filter = .incorrect

    private var shown: [ResultItem] {
        switch filter {
        case .all: result.items
        case .incorrect: result.items.filter { !$0.isCorrect }
        case .flagged: result.items.filter(\.flagged)
        }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Image(systemName: result.passed ? "checkmark.seal.fill" : "xmark.seal.fill")
                    .foregroundStyle(result.passed ? Palette.ok : Palette.err)
                Text("\(result.mode == .exam ? "Exam" : "Practice") complete").font(.headline)
                Text(result.exam.title).foregroundStyle(.secondary).lineLimit(1)
                Spacer()
                if let onRetry, !result.missedIDs.isEmpty {
                    Button("Retry \(result.missedIDs.count) Missed", action: onRetry)
                }
                Button(action: onDone) { Label(doneLabel, systemImage: "chevron.backward") }
                    .buttonStyle(.borderedProminent)
                    .tint(Palette.brandFill)
                    .keyboardShortcut(.cancelAction)
                    .help("\(doneLabel) (Esc)")
            }
            .padding(.horizontal, 18).padding(.vertical, 10)
            .background(.bar)
            Divider()
            report
        }
    }

    private var report: some View {
        Form {
            Section {
                ScoreReport(result: result)
            } footer: {
                if result.exam.scale != nil {
                    Text("The scaled score is a linear estimate from your percentage. Vendors don't publish their real scaling.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }

            Section("Performance by domain") {
                DomainTable(result: result)
            }

            Section {
                Picker("Show", selection: $filter) {
                    ForEach(Filter.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.segmented)
                if shown.isEmpty {
                    Text("Nothing here.").foregroundStyle(.secondary)
                }
                ForEach(shown) { ReviewRow(item: $0) }
            } header: {
                Text("Review")
            }
        }
        .formStyle(.grouped)
    }
}

func points(_ value: Double) -> String { value.formatted(.number.precision(.fractionLength(0...2))) }

struct ReviewRow: View {
    let item: ResultItem

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            HStack(alignment: .firstTextBaseline) {
                Image(systemName: item.isCorrect ? "checkmark.circle.fill" : item.score > 0 ? "circle.lefthalf.filled" : "xmark.circle.fill")
                    .foregroundStyle(item.isCorrect ? .green : item.score > 0 ? .orange : .red)
                Text(markdown(item.question.prompt.components(separatedBy: "```").first ?? "")).fixedSize(horizontal: false, vertical: true)
                Spacer()
                if item.question.points != 1 || item.question.partialCredit {
                    Text("\(points(item.earned))/\(points(item.question.points)) pts").font(.caption).monospacedDigit().foregroundStyle(.secondary)
                }
                if item.flagged { Image(systemName: "flag.fill").foregroundStyle(.orange) }
            }
            Text(markdown("Your answer: \(item.question.describe(item.response))"))
                .foregroundStyle(item.isCorrect ? Color.secondary : Color.red)
            if !item.isCorrect {
                Text(markdown("Correct: \(item.question.describeAnswer)")).foregroundStyle(.green)
            }
            if let e = item.question.explanation {
                Text(markdown(e)).font(.callout).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(.vertical, 4)
        .textSelection(.enabled)
    }
}

struct TopicRow: View {
    let score: TopicScore

    var body: some View {
        HStack {
            Text(score.topic)
            Spacer()
            ProgressView(value: score.fraction)
                .frame(width: 140)
                .tint(score.fraction >= 0.7 ? .green : score.fraction >= 0.5 ? .yellow : .red)
            Text("\(points(score.earned))/\(points(score.possible))")
                .monospacedDigit()
                .foregroundStyle(.secondary)
                .frame(width: 60, alignment: .trailing)
        }
        .accessibilityElement(children: .combine)
    }
}

/// Pass/fail with the score placed on the exam's scale and the passing line marked.
struct ScoreReport: View {
    let result: SessionResult

    private var range: (min: Double, max: Double, pass: Double, you: Double) {
        if let s = result.exam.scale, let you = result.scaledScore {
            return (Double(s.min), Double(s.max), Double(s.pass), Double(you))
        }
        return (0, 100, Double(result.exam.passPercent), Double(result.percent))
    }

    var body: some View {
        let r = range
        HStack(alignment: .top, spacing: 28) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Score report").font(.caption.weight(.semibold)).textCase(.uppercase).foregroundStyle(.secondary)
                Text(result.exam.title).font(.title2.bold())
                Text("\(result.mode.label) · \(result.finishedAt.formatted(date: .abbreviated, time: .shortened)) · \(formatDuration(result.duration))")
                    .foregroundStyle(.secondary)
                Text(result.passed ? "PASS" : "FAIL")
                    .font(.system(size: 30, weight: .heavy))
                    .foregroundStyle(result.passed ? Palette.ok : Palette.err)
                    .padding(.top, 4)
            }
            Spacer(minLength: 0)
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text("Your score: \(Int(r.you))\(result.exam.scale == nil ? "%" : "")").font(.headline).monospacedDigit()
                    Spacer()
                    Text("Passing: \(Int(r.pass))\(result.exam.scale == nil ? "%" : "")").foregroundStyle(.secondary).monospacedDigit()
                }
                ScaleBar(min: r.min, max: r.max, pass: r.pass, you: r.you, passed: result.passed)
                    .frame(height: 30)
                HStack {
                    Text("\(Int(r.min))"); Spacer(); Text("\(Int(r.max))")
                }
                .font(.caption).foregroundStyle(.secondary).monospacedDigit()
                Text("\(result.correctCount) of \(result.items.count) fully correct · \(points(result.earned)) of \(points(result.possible)) points · \(result.percent)%")
                    .font(.callout).foregroundStyle(.secondary)
            }
            .frame(width: 340)
        }
        .padding(.vertical, 8)
        .accessibilityElement(children: .combine)
    }
}

struct ScaleBar: View {
    let min: Double, max: Double, pass: Double, you: Double, passed: Bool

    var body: some View {
        GeometryReader { geo in
            let w = geo.size.width
            let x = { (v: Double) in CGFloat((v - min) / (max - min)) * w }
            ZStack(alignment: .topLeading) {
                HStack(spacing: 0) {
                    Rectangle().fill(Palette.errSoft).frame(width: x(pass))
                    Rectangle().fill(Palette.okSoft)
                }
                .frame(height: 8).clipShape(Capsule()).offset(y: 14)
                Rectangle().fill(.primary).frame(width: 2, height: 18).offset(x: x(pass) - 1, y: 9)
                Text("\(Int(you))")
                    .font(.caption2.bold()).monospacedDigit()
                    .padding(.horizontal, 5).padding(.vertical, 1)
                    .background(passed ? Palette.ok : Palette.err, in: RoundedRectangle(cornerRadius: 3))
                    .foregroundStyle(.white)
                    .fixedSize()
                    .position(x: Swift.min(Swift.max(x(you), 14), w - 14), y: 6)
            }
        }
    }
}

/// Each topic's weight, score, and whether it meets the exam's passing standard.
struct DomainTable: View {
    let result: SessionResult

    private var standard: Double {
        if let s = result.exam.scale { return Double(s.pass - s.min) / Double(s.max - s.min) }
        return Double(result.exam.passPercent) / 100
    }

    var body: some View {
        Grid(alignment: .leading, horizontalSpacing: 16, verticalSpacing: 10) {
            GridRow {
                Text("Domain"); Text("Weight"); Text("Performance"); Text("Result")
            }
            .font(.caption.weight(.semibold)).foregroundStyle(.secondary)
            ForEach(result.topics) { t in
                let meets = t.fraction >= standard
                GridRow {
                    Text(t.topic)
                    Text("\(Int(result.exam.weight(of: t.topic).rounded()))%").monospacedDigit().foregroundStyle(.secondary)
                    HStack(spacing: 8) {
                        ProgressView(value: t.fraction).frame(width: 140).tint(meets ? Palette.brand : Palette.err)
                        Text("\(Int((t.fraction * 100).rounded()))%").monospacedDigit().frame(width: 40, alignment: .trailing)
                    }
                    Text(meets ? "Meets the standard" : "Needs improvement")
                        .fontWeight(meets ? .regular : .semibold)
                        .foregroundStyle(meets ? Color.primary : Palette.err)
                }
                .accessibilityElement(children: .combine)
            }
        }
        .padding(.vertical, 4)
    }
}
