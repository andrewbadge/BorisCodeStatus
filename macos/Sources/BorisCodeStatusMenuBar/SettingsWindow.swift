import AppKit
import BorisCodeStatusCore
import SwiftUI

/// The settings window the menu's **Settings…** opens: Notify, Card, Usage, Advanced and About, as
/// on Windows, less what the Mac build does not have (the HTTP service, the usage API and the
/// firewall). Hook registration arrives on the Advanced page with step 5 of docs/macos-port.md, and
/// the update check with the Mac release in step 6.
///
/// It owns no state. Every value is read from `MenuBarController`, and every control calls it, so
/// the menu bar and the cards change at once and the file write happens in one place.
@MainActor
public final class SettingsWindowController: NSObject, NSWindowDelegate {
    private let controller: MenuBarController
    private let model: SettingsModel
    private var window: NSWindow?

    public init(controller: MenuBarController) {
        self.controller = controller
        model = SettingsModel(controller: controller)
        super.init()
    }

    /// Opens the window, or brings the open one forward — never a second copy.
    public func show() {
        if window == nil {
            let window = NSWindow(contentViewController: NSHostingController(rootView: SettingsView(model: model)))
            window.title = "BorisCodeStatus Settings"
            window.styleMask = [.titled, .closable, .miniaturizable]
            window.isReleasedWhenClosed = false
            window.delegate = self
            window.center()
            self.window = window
        }

        model.reload()
        model.loadApps()
        // An accessory app has to activate itself, or the window opens behind whatever is in front.
        // The user asked for it, so taking focus here is right — unlike the cards.
        NSApp.activate()
        window?.makeKeyAndOrderFront(nil)
    }

    /// Live state for the window while it is open; a no-op when it is not.
    public func refresh() {
        guard window?.isVisible == true else { return }
        model.reload()
    }

    public func windowWillClose(_ notification: Notification) {
        window = nil
    }
}

/// What the window shows, copied from the controller on every refresh so SwiftUI sees the change.
@MainActor
@Observable
final class SettingsModel {
    let controller: MenuBarController

    var pet: Pet = .cat
    var cardOn: [NotifyMoment: Bool] = [:]
    var soundOn: [NotifyMoment: Bool] = [:]
    var soundAlternate: [NotifyMoment: Bool] = [:]
    var pinned = false
    var mini = false
    var warnAt = SessionWarningPreference.defaultPercent
    var state = VitalsState()
    var clickTarget: String?
    var apps: [(identifier: String, name: String)] = []

    init(controller: MenuBarController) {
        self.controller = controller
        reload()
    }

    func reload() {
        pet = controller.pet
        for moment in [NotifyMoment.waiting, .idle] {
            cardOn[moment] = controller.cardOn(moment)
            soundOn[moment] = controller.soundOn(moment)
            soundAlternate[moment] = controller.soundAlternate(moment)
        }
        pinned = controller.statusPinned
        mini = controller.statusMini
        warnAt = controller.sessionWarnAt
        state = controller.state
        clickTarget = ClickTargetPreference.get()
    }

    /// The apps that are open now, plus a saved choice that is not running — listed anyway, so the
    /// window never hides what is set. Rebuilt each time the window opens.
    func loadApps() {
        let own = Bundle.main.bundleIdentifier
        var found: [String: String] = [:]
        for app in NSWorkspace.shared.runningApplications where app.activationPolicy == .regular {
            guard let identifier = app.bundleIdentifier, identifier != own else { continue }
            found[identifier] = app.localizedName ?? identifier
        }

        var list = found.map { (identifier: $0.key, name: $0.value) }.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
        if let current = clickTarget, found[current] == nil {
            let name = NSWorkspace.shared.urlForApplication(withBundleIdentifier: current)
                .map { FileManager.default.displayName(atPath: $0.path) } ?? current
            list.insert((current, "\(name.replacingOccurrences(of: ".app", with: "")) (not running)"), at: 0)
        }
        apps = list
    }

    func setClickTarget(_ identifier: String?) {
        if !ClickTargetPreference.trySet(identifier) {
            controller.notice("That preference could not be saved.")
        }
        clickTarget = ClickTargetPreference.get()
    }
}

struct SettingsView: View {
    @Bindable var model: SettingsModel

    var body: some View {
        TabView {
            NotifyPage(model: model).tabItem { Label("Notify", systemImage: "bell") }
            CardPage(model: model).tabItem { Label("Card", systemImage: "rectangle.on.rectangle") }
            UsagePage(model: model).tabItem { Label("Usage", systemImage: "gauge.with.dots.needle.67percent") }
            AdvancedPage().tabItem { Label("Advanced", systemImage: "gearshape.2") }
            AboutPage().tabItem { Label("About", systemImage: "info.circle") }
        }
        .frame(width: 520, height: 560)
        .padding(.top, 8)
    }
}

private struct NotifyPage: View {
    @Bindable var model: SettingsModel

