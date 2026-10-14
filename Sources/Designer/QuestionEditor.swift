import MockiCore
import SwiftUI

struct QuestionEditor: View {
    @Binding var document: ExamProjectDocument
    let index: Int
    let onRename: (String) -> Void

    @State private var idDraft = ""
    @State private var idError: String?

    private var q: Binding<QuestionDocument> { $document.questions[index] }
    private var kind: QuestionKind? { q.wrappedValue.kind.flatMap(QuestionKind.init) }

    var body: some View {
        Form {
            Section("Question") {
                HStack {
                    TextField("Id", text: $idDraft).font(.body.monospaced()).onSubmit(commitID)
                    if idDraft != (q.wrappedValue.id ?? "") { Button("Rename", action: commitID) }
                }
                if let idError { Text(idError).font(.caption).foregroundStyle(.red) }
                Picker("Kind", selection: Binding(get: { kind ?? .single }, set: changeKind)) {
                    ForEach(QuestionKind.allCases, id: \.self) { Text($0.label).tag($0) }
                }
                TopicField(topic: q.topic, topics: Set(document.questions.compactMap(\.topic)).sorted())
                HStack {
                    TextField("Points", value: Binding(get: { q.wrappedValue.points ?? 1 }, set: { q.wrappedValue.points = $0 == 1 ? nil : $0 }), format: .number)
                        .frame(width: 140)
                    Picker("Partial credit", selection: Binding(
                        get: { q.wrappedValue.partialCredit.map { $0 ? 1 : 0 } ?? -1 },
                        set: { q.wrappedValue.partialCredit = $0 == -1 ? nil : $0 == 1 })) {
                        Text("Exam default (\(document.exam.partialCredit == true ? "on" : "off"))").tag(-1)
                        Text("On").tag(1)
                        Text("Off").tag(0)
                    }
                }
                if !(document.exam.caseStudies ?? []).isEmpty {
                    Picker("Case study", selection: q.caseStudy.orEmpty) {
                        Text("None").tag("")
                        ForEach(document.exam.caseStudies ?? [], id: \.id) { Text($0.title ?? $0.id ?? "").tag($0.id ?? "") }
                    }
                }
                TextArea(title: "Prompt (Markdown; ``` for code blocks)", text: q.prompt.orEmpty, minHeight: 90)
            }

            KindEditor(question: q, document: $document)

            ExhibitsEditor(exhibits: q.exhibits, document: $document)

            Section("Explanation") {
                TextArea(title: "Shown after checking in practice, and in the review", text: q.explanation.orEmpty, minHeight: 70)
            }
        }
        .formStyle(.grouped)
        .navigationTitle(q.wrappedValue.id ?? "Question")
        .onAppear { idDraft = q.wrappedValue.id ?? "" }
    }

    private func commitID() {
        let new = idDraft.trimmingCharacters(in: .whitespaces)
        let old = q.wrappedValue.id ?? ""
        guard new != old else { idError = nil; return }
        if new.isEmpty { idError = "Id can't be empty."; return }
        if document.questions.contains(where: { $0.id == new }) { idError = "Another question already uses \"\(new)\"."; return }
        idError = nil
        document.renameQuestion(old, to: new)
        onRename(new)
    }

    /// Switches kind, keeping shared fields and starting the kind-specific part from its template.
    private func changeKind(_ new: QuestionKind) {
        guard new != kind else { return }
        let old = q.wrappedValue
        var fresh = (try? JSONDecoder().decode(QuestionDocument.self, from: Data(Templates.question(new).utf8)))
            ?? QuestionDocument(id: old.id ?? "q", kind: new, prompt: "")
        if new == .hotspot { fresh.image = document.assets.keys.sorted().first; fresh.regions = []; fresh.answer = .strings([]) }
        fresh.id = old.id
        fresh.prompt = old.prompt
        fresh.topic = old.topic
        fresh.explanation = old.explanation
        fresh.points = old.points
        fresh.partialCredit = old.partialCredit
        fresh.exhibits = old.exhibits
        fresh.caseStudy = old.caseStudy
        q.wrappedValue = fresh
    }
}

