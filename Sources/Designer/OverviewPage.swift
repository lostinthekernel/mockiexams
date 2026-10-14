import MockiCore
import SwiftUI

/// Where a project opens: validation status, blueprint coverage, and what the bank contains.
struct OverviewPage: View {
    @Binding var document: ExamProjectDocument
    let issues: [Issue]
    let showIssues: () -> Void
    let go: (DesignerPage) -> Void
    let preview: () -> Void
    let install: () -> Void

    @State private var editingBlueprint = false

    private var errors: Int { issues.count { $0.severity == .error } }
    private var warnings: Int { issues.count - errors }

    var body: some View {
        ConsolePage(crumbs: ["Projects"], title: document.exam.title ?? "Untitled Exam", info: document.exam.description) {
            Button("Preview Exam", action: preview)
            Button("Install to Library", action: install).buttonStyle(.borderedProminent).tint(Palette.brandFill)
        } content: {
            ScrollView {
                VStack(alignment: .leading, spacing: 14) {
                    statusBanner
                    HStack(alignment: .top, spacing: 14) {
                        coverage.frame(maxWidth: .infinity)
                        VStack(spacing: 14) {
                            questionsCard
                            assetsCard
                            setupCard
                        }
                        .frame(width: 320)
                    }
                }
                .padding(.horizontal, 22).padding(.bottom, 22)
            }
        }
        .sheet(isPresented: $editingBlueprint) {
            VStack(spacing: 0) {
                Form { BlueprintEditor(document: $document) }.formStyle(.grouped)
                Divider()
                HStack { Spacer(); Button("Done") { editingBlueprint = false }.keyboardShortcut(.defaultAction) }.padding(12)
            }
            .frame(width: 520, height: 520)
        }
    }

