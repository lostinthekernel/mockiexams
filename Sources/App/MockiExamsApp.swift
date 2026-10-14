import MockiCore
import SwiftData
import SwiftUI

@main
struct MockiExamsApp: App {
    var body: some Scene {
        Window("MockiExams Library", id: "library") {
            LibraryView()
                .frame(minWidth: 900, minHeight: 600)
                .windowToolbarFullScreenVisibility(.onHover)
                .appAppearance()
        }
        .modelContainer(for: Attempt.self)
        .commands {
            CommandGroup(after: .windowArrangement) {
                OpenLibraryButton()
            }
        }

        DocumentGroup(newDocument: ExamProjectDocument()) { file in
            DesignerView(document: file.$document)
                .appAppearance()
                .frame(minWidth: 1100, minHeight: 700)
        }

        Settings {
            AppSettingsView().appAppearance()
        }
    }
}

private struct OpenLibraryButton: View {
    @Environment(\.openWindow) private var openWindow
    var body: some View {
        Button("Library") { openWindow(id: "library") }
            .keyboardShortcut("l", modifiers: [.command, .shift])
    }
}