/// The answer part of a question, one form per kind. Used by the editor and the create flow.
struct KindEditor: View {
    @Binding var question: QuestionDocument
    @Binding var document: ExamProjectDocument

    var body: some View {
        let kind = question.kind.flatMap(QuestionKind.init)
        Group {
            switch kind {
            case .single, .multiple: ChoicesEditor(question: $question, multi: kind == .multiple)
            case .ordering: OrderingEditor(question: $question)
            case .trueFalse: TrueFalseEditor(question: $question)
            case .yesNo: YesNoEditor(question: $question)
            case .matching: MatchingEditor(question: $question)
            case .dropdowns: DropdownsEditor(question: $question)
            case .fillIn: FillInEditor(question: $question)
            case .hotspot: HotspotQuestionEditor(question: $question, document: $document)
            case nil: Text("Unknown kind \"\(question.kind ?? "")\". Pick a kind above.").foregroundStyle(.red)
            }
        }
        // List rows read better as plain bordered fields than as "Label … value" form rows.
        .textFieldStyle(.roundedBorder)
        .labelsHidden()
    }
}

struct TopicField: View {
    @Binding var topic: String?
    let topics: [String]

    var body: some View {
        HStack {
            TextField("Topic / objective", text: $topic.orEmpty)
            if !topics.isEmpty {
                Menu {
                    ForEach(topics, id: \.self) { t in Button(t) { topic = t } }
                } label: { Image(systemName: "chevron.down.circle") }
                .menuStyle(.borderlessButton).fixedSize()
                .help("Use an existing topic")
            }
        }
    }
}

// MARK: - Shared list helpers

private func nextID(_ prefix: String, _ used: [String?]) -> String {
    let taken = Set(used.compactMap { $0 })
    return (1...).lazy.map { "\(prefix)\($0)" }.first { !taken.contains($0) }!
}

private func nextLetter(_ used: [String?]) -> String {
    let taken = Set(used.compactMap { $0 })
    return (0..<702).lazy.map { i in i < 26 ? String(UnicodeScalar(UInt8(97 + i))) : "c\(i)" }.first { !taken.contains($0) }!
}

extension QuestionDocument {
    var answerIDs: [String] { answer.flatMap { if case let .string(s) = $0 { [s] } else if case let .strings(l) = $0 { l } else { nil } } ?? [] }
}

struct RowButtons: View {
    let index: Int
    let count: Int
    let move: (Int, Int) -> Void
    let delete: () -> Void

    var body: some View {
        HStack(spacing: 2) {
            Button { move(index, -1) } label: { Image(systemName: "chevron.up") }.disabled(index == 0).accessibilityLabel("Move up")
            Button { move(index, 1) } label: { Image(systemName: "chevron.down") }.disabled(index == count - 1).accessibilityLabel("Move down")
            Button(role: .destructive, action: delete) { Image(systemName: "minus.circle") }.accessibilityLabel("Delete")
        }
        .buttonStyle(.borderless)
    }
}

// MARK: - Kinds

struct ChoicesEditor: View {
    @Binding var question: QuestionDocument
    let multi: Bool

    private var choices: [ChoiceDocument] { question.choices ?? [] }

    var body: some View {
        Section {
            ForEach(choices.indices, id: \.self) { i in
                let id = choices[i].id ?? ""
                let correct = question.answerIDs.contains(id)
                HStack {
                    Button { toggle(id) } label: {
                        Image(systemName: multi ? (correct ? "checkmark.square.fill" : "square") : (correct ? "largecircle.fill.circle" : "circle"))
                            .foregroundStyle(correct ? .green : .secondary)
                    }
                    .buttonStyle(.borderless)
                    .accessibilityLabel(correct ? "Correct answer" : "Mark as correct")
                    TextField("Choice \(i + 1)", text: Binding(get: { choices[i].text ?? "" }, set: { question.choices![i].text = $0 }))
                    RowButtons(index: i, count: choices.count, move: { a, d in question.choices!.swapAt(a, a + d) }) {
                        question.choices!.remove(at: i)
                        setAnswer(question.answerIDs.filter { $0 != id })
                    }
                }
            }
            Button("Add Choice") {
                question.choices = choices + [ChoiceDocument(id: nextLetter(choices.map(\.id)), text: "")]
            }
        } header: {
            Text(multi ? "Choices — tick every correct answer" : "Choices — select the correct answer")
        }
    }

