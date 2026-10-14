// Draws the sample exam's images and prints each hotspot region as fractions of the image.
// Usage: swift scripts/make-sample-images.swift <output-dir>
import AppKit

let out = URL(filePath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "samples/networking-sim/images", directoryHint: .isDirectory)
try FileManager.default.createDirectory(at: out, withIntermediateDirectories: true)

struct Canvas {
    let width: CGFloat, height: CGFloat
    var regions: [(String, CGRect)] = []

    func draw(_ name: String, _ body: (CGContext) -> Void) {
        let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: Int(width), pixelsHigh: Int(height), bitsPerSample: 8,
                                   samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
        let ctx = NSGraphicsContext(bitmapImageRep: rep)!
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(cgContext: ctx.cgContext, flipped: true)
        let cg = ctx.cgContext
        cg.translateBy(x: 0, y: height)
        cg.scaleBy(x: 1, y: -1)
        body(cg)
        NSGraphicsContext.restoreGraphicsState()
        try! rep.representation(using: .png, properties: [:])!.write(to: out.appending(path: name))
    }

    func print(_ name: String) {
        Swift.print("\n\(name):")
        for (id, r) in regions {
            Swift.print(String(format: "  { \"id\": \"%@\", \"x\": %.3f, \"y\": %.3f, \"width\": %.3f, \"height\": %.3f },",
                               id, r.minX / width, r.minY / height, r.width / width, r.height / height))
        }
    }
}

func text(_ s: String, _ p: CGPoint, size: CGFloat = 16, color: NSColor = .black, bold: Bool = false, mono: Bool = false, center: Bool = true) {
    let font = mono ? NSFont.monospacedSystemFont(ofSize: size, weight: bold ? .bold : .regular)
                    : NSFont.systemFont(ofSize: size, weight: bold ? .semibold : .regular)
    let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: color]
    let str = NSAttributedString(string: s, attributes: attrs)
    let size = str.size()
    str.draw(at: CGPoint(x: center ? p.x - size.width / 2 : p.x, y: p.y))
}

func box(_ cg: CGContext, _ r: CGRect, fill: NSColor, stroke: NSColor = .darkGray, radius: CGFloat = 10, line: CGFloat = 2) {
    let path = CGPath(roundedRect: r, cornerWidth: radius, cornerHeight: radius, transform: nil)
    cg.addPath(path); cg.setFillColor(fill.cgColor); cg.fillPath()
    cg.addPath(path); cg.setStrokeColor(stroke.cgColor); cg.setLineWidth(line); cg.strokePath()
}

func lineBetween(_ cg: CGContext, _ a: CGPoint, _ b: CGPoint, color: NSColor = .darkGray, width: CGFloat = 3, dashed: Bool = false) {
    cg.setStrokeColor(color.cgColor); cg.setLineWidth(width)
    cg.setLineDash(phase: 0, lengths: dashed ? [8, 6] : [])
    cg.move(to: a); cg.addLine(to: b); cg.strokePath()
    cg.setLineDash(phase: 0, lengths: [])
}

// MARK: Topology

var topo = Canvas(width: 1000, height: 640)
let devices: [(id: String, label: String, sub: String, rect: CGRect, color: NSColor)] = [
    ("isp", "Internet / ISP", "203.0.113.1", CGRect(x: 400, y: 20, width: 200, height: 80), NSColor(calibratedRed: 0.85, green: 0.92, blue: 1, alpha: 1)),
    ("firewall", "FW1 (firewall)", "outside 203.0.113.2", CGRect(x: 400, y: 150, width: 200, height: 80), NSColor(calibratedRed: 1, green: 0.88, blue: 0.85, alpha: 1)),
    ("router", "R1 (router)", "Gi0/0 10.0.0.1", CGRect(x: 400, y: 280, width: 200, height: 80), NSColor(calibratedRed: 0.88, green: 1, blue: 0.88, alpha: 1)),
    ("switch", "SW1 (L2 switch)", "VLAN 10 / VLAN 20", CGRect(x: 400, y: 410, width: 200, height: 70), NSColor(calibratedRed: 1, green: 0.97, blue: 0.82, alpha: 1)),
    ("pc1", "PC-A", "VLAN 10 · DHCP", CGRect(x: 120, y: 540, width: 170, height: 70), NSColor(calibratedWhite: 0.95, alpha: 1)),
    ("server", "SRV1", "VLAN 20 · 10.0.20.10", CGRect(x: 415, y: 540, width: 170, height: 70), NSColor(calibratedWhite: 0.95, alpha: 1)),
    ("ap", "AP1 (wireless)", "SSID corp", CGRect(x: 710, y: 540, width: 170, height: 70), NSColor(calibratedWhite: 0.95, alpha: 1)),
]
topo.draw("topology.png") { cg in
    cg.setFillColor(NSColor.white.cgColor); cg.fill(CGRect(x: 0, y: 0, width: 1000, height: 640))
    let center = { (r: CGRect) in CGPoint(x: r.midX, y: r.midY) }
    let r = Dictionary(uniqueKeysWithValues: devices.map { ($0.id, $0.rect) })
    lineBetween(cg, center(r["isp"]!), center(r["firewall"]!), dashed: true)
    for (a, b) in [("firewall", "router"), ("router", "switch"), ("switch", "pc1"), ("switch", "server"), ("switch", "ap")] {
        lineBetween(cg, center(r[a]!), center(r[b]!))
    }
    for d in devices {
        box(cg, d.rect, fill: d.color)
        text(d.label, CGPoint(x: d.rect.midX, y: d.rect.minY + 14), size: 20, bold: true)
        text(d.sub, CGPoint(x: d.rect.midX, y: d.rect.minY + 44), size: 15, color: .darkGray)
    }
    text("Branch office — Figure 1", CGPoint(x: 20, y: 20), size: 15, color: .gray, center: false)
}
topo.regions = devices.map { ($0.id, $0.rect.insetBy(dx: -6, dy: -6)) }
topo.print("topology.png")

