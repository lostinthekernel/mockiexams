import MockiCore
import SwiftUI
import UniformTypeIdentifiers

/// The bank as a table: filter, sort, select one to edit below, or several for bulk actions.
struct QuestionBankPage: View {
    @Binding var document: ExamProjectDocument
    @Binding var selection: Set<Int>
    @Binding var search: String
    let issues: [Issue]
    let create: () -> Void
    let preview: (Int) -> Void
    let message: (String) -> Void

    @State private var kindFilter: QuestionKind?
    @State private var topicFilter: String?
    @State private var statusFilter: StatusFilter?
    @State private var sortOrder = [KeyPathComparator(\BankRow.index)]
    /// Which columns show; right-click the header to change it. ID starts hidden.
    @SceneStorage("questionBankColumns") private var columns = TableColumnCustomization<BankRow>()
    @State private var importing = false
    @State private var bulkTopic: String?
    @State private var bulkPoints: String?

    enum StatusFilter: String, CaseIterable { case needsAttention = "Needs attention", errors = "Errors", valid = "Valid" }

    struct BankRow: Identifiable {
        let index: Int
        let questionID: String
        let prompt: String
        let kind: String
        let topic: String
        let points: Double
        let status: RowStatus
        var id: Int { index }
    }

    enum RowStatus: Comparable {
        case errors(Int), warnings(Int), noExplanation, valid
        var rank: Int {
            switch self { case .errors: 0; case .warnings: 1; case .noExplanation: 2; case .valid: 3 }
        }
        static func < (a: Self, b: Self) -> Bool { a.rank < b.rank }
    }

    private var allRows: [BankRow] {
        document.questions.enumerated().map { i, q in
            let mine = issues.filter { $0.path == "questions[\(i)]" || $0.path.hasPrefix("questions[\(i)].") }
            let errs = mine.count { $0.severity == .error }
            let status: RowStatus = errs > 0 ? .errors(errs) : !mine.isEmpty ? .warnings(mine.count)
                : q.explanation?.nonEmpty == nil ? .noExplanation : .valid
            return BankRow(
                index: i, questionID: q.id ?? "",
                prompt: (q.prompt ?? "").components(separatedBy: .newlines).first ?? "",
                kind: q.kind.flatMap(QuestionKind.init)?.label ?? (q.kind ?? "?"),
                topic: q.topic?.nonEmpty ?? "General", points: q.points ?? 1, status: status)
        }
    }

    private var rows: [BankRow] {
        let text = search.trimmingCharacters(in: .whitespaces)
        return allRows.filter { r in
            let q = document.questions[r.index]
            if let kindFilter, q.kind != kindFilter.rawValue { return false }
            if let topicFilter, r.topic != topicFilter { return false }
            switch statusFilter {
            case .errors?: if case .errors = r.status {} else { return false }
            case .needsAttention?: if r.status == .valid { return false }
            case .valid?: if r.status != .valid { return false }
            case nil: break
            }
            return text.isEmpty || [r.questionID, r.prompt, r.topic, r.kind].contains { $0.localizedCaseInsensitiveContains(text) }
        }
        .sorted(using: sortOrder)
    }