    private func toggle(_ id: String) {
        if multi {
            var ids = question.answerIDs
            if let i = ids.firstIndex(of: id) { ids.remove(at: i) } else { ids.append(id) }
            setAnswer(ids)
        } else {
            setAnswer([id])
        }
    }

    private func setAnswer(_ ids: [String]) {
        question.answer = multi ? .strings(ids) : ids.first.map { .string($0) }
    }
}

struct OrderingEditor: View {
    @Binding var question: QuestionDocument
    private var choices: [ChoiceDocument] { question.choices ?? [] }
    private func text(_ id: String) -> String { choices.first { $0.id == id }?.text ?? id }

    var body: some View {
        Section("Options (all items the candidate sees)") {
            ForEach(choices.indices, id: \.self) { i in
                HStack {
                    TextField("Option \(i + 1)", text: Binding(get: { choices[i].text ?? "" }, set: { question.choices![i].text = $0 }))
                    if !question.answerIDs.contains(choices[i].id ?? "") { Text("distractor").font(.caption).foregroundStyle(.secondary) }
                    RowButtons(index: i, count: choices.count, move: { a, d in question.choices!.swapAt(a, a + d) }) {
                        let id = choices[i].id
                        question.choices!.remove(at: i)
                        question.answer = .strings(question.answerIDs.filter { $0 != id })
                    }
                }
            }
            Button("Add Option") {
                let id = nextLetter(choices.map(\.id))
                question.choices = choices + [ChoiceDocument(id: id, text: "")]
                question.answer = .strings(question.answerIDs + [id])
            }
        }
        Section("Correct order") {
            let order = question.answerIDs
            ForEach(order.indices, id: \.self) { i in
                HStack {
                    Text("\(i + 1).").monospacedDigit().foregroundStyle(.secondary)
                    Text(text(order[i])).frame(maxWidth: .infinity, alignment: .leading)
                    RowButtons(index: i, count: order.count, move: { a, d in var o = order; o.swapAt(a, a + d); question.answer = .strings(o) }) {
                        question.answer = .strings(order.filter { $0 != order[i] })
                    }
                }
            }
            let unused = choices.compactMap(\.id).filter { !order.contains($0) }
            if !unused.isEmpty {
                Menu("Add to Sequence") {
                    ForEach(unused, id: \.self) { id in Button(text(id)) { question.answer = .strings(order + [id]) } }
                }
                .fixedSize()
            }
            Text("Options left out of the sequence are distractors.").font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct TrueFalseEditor: View {
    @Binding var question: QuestionDocument
    var body: some View {
        Section("Answer") {
            Picker("The statement is", selection: Binding(
                get: { if case let .bool(b)? = question.answer { b } else { true } },
                set: { question.answer = .bool($0) })) {
                Text("True").tag(true)
                Text("False").tag(false)
            }
            .pickerStyle(.segmented)
        }
    }
}

struct YesNoEditor: View {
    @Binding var question: QuestionDocument
    private var statements: [StatementDocument] { question.statements ?? [] }

    var body: some View {
        Section("Statements") {
            ForEach(statements.indices, id: \.self) { i in
                HStack {
                    TextField("Statement \(i + 1)", text: Binding(get: { statements[i].text ?? "" }, set: { question.statements![i].text = $0 }))
                    Picker("Answer", selection: Binding(get: { statements[i].answer ?? true }, set: { question.statements![i].answer = $0 })) {
                        Text("Yes").tag(true)
                        Text("No").tag(false)
                    }
                    .pickerStyle(.segmented).labelsHidden().frame(width: 110)
                    RowButtons(index: i, count: statements.count, move: { a, d in question.statements!.swapAt(a, a + d) }) {
                        question.statements!.remove(at: i)
                    }
                }
            }
            Button("Add Statement") {
                question.statements = statements + [StatementDocument(id: nextID("s", statements.map(\.id)), text: "", answer: true)]
            }
        }
    }
}

struct MatchingEditor: View {
    @Binding var question: QuestionDocument
    private var items: [ChoiceDocument] { question.items ?? [] }
    private var targets: [ChoiceDocument] { question.targets ?? [] }
    private var map: [String: String] { if case let .map(m)? = question.answer { m } else { [:] } }

    var body: some View {
        Section("Targets (where items are dropped)") {
            ForEach(targets.indices, id: \.self) { i in
                HStack {
                    TextField("Target \(i + 1)", text: Binding(get: { targets[i].text ?? "" }, set: { question.targets![i].text = $0 }))
                    RowButtons(index: i, count: targets.count, move: { a, d in question.targets!.swapAt(a, a + d) }) {
                        let id = targets[i].id
                        question.targets!.remove(at: i)
                        question.answer = .map(map.filter { $0.value != id })
                    }
                }
            }
            Button("Add Target") { question.targets = targets + [ChoiceDocument(id: nextID("t", targets.map(\.id)), text: "")] }
        }
        Section("Items and their correct target") {
            ForEach(items.indices, id: \.self) { i in
                let id = items[i].id ?? ""
                HStack {
                    TextField("Item \(i + 1)", text: Binding(get: { items[i].text ?? "" }, set: { question.items![i].text = $0 }))
                    Image(systemName: "arrow.right").foregroundStyle(.secondary)
                    Picker("Target", selection: Binding(
                        get: { map[id] ?? "" },
                        set: { var m = map; m[id] = $0.isEmpty ? nil : $0; question.answer = .map(m) })) {
                        Text("Distractor (leave unplaced)").tag("")
                        ForEach(targets, id: \.id) { Text($0.text?.nonEmptyOr($0.id ?? "") ?? "").tag($0.id ?? "") }
                    }
                    .labelsHidden().frame(width: 230)
                    RowButtons(index: i, count: items.count, move: { a, d in question.items!.swapAt(a, a + d) }) {
                        question.items!.remove(at: i)
                        question.answer = .map(map.filter { $0.key != id })
                    }
                }
            }
            Button("Add Item") { question.items = items + [ChoiceDocument(id: nextID("i", items.map(\.id)), text: "")] }
        }
    }
}

struct DropdownsEditor: View {
    @Binding var question: QuestionDocument
    private var rows: [RowDocument] { question.rows ?? [] }

    var body: some View {
        Section("Dropdown lists") {
            ForEach(rows.indices, id: \.self) { i in
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        TextField("Label", text: Binding(get: { rows[i].label ?? "" }, set: { question.rows![i].label = $0 }))
                        RowButtons(index: i, count: rows.count, move: { a, d in question.rows!.swapAt(a, a + d) }) { question.rows!.remove(at: i) }
                    }
                    let options = rows[i].options ?? []
                    ForEach(options.indices, id: \.self) { j in
                        HStack {
                            Button { question.rows![i].answer = options[j] } label: {
                                Image(systemName: rows[i].answer == options[j] ? "largecircle.fill.circle" : "circle")
                                    .foregroundStyle(rows[i].answer == options[j] ? .green : .secondary)
                            }
                            .buttonStyle(.borderless)
                            .accessibilityLabel("Mark as correct")
                            TextField("Option", text: Binding(get: { options[j] }, set: { new in
                                if question.rows![i].answer == options[j] { question.rows![i].answer = new }
                                question.rows![i].options![j] = new
                            }))
                            Button(role: .destructive) { question.rows![i].options!.remove(at: j) } label: { Image(systemName: "minus.circle") }
                                .buttonStyle(.borderless)
                        }
                        .padding(.leading, 16)
                    }
                    Button("Add Option") { question.rows![i].options = options + ["Option \(options.count + 1)"] }
                        .padding(.leading, 16)
                }
                .padding(.vertical, 4)
            }
            Button("Add Dropdown") {
                question.rows = rows + [RowDocument(id: nextID("r", rows.map(\.id)), label: "", options: ["Option 1", "Option 2"], answer: "Option 1")]
            }
        }
    }
}

struct FillInEditor: View {
    @Binding var question: QuestionDocument
    private var blanks: [BlankDocument] { question.blanks ?? [] }

