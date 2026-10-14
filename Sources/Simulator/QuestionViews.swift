import AppKit
import MockiCore
import SwiftUI

/// The answering area for any question kind. Reads and writes the session.
struct QuestionBodyView: View {
    @Binding var session: Session
    let question: Question

    private var locked: Bool { session.isLocked(question) }
    private var response: Response? { session.response(for: question) }

    var body: some View {
        switch question.body {
        case let .single(choices, answer):
            choiceList(choices, multi: false, correct: [answer])
        case let .multiple(choices, answer):
            choiceList(choices, multi: true, correct: answer)
        case let .ordering(choices, answer):
            OrderingView(session: $session, question: question, choices: choices, answer: answer)
        case let .matching(items, targets, answer):
            MatchingView(session: $session, question: question, items: items, targets: targets, answer: answer)
        case let .dropdowns(rows):
            dropdowns(rows)
        case let .fillIn(blanks):
            fillIn(blanks)
        case let .hotspot(image, regions, answer):
            HotspotView(session: $session, question: question, image: image, regions: regions, answer: answer)
        }
    }

    // MARK: Single / multiple

    private func choiceList(_ choices: [Choice], multi: Bool, correct: Set<String>) -> some View {
        let picked: Set<String> = switch response {
        case let .single(s)?: [s]
        case let .multiple(s)?: s
        default: []
        }
        return VStack(spacing: 8) {
            ForEach(Array(choices.enumerated()), id: \.element.id) { index, choice in
                ChoiceRow(
                    number: index + 1,
                    text: choice.text,
                    isMulti: multi,
                    isSelected: picked.contains(choice.id),
                    verdict: locked ? (correct.contains(choice.id) ? .correct : picked.contains(choice.id) ? .wrong : nil) : nil
                ) { session.choose(choice.id) }
                .keyboardShortcut(index < 9 ? KeyEquivalent(Character("\(index + 1)")) : nil)
                .disabled(locked)
            }
        }
    }

    // MARK: Dropdowns / yes-no

    private func dropdowns(_ rows: [DropdownRow]) -> some View {
        let picked: [String: String] = if case let .dropdowns(m)? = response { m } else { [:] }
        let yesNo = question.kind == .yesNo
        return VStack(spacing: 8) {
            if yesNo {
                HStack {
                    Text("Statement").font(.caption.bold()).foregroundStyle(.secondary)
                    Spacer()
                    Text("Yes / No").font(.caption.bold()).foregroundStyle(.secondary).frame(width: 140)
                    if locked { Spacer().frame(width: 20) }
                }
                .padding(.horizontal, 12)
            }
            ForEach(rows) { row in
                HStack(spacing: 12) {
                    Text(markdown(row.label)).frame(maxWidth: .infinity, alignment: .leading)
                    let selection = Binding(
                        get: { picked[row.id] ?? "" },
                        set: { (value: String) in
                            var m = picked
                            m[row.id] = value.isEmpty ? nil : value
                            session.respond(.dropdowns(m), to: question)
                        }
                    )
                    Group {
                        if yesNo {
                            Picker(row.label, selection: selection) {
                                ForEach(row.options, id: \.self) { Text($0).tag($0) }
                            }
                            .pickerStyle(.segmented)
                            .frame(width: 140)
                        } else {
                            Picker(row.label, selection: selection) {
                                Text("Select…").tag("")
                                ForEach(row.options, id: \.self) { Text($0).tag($0) }
                            }
                            .frame(width: 240)
                        }
                    }
                    .labelsHidden()
                    .disabled(locked)
                    if locked { VerdictIcon(ok: picked[row.id] == row.answer) }
                }
                .rowBox()
            }
        }
    }

    // MARK: Fill in

