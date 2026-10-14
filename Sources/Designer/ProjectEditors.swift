import AppKit
import MockiCore
import SwiftUI
import UniformTypeIdentifiers

extension Binding where Value == String? {
    /// Edits an optional string as text; empty text stores nil.
    var orEmpty: Binding<String> {
        Binding<String>(get: { wrappedValue ?? "" }, set: { new in
            // Writing an unchanged value would still mark the document edited.
            if (wrappedValue ?? "") != new { wrappedValue = new.isEmpty ? nil : new }
        })
    }
}

extension Binding where Value == Bool? {
    func or(_ fallback: Bool) -> Binding<Bool> {
        Binding<Bool>(get: { wrappedValue ?? fallback }, set: { if (wrappedValue ?? fallback) != $0 { wrappedValue = $0 } })
    }
}

/// A multi-line text field sized for prompts and scenarios.
struct TextArea: View {
    let title: String
    @Binding var text: String
    var minHeight: CGFloat = 80
    var mono = false

    var body: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text(title).font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $text)
                .font(mono ? .system(.body, design: .monospaced) : .body)
                .frame(minHeight: minHeight)
                .scrollContentBackground(.hidden)
                .padding(6)
                .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 6))
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(.quaternary))
                .accessibilityLabel(title)
        }
    }
}

// MARK: - Exam settings

struct ExamSettingsView: View {
    @Binding var document: ExamProjectDocument

