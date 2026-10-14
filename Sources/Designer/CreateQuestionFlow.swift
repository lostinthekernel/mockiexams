import MockiCore
import SwiftUI

/// Create a question in steps. Each step is checked before moving on, so new questions start valid.
struct CreateQuestionFlow: View {
    @Binding var document: ExamProjectDocument
    let onCreate: (Int) -> Void

    @Environment(\.dismiss) private var dismiss
    @State private var step = 0
    @State private var draft: QuestionDocument?

    private let steps = ["Question type", "Prompt and exhibits", "Answer", "Scoring", "Review and create"]
    private let answerKeys = ["choices", "items", "targets", "rows", "statements", "blanks", "image", "regions", "answer"]

    var body: some View {
        HStack(spacing: 0) {
            stepList
                .frame(width: 220)
                .background(Palette.consolePanel)
            Divider()
            VStack(spacing: 0) {
                VStack(alignment: .leading, spacing: 4) {
                    Text("Create question").font(.caption).foregroundStyle(.secondary)
                    Text(steps[step]).font(.title2.weight(.semibold))
                }
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 22).padding(.top, 18).padding(.bottom, 8)
                content
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                if !stepIssues.isEmpty {
                    VStack(alignment: .leading, spacing: 4) {
                        ForEach(Array(stepIssues.prefix(4).enumerated()), id: \.offset) { _, issue in
                            Label(describe(issue), systemImage: "exclamationmark.circle.fill")
                                .foregroundStyle(Palette.err).font(.callout)
                        }
                    }
                    .frame(maxWidth: .infinity, alignment: .leading)
                    .padding(.horizontal, 22).padding(.vertical, 8)
                    .background(Palette.errSoft)
                }
                Divider()
                HStack {
                    Button("Cancel", role: .cancel) { dismiss() }.keyboardShortcut(.cancelAction)
                    Spacer()
                    if step > 0 { Button("Previous") { step -= 1 } }
                    if step < steps.count - 1 {
                        Button("Next: \(steps[step + 1])") { step += 1 }
                            .buttonStyle(.borderedProminent).tint(Palette.brandFill)
                            .keyboardShortcut(.defaultAction)
                            .disabled(draft == nil || !stepIssues.isEmpty)
                    } else {
                        Button("Create Question", action: create)
                            .buttonStyle(.borderedProminent).tint(Palette.brandFill)
                            .keyboardShortcut(.defaultAction)
                            .disabled(!allIssues.isEmpty)
                    }
                }
                .padding(14)
            }
        }
        .frame(minWidth: 940, idealWidth: 1040, minHeight: 640, idealHeight: 720)
        .background(Palette.consolePage)
    }

    // MARK: Steps

    private var stepList: some View {
        VStack(alignment: .leading, spacing: 2) {
            ForEach(steps.indices, id: \.self) { i in
                Button { if canJump(to: i) { step = i } } label: {
                    HStack(alignment: .top, spacing: 10) {
                        ZStack {
                            Circle().strokeBorder(i <= step ? Palette.brand : Color.secondary, lineWidth: 1.5)
                                .background(Circle().fill(i == step ? Palette.brand : .clear))
                            if i < step { Image(systemName: "checkmark").font(.caption.bold()).foregroundStyle(Palette.brand) }
                            else { Text("\(i + 1)").font(.caption.bold()).foregroundStyle(i == step ? .white : .secondary) }
                        }
                        .frame(width: 22, height: 22)
                        VStack(alignment: .leading, spacing: 1) {
                            Text(steps[i]).fontWeight(i == step ? .semibold : .regular).foregroundStyle(i <= step ? .primary : .secondary)
                            if let s = summary(i) { Text(s).font(.caption).foregroundStyle(.secondary).lineLimit(1) }
                        }
                    }
                    .padding(.vertical, 7)
                    .contentShape(Rectangle())
                }
                .buttonStyle(.plain)
            }
            Spacer()
        }
        .padding(16)
    }

    private func summary(_ i: Int) -> String? {
        guard let d = draft else { return nil }
        switch i {
        case 0: return d.kind.flatMap(QuestionKind.init)?.label
        case 1: return d.exhibits.map { "\($0.count) exhibit\($0.count == 1 ? "" : "s")" }
        case 3: return "\((d.points ?? 1).formatted()) pt\(d.points == 1 || d.points == nil ? "" : "s")"
        default: return nil
        }
    }

    private func canJump(to i: Int) -> Bool {
        i <= step || (draft != nil && (step..<i).allSatisfy { issues(for: $0).isEmpty })
    }

    @ViewBuilder
    private var content: some View {
        if step == 0 {
            typePicker
        } else if let binding = draftBinding {
            Form {
                switch step {
                case 1:
                    Section("Question") {
                        TextField("Id", text: binding.id.orEmpty).font(.body.monospaced())
                        TopicField(topic: binding.topic, topics: document.topics)
                        if !(document.exam.caseStudies ?? []).isEmpty {
                            Picker("Case study", selection: binding.caseStudy.orEmpty) {
                                Text("None").tag("")
                                ForEach(document.exam.caseStudies ?? [], id: \.id) { Text($0.title ?? $0.id ?? "").tag($0.id ?? "") }
                            }
                        }
                        TextArea(title: "Prompt (Markdown; ``` for code blocks)", text: binding.prompt.orEmpty, minHeight: 120)
                    }
                    ExhibitsEditor(exhibits: binding.exhibits, document: $document)
                case 2:
                    Section {
                        Text(instructionText).foregroundStyle(.secondary)
                    }
                    KindEditor(question: binding, document: $document)
                case 3:
                    Section("Scoring") {
                        TextField("Points", value: Binding(get: { binding.wrappedValue.points ?? 1 }, set: { binding.wrappedValue.points = $0 == 1 ? nil : $0 }), format: .number)
                        Picker("Partial credit", selection: Binding(
                            get: { binding.wrappedValue.partialCredit.map { $0 ? 1 : 0 } ?? -1 },
                            set: { binding.wrappedValue.partialCredit = $0 == -1 ? nil : $0 == 1 })) {
                            Text("Exam default (\(document.exam.partialCredit == true ? "on" : "off"))").tag(-1)
                            Text("On: award a share for partly correct answers").tag(1)
                            Text("Off: all or nothing").tag(0)
                        }
                    }
                    Section("Explanation") {
                        TextArea(title: "Shown after checking in practice, and in the review", text: binding.explanation.orEmpty, minHeight: 100)
                    }
                default:
                    reviewStep(binding.wrappedValue)
                }
            }
            .formStyle(.grouped)
        }
    }

    private var typePicker: some View {
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 12)], spacing: 12) {
                ForEach(QuestionKind.allCases, id: \.self) { kind in
                    let chosen = draft?.kind == kind.rawValue
                    Button { choose(kind) } label: {
                        VStack(alignment: .leading, spacing: 8) {
                            Image(systemName: KindIcon.name(kind)).font(.title2).foregroundStyle(Palette.brand)
                            Text(kind.label).font(.headline)
                            Text(KindIcon.blurb(kind)).font(.callout).foregroundStyle(.secondary)
                                .fixedSize(horizontal: false, vertical: true)
                        }
                        .frame(maxWidth: .infinity, minHeight: 110, alignment: .topLeading)
                        .padding(14)
                        .background(chosen ? Palette.brandSoft : Palette.consolePanel, in: RoundedRectangle(cornerRadius: 8))
                        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(chosen ? Palette.brand : Palette.consoleLine, lineWidth: chosen ? 2 : 1))
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(chosen ? .isSelected : [])
                }
            }
            .padding(.horizontal, 22).padding(.vertical, 8)
        }
    }

    private func reviewStep(_ q: QuestionDocument) -> some View {
        Section("Summary") {
            LabeledContent("Id", value: q.id ?? "")
            LabeledContent("Kind", value: q.kind.flatMap(QuestionKind.init)?.label ?? "")
            LabeledContent("Topic", value: q.topic?.nonEmpty ?? "General")
            LabeledContent("Points", value: (q.points ?? 1).formatted())
            LabeledContent("Exhibits", value: "\(q.exhibits?.count ?? 0)")
            Text(markdown(q.prompt ?? "")).lineLimit(4)
            if allIssues.isEmpty {
                Label("Ready to add to the bank.", systemImage: "checkmark.circle.fill").foregroundStyle(Palette.ok)
            }
        }
    }

    private var instructionText: String {
        switch draft?.kind.flatMap(QuestionKind.init) {
        case .single: "Add the options and select the correct one."
        case .multiple: "Add the options and tick every correct one."
        case .trueFalse: "Choose whether the statement is true."
        case .yesNo: "Add statements and set Yes or No for each."
        case .ordering: "Add every option the candidate sees, then build the correct sequence. Options left out are distractors."
        case .matching: "Add targets, then items, and pick each item's correct target."
        case .dropdowns: "Add a list per blank or cell and mark its correct option."
        case .fillIn: "Add a field per answer and list every accepted answer."
        case .hotspot: "Choose an image, drag to draw areas, and tick the correct one."
        case nil: ""
        }
    }

    // MARK: Draft

    private var draftBinding: Binding<QuestionDocument>? {
        guard draft != nil else { return nil }
        return Binding(get: { draft! }, set: { draft = $0 })
    }

    private func choose(_ kind: QuestionKind) {
        let keep = draft
        var q = document.makeQuestion(kind)
        if let keep {
            q.id = keep.id
            q.prompt = keep.prompt
            q.topic = keep.topic
            q.exhibits = keep.exhibits
            q.caseStudy = keep.caseStudy
        } else {
            q.prompt = ""
        }
        // Start from blank text so sample answers from the templates don't slip into the bank.
        q.choices = q.choices?.map { var c = $0; c.text = ""; return c }
        q.items = q.items?.map { var c = $0; c.text = ""; return c }
        q.targets = q.targets?.map { var c = $0; c.text = ""; return c }
        q.statements = q.statements?.map { var s = $0; s.text = ""; return s }
        q.rows = q.rows?.map { var r = $0; r.label = ""; return r }
        q.blanks = q.blanks?.map { var b = $0; b.accept = [""]; b.pattern = nil; b.label = nil; return b }
        if kind == .single || kind == .multiple { q.answer = nil }
        draft = q
    }

    /// Validation of the draft alone, mapped to the step that can fix each problem.
    private var allIssues: [Issue] {
        guard let draft else { return [] }
        var probe = document
        probe.questions = [draft]
        probe.exam.sections = nil
        probe.exam.topicWeights = nil
        var issues = probe.validate().issues.filter { $0.severity == .error && $0.path.hasPrefix("questions[0]") }
        if let id = draft.id, document.questions.contains(where: { $0.id == id }) {
            issues.append(Issue(severity: .error, path: "questions[0].id", message: "Another question already uses the id \"\(id)\"."))
        }
        return issues
    }

    private func issues(for step: Int) -> [Issue] {
        allIssues.filter { issue in
            let field = issue.path.dropFirst("questions[0]".count).drop { $0 == "." }.prefix { $0 != "." && $0 != "[" }
            switch step {
            case 1: return ["", "id", "prompt", "topic", "exhibits", "caseStudy", "kind"].contains(String(field))
            case 2: return answerKeys.contains(String(field))
            case 3: return ["points", "partialCredit", "explanation"].contains(String(field))
            default: return false
            }
        }
    }

    /// "questions[0].choices[1].text: Missing" reads as "Choice 2 text: Missing".
    private func describe(_ issue: Issue) -> String {
        var path = String(issue.path.dropFirst("questions[0]".count).drop { $0 == "." })
        guard !path.isEmpty else { return issue.message }
        let names = ["prompt": "Prompt", "id": "Id", "choices": "Choice", "items": "Item", "targets": "Target", "rows": "Row",
                     "statements": "Statement", "blanks": "Field", "regions": "Area", "answer": "Correct answer", "image": "Image",
                     "exhibits": "Exhibit", "text": "text", "label": "label", "options": "options", "accept": "accepted answers", "points": "Points"]
        path = path.replacing(/\[(\d+)\]/) { m in " \(Int(m.1)! + 1)" }
        let words = path.split(separator: ".").map { part -> String in
            let head = part.split(separator: " ").first.map(String.init) ?? String(part)
            return part.replacingOccurrences(of: head, with: names[head] ?? head)
        }
        return "\(words.joined(separator: " ")): \(issue.message)"
    }

    private var stepIssues: [Issue] { step == 0 ? [] : issues(for: step) }

    private func create() {
        guard let draft, allIssues.isEmpty else { return }
        document.questions.append(draft)
        onCreate(document.questions.count - 1)
        dismiss()
    }
}
