import SwiftUI

enum Appearance: String, CaseIterable, Identifiable {
    case system, light, dark
    var id: Self { self }
    var label: String { rawValue.capitalized }
    var colorScheme: ColorScheme? {
        switch self {
        case .system: nil
        case .light: .light
        case .dark: .dark
        }
    }
}

/// MockiExams > Settings.
struct AppSettingsView: View {
    @AppStorage("appearance") private var appearance: Appearance = .system
    @AppStorage("examStyle") private var style: ExamStyle = .modern
    @AppStorage("fullScreenExams") private var fullScreen = true

    var body: some View {
        Form {
            Picker("Appearance", selection: $appearance) {
                ForEach(Appearance.allCases) { Text($0.label).tag($0) }
            }
            .pickerStyle(.segmented)
            Picker("Exam screen", selection: $style) {
                ForEach(ExamStyle.allCases) { Text($0.label).tag($0) }
            }
            Toggle("Run exam simulations in full screen", isOn: $fullScreen)
        }
        .formStyle(.grouped)
        .frame(width: 460)
        .fixedSize(horizontal: false, vertical: true)
    }
}

/// Applies the Appearance setting to a window's content.
struct AppearanceModifier: ViewModifier {
    @AppStorage("appearance") private var appearance: Appearance = .system
    func body(content: Content) -> some View { content.preferredColorScheme(appearance.colorScheme) }
}

extension View {
    func appAppearance() -> some View { modifier(AppearanceModifier()) }
}