    private func fillIn(_ blanks: [Blank]) -> some View {
        let typed: [String: String] = if case let .fillIn(m)? = response { m } else { [:] }
        return VStack(spacing: 8) {
            ForEach(blanks) { blank in
                HStack(spacing: 12) {
                    if let label = blank.label {
                        Text(label).frame(width: 180, alignment: .leading)
                    }
                    TextField(blank.label ?? "Answer", text: Binding(
                        get: { typed[blank.id] ?? "" },
                        set: { value in
                            var m = typed
                            m[blank.id] = value
                            session.respond(.fillIn(m), to: question)
                        }
                    ))
                    .textFieldStyle(.roundedBorder)
                    .font(.system(.body, design: .monospaced))
                    .autocorrectionDisabled()
                    .disabled(locked)
                    if locked { VerdictIcon(ok: blank.accepts(typed[blank.id] ?? "")) }
                }
                .rowBox()
            }
        }
    }
}

// MARK: - Drag and drop

/// A draggable answer tile. Click to select it (then click a drop zone), or drag it.
struct DragTile: View {
    let text: String
    var number: Int? = nil
    var selected = false
    var verdict: Bool? = nil

    var body: some View {
        HStack(spacing: 8) {
            Image(systemName: "line.3.horizontal").foregroundStyle(.tertiary)
            if let number { Text("\(number)").monospacedDigit().foregroundStyle(.secondary) }
            Text(markdown(text)).frame(maxWidth: .infinity, alignment: .leading)
            if let verdict { VerdictIcon(ok: verdict) }
        }
        .padding(.horizontal, 10).padding(.vertical, 8)
        .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 7))
        .overlay(RoundedRectangle(cornerRadius: 7).strokeBorder(selected ? Color.accentColor : Color.secondary.opacity(0.35), lineWidth: selected ? 2 : 1))
        .contentShape(Rectangle())
    }
}

/// A drop area that highlights while something is dragged over it.
struct DropZone<Content: View>: View {
    let title: String?
    var minHeight: CGFloat = 54
    /// Called per dropped id with the drop point in the zone's `space` coordinates.
    let onDrop: (String, CGPoint) -> Void
    var onTap: (() -> Void)? = nil
    var space = "dropZone"
    @ViewBuilder let content: Content
    @State private var targeted = false

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            if let title { Text(markdown(title)).font(.headline) }
            content
        }
        .padding(10)
        .frame(maxWidth: .infinity, minHeight: minHeight, alignment: .topLeading)
        .background(targeted ? Color.accentColor.opacity(0.12) : Color.secondary.opacity(0.06), in: RoundedRectangle(cornerRadius: 10))
        .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(targeted ? Color.accentColor : Color.secondary.opacity(0.4), style: StrokeStyle(lineWidth: targeted ? 2 : 1, dash: [6, 4])))
        .contentShape(Rectangle())
        .onTapGesture { onTap?() }
        .coordinateSpace(.named(space))
        .dropDestination(for: String.self) { ids, location in
            ids.forEach { onDrop($0, location) }
            return !ids.isEmpty
        } isTargeted: { targeted = $0 }
    }
}

extension View {
    /// Draggable with an id payload, unless locked.
    @ViewBuilder
    func draggableID(_ id: String, enabled: Bool, preview: some View) -> some View {
        if enabled { draggable(id) { preview } } else { self }
    }
}

// MARK: - Matching

/// Drag each item onto a target box (a target can hold several items), or back to the pool.
struct MatchingView: View {
    @Binding var session: Session
    let question: Question
    let items: [Choice]
    let targets: [Choice]
    let answer: [String: String]
    @State private var selected: String?

    private var placed: [String: String] { if case let .matching(m)? = session.response(for: question) { m } else { [:] } }
    private var locked: Bool { session.isLocked(question) }