    private var statusBanner: some View {
        HStack(spacing: 10) {
            Image(systemName: errors == 0 ? "checkmark.circle.fill" : "exclamationmark.octagon.fill")
                .foregroundStyle(errors == 0 ? Palette.ok : Palette.err)
            Text(errors == 0
                 ? "Valid. \(warnings == 0 ? "No warnings." : "\(warnings) warning\(warnings == 1 ? "" : "s").") Ready to preview and install."
                 : "\(errors) error\(errors == 1 ? "" : "s") and \(warnings) warning\(warnings == 1 ? "" : "s") to fix before this exam can be previewed or installed.")
            Spacer()
            if !issues.isEmpty { Button("View Issues", action: showIssues).buttonStyle(.link) }
        }
        .padding(.horizontal, 14).padding(.vertical, 10)
        .background(errors == 0 ? Palette.okSoft : Palette.errSoft, in: RoundedRectangle(cornerRadius: 8))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder((errors == 0 ? Palette.ok : Palette.err).opacity(0.35)))
    }

    // MARK: Blueprint

    private struct Row: Identifiable {
        let topic: String
        let actual: Double
        let target: Double?
        var id: String { topic }
    }

    private var rows: [Row] {
        let asked = document.askedQuestions
        let total = asked.reduce(0) { $0 + ($1.points ?? 1) }
        let weights = document.exam.topicWeights ?? [:]
        return document.topics.map { topic in
            let pts = asked.filter { ($0.topic?.nonEmpty ?? "General") == topic }.reduce(0) { $0 + ($1.points ?? 1) }
            return Row(topic: topic, actual: total == 0 ? 0 : pts / total * 100, target: weights[topic])
        }
    }

    private var coverage: some View {
        ConsoleCard(title: "Blueprint coverage", trailing: AnyView(Button(document.exam.topicWeights == nil ? "Set Targets" : "Edit Targets") { editingBlueprint = true }.buttonStyle(.link))) {
            Text("Each topic's share of the exam's points, against its target weight.")
                .font(.callout).foregroundStyle(.secondary)
            if rows.isEmpty {
                Text("No questions yet.").foregroundStyle(.secondary)
            }
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 10) {
                ForEach(rows) { row in
                    GridRow {
                        Text(row.topic).lineLimit(1).frame(minWidth: 120, alignment: .leading)
                        CoverageBar(actual: row.actual, target: row.target).frame(height: 10)
                        Text(row.target.map { "\(Int(row.actual.rounded()))% · target \(Int($0.rounded()))%" } ?? "\(Int(row.actual.rounded()))%")
                            .font(.caption).monospacedDigit().foregroundStyle(gapColor(row))
                            .frame(width: 130, alignment: .trailing)
                    }
                    .accessibilityElement(children: .combine)
                }
            }
            if let note = coverageNote { Text(note).font(.callout).foregroundStyle(Palette.warn) }
            if document.exam.topicWeights == nil, !rows.isEmpty {
                Text("Set target weights from the exam's official objectives to see where the bank is short or heavy.")
                    .font(.callout).foregroundStyle(.secondary)
            }
        }
    }

    private func gapColor(_ row: Row) -> Color {
        guard let t = row.target else { return .secondary }
        return abs(row.actual - t) > 5 ? Palette.warn : .secondary
    }

    private var coverageNote: String? {
        let asked = document.askedQuestions
        guard let worst = rows.filter({ $0.target != nil }).max(by: { abs($0.actual - $0.target!) < abs($1.actual - $1.target!) }),
              let target = worst.target, abs(worst.actual - target) > 5 else { return nil }
        let perQuestion = asked.isEmpty ? 1 : 100 / Double(asked.count)
        let n = Int((abs(target - worst.actual) / perQuestion).rounded(.up))
        return worst.actual < target
            ? "\(worst.topic) is about \(n) question\(n == 1 ? "" : "s") short of its target."
            : "\(worst.topic) is about \(n) question\(n == 1 ? "" : "s") over its target."
    }

    // MARK: Side cards

    private var questionsCard: some View {
        let qs = document.questions
        let points = qs.reduce(0) { $0 + ($1.points ?? 1) }
        let byKind = QuestionKind.allCases.map { k in (k, qs.count { $0.kind == k.rawValue }) }.filter { $0.1 > 0 }
        let top = byKind.map(\.1).max() ?? 1
        return ConsoleCard(title: "Questions", trailing: AnyView(Button("Open Bank") { go(.bank) }.buttonStyle(.link))) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text("\(qs.count)").font(.system(size: 30, weight: .semibold)).monospacedDigit()
                Text("· \(points.formatted()) points").foregroundStyle(.secondary)
            }
            VStack(alignment: .leading, spacing: 5) {
                ForEach(byKind, id: \.0) { kind, n in
                    HStack(spacing: 8) {
                        Image(systemName: KindIcon.name(kind)).frame(width: 16).foregroundStyle(.secondary)
                        Text(kind.label).font(.caption).frame(width: 96, alignment: .leading)
                        GeometryReader { g in
                            Capsule().fill(Palette.brand.opacity(0.75)).frame(width: max(4, g.size.width * Double(n) / Double(top)))
                        }
                        .frame(height: 6)
                        Text("\(n)").font(.caption).monospacedDigit().frame(width: 26, alignment: .trailing)
                    }
                }
            }
            let explained = qs.count { $0.explanation?.nonEmpty != nil }
            if explained < qs.count {
                Text("\(qs.count - explained) without an explanation").font(.caption).foregroundStyle(.secondary)
            }
        }
    }

    private var assetsCard: some View {
        let bytes = document.assets.values.reduce(0) { $0 + $1.count }
        let unused = document.assets.keys.count { document.usage(of: $0) == 0 }
        return ConsoleCard(title: "Assets", trailing: AnyView(Button("Manage") { go(.assets) }.buttonStyle(.link))) {
            HStack(alignment: .firstTextBaseline, spacing: 6) {
                Text("\(document.assets.count)").font(.system(size: 30, weight: .semibold)).monospacedDigit()
                Text("· \(ByteCountFormatter.string(fromByteCount: Int64(bytes), countStyle: .file))").foregroundStyle(.secondary)
            }
            Text(unused == 0 ? "All in use" : "\(unused) unused").font(.caption).foregroundStyle(unused == 0 ? Color.secondary : Palette.warn)
        }
    }

    private var setupCard: some View {
        ConsoleCard(title: "Exam setup", trailing: AnyView(Button("Edit") { go(.settings) }.buttonStyle(.link))) {
            Grid(alignment: .leading, horizontalSpacing: 12, verticalSpacing: 6) {
                setupRow("Time limit", document.exam.timeLimitMinutes.map { "\(Int($0)) min" } ?? "None")
                setupRow("Pass mark", document.exam.scale.map { "\($0.pass) of \($0.max)" } ?? "\(document.exam.passPercent ?? 70)%")
                setupRow("Sections", "\(document.exam.sections?.count ?? 0)")
                setupRow("Case studies", "\(document.exam.caseStudies?.count ?? 0)")
                setupRow("Going back", document.exam.allowBack == false ? "Not allowed" : "Allowed")
            }
            .font(.callout)
        }
    }

    private func setupRow(_ label: String, _ value: String) -> some View {
        GridRow {
            Text(label).foregroundStyle(.secondary)
            Text(value).monospacedDigit()
        }
    }
}

/// Target bar: filled to the actual share, with a tick at the target.
struct CoverageBar: View {
    let actual: Double
    let target: Double?

    var body: some View {
        GeometryReader { g in
            let w = g.size.width
            ZStack(alignment: .leading) {
                Capsule().fill(Palette.consoleLine.opacity(0.6))
                Capsule().fill(Palette.brand).frame(width: max(3, w * min(actual, 100) / 100))
                if let target {
                    Rectangle().fill(.primary).frame(width: 2, height: g.size.height + 6).offset(x: w * min(target, 100) / 100 - 1)
                }
            }
        }
    }
}