    var body: some View {
        Form {
            moment(.waiting, "When Claude is waiting")
            moment(.idle, "When Claude finishes a turn")

            Section {
                Picker("Clicking a card", selection: Binding(
                    get: { model.clickTarget ?? "" },
                    set: { model.setClickTarget($0.isEmpty ? nil : $0) }
                )) {
                    Text("Just dismisses it").tag("")
                    ForEach(model.apps, id: \.identifier) { app in
                        Text(app.name).tag(app.identifier)
                    }
                }
            } header: {
                Text("When a card is clicked")
            } footer: {
                Text("Clicking a card closes it. It can also bring one of your open apps to the front.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func moment(_ moment: NotifyMoment, _ title: String) -> some View {
        let names = WaitingSound.names(model.pet)
        return Section(title) {
            Toggle("Show a card", isOn: Binding(
                get: { model.cardOn[moment] ?? false },
                set: { model.controller.setCard(moment, $0); model.reload() }
            ))
            Toggle("Play a sound", isOn: Binding(
                get: { model.soundOn[moment] ?? false },
                set: { model.controller.setSound(moment, $0); model.reload() }
            ))
            Picker("Sound", selection: Binding(
                get: { model.soundAlternate[moment] ?? false },
                set: { model.controller.setSoundAlternate(moment, $0); model.reload() }
            )) {
                Text(names.default).tag(false)
                Text(names.alternate).tag(true)
            }
            .pickerStyle(.segmented)
        }
    }
}

private struct CardPage: View {
    @Bindable var model: SettingsModel

    var body: some View {
        Form {
            Section("Status card") {
                Toggle("Keep status card on screen", isOn: Binding(
                    get: { model.pinned },
                    set: { model.controller.setStatusPinned($0); model.reload() }
                ))
                Toggle("Mini status card", isOn: Binding(
                    get: { model.mini },
                    set: { model.controller.setStatusMini($0); model.reload() }
                ))
                Button("Show status card") { model.controller.showStatusCard() }
            }

            Section("Who keeps you company?") {
                LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], alignment: .leading, spacing: 8) {
                    ForEach(Pet.allCases, id: \.self) { pet in
                        petChoice(pet)
                    }
                }
                .padding(.vertical, 4)
            }
        }
        .formStyle(.grouped)
    }

    private func petChoice(_ pet: Pet) -> some View {
        let chosen = model.pet == pet
        return Button {
            model.controller.setPet(pet)
            model.reload()
        } label: {
            HStack(spacing: 10) {
                Image(nsImage: IconRenderer.spriteImage(pet: pet, dog: .idle, cell: 2))
                    .interpolation(.none)
                Text(Self.label(pet))
                Spacer()
                if chosen {
                    Image(systemName: "checkmark").foregroundStyle(.tint)
                }
            }
            .padding(8)
            .contentShape(Rectangle())
            .background(chosen ? Color.accentColor.opacity(0.15) : .clear, in: RoundedRectangle(cornerRadius: 6))
        }
        .buttonStyle(.plain)
    }

    static func label(_ pet: Pet) -> String {
        switch pet {
        case .bot: "Sentry bot"
        case .duck: "Rubber duck"
        default: pet.rawValue
        }
    }
}

private struct UsagePage: View {
    @Bindable var model: SettingsModel
    @State private var dragging: Double?

    var body: some View {
        Form {
            Section("Now") {
                LabeledContent("Status", value: MenuBarController.describe(model.state.activity))
                LabeledContent("Session  5h", value: window(model.state.session))
                LabeledContent("Week  7d", value: window(model.state.week))
            }

            Section {
                let value = dragging ?? Double(model.warnAt)
                LabeledContent("Warn at", value: value >= 100 ? "Never" : "\(Int(value))%")
                // Saved on release, not on every step of the drag: each change is a file write and
                // a refresh.
                Slider(
                    value: Binding(get: { dragging ?? Double(model.warnAt) }, set: { dragging = $0 }),
                    in: Double(SessionWarningPreference.range.lowerBound)...Double(SessionWarningPreference.range.upperBound),
                    step: 5
                ) { editing in
                    if !editing, let dragging {
                        model.controller.setSessionWarnAt(Int(dragging))
                        self.dragging = nil
                        model.reload()
                    }
                }
            } header: {
                Text("Session warning")
            } footer: {
                Text("Warn me once per session when 5h usage passes this. 100% never warns.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }

    private func window(_ window: RateLimitWindow?) -> String {
        MenuBarController.formatWindow(window, now: Date())
    }
}

private struct AdvancedPage: View {
    var body: some View {
        Form {
            Section {
                LabeledContent("Data folder") {
                    Button("Show in Finder") {
                        NSWorkspace.shared.activateFileViewerSelecting([VitalsPaths.stateFile])
                    }
                }
                LabeledContent("Error log") {
                    Button("Show in Finder") {
                        let log = VitalsPaths.errorLogFile
                        NSWorkspace.shared.activateFileViewerSelecting([FileManager.default.fileExists(atPath: log.path) ? log : VitalsPaths.dataDirectory])
                    }
                }
            } footer: {
                Text(VitalsPaths.dataDirectory.path(percentEncoded: false))
                    .foregroundStyle(.secondary)
                    .textSelection(.enabled)
            }
        }
        .formStyle(.grouped)
    }
}

private struct AboutPage: View {
    var body: some View {
        Form {
            Section {
                LabeledContent("Version", value: "v\(MenuBarController.version)")
                LabeledContent("Source") {
                    Button("Open on GitHub") { NSWorkspace.shared.open(MenuBarController.repositoryURL) }
                }
                LabeledContent("Releases") {
                    Button("Open") { NSWorkspace.shared.open(MenuBarController.repositoryURL.appending(path: "releases")) }
                }
            } footer: {
                Text("Free software under the GPL-3.0-or-later. No telemetry, and no network calls at all.")
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
    }
}