    private func place(_ itemID: String, in target: String?) {
        guard !locked, items.contains(where: { $0.id == itemID }) else { return }
        var m = placed
        m[itemID] = target
        session.respond(.matching(m), to: question)
        selected = nil
    }

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Items").font(.caption.bold()).foregroundStyle(.secondary)
                DropZone(title: nil, minHeight: 120, onDrop: { id, _ in place(id, in: nil) }, onTap: { if let s = selected { place(s, in: nil) } }) {
                    ForEach(items.filter { placed[$0.id] == nil }) { tile($0, verdict: locked ? answer[$0.id] == nil : nil) }
                    if items.allSatisfy({ placed[$0.id] != nil }) {
                        Text("All items placed.").font(.callout).foregroundStyle(.tertiary)
                    }
                }
            }
            .frame(maxWidth: .infinity)

            VStack(alignment: .leading, spacing: 8) {
                Text("Targets").font(.caption.bold()).foregroundStyle(.secondary)
                ForEach(targets) { target in
                    DropZone(title: target.text, onDrop: { id, _ in place(id, in: target.id) }, onTap: { if let s = selected { place(s, in: target.id) } }) {
                        let here = items.filter { placed[$0.id] == target.id }
                        ForEach(here) { tile($0, verdict: locked ? answer[$0.id] == target.id : nil) }
                        if here.isEmpty {
                            Text(selected == nil ? "Drag items here" : "Click to place the selected item")
                                .font(.callout).foregroundStyle(.tertiary)
                        }
                    }
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("Target \(target.text)")
                }
            }
            .frame(maxWidth: .infinity)
        }
    }

    private func tile(_ item: Choice, verdict: Bool?) -> some View {
        DragTile(text: item.text, selected: selected == item.id, verdict: verdict)
            .onTapGesture { if !locked { selected = selected == item.id ? nil : item.id } }
            .draggableID(item.id, enabled: !locked, preview: DragTile(text: item.text).frame(width: 240))
            .contextMenu {
                if !locked {
                    ForEach(targets) { t in Button("Place on \(t.text)") { place(item.id, in: t.id) } }
                    Divider()
                    Button("Return to Items") { place(item.id, in: nil) }
                }
            }
            .accessibilityLabel(item.text)
            .accessibilityHint("Drag onto a target, or use the actions menu.")
    }
}

// MARK: - Ordering

/// Drag options into the answer area and drag rows to reorder. Clicking an option appends it;
/// the arrow and remove buttons are the keyboard alternative.
struct OrderingView: View {
    @Binding var session: Session
    let question: Question
    let choices: [Choice]
    let answer: [String]

    @State private var rowFrames: [String: CGRect] = [:]

    private var order: [String] { if case let .ordering(o)? = session.response(for: question) { o } else { [] } }
    private var locked: Bool { session.isLocked(question) }
    private func text(_ id: String) -> String { choices.first { $0.id == id }?.text ?? id }
    private func set(_ o: [String]) { session.respond(.ordering(o), to: question) }

    /// Dropping above a row's middle inserts before it; below every row appends.
    private func insertionIndex(at point: CGPoint) -> Int {
        order.firstIndex { id in rowFrames[id].map { point.y < $0.midY } ?? false } ?? order.count
    }

    /// Moves (or adds) an option so it sits at `index` in the answer area; nil removes it.
    private func put(_ id: String, at index: Int?) {
        guard !locked, choices.contains(where: { $0.id == id }) else { return }
        var o = order
        let old = o.firstIndex(of: id)
        o.removeAll { $0 == id }
        guard var index else { set(o); return }
        if let old, old < index { index -= 1 }
        o.insert(id, at: min(max(index, 0), o.count))
        set(o)
    }

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            VStack(alignment: .leading, spacing: 6) {
                Text("Options").font(.caption.bold()).foregroundStyle(.secondary)
                DropZone(title: nil, minHeight: 120, onDrop: { id, _ in put(id, at: nil) }) {
                    ForEach(choices.filter { !order.contains($0.id) }) { choice in
                        DragTile(text: choice.text, verdict: locked ? !answer.contains(choice.id) : nil)
                            .onTapGesture { put(choice.id, at: order.count) }
                            .draggableID(choice.id, enabled: !locked, preview: DragTile(text: choice.text).frame(width: 240))
                            .accessibilityAddTraits(.isButton)
                            .accessibilityHint("Adds to the end of the answer area.")
                    }
                    if choices.allSatisfy({ order.contains($0.id) }) {
                        Text("All options placed.").font(.callout).foregroundStyle(.tertiary)
                    }
                }
            }
            .frame(maxWidth: .infinity)