// MARK: Switch front panel

var panel = Canvas(width: 1200, height: 300)
var ports: [(String, CGRect)] = []
panel.draw("switch-panel.png") { cg in
    cg.setFillColor(NSColor(calibratedWhite: 0.93, alpha: 1).cgColor); cg.fill(CGRect(x: 0, y: 0, width: 1200, height: 300))
    box(cg, CGRect(x: 20, y: 40, width: 1160, height: 220), fill: NSColor(calibratedWhite: 0.22, alpha: 1), stroke: .black, radius: 6)
    text("24-PORT GIGABIT MANAGED SWITCH", CGPoint(x: 40, y: 52), size: 14, color: .lightGray, bold: true, center: false)
    // Status LEDs
    for (i, name) in ["PWR", "SYS", "FAN"].enumerated() {
        let c = CGPoint(x: 60 + CGFloat(i) * 50, y: 110)
        cg.setFillColor((i == 2 ? NSColor.orange : NSColor.green).cgColor)
        cg.fillEllipse(in: CGRect(x: c.x - 7, y: c.y - 7, width: 14, height: 14))
        text(name, CGPoint(x: c.x, y: c.y + 12), size: 12, color: .lightGray)
    }
    // RJ-45 ports, two rows of 12
    for i in 0..<24 {
        let col = i / 2, row = i % 2
        let rect = CGRect(x: 230 + CGFloat(col) * 52, y: 95 + CGFloat(row) * 70, width: 42, height: 40)
        box(cg, rect, fill: .black, stroke: NSColor(calibratedWhite: 0.6, alpha: 1), radius: 3)
        box(cg, CGRect(x: rect.midX - 8, y: rect.maxY - 12, width: 16, height: 8), fill: NSColor(calibratedWhite: 0.25, alpha: 1), stroke: .clear, radius: 1)
        text("\(i + 1)", CGPoint(x: rect.midX, y: row == 0 ? rect.minY - 18 : rect.maxY + 2), size: 11, color: .lightGray)
        ports.append(("port\(i + 1)", rect))
    }
    // SFP uplinks
    for i in 0..<2 {
        let rect = CGRect(x: 880 + CGFloat(i) * 70, y: 115, width: 56, height: 28)
        box(cg, rect, fill: NSColor(calibratedWhite: 0.1, alpha: 1), stroke: NSColor(calibratedWhite: 0.7, alpha: 1), radius: 2)
        text("SFP \(25 + i)", CGPoint(x: rect.midX, y: rect.maxY + 4), size: 11, color: .lightGray)
    }
    // Console (RJ-45, light blue) and USB
    let console = CGRect(x: 1040, y: 105, width: 44, height: 40)
    box(cg, console, fill: NSColor(calibratedRed: 0.55, green: 0.75, blue: 0.95, alpha: 1), stroke: .white, radius: 3)
    text("CONSOLE", CGPoint(x: console.midX, y: console.maxY + 4), size: 11, color: .lightGray)
    let usb = CGRect(x: 1110, y: 115, width: 40, height: 18)
    box(cg, usb, fill: .black, stroke: .white, radius: 2)
    text("USB", CGPoint(x: usb.midX, y: usb.maxY + 4), size: 11, color: .lightGray)
}
panel.regions = [
    ("sfp", CGRect(x: 874, y: 105, width: 132, height: 60)),
    ("console", CGRect(x: 1034, y: 99, width: 56, height: 52)),
    ("usb", CGRect(x: 1104, y: 105, width: 52, height: 38)),
    ("ports", CGRect(x: 224, y: 70, width: 630, height: 180)),
    ("leds", CGRect(x: 40, y: 92, width: 140, height: 50)),
]
panel.print("switch-panel.png")

// MARK: Server rear