    var body: some View {
        Form {
            Section("Exam") {
                TextField("Title", text: $document.exam.title.orEmpty)
                TextField("Id", text: $document.exam.id.orEmpty)
                    .font(.body.monospaced())
                Text("Re-installing an exam with the same id replaces it in the Library and keeps its history.")
                    .font(.caption).foregroundStyle(.secondary)
                TextArea(title: "Description", text: $document.exam.description.orEmpty, minHeight: 60)
            }
            Section("Timing") {
                Toggle("Time limit", isOn: Binding(
                    get: { document.exam.timeLimitMinutes != nil },
                    set: { document.exam.timeLimitMinutes = $0 ? 60 : nil }))
                if document.exam.timeLimitMinutes != nil {
                    Stepper(value: Binding(get: { document.exam.timeLimitMinutes ?? 60 }, set: { document.exam.timeLimitMinutes = $0 }), in: 1...600, step: 5) {
                        LabeledContent("Minutes", value: "\(Int(document.exam.timeLimitMinutes ?? 60))")
                    }
                }
                Toggle("Allow going back to earlier questions", isOn: $document.exam.allowBack.or(true))
                Text("Applies in exam simulation. Practice mode always allows going back.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            Section("Scoring") {
                Stepper(value: Binding(get: { document.exam.passPercent ?? 70 }, set: { document.exam.passPercent = $0 }), in: 0...100) {
                    LabeledContent("Pass mark", value: "\(document.exam.passPercent ?? 70)%")
                }
                Toggle("Scaled score", isOn: Binding(
                    get: { document.exam.scale != nil },
                    set: { document.exam.scale = $0 ? ScoreScale(min: 100, max: 900, pass: 700) : nil }))
                if let scale = document.exam.scale {
                    HStack {
                        TextField("Min", value: Binding(get: { scale.min }, set: { document.exam.scale = ScoreScale(min: $0, max: scale.max, pass: scale.pass) }), format: .number)
                        TextField("Max", value: Binding(get: { scale.max }, set: { document.exam.scale = ScoreScale(min: scale.min, max: $0, pass: scale.pass) }), format: .number)
                        TextField("Pass", value: Binding(get: { scale.pass }, set: { document.exam.scale = ScoreScale(min: scale.min, max: scale.max, pass: $0) }), format: .number)
                    }
                    Text("When set, pass/fail uses the scaled pass score. The scale is a linear estimate.")
                        .font(.caption).foregroundStyle(.secondary)
                }
                Toggle("Partial credit by default", isOn: $document.exam.partialCredit.or(false))
                Text("Questions can override this. Partial credit awards a share of the points for partly correct answers.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            BlueprintEditor(document: $document)
            Section("Summary") {
                let qs = document.questions
                LabeledContent("Questions in bank", value: "\(qs.count)")
                LabeledContent("Total points", value: points(qs.reduce(0) { $0 + ($1.points ?? 1) }))
                ForEach(QuestionKind.allCases, id: \.self) { kind in
                    let n = qs.count { $0.kind == kind.rawValue }
                    if n > 0 { LabeledContent(kind.label, value: "\(n)") }
                }
            }
        }
        .formStyle(.grouped)
    }
}

// MARK: - Sections

struct SectionsEditor: View {
    @Binding var document: ExamProjectDocument

    private var sections: [SectionDocument] { document.exam.sections ?? [] }
    private var placed: Set<String> { Set(sections.flatMap { $0.questions ?? [] }) }

    private func prompt(_ id: String) -> String {
        document.questions.first { $0.id == id }?.prompt?.components(separatedBy: .newlines).first ?? id
    }

    var body: some View {
        Form {
            if sections.isEmpty {
                Section {
                    Text("No sections: the exam asks every question in the bank, in bank order.")
                        .foregroundStyle(.secondary)
                    Button("Create a Section with All Questions") {
                        document.exam.sections = [SectionDocument(id: "section-1", title: "Section 1", questions: document.questions.compactMap(\.id))]
                    }
                }
            }
            ForEach(sections.indices, id: \.self) { i in
                Section {
                    TextField("Title", text: Binding(get: { sections[i].title ?? "" }, set: { document.exam.sections![i].title = $0 }))
                    Toggle("Shuffle within this section", isOn: Binding(get: { sections[i].shuffle ?? true }, set: { document.exam.sections![i].shuffle = $0 }))
                    let ids = sections[i].questions ?? []
                    ForEach(Array(ids.enumerated()), id: \.offset) { j, id in
                        HStack {
                            Text("\(j + 1).").monospacedDigit().foregroundStyle(.secondary)
                            Text(prompt(id)).lineLimit(1)
                            Text(id).font(.caption.monospaced()).foregroundStyle(.secondary)
                            Spacer()
                            Button { moveQuestion(i, j, -1) } label: { Image(systemName: "chevron.up") }.disabled(j == 0)
                            Button { moveQuestion(i, j, 1) } label: { Image(systemName: "chevron.down") }.disabled(j == ids.count - 1)
                            Button { document.exam.sections![i].questions!.remove(at: j) } label: { Image(systemName: "minus.circle") }
                                .accessibilityLabel("Remove from section")
                        }
                        .buttonStyle(.borderless)
                    }
                    let unplaced = document.questions.compactMap(\.id).filter { !placed.contains($0) }
                    Menu("Add Questions") {
                        Button("All \(unplaced.count) unplaced") { document.exam.sections![i].questions = ids + unplaced }
                            .disabled(unplaced.isEmpty)
                        Divider()
                        ForEach(unplaced, id: \.self) { id in
                            Button("\(id) — \(prompt(id))") { document.exam.sections![i].questions = ids + [id] }
                        }
                    }
                    .disabled(unplaced.isEmpty)
                } header: {
                    HStack {
                        Text("Section \(i + 1)")
                        Spacer()
                        Button { moveSection(i, -1) } label: { Image(systemName: "arrow.up") }.disabled(i == 0)
                        Button { moveSection(i, 1) } label: { Image(systemName: "arrow.down") }.disabled(i == sections.count - 1)
                        Button(role: .destructive) { removeSection(i) } label: { Image(systemName: "trash") }
                    }
                    .buttonStyle(.borderless)
                }
            }
            if !sections.isEmpty {
                Section {
                    Button("Add Section") {
                        let used = Set(sections.compactMap(\.id))
                        let n = ((sections.count + 1)...).first { !used.contains("section-\($0)") }!
                        document.exam.sections!.append(SectionDocument(id: "section-\(n)", title: "Section \(n)"))
                    }
                    let unplaced = document.questions.count { !placed.contains($0.id ?? "") }
                    if unplaced > 0 {
                        Text("\(unplaced) bank question\(unplaced == 1 ? " is" : "s are") in no section and won't be asked.")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                }
            }
        }
        .formStyle(.grouped)
    }

    private func moveQuestion(_ s: Int, _ j: Int, _ d: Int) {
        guard var ids = document.exam.sections?[s].questions, ids.indices.contains(j + d) else { return }
        ids.swapAt(j, j + d)
        document.exam.sections![s].questions = ids
    }

    private func moveSection(_ i: Int, _ d: Int) {
        guard var list = document.exam.sections, list.indices.contains(i + d) else { return }
        list.swapAt(i, i + d)
        document.exam.sections = list
    }

    private func removeSection(_ i: Int) {
        document.exam.sections!.remove(at: i)
        if document.exam.sections!.isEmpty { document.exam.sections = nil }
    }
}

// MARK: - Assets

struct AssetsView: View {
    @Binding var document: ExamProjectDocument
    @State private var importing = false

    var body: some View {
        ScrollView {
            LazyVGrid(columns: [GridItem(.adaptive(minimum: 220), spacing: 16)], spacing: 16) {
                ForEach(document.assets.keys.sorted(), id: \.self) { path in
                    let uses = document.usage(of: path)
                    VStack(alignment: .leading, spacing: 6) {
                        Thumbnail(data: document.assets[path] ?? Data())
                            .frame(height: 140)
                            .frame(maxWidth: .infinity)
                            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
                        Text(path).font(.caption.monospaced()).lineLimit(1).truncationMode(.middle)
                        HStack {
                            Text(uses == 0 ? "Unused" : "Used \(uses)×").font(.caption).foregroundStyle(uses == 0 ? .orange : .secondary)
                            Text(ByteCountFormatter.string(fromByteCount: Int64(document.assets[path]?.count ?? 0), countStyle: .file))
                                .font(.caption).foregroundStyle(.secondary)
                            Spacer()
                            Button(role: .destructive) { document.assets[path] = nil } label: { Image(systemName: "trash") }
                                .buttonStyle(.borderless)
                                .disabled(uses > 0)
                                .help(uses > 0 ? "Remove it from questions first" : "Delete")
                        }
                    }
                    .padding(10)
                    .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(.quaternary))
                }
            }
            .padding(20)
            if document.assets.isEmpty {
                ContentUnavailableView("No images yet", systemImage: "photo.on.rectangle",
                                       description: Text("Import network diagrams, device photos, screenshots or PDFs to use as exhibits and hotspots."))
            }
        }
        .toolbar {
            Button { importing = true } label: { Label("Import Images", systemImage: "photo.badge.plus") }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.image, .pdf], allowsMultipleSelection: true) { result in
            guard case let .success(urls) = result else { return }
            for url in urls { importAsset(url, into: &document) }
        }
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            for p in providers {
                _ = p.loadObject(ofClass: URL.self) { url, _ in
                    guard let url else { return }
                    let data = try? Data(contentsOf: url)
                    Task { @MainActor in if let data { _ = document.addAsset(data, named: url.lastPathComponent) } }
                }
            }
            return true
        }
    }
}

@discardableResult
func importAsset(_ url: URL, into document: inout ExamProjectDocument) -> String? {
    let scoped = url.startAccessingSecurityScopedResource()
    defer { if scoped { url.stopAccessingSecurityScopedResource() } }
    guard let data = try? Data(contentsOf: url) else { return nil }
    return document.addAsset(data, named: url.lastPathComponent)
}

struct Thumbnail: View {
    let data: Data
    var body: some View {
        if let image = NSImage(data: data) {
            Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit).padding(6)
        } else {
            Image(systemName: "doc").font(.largeTitle).foregroundStyle(.secondary)
        }
    }
}

