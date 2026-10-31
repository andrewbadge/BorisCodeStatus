import AppKit
import BorisCodeStatusCore
import BorisCodeStatusMenuBar

// The menu-bar app: one long-lived process that watches state.json and draws the pet. It serves
// nothing over the network — the Mac build has no HTTP service (see docs/macos-port.md).

@MainActor
final class AppDelegate: NSObject, NSApplicationDelegate {
    private var menuBar: MenuBarController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        // One copy only: two would draw two icons and race each other over the same files.
        // A bundle identifier exists only in the built .app, not under `swift run`.
        if let identifier = Bundle.main.bundleIdentifier,
           NSRunningApplication.runningApplications(withBundleIdentifier: identifier)
               .contains(where: { $0 != NSRunningApplication.current }) {
            NSApp.terminate(nil)
            return
        }

        menuBar = MenuBarController(store: VitalsStateStore.makeDefault())
    }
}

let app = NSApplication.shared
let delegate = AppDelegate()
app.delegate = delegate
// No Dock icon and no main menu, as LSUIElement does in the bundle; set here too so `swift run`
// behaves the same.
app.setActivationPolicy(.accessory)
app.run()