/// Target weight per topic. Used on the overview sheet and in exam settings.
struct BlueprintEditor: View {
    @Binding var document: ExamProjectDocument

    private var weights: [String: Double] { document.exam.topicWeights ?? [:] }
    private var total: Double { weights.values.reduce(0, +) }

    var body: some View {
        Section {
            ForEach(document.topics, id: \.self) { topic in
                LabeledContent(topic) {
                    HStack(spacing: 4) {
                        TextField("Weight", value: Binding(
                            get: { weights[topic] },
                            set: { v in
                                var w = weights
                                w[topic] = v
                                document.exam.topicWeights = w.isEmpty ? nil : w
                            }), format: .number)
                            .multilineTextAlignment(.trailing)
                            .frame(width: 70)
                            .textFieldStyle(.roundedBorder)
                            .labelsHidden()
                        Text("%").foregroundStyle(.secondary)
                    }
                }
            }
            LabeledContent("Total") {
                Text("\(total.formatted())%").monospacedDigit()
                    .foregroundStyle(weights.isEmpty || abs(total - 100) <= 0.5 ? Color.secondary : Palette.warn)
            }
            HStack {
                Button("Use Current Mix") { useCurrentMix() }
                Button("Clear Targets", role: .destructive) { document.exam.topicWeights = nil }
                    .disabled(weights.isEmpty)
            }
        } header: {
            Text("Blueprint targets")
        } footer: {
            Text("Copy the domain weights from the exam's official objectives. Targets should add up to 100%. The score report uses them as each domain's weight.")
                .font(.caption).foregroundStyle(.secondary)
        }
    }

    private func useCurrentMix() {
        let asked = document.askedQuestions
        let total = asked.reduce(0) { $0 + ($1.points ?? 1) }
        guard total > 0 else { return }
        var w: [String: Double] = [:]
        for topic in document.topics {
            let pts = asked.filter { ($0.topic?.nonEmpty ?? "General") == topic }.reduce(0) { $0 + ($1.points ?? 1) }
            if pts > 0 { w[topic] = (pts / total * 100).rounded() }
        }
        document.exam.topicWeights = w
    }
}

/// Case studies as a list beside the selected one's editor.
struct CaseStudiesPage: View {
    @Binding var document: ExamProjectDocument
    let projectName: String
    @State private var selected: Int? = 0

    private var list: [CaseStudyDocument] { document.exam.caseStudies ?? [] }

    var body: some View {
        ConsolePage(crumbs: [projectName], title: "Case studies (\(list.count))", info: "Shared scenarios shown beside every question that belongs to them.") {
            Button("Delete", role: .destructive) { if let s = selected { delete(s) } }
                .disabled(selected == nil || list.isEmpty)
            Button("Create Case Study", action: add).buttonStyle(.borderedProminent).tint(Palette.brandFill)
        } content: {
            HSplitView {
                List(selection: $selected) {
                    ForEach(Array(list.enumerated()), id: \.offset) { i, c in
                        VStack(alignment: .leading, spacing: 2) {
                            Text(c.title?.nonEmpty ?? c.id ?? "Case study")
                            Text("\(document.questions.count { $0.caseStudy == c.id }) questions · \(c.exhibits?.count ?? 0) exhibits")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                        .tag(i)
                    }
                }
                .frame(minWidth: 220, idealWidth: 260, maxWidth: 340)
                Group {
                    if let i = selected, list.indices.contains(i) {
                        CaseStudyEditor(
                            caseStudy: Binding(get: { document.exam.caseStudies![i] }, set: { document.exam.caseStudies![i] = $0 }),
                            document: $document)
                        .id(i)
                    } else {
                        ContentUnavailableView("No case study selected", systemImage: "briefcase",
                                               description: Text("Create one to group questions around a shared scenario."))
                    }
                }
                .frame(minWidth: 420, maxWidth: .infinity, maxHeight: .infinity)
            }
        }
    }

    private func add() {
        var l = list
        let used = Set(l.compactMap(\.id))
        let id = (1...).lazy.map { "case-\($0)" }.first { !used.contains($0) }!
        l.append(CaseStudyDocument(id: id, title: "New case study", scenario: ""))
        document.exam.caseStudies = l
        selected = l.count - 1
    }

    private func delete(_ i: Int) {
        guard var l = document.exam.caseStudies, l.indices.contains(i) else { return }
        let id = l.remove(at: i).id
        document.exam.caseStudies = l.isEmpty ? nil : l
        document.questions = document.questions.map { q in var q = q; if q.caseStudy == id { q.caseStudy = nil }; return q }
        selected = l.isEmpty ? nil : max(0, i - 1)
    }
}