// MARK: - Case studies and exhibits

struct CaseStudyEditor: View {
    @Binding var caseStudy: CaseStudyDocument
    @Binding var document: ExamProjectDocument

    var body: some View {
        Form {
            Section("Case study") {
                TextField("Title", text: $caseStudy.title.orEmpty)
                LabeledContent("Id") { Text(caseStudy.id ?? "").font(.body.monospaced()).textSelection(.enabled) }
                TextArea(title: "Scenario (Markdown; ``` for code blocks)", text: $caseStudy.scenario.orEmpty, minHeight: 220)
            }
            ExhibitsEditor(exhibits: $caseStudy.exhibits, document: $document)
            Section("Questions in this case study") {
                let qs = document.questions.filter { $0.caseStudy == caseStudy.id }
                if qs.isEmpty { Text("None yet. Pick this case study in a question's settings.").foregroundStyle(.secondary) }
                ForEach(qs.indices, id: \.self) { Text(qs[$0].prompt ?? qs[$0].id ?? "").lineLimit(1) }
            }
        }
        .formStyle(.grouped)
        .navigationTitle(caseStudy.title ?? "Case study")
    }
}

struct ExhibitsEditor: View {
    @Binding var exhibits: [ExhibitDocument]?
    @Binding var document: ExamProjectDocument
    @State private var importingFor: Int?