    var body: some View {
        Section {
            ForEach(blanks.indices, id: \.self) { i in
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        TextField("Field label (optional)", text: Binding(get: { blanks[i].label ?? "" }, set: { question.blanks![i].label = $0.isEmpty ? nil : $0 }))
                        Toggle("Case sensitive", isOn: Binding(get: { blanks[i].caseSensitive ?? false }, set: { question.blanks![i].caseSensitive = $0 ? true : nil }))
                        RowButtons(index: i, count: blanks.count, move: { a, d in question.blanks!.swapAt(a, a + d) }) { question.blanks!.remove(at: i) }
                    }
                    let accept = blanks[i].accept ?? []
                    ForEach(accept.indices, id: \.self) { j in
                        HStack {
                            TextField("Accepted answer", text: Binding(get: { accept[j] }, set: { question.blanks![i].accept![j] = $0 }))
                                .font(.body.monospaced())
                            Button(role: .destructive) { question.blanks![i].accept!.remove(at: j) } label: { Image(systemName: "minus.circle") }
                                .buttonStyle(.borderless)
                        }
                        .padding(.leading, 16)
                    }
                    Button("Add Accepted Answer") { question.blanks![i].accept = accept + [""] }.padding(.leading, 16)
                    TextField("Or a regular expression the whole answer must match (optional)", text: Binding(
                        get: { blanks[i].pattern ?? "" }, set: { question.blanks![i].pattern = $0.isEmpty ? nil : $0 }))
                        .font(.body.monospaced()).padding(.leading, 16)
                }
                .padding(.vertical, 4)
            }
            Button("Add Field") { question.blanks = blanks + [BlankDocument(id: nextID("b", blanks.map(\.id)), accept: [""])] }
        } header: {
            Text("Fields")
        } footer: {
            Text("Spacing is normalized before comparing, so \"show  ip route \" matches \"show ip route\".").font(.caption)
        }
    }
}

