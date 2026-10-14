import AppKit
import MockiCore
import SwiftUI

/// Inline Markdown plus fenced ``` code blocks, which exam prompts use for configs and output.
struct RichText: View {
    let source: String
    var font: Font = .body

    private enum Block { case text(String), code(String) }

    private var blocks: [Block] {
        var out: [Block] = [], buffer: [String] = [], inCode = false
        func flush() {
            let joined = buffer.joined(separator: "\n")
            if inCode { out.append(.code(joined)) }
            else if !joined.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                out.append(.text(joined.trimmingCharacters(in: .newlines)))
            }
            buffer = []
        }
        for line in source.components(separatedBy: "\n") {
            if line.trimmingCharacters(in: .whitespaces).hasPrefix("```") {
                flush()
                inCode.toggle()
            } else {
                buffer.append(line)
            }
        }
        flush()
        return out
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            ForEach(Array(blocks.enumerated()), id: \.offset) { _, block in
                switch block {
                case let .text(s):
                    Text(markdown(s)).font(font).fixedSize(horizontal: false, vertical: true)
                case let .code(s):
                    CodeBox(text: s)
                }
            }
        }
        .textSelection(.enabled)
    }
}

struct CodeBox: View {
    let text: String

    var body: some View {
        ScrollView(.horizontal) {
            Text(text)
                .font(.system(.callout, design: .monospaced))
                .textSelection(.enabled)
                .fixedSize()
                .padding(12)
        }
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(Color(nsColor: .textBackgroundColor), in: RoundedRectangle(cornerRadius: 8))
        .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(.quaternary))
    }
}

func markdown(_ s: String) -> AttributedString {
    (try? AttributedString(markdown: s, options: .init(interpretedSyntax: .inlineOnlyPreservingWhitespace))) ?? AttributedString(s)
}

/// Loads an exam image (PNG, JPEG, PDF…) relative to the exam folder.
struct AssetImage: View {
    let exam: Exam
    let path: String

    var body: some View {
        if let url = exam.assetURL(path), let image = NSImage(contentsOf: url) {
            Image(nsImage: image).resizable().interpolation(.high).aspectRatio(contentMode: .fit)
        } else {
            ContentUnavailableView("Missing image", systemImage: "photo.badge.exclamationmark", description: Text(path))
        }
    }
}

/// Text exhibits inline; image exhibits as cards that open a zoomable viewer.
struct ExhibitList: View {
    let exam: Exam
    let exhibits: [Exhibit]
    @State private var open: Exhibit?

    var body: some View {
        if !exhibits.isEmpty {
            VStack(alignment: .leading, spacing: 10) {
                ForEach(Array(exhibits.enumerated()), id: \.offset) { _, exhibit in
                    switch exhibit.content {
                    case let .text(text):
                        VStack(alignment: .leading, spacing: 4) {
                            Label(exhibit.title, systemImage: "doc.plaintext").font(.caption.bold()).foregroundStyle(.secondary)
                            CodeBox(text: text)
                        }
                    case let .image(path):
                        Button { open = exhibit } label: {
                            VStack(alignment: .leading, spacing: 6) {
                                AssetImage(exam: exam, path: path)
                                    .frame(maxHeight: 220)
                                    .frame(maxWidth: .infinity)
                                Label("\(exhibit.title) — click to enlarge", systemImage: "magnifyingglass")
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            .padding(10)
                            .background(Color(nsColor: .controlBackgroundColor), in: RoundedRectangle(cornerRadius: 10))
                            .overlay(RoundedRectangle(cornerRadius: 10).strokeBorder(.quaternary))
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel("Exhibit: \(exhibit.title). Open larger.")
                    }
                }
            }
            .sheet(item: $open) { ExhibitViewer(exam: exam, exhibit: $0) }
        }
    }
}

extension Exhibit: @retroactive Identifiable {
    public var id: String { title + (imagePath ?? "") }
}

struct ExhibitViewer: View {
    let exam: Exam
    let exhibit: Exhibit
    @Environment(\.dismiss) private var dismiss
    @State private var zoom: CGFloat = 1

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                Text(exhibit.title).font(.headline)
                Spacer()
                Button { zoom = max(0.5, zoom - 0.25) } label: { Image(systemName: "minus.magnifyingglass") }
                    .keyboardShortcut("-", modifiers: .command)
                Text("\(Int(zoom * 100))%").monospacedDigit().frame(width: 48)
                Button { zoom = min(4, zoom + 0.25) } label: { Image(systemName: "plus.magnifyingglass") }
                    .keyboardShortcut("=", modifiers: .command)
                Button("Done") { dismiss() }.keyboardShortcut(.cancelAction)
            }
            .padding(12)
            Divider()
            GeometryReader { geo in
                ScrollView([.horizontal, .vertical]) {
                    switch exhibit.content {
                    case let .image(path):
                        AssetImage(exam: exam, path: path)
                            .frame(width: geo.size.width * zoom, height: geo.size.height * zoom)
                    case let .text(text):
                        Text(text).font(.system(size: 13 * zoom, design: .monospaced)).textSelection(.enabled).padding()
                    }
                }
                .gesture(MagnifyGesture().onChanged { zoom = min(4, max(0.5, $0.magnification)) })
            }
        }
        .frame(minWidth: 820, idealWidth: 1100, minHeight: 560, idealHeight: 760)
    }
}

/// The scenario panel shown beside case-study questions.
struct CaseStudyPanel: View {
    let exam: Exam
    let caseStudy: CaseStudy

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 14) {
                Label(caseStudy.title, systemImage: "briefcase").font(.title3.bold())
                RichText(source: caseStudy.scenario)
                ExhibitList(exam: exam, exhibits: caseStudy.exhibits)
            }
            .padding(16)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
    }
}
