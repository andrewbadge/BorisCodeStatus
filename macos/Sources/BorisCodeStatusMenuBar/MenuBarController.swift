import AppKit
import BorisCodeStatusCore

/// The menu-bar item: draws the pet from live state, keeps a tooltip, and owns the short menu.
///
/// `refresh` runs on every state write *and* every pose-timer tick, so anything with a side
/// effect added here later — a card, a sound — must fire on a transition keyed off
/// `activityChangedUtc`, never on "state is currently X", or it repeats forever.
@MainActor
public final class MenuBarController: NSObject {
    public static let repositoryURL = URL(string: "https://github.com/andrewbadge/BorisCodeStatus")!

    /// Half a minute is well under `DogStates.sleepAfter` and costs nothing. It exists purely so
    /// the pet can fall asleep: nothing writes to the state file while a session is idle, so
    /// without a clock of its own the icon would sit awake indefinitely.
    static let poseInterval: TimeInterval = 30

    private let store: VitalsStateStore
    private let statusItem: NSStatusItem
    private var watcher: DirectoryWatcher?
    private var poseTimer: Timer?
    private var wagTimer: Timer?
    private var pet: Pet = .dog

    /// What is on screen now, so a refresh that changes nothing visible draws nothing.
    private var rendered: (dog: DogState, bucket: Int, pet: Pet, frame: Int)?

    public init(store: VitalsStateStore) {
        self.store = store
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()

        statusItem.menu = buildMenu()
        refresh(now: Date())

        watcher = DirectoryWatcher(directory: store.url.deletingLastPathComponent()) { [weak self] in
            MainActor.assumeIsolated {
                guard let self, self.store.reload() else { return }
                self.refresh(now: Date())
            }
        }

        poseTimer = Timer.scheduledTimer(withTimeInterval: Self.poseInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.refresh(now: Date()) }
        }
    }

    /// Redraws the icon and tooltip for the current state, judged at `now`.
    public func refresh(now: Date) {
        let state = store.state
        updateIcon(state, now: now)
        statusItem.button?.toolTip = Self.tooltip(for: state)
    }

    private func updateIcon(_ state: VitalsState, now: Date) {
        // Redraw only on a visible change: the ring moves in 5% steps, so this is a handful of
        // renders per session rather than one per hook event.
        let bucket = state.session?.usedPercentage.map { Int(min(max($0, 0), 100) / 5) } ?? -1
        let dog = DogStates.pose(for: state, at: now)
        let frame = PetSprites.wags(pet, dog) ? PetSprites.wagFrame(at: now) : 0

        updateWagTimer(wagging: PetSprites.wags(pet, dog))

        if let rendered, rendered == (dog, bucket, pet, frame) {
            return
        }

        statusItem.button?.image = IconRenderer.image(dog: dog, sessionUsedPercentage: state.session?.usedPercentage, pet: pet, frame: frame)
        rendered = (dog, bucket, pet, frame)
    }

    /// Runs the 250ms wag clock only while the goat is actually wagging, rather than four
    /// wake-ups a second for a still icon.
    private func updateWagTimer(wagging: Bool) {
        if wagging, wagTimer == nil {
            wagTimer = Timer.scheduledTimer(withTimeInterval: PetSprites.wagInterval, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated {
                    guard let self else { return }
                    self.updateIcon(self.store.state, now: Date())
                }
            }
        } else if !wagging, let timer = wagTimer {
            timer.invalidate()
            wagTimer = nil
        }
    }

    // MARK: - Menu

    /// Kept short so it opens instantly; the settings window arrives in a later step.
    private func buildMenu() -> NSMenu {
        let menu = NSMenu()

        let header = NSMenuItem(title: "BorisCodeStatus v\(Self.version)", action: #selector(openRepository), keyEquivalent: "")
        header.target = self
        menu.addItem(header)
        menu.addItem(.separator())

        let quit = NSMenuItem(title: "Quit BorisCodeStatus", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
        return menu
    }

    @objc private func openRepository() {
        NSWorkspace.shared.open(Self.repositoryURL)
    }

    /// The bundle's version, which the build stamps from `<Version>` in Directory.Build.props.
    /// "dev" when run straight from `swift run`, which has no bundle.
    static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
    }

    // MARK: - Text

    static func describe(_ activity: ActivityState) -> String {
        switch activity {
        case .working: "Working"
        case .waiting: "Waiting for you"
        case .idle: "Idle"
        case .unknown: "No session seen yet"
        }
    }

    static func tooltip(for state: VitalsState) -> String {
        var lines = ["BorisCodeStatus — \(describe(state.activity))"]
        if let session = state.session?.usedPercentage {
            lines.append("5h \(Int(session.rounded()))%")
        }
        if let week = state.week?.usedPercentage {
            lines.append("7d \(Int(week.rounded()))%")
        }
        return lines.joined(separator: "\n")
    }
}