struct HotspotQuestionEditor: View {
    @Binding var question: QuestionDocument
    @Binding var document: ExamProjectDocument
    @State private var selected: String?

    private var regions: [RegionDocument] { question.regions ?? [] }

    var body: some View {
        Section {
            AssetPicker(selection: $question.image.orEmpty, document: $document)
            if let path = question.image, let data = document.assets[path] {
                HotspotCanvas(imageData: data, regions: Binding(get: { regions }, set: { question.regions = $0 }),
                              correct: Set(question.answerIDs), selected: $selected)
                    .frame(minHeight: 260)
                Text("Drag on the image to draw an area. Drag an area to move it, or its corner to resize. Green areas are correct.")
                    .font(.caption).foregroundStyle(.secondary)
            }
        } header: {
            Text("Image and clickable areas")
        }
        Section("Areas") {
            ForEach(regions.indices, id: \.self) { i in
                let id = regions[i].id ?? ""
                HStack {
                    Toggle("Correct", isOn: Binding(
                        get: { question.answerIDs.contains(id) },
                        set: { on in question.answer = .strings(on ? question.answerIDs + [id] : question.answerIDs.filter { $0 != id }) }))
                    .toggleStyle(.checkbox).labelsHidden()
                    .accessibilityLabel("Correct area")
                    TextField("Label", text: Binding(get: { regions[i].label ?? "" }, set: { question.regions![i].label = $0.isEmpty ? nil : $0 }))
                    Text(String(format: "x %.2f  y %.2f  %.2f×%.2f", regions[i].x ?? 0, regions[i].y ?? 0, regions[i].width ?? 0, regions[i].height ?? 0))
                        .font(.caption.monospaced()).foregroundStyle(.secondary)
                    Button(role: .destructive) {
                        question.regions!.remove(at: i)
                        question.answer = .strings(question.answerIDs.filter { $0 != id })
                    } label: { Image(systemName: "minus.circle") }
                    .buttonStyle(.borderless)
                }
                .padding(4)
                .background(selected == id ? Color.accentColor.opacity(0.12) : .clear, in: RoundedRectangle(cornerRadius: 6))
                .onTapGesture { selected = id }
            }
            if regions.isEmpty { Text("No areas yet. Draw one on the image.").foregroundStyle(.secondary) }
        }
    }
}