    var body: some View {
        ConsolePage(crumbs: [document.exam.title ?? "Untitled Exam"], title: "Question bank (\(document.questions.count))") {
            actionsMenu
            Button("Import…") { importing = true }
                .help("Add questions from a CSV, Aiken, GIFT or MockiExams JSON file")
            Button("Create Question", action: create).buttonStyle(.borderedProminent).tint(Palette.brandFill)
        } content: {
            VSplitView {
                VStack(spacing: 0) {
                    filterBar
                    table
                }
                .frame(minHeight: 220)
                bottomPanel
            }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.commaSeparatedText, .plainText, .json, UTType(filenameExtension: "gift") ?? .plainText, .data], allowsMultipleSelection: true) { result in
            if case let .success(urls) = result { importFiles(urls) }
        }
        .alert("Set topic", isPresented: Binding(get: { bulkTopic != nil }, set: { if !$0 { bulkTopic = nil } })) {
            TextField("Topic", text: Binding(get: { bulkTopic ?? "" }, set: { bulkTopic = $0 }))
            Button("Apply") { apply { $0.topic = bulkTopic?.nonEmpty }; bulkTopic = nil }
            Button("Cancel", role: .cancel) { bulkTopic = nil }
        } message: { Text("Applies to \(selection.count) selected question\(selection.count == 1 ? "" : "s").") }
        .alert("Set points", isPresented: Binding(get: { bulkPoints != nil }, set: { if !$0 { bulkPoints = nil } })) {
            TextField("Points", text: Binding(get: { bulkPoints ?? "" }, set: { bulkPoints = $0 }))
            Button("Apply") {
                if let p = Double(bulkPoints ?? ""), p > 0 { apply { $0.points = p == 1 ? nil : p } }
                bulkPoints = nil
            }
            Button("Cancel", role: .cancel) { bulkPoints = nil }
        } message: { Text("Applies to \(selection.count) selected question\(selection.count == 1 ? "" : "s").") }
    }

    // MARK: Filters

    private var filterBar: some View {
        HStack(spacing: 8) {
            Image(systemName: "line.3.horizontal.decrease").foregroundStyle(.secondary)
            filterMenu("Kind", value: kindFilter?.label) {
                ForEach(QuestionKind.allCases, id: \.self) { k in Button(k.label) { kindFilter = k } }
            } clear: { kindFilter = nil }
            filterMenu("Topic", value: topicFilter) {
                ForEach(document.topics, id: \.self) { t in Button(t) { topicFilter = t } }
            } clear: { topicFilter = nil }
            filterMenu("Status", value: statusFilter?.rawValue) {
                ForEach(StatusFilter.allCases, id: \.self) { s in Button(s.rawValue) { statusFilter = s } }
            } clear: { statusFilter = nil }
            if !search.isEmpty {
                chip("Text: \(search)") { search = "" }
            }
            Spacer()
            Text(rows.count == document.questions.count ? "\(rows.count) questions" : "\(rows.count) of \(document.questions.count)")
                .font(.callout).foregroundStyle(.secondary).monospacedDigit()
        }
        .padding(.horizontal, 22).padding(.bottom, 8)
    }

    @ViewBuilder
    private func filterMenu<Items: View>(_ name: String, value: String?, @ViewBuilder items: () -> Items, clear: @escaping () -> Void) -> some View {
        if let value {
            chip("\(name) = \(value)", clear: clear)
        } else {
            Menu(name) { items() }.menuStyle(.button).fixedSize()
        }
    }

    private func chip(_ text: String, clear: @escaping () -> Void) -> some View {
        Button(action: clear) {
            HStack(spacing: 4) { Text(text).lineLimit(1); Image(systemName: "xmark").font(.caption2.bold()) }
                .font(.callout)
                .padding(.horizontal, 9).padding(.vertical, 3)
                .foregroundStyle(Palette.brand)
                .background(Palette.brandSoft, in: Capsule())
                .overlay(Capsule().strokeBorder(Palette.brand.opacity(0.4)))
        }
        .buttonStyle(.plain)
        .help("Remove filter")
    }

    // MARK: Table

    private var table: some View {
        Table(rows, selection: $selection, sortOrder: $sortOrder, columnCustomization: $columns) {
            TableColumn("#", value: \.index) { Text("\($0.index + 1)").monospacedDigit().foregroundStyle(.secondary) }
                .width(36)
                .customizationID("number")
            TableColumn("ID", value: \.questionID) { Text($0.questionID).font(.callout.monospaced()) }
                .width(min: 90, ideal: 130)
                .customizationID("id")
                .defaultVisibility(.hidden)
            TableColumn("Question", value: \.prompt) { Text(markdown($0.prompt)).lineLimit(1) }
                .width(min: 200, ideal: 380)
                .customizationID("question")
                .disabledCustomizationBehavior(.visibility)
            TableColumn("Kind", value: \.kind) { r in
                Label(r.kind, systemImage: QuestionKind.allCases.first { $0.label == r.kind }.map(KindIcon.name) ?? "questionmark")
            }
            .width(min: 90, ideal: 130)
            .customizationID("kind")
            TableColumn("Topic", value: \.topic).width(min: 80, ideal: 120).customizationID("topic")
            TableColumn("Points", value: \.points) { Text($0.points.formatted()).monospacedDigit() }.width(54).customizationID("points")
            TableColumn("Status", value: \.status) { StatusBadge(status: $0.status) }.width(min: 90, ideal: 120).customizationID("status")
        }
        .contextMenu(forSelectionType: Int.self) { ids in
            if ids.count == 1, let i = ids.first {
                Button("Preview") { preview(i) }
                Button("Duplicate") { duplicate(i) }
            }
            Button("Delete \(ids.count == 1 ? "Question" : "\(ids.count) Questions")", role: .destructive) { delete(ids) }
        } primaryAction: { ids in
            if ids.count == 1, let i = ids.first { selection = [i] }
        }
        .onDeleteCommand { delete(selection) }
        .overlay {
            if rows.isEmpty {
                ContentUnavailableView {
                    Label(document.questions.isEmpty ? "No questions yet" : "No matches", systemImage: "list.bullet.rectangle")
                } description: {
                    Text(document.questions.isEmpty ? "Create a question or import a file to start the bank." : "Clear a filter to see more questions.")
                } actions: {
                    if document.questions.isEmpty { Button("Create Question", action: create) }
                }
            }
        }
    }

    // MARK: Bottom panel

    @ViewBuilder
    private var bottomPanel: some View {
        if selection.count == 1, let i = selection.first, document.questions.indices.contains(i) {
            VStack(spacing: 0) {
                HStack(spacing: 10) {
                    Text(document.questions[i].id ?? "Question").font(.headline.monospaced())
                    Text(document.questions[i].kind.flatMap(QuestionKind.init)?.label ?? "").foregroundStyle(.secondary)
                    Spacer()
                    Button { moveSelected(-1) } label: { Image(systemName: "arrow.up") }.help("Move up in the bank").disabled(i == 0)
                    Button { moveSelected(1) } label: { Image(systemName: "arrow.down") }.help("Move down in the bank").disabled(i == document.questions.count - 1)
                    Button("Preview") { preview(i) }
                    Button("Duplicate") { duplicate(i) }
                    Button { selection = [] } label: { Image(systemName: "xmark") }.help("Close the editor")
                }
                .padding(.horizontal, 16).padding(.vertical, 8)
                .background(Palette.consolePanel)
                Divider()
                QuestionEditor(document: $document, index: i, onRename: { _ in })
                    .id(i)
            }
            .frame(minHeight: 280, idealHeight: 460)
        } else if selection.count > 1 {
            VStack(spacing: 10) {
                Text("\(selection.count) questions selected").font(.headline)
                Text("Use Actions to set a topic, set points, add to a section, or delete them together.")
                    .foregroundStyle(.secondary)
            }
            .frame(maxWidth: .infinity, minHeight: 120)
            .background(Palette.consolePanel)
        } else {
            Text("Select a question to edit it here.")
                .foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, minHeight: 60)
                .background(Palette.consolePanel)
        }
    }

    // MARK: Actions

    private var actionsMenu: some View {
        Menu("Actions") {
            Button("Set Topic…") { bulkTopic = "" }
            Button("Set Points…") { bulkPoints = "" }
            Menu("Partial Credit") {
                Button("On") { apply { $0.partialCredit = true } }
                Button("Off") { apply { $0.partialCredit = false } }
                Button("Exam Default") { apply { $0.partialCredit = nil } }
            }
            Menu("Add to Section") {
                ForEach(Array((document.exam.sections ?? []).enumerated()), id: \.offset) { si, s in
                    Button(s.title?.nonEmpty ?? s.id ?? "Section \(si + 1)") { addToSection(si) }
                }
                if (document.exam.sections ?? []).isEmpty { Text("No sections yet") }
            }
            Divider()
            Button("Delete", role: .destructive) { delete(selection) }
        }
        .disabled(selection.isEmpty)
        .fixedSize()
    }

    private func apply(_ change: (inout QuestionDocument) -> Void) {
        for i in selection where document.questions.indices.contains(i) { change(&document.questions[i]) }
    }

    private func addToSection(_ si: Int) {
        let ids = selection.sorted().compactMap { document.questions[safe: $0]?.id }
        guard var sections = document.exam.sections, sections.indices.contains(si) else { return }
        for s in sections.indices { sections[s].questions?.removeAll { ids.contains($0) } }
        sections[si].questions = (sections[si].questions ?? []) + ids
        document.exam.sections = sections
    }

    private func delete(_ indices: Set<Int>) {
        let ids = indices.compactMap { document.questions[safe: $0]?.id }
        for id in ids { document.deleteQuestion(id) }
        selection = []
    }

    private func duplicate(_ i: Int) {
        guard let id = document.questions[safe: i]?.id, let copy = document.duplicateQuestion(id),
              let j = document.questions.firstIndex(where: { $0.id == copy }) else { return }
        selection = [j]
    }

    private func moveSelected(_ d: Int) {
        guard let i = selection.first, document.questions.indices.contains(i + d) else { return }
        document.questions.swapAt(i, i + d)
        selection = [i + d]
    }

    private func importFiles(_ urls: [URL]) {
        var added = 0, skipped: [String] = []
        for url in urls {
            let scoped = url.startAccessingSecurityScopedResource()
            defer { if scoped { url.stopAccessingSecurityScopedResource() } }
            guard let data = try? Data(contentsOf: url) else { skipped.append("\(url.lastPathComponent): can't read"); continue }
            if url.pathExtension.lowercased() == "json", let doc = try? JSONDecoder().decode(ExamDocument.self, from: data) {
                added += document.append(doc.questions ?? [])
                continue
            }
            guard let text = String(data: data, encoding: .utf8),
                  let format = SourceFormat.detect(fileName: url.lastPathComponent, text: text) else {
                skipped.append("\(url.lastPathComponent): not CSV, Aiken, GIFT or MockiExams JSON")
                continue
            }
            let c = Converter.convert(text, format: format, id: "import", title: "Import")
            added += document.append(c.document.questions ?? [])
            skipped += c.skipped.map { "\(url.lastPathComponent) \($0.path): \($0.message)" }
        }
        message("Imported \(added) question\(added == 1 ? "" : "s").\(skipped.isEmpty ? "" : "\n\nSkipped:\n" + skipped.prefix(10).joined(separator: "\n"))")
    }
}

struct StatusBadge: View {
    let status: QuestionBankPage.RowStatus

    var body: some View {
        let (text, color): (String, Color) = switch status {
        case let .errors(n): ("\(n) error\(n == 1 ? "" : "s")", Palette.err)
        case let .warnings(n): ("\(n) warning\(n == 1 ? "" : "s")", Palette.warn)
        case .noExplanation: ("No explanation", .secondary)
        case .valid: ("Valid", Palette.ok)
        }
        HStack(spacing: 5) {
            Circle().fill(color).frame(width: 7, height: 7)
            Text(text).font(.callout.weight(.medium)).foregroundStyle(color)
        }
    }
}
