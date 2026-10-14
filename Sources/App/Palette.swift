import AppKit
import SwiftUI

/// Colors that follow the system appearance. System colors cover most of the UI; these are
/// the few surfaces that need a fixed character in both light and dark mode.
enum Palette {
    static func dynamic(light: NSColor, dark: NSColor) -> Color {
        Color(nsColor: NSColor(name: nil) { $0.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua ? dark : light })
    }

    private static func hex(_ v: UInt32, _ a: CGFloat = 1) -> NSColor {
        NSColor(srgbRed: CGFloat((v >> 16) & 0xFF) / 255, green: CGFloat((v >> 8) & 0xFF) / 255, blue: CGFloat(v & 0xFF) / 255, alpha: a)
    }

    // Designer
    static let brand = dynamic(light: hex(0x0B6E69), dark: hex(0x4FB8B0))
    /// Fill for prominent buttons; dark enough for white text in both modes.
    static let brandFill = dynamic(light: hex(0x0B6E69), dark: hex(0x1B7770))
    static let brandSoft = dynamic(light: hex(0xE3F2F1), dark: hex(0x163432))
    static let consolePage = dynamic(light: hex(0xF2F4F7), dark: hex(0x15191F))
    static let consolePanel = dynamic(light: hex(0xFFFFFF), dark: hex(0x1D232B))
    static let consoleLine = dynamic(light: hex(0xD8DEE5), dark: hex(0x313A46))
    static let ok = dynamic(light: hex(0x1F7A3A), dark: hex(0x5CC27C))
    static let okSoft = dynamic(light: hex(0xE5F4EA), dark: hex(0x183223))
    static let warn = dynamic(light: hex(0x8A5300), dark: hex(0xE7B04A))
    static let warnSoft = dynamic(light: hex(0xFDF1DC), dark: hex(0x3A2C12))
    static let err = dynamic(light: hex(0xB42318), dark: hex(0xF07B70))
    static let errSoft = dynamic(light: hex(0xFDE8E6), dark: hex(0x3D1C1A))

    // Exam, modern
    static let examPage = dynamic(light: hex(0xF6F7F9), dark: hex(0x14171B))
    static let examSurface = dynamic(light: hex(0xFFFFFF), dark: hex(0x1C2026))
    static let examLine = dynamic(light: hex(0xDDE2E8), dark: hex(0x323944))
    static let examInk = dynamic(light: hex(0x15191E), dark: hex(0xE8ECF1))
    static let answered = dynamic(light: hex(0x2F6A9F), dark: hex(0x5A9AD6))
    static let flagged = dynamic(light: hex(0xC77700), dark: hex(0xE7A23A))
    static let unanswered = dynamic(light: hex(0xDBE1E7), dark: hex(0x39414C))
    static let timerSoft = dynamic(light: hex(0xFFF6E5), dark: hex(0x3A2C12))

    // Exam, classic
    static let classicTitle = dynamic(light: hex(0x233A55), dark: hex(0x1A2A3E))
    static let classicBar = dynamic(light: hex(0xDFE4EA), dark: hex(0x2A3038))
    static let classicPage = dynamic(light: hex(0xEEF1F4), dark: hex(0x181B20))
    static let classicStage = dynamic(light: hex(0xFFFFFF), dark: hex(0x22262C))
    static let classicLine = dynamic(light: hex(0xC4CCD5), dark: hex(0x3B434E))
}

extension Font {
    /// The plain face testing-center software uses.
    static func classic(_ size: CGFloat, weight: Font.Weight = .regular) -> Font {
        .custom("Arial", size: size).weight(weight)
    }
}