    private var list: [ExhibitDocument] { exhibits ?? [] }

    var body: some View {
        Section {
            ForEach(list.indices, id: \.self) { i in
                VStack(alignment: .leading, spacing: 8) {
                    HStack {
                        TextField("Exhibit title", text: Binding(get: { list[i].title ?? "" }, set: { exhibits![i].title = $0.isEmpty ? nil : $0 }))
                            .textFieldStyle(.roundedBorder).labelsHidden()
                        Picker("Type", selection: Binding(
                            get: { list[i].image != nil ? "image" : "text" },
                            set: { kind in
                                if kind == "image" { exhibits![i].text = nil; exhibits![i].image = document.assets.keys.sorted().first ?? "" }
                                else { exhibits![i].image = nil; exhibits![i].text = "" }
                            })) {
                            Text("Text").tag("text")
                            Text("Image / PDF").tag("image")
                        }
                        .pickerStyle(.segmented).frame(width: 200).labelsHidden()
                        Button(role: .destructive) { exhibits!.remove(at: i); if exhibits!.isEmpty { exhibits = nil } } label: { Image(systemName: "trash") }
                            .buttonStyle(.borderless)
                    }
                    if list[i].image != nil {
                        AssetPicker(selection: Binding(get: { list[i].image ?? "" }, set: { exhibits![i].image = $0 }), document: $document)
                    } else {
                        TextArea(title: "Text (shown monospaced: CLI output, configs, logs)", text: Binding(get: { list[i].text ?? "" }, set: { exhibits![i].text = $0 }), minHeight: 90, mono: true)
                    }
                }
                .padding(.vertical, 4)
            }
            Menu("Add Exhibit") {
                Button("Text") { exhibits = list + [ExhibitDocument(title: "Exhibit \(list.count + 1)", text: "")] }
                Button("Image or PDF") { exhibits = list + [ExhibitDocument(title: "Exhibit \(list.count + 1)", image: document.assets.keys.sorted().first ?? "")] }
            }
            .fixedSize()
        } header: {
            Text("Exhibits")
        }
    }
}

/// Chooses an asset, or imports a new one on the spot.
struct AssetPicker: View {
    @Binding var selection: String
    @Binding var document: ExamProjectDocument
    @State private var importing = false

    var body: some View {
        HStack(alignment: .top) {
            Picker("Image", selection: $selection) {
                if !document.assets.keys.contains(selection) { Text(selection.isEmpty ? "Choose…" : "\(selection) (missing)").tag(selection) }
                ForEach(document.assets.keys.sorted(), id: \.self) { Text($0).tag($0) }
            }
            Button("Import…") { importing = true }
            if let data = document.assets[selection] {
                Thumbnail(data: data).frame(width: 120, height: 70)
            }
        }
        .fileImporter(isPresented: $importing, allowedContentTypes: [.image, .pdf]) { result in
            if case let .success(url) = result, let path = importAsset(url, into: &document) { selection = path }
        }
    }
}