            VStack(alignment: .leading, spacing: 6) {
                Text("Answer area").font(.caption.bold()).foregroundStyle(.secondary)
                DropZone(title: nil, minHeight: 120, onDrop: { id, point in put(id, at: insertionIndex(at: point)) }, space: "answerArea") {
                    ForEach(Array(order.enumerated()), id: \.element) { index, id in
                        HStack(spacing: 6) {
                            DragTile(text: text(id), number: index + 1,
                                     verdict: locked ? (index < answer.count && answer[index] == id) : nil)
                                .draggableID(id, enabled: !locked, preview: DragTile(text: text(id)).frame(width: 240))
                                .background(GeometryReader { geo in
                                    Color.clear.preference(key: RowFrames.self, value: [id: geo.frame(in: .named("answerArea"))])
                                })
                            if !locked {
                                Button { put(id, at: index - 1) } label: { Image(systemName: "chevron.up") }
                                    .disabled(index == 0).accessibilityLabel("Move up")
                                Button { put(id, at: index + 2) } label: { Image(systemName: "chevron.down") }
                                    .disabled(index == order.count - 1).accessibilityLabel("Move down")
                                Button { put(id, at: nil) } label: { Image(systemName: "xmark") }
                                    .accessibilityLabel("Remove")
                            }
                        }
                        .buttonStyle(.borderless)
                    }
                    if order.isEmpty {
                        Text("Drag options here, in order (or click them).").font(.callout).foregroundStyle(.tertiary)
                    }
                }
                .onPreferenceChange(RowFrames.self) { rowFrames = $0 }
            }
            .frame(maxWidth: .infinity)
        }
    }
}

// MARK: - Hotspot

struct HotspotView: View {
    @Binding var session: Session
    let question: Question
    let image: String
    let regions: [Region]
    let answer: Set<String>

    private var picked: Set<String> { if case let .hotspot(s)? = session.response(for: question) { s } else { [] } }
    private var locked: Bool { session.isLocked(question) }

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            if let url = session.exam.assetURL(image), let ns = NSImage(contentsOf: url) {
                Image(nsImage: ns)
                    .resizable()
                    .interpolation(.high)
                    .aspectRatio(ns.size, contentMode: .fit)
                    .overlay {
                        GeometryReader { geo in
                            ZStack(alignment: .topLeading) {
                                Color.clear.contentShape(Rectangle())
                                    .onTapGesture(coordinateSpace: .local) { p in tap(p, in: geo.size) }
                                ForEach(regions) { region in marker(region, in: geo.size) }
                            }
                        }
                    }
                    .clipShape(RoundedRectangle(cornerRadius: 8))
                    .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(.quaternary))
                    .frame(maxWidth: 900)
                    .accessibilityElement(children: .contain)
                    .accessibilityLabel("Hotspot image")
                if !picked.isEmpty {
                    Text("Selected: " + regions.filter { picked.contains($0.id) }.map { $0.label ?? $0.id }.joined(separator: ", "))
                        .font(.callout)
                }
            } else {
                ContentUnavailableView("Missing image", systemImage: "photo.badge.exclamationmark", description: Text(image))
            }
            // Keyboard / VoiceOver alternative to clicking.
            if !locked {
                HStack {
                    Text("Or choose:").font(.caption).foregroundStyle(.secondary)
                    ForEach(regions) { region in
                        Toggle(region.label ?? region.id, isOn: Binding(get: { picked.contains(region.id) }, set: { _ in session.toggleRegion(region.id) }))
                            .toggleStyle(.button).controlSize(.small)
                    }
                }
                .opacity(0.85)
            }
        }
    }

    private func tap(_ p: CGPoint, in size: CGSize) {
        guard !locked, size.width > 0, size.height > 0 else { return }
        let x = p.x / size.width, y = p.y / size.height
        // The smallest region wins when regions overlap.
        if let hit = regions.filter({ $0.contains(x: x, y: y) }).min(by: { $0.width * $0.height < $1.width * $1.height }) {
            session.toggleRegion(hit.id)
        }
    }

    @ViewBuilder
    private func marker(_ r: Region, in size: CGSize) -> some View {
        let rect = CGRect(x: r.x * size.width, y: r.y * size.height, width: r.width * size.width, height: r.height * size.height)
        let isPicked = picked.contains(r.id), isAnswer = answer.contains(r.id)
        if locked && (isPicked || isAnswer) {
            let color: Color = isAnswer ? .green : .red
            RoundedRectangle(cornerRadius: 4)
                .strokeBorder(color, lineWidth: 3)
                .background(color.opacity(0.18), in: RoundedRectangle(cornerRadius: 4))
                .frame(width: rect.width, height: rect.height)
                .offset(x: rect.minX, y: rect.minY)
                .allowsHitTesting(false)
        } else if isPicked {
            ZStack {
                RoundedRectangle(cornerRadius: 4).strokeBorder(Color.accentColor, lineWidth: 2)
                    .background(Color.accentColor.opacity(0.15), in: RoundedRectangle(cornerRadius: 4))
                Image(systemName: "scope").font(.title2.bold()).foregroundStyle(Color.accentColor)
                    .shadow(color: .white, radius: 2)
            }
            .frame(width: rect.width, height: rect.height)
            .offset(x: rect.minX, y: rect.minY)
            .allowsHitTesting(false)
        }
    }
}