var rear = Canvas(width: 1200, height: 320)
rear.draw("server-rear.png") { cg in
    cg.setFillColor(NSColor(calibratedWhite: 0.95, alpha: 1).cgColor); cg.fill(CGRect(x: 0, y: 0, width: 1200, height: 320))
    box(cg, CGRect(x: 20, y: 30, width: 1160, height: 260), fill: NSColor(calibratedWhite: 0.3, alpha: 1), stroke: .black, radius: 6)
    text("1U SERVER — REAR", CGPoint(x: 40, y: 42), size: 14, color: .lightGray, bold: true, center: false)
    // PSUs
    for i in 0..<2 {
        let r = CGRect(x: 40 + CGFloat(i) * 170, y: 90, width: 150, height: 170)
        box(cg, r, fill: NSColor(calibratedWhite: 0.2, alpha: 1), stroke: .gray, radius: 4)
        box(cg, CGRect(x: r.midX - 30, y: r.minY + 20, width: 60, height: 40), fill: .black, stroke: .gray, radius: 4)
        text("PSU \(i + 1)", CGPoint(x: r.midX, y: r.maxY - 30), size: 13, color: .lightGray)
    }
    let vga = CGRect(x: 420, y: 140, width: 90, height: 40)
    box(cg, vga, fill: NSColor(calibratedRed: 0.2, green: 0.3, blue: 0.7, alpha: 1), stroke: .white, radius: 8)
    text("VGA", CGPoint(x: vga.midX, y: vga.maxY + 6), size: 12, color: .lightGray)
    for i in 0..<2 {
        let usb = CGRect(x: 540 + CGFloat(i) * 50, y: 150, width: 36, height: 18)
        box(cg, usb, fill: .black, stroke: .white, radius: 2)
    }
    text("USB", CGPoint(x: 583, y: 178), size: 12, color: .lightGray)
    let bmc = CGRect(x: 670, y: 140, width: 44, height: 40)
    box(cg, bmc, fill: .black, stroke: .white, radius: 3)
    text("MGMT", CGPoint(x: bmc.midX, y: bmc.maxY + 6), size: 12, color: .lightGray)
    for i in 0..<4 {
        let nic = CGRect(x: 770 + CGFloat(i) * 54, y: 140, width: 44, height: 40)
        box(cg, nic, fill: .black, stroke: .white, radius: 3)
        text("\(i + 1)", CGPoint(x: nic.midX, y: nic.maxY + 6), size: 12, color: .lightGray)
    }
    text("LAN", CGPoint(x: 875, y: 205), size: 12, color: .lightGray)
    let serial = CGRect(x: 1010, y: 140, width: 80, height: 36)
    box(cg, serial, fill: NSColor(calibratedRed: 0.3, green: 0.6, blue: 0.4, alpha: 1), stroke: .white, radius: 8)
    text("SERIAL", CGPoint(x: serial.midX, y: serial.maxY + 6), size: 12, color: .lightGray)
}
rear.regions = [
    ("psu", CGRect(x: 34, y: 84, width: 332, height: 182)),
    ("vga", CGRect(x: 412, y: 132, width: 106, height: 70)),
    ("usb", CGRect(x: 532, y: 142, width: 102, height: 56)),
    ("mgmt", CGRect(x: 662, y: 132, width: 60, height: 70)),
    ("lan", CGRect(x: 762, y: 132, width: 222, height: 90)),
    ("serial", CGRect(x: 1002, y: 132, width: 96, height: 70)),
]
rear.print("server-rear.png")

// MARK: Terminal capture

let lines = [
    "R1# show ip interface brief",
    "Interface              IP-Address      OK? Method Status                Protocol",
    "GigabitEthernet0/0     10.0.0.1        YES NVRAM  up                    up",
    "GigabitEthernet0/1     192.168.1.1     YES NVRAM  administratively down down",
    "GigabitEthernet0/2     unassigned      YES unset  up                    down",
    "Loopback0              1.1.1.1         YES NVRAM  up                    up",
    "R1#",
]
let term = Canvas(width: 1000, height: 260)
term.draw("terminal.png") { cg in
    box(cg, CGRect(x: 0, y: 0, width: 1000, height: 260), fill: NSColor(calibratedWhite: 0.08, alpha: 1), stroke: .clear, radius: 0)
    box(cg, CGRect(x: 0, y: 0, width: 1000, height: 30), fill: NSColor(calibratedWhite: 0.25, alpha: 1), stroke: .clear, radius: 0)
    for (i, c) in [NSColor.systemRed, .systemYellow, .systemGreen].enumerated() {
        cg.setFillColor(c.cgColor); cg.fillEllipse(in: CGRect(x: 12 + CGFloat(i) * 22, y: 9, width: 12, height: 12))
    }
    text("console — R1", CGPoint(x: 500, y: 6), size: 13, color: .lightGray)
    for (i, l) in lines.enumerated() {
        text(l, CGPoint(x: 18, y: 46 + CGFloat(i) * 28), size: 15, color: NSColor(calibratedRed: 0.6, green: 1, blue: 0.6, alpha: 1), mono: true, center: false)
    }
}
print("\nWrote images to \(out.path)")
