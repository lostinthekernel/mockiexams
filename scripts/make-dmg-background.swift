// Renders the installer window background at 1x and 2x.
// Usage: swift scripts/make-dmg-background.swift <out-dir>
// Then: tiffutil -cathidpicheck <out-dir>/bg.png <out-dir>/bg@2x.png -out design/dmg-background.tiff
import AppKit

let outDir = URL(fileURLWithPath: CommandLine.arguments[1])
let navy = NSColor(srgbRed: 0.08, green: 0.20, blue: 0.47, alpha: 1)

func render(scale: CGFloat, to url: URL) {
    let size = NSSize(width: 480, height: 320)
    let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(size.width * scale),
                               pixelsHigh: Int(size.height * scale), bitsPerSample: 8,
                               samplesPerPixel: 4, hasAlpha: true, isPlanar: false,
                               colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    rep.size = size
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
    let bounds = NSRect(origin: .zero, size: size)

    NSGradient(colors: [NSColor(srgbRed: 0.97, green: 0.98, blue: 1.00, alpha: 1),
                        NSColor(srgbRed: 0.90, green: 0.93, blue: 0.98, alpha: 1)])!
        .draw(in: bounds, angle: -90)
    NSGradient(colors: [navy.withAlphaComponent(0.10), navy.withAlphaComponent(0)])!
        .draw(fromCenter: NSPoint(x: 430, y: 20), radius: 0, toCenter: NSPoint(x: 430, y: 20), radius: 260, options: [])
    NSGradient(colors: [NSColor.white.withAlphaComponent(0.7), NSColor.white.withAlphaComponent(0)])!
        .draw(fromCenter: NSPoint(x: 60, y: 300), radius: 0, toCenter: NSPoint(x: 60, y: 300), radius: 220, options: [])

    // Icons sit at x 120 and 360, y 160 from the top; their artwork centre is a little higher.
    let y: CGFloat = 320 - 152
    let xs: [CGFloat] = [188, 212, 236, 260]
    for (i, x) in xs.enumerated() {
        let r: CGFloat = 6
        let dot = NSBezierPath(ovalIn: NSRect(x: x - r, y: y - r, width: 2 * r, height: 2 * r))
        dot.lineWidth = 1.5
        navy.withAlphaComponent(0.22 + 0.12 * CGFloat(i)).setStroke()
        dot.stroke()
    }

    let r: CGFloat = 11
    let cx: CGFloat = 288
    let filled = NSBezierPath(ovalIn: NSRect(x: cx - r, y: y - r, width: 2 * r, height: 2 * r))
    navy.setFill()
    filled.fill()
    let check = NSBezierPath()
    check.lineWidth = 2.2
    check.lineCapStyle = .round
    check.lineJoinStyle = .round
    check.move(to: NSPoint(x: cx - 5, y: y))
    check.line(to: NSPoint(x: cx - 1.5, y: y - 4))
    check.line(to: NSPoint(x: cx + 5.5, y: y + 4.5))
    NSColor.white.setStroke()
    check.stroke()

    NSGraphicsContext.restoreGraphicsState()
    try! rep.representation(using: .png, properties: [:])!.write(to: url)
}

render(scale: 1, to: outDir.appendingPathComponent("bg.png"))
render(scale: 2, to: outDir.appendingPathComponent("bg@2x.png"))