// MARK: - Pieces

struct ChoiceRow: View {
    enum Verdict { case correct, wrong }

    let number: Int
    let text: String
    let isMulti: Bool
    let isSelected: Bool
    let verdict: Verdict?
    let action: () -> Void
    @Environment(\.classicLook) private var classic

    var body: some View {
        if classic { classicRow } else { modernRow }
    }

    /// Plain lettered rows, as testing-center software shows them.
    private var classicRow: some View {
        Button(action: action) {
            HStack(alignment: .firstTextBaseline, spacing: 10) {
                Image(systemName: isMulti ? (isSelected ? "checkmark.square.fill" : "square") : (isSelected ? "largecircle.fill.circle" : "circle"))
                Text("\(String(UnicodeScalar(UInt8(64 + min(number, 26))))). ").monospacedDigit() + Text(markdown(text))
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(.vertical, 4)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private var modernRow: some View {
        Button(action: action) {
            HStack(alignment: .firstTextBaseline, spacing: 12) {
                Image(systemName: isMulti
                      ? (isSelected ? "checkmark.square.fill" : "square")
                      : (isSelected ? "largecircle.fill.circle" : "circle"))
                    .foregroundStyle(isSelected ? Color.accentColor : .secondary)
                Text("\(number)").monospacedDigit().foregroundStyle(.secondary)
                Text(markdown(text)).frame(maxWidth: .infinity, alignment: .leading)
                if let verdict { VerdictIcon(ok: verdict == .correct) }
            }
            .padding(12)
            .contentShape(Rectangle())
            .background(background, in: RoundedRectangle(cornerRadius: 8))
            .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(isSelected ? Color.accentColor : .clear, lineWidth: 1.5))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }

    private var background: Color {
        switch verdict {
        case .correct: .green.opacity(0.15)
        case .wrong: .red.opacity(0.15)
        case nil: Color(nsColor: .controlBackgroundColor)
        }
    }
}

struct VerdictIcon: View {
    let ok: Bool
    var body: some View {
        Image(systemName: ok ? "checkmark.circle.fill" : "xmark.circle.fill")
            .foregroundStyle(ok ? .green : .red)
            .accessibilityLabel(ok ? "Correct" : "Incorrect")
    }
}

extension View {
    func rowBox() -> some View {
        padding(10)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
    }

    /// A modifier-free shortcut, or none.
    @ViewBuilder
    func keyboardShortcut(_ key: KeyEquivalent?) -> some View {
        if let key { keyboardShortcut(key, modifiers: []) } else { self }
    }
}

private struct RowFrames: PreferenceKey {
    static let defaultValue: [String: CGRect] = [:]
    static func reduce(value: inout [String: CGRect], nextValue: () -> [String: CGRect]) {
        value.merge(nextValue()) { $1 }
    }
}
