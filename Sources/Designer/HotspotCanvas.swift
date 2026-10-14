import AppKit
import MockiCore
import SwiftUI

/// Draw, move and resize hotspot rectangles on an image. Coordinates are stored as fractions.
struct HotspotCanvas: View {
    let imageData: Data
    @Binding var regions: [RegionDocument]
    let correct: Set<String>
    @Binding var selected: String?

    private enum Mode {
        case draw(start: CGPoint)
        case move(index: Int, original: CGRect)
        case resize(index: Int, original: CGRect)
    }

    @State private var mode: Mode?
    @State private var draft: CGRect?

    var body: some View {
        if let image = NSImage(data: imageData) {
            Image(nsImage: image)
                .resizable()
                .interpolation(.high)
                .aspectRatio(image.size, contentMode: .fit)
                .overlay {
                    GeometryReader { geo in
                        let size = geo.size
                        ZStack(alignment: .topLeading) {
                            Color.clear.contentShape(Rectangle())
                            ForEach(regions.indices, id: \.self) { i in box(i, size) }
                            if let draft {
                                Rectangle().strokeBorder(Color.accentColor, style: StrokeStyle(lineWidth: 2, dash: [6]))
                                    .frame(width: draft.width * size.width, height: draft.height * size.height)
                                    .offset(x: draft.minX * size.width, y: draft.minY * size.height)
                            }
                        }
                        .gesture(drag(size))
                    }
                }
                .clipShape(RoundedRectangle(cornerRadius: 6))
                .overlay(RoundedRectangle(cornerRadius: 6).strokeBorder(.quaternary))
                .accessibilityLabel("Hotspot image editor")
        } else {
            ContentUnavailableView("Can't display this image", systemImage: "photo.badge.exclamationmark")
        }
    }

    private func rect(_ r: RegionDocument) -> CGRect {
        CGRect(x: r.x ?? 0, y: r.y ?? 0, width: r.width ?? 0, height: r.height ?? 0)
    }

    private func box(_ i: Int, _ size: CGSize) -> some View {
        let r = rect(regions[i])
        let id = regions[i].id ?? ""
        let color: Color = correct.contains(id) ? .green : .blue
        let isSelected = selected == id
        return ZStack(alignment: .bottomTrailing) {
            Rectangle()
                .fill(color.opacity(isSelected ? 0.28 : 0.16))
                .overlay(Rectangle().strokeBorder(color, lineWidth: isSelected ? 3 : 2))
                .overlay(alignment: .topLeading) {
                    Text(regions[i].label ?? id)
                        .font(.caption2.bold()).padding(.horizontal, 4).padding(.vertical, 1)
                        .background(color, in: RoundedRectangle(cornerRadius: 3)).foregroundStyle(.white)
                        .padding(2)
                }
            if isSelected {
                Rectangle().fill(.white).frame(width: 10, height: 10).overlay(Rectangle().strokeBorder(color, lineWidth: 2))
                    .offset(x: 5, y: 5)
            }
        }
        .frame(width: r.width * size.width, height: r.height * size.height)
        .offset(x: r.minX * size.width, y: r.minY * size.height)
        .allowsHitTesting(false)
    }

    private func drag(_ size: CGSize) -> some Gesture {
        DragGesture(minimumDistance: 0, coordinateSpace: .local)
            .onChanged { value in
                let p = CGPoint(x: clamp(value.location.x / size.width), y: clamp(value.location.y / size.height))
                let start = CGPoint(x: clamp(value.startLocation.x / size.width), y: clamp(value.startLocation.y / size.height))
                if mode == nil { mode = beginMode(at: start, size: size) }
                let dx = p.x - start.x, dy = p.y - start.y
                switch mode {
                case let .draw(s):
                    draft = CGRect(x: min(s.x, p.x), y: min(s.y, p.y), width: abs(p.x - s.x), height: abs(p.y - s.y))
                case let .move(i, o):
                    let x = min(max(0, o.minX + dx), 1 - o.width), y = min(max(0, o.minY + dy), 1 - o.height)
                    set(i, CGRect(x: x, y: y, width: o.width, height: o.height))
                case let .resize(i, o):
                    set(i, CGRect(x: o.minX, y: o.minY, width: max(0.01, min(1 - o.minX, o.width + dx)), height: max(0.01, min(1 - o.minY, o.height + dy))))
                case nil:
                    break
                }
            }
            .onEnded { value in
                defer { mode = nil; draft = nil }
                let moved = hypot(value.translation.width, value.translation.height) > 3
                if case .draw = mode {
                    if moved, let d = draft, d.width * size.width > 6, d.height * size.height > 6 {
                        let id = (1...).lazy.map { "r\($0)" }.first { candidate in !regions.contains { $0.id == candidate } }!
                        regions.append(RegionDocument(id: id, label: "Area \(regions.count + 1)", x: round3(d.minX), y: round3(d.minY), width: round3(d.width), height: round3(d.height)))
                        selected = id
                    } else if !moved {
                        selected = nil
                    }
                }
            }
    }

    private func beginMode(at p: CGPoint, size: CGSize) -> Mode {
        if let selected, let i = regions.firstIndex(where: { $0.id == selected }) {
            let r = rect(regions[i])
            let corner = CGPoint(x: r.maxX * size.width, y: r.maxY * size.height)
            if hypot(p.x * size.width - corner.x, p.y * size.height - corner.y) < 12 { return .resize(index: i, original: r) }
        }
        let hits = regions.indices.filter { rect(regions[$0]).contains(p) }
        if let i = hits.min(by: { rect(regions[$0]).width * rect(regions[$0]).height < rect(regions[$1]).width * rect(regions[$1]).height }) {
            selected = regions[i].id
            return .move(index: i, original: rect(regions[i]))
        }
        return .draw(start: p)
    }

    private func set(_ i: Int, _ r: CGRect) {
        regions[i].x = round3(r.minX)
        regions[i].y = round3(r.minY)
        regions[i].width = round3(r.width)
        regions[i].height = round3(r.height)
    }

    private func clamp(_ v: CGFloat) -> CGFloat { min(max(v, 0), 1) }
    private func round3(_ v: CGFloat) -> Double { (Double(v) * 1000).rounded() / 1000 }
}
