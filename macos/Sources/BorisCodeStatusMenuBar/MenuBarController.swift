import AppKit
import BorisCodeStatusCore

/// The menu-bar item and everything it raises: draws the pet from live state, keeps a tooltip,
/// owns the short menu, and shows the cards and sounds. The Mac counterpart of the Windows
/// `TrayIcon`, minus the HTTP service and the firewall.
///
/// `refresh` runs on every state write *and* every pose-timer tick, so anything with a side
/// effect — a card, a sound — fires on a transition keyed off `activityChangedUtc`, never on
/// "state is currently X", or it would repeat forever.
@MainActor
public final class MenuBarController: NSObject {
    public static let repositoryURL = URL(string: "https://github.com/andrewbadge/BorisCodeStatus")!

    /// Unverified: claude.ai's usage page is not documented; this is where Settings → Usage lives.
    static let usageURL = URL(string: "https://claude.ai/settings/usage")!

    /// Half a minute is well under `DogStates.sleepAfter` and costs nothing. It exists purely so
    /// the pet can fall asleep: nothing writes to the state file while a session is idle, so
    /// without a clock of its own the icon would sit awake indefinitely.
    static let poseInterval: TimeInterval = 30

    private let store: VitalsStateStore
    private let statusItem: NSStatusItem
    private var watcher: DirectoryWatcher?
    private var poseTimer: Timer?
    private var wagTimer: Timer?

    // The preferences, as they stand in this process. Each is read from disk once at startup and
    // kept in step by its setter, so a refresh — every state write — never touches the disk for
    // them. The exceptions, read at the moment of use, say so where they are read.
    public private(set) var pet: Pet
    public private(set) var notificationsOn: Bool
    public private(set) var idleCardOn: Bool
    public private(set) var waitingSoundOn: Bool
    public private(set) var idleSoundOn: Bool
    public private(set) var statusPinned: Bool
    public private(set) var statusMini: Bool
    public private(set) var sessionWarnAt: Int
    public private(set) var updateCheckOn: Bool

    /// What is on screen now, so a refresh that changes nothing visible draws nothing.
    private var rendered: (dog: DogState, bucket: Int, pet: Pet, frame: Int)?

    /// When the wait we last announced began. Guards against announcing the same prompt again on
    /// every refresh, which includes every pose-timer tick.
    private var lastNotifiedWaitAt: Date?

    /// When the turn we last announced ended; the idle counterpart of `lastNotifiedWaitAt`.
    private var lastNotifiedIdleAt: Date?

    /// True once the usage warning has been shown for the current spell above the threshold.
    /// Cleared when usage is back at or below it — which is what a session reset does — so it
    /// fires once per session.
    private var sessionWarned = false

    // Each card is its own instance, created on first use: someone who never meets a prompt, or
    // has the card off, never pays for the window. Separate instances also mean dismissing one —
    // the waiting card comes down on every non-waiting refresh — cannot take another down.
    private var waitingCard: CardPanel?
    private var idleCard: CardPanel?
    private var statusCard: CardPanel?
    private var usageCard: CardPanel?
    private var noticeCard: CardPanel?
    private var updateCard: CardPanel?

    /// How often the opt-in update check runs while it is switched on.
    static let updateCheckInterval: TimeInterval = 6 * 60 * 60
    private var updateTimer: Timer?
    private var checkingForUpdate = false

    /// Called after any refresh, so an open settings window can show the live state.
    public var onRefresh: (() -> Void)?

    /// Opens the settings window. Set by the app, which owns it.
    public var onShowSettings: (() -> Void)?

    public init(store: VitalsStateStore) {
        self.store = store
        let settings = SettingsStore.load()
        pet = settings.pet
        notificationsOn = settings.notificationsEnabled
        idleCardOn = settings.idleCardEnabled
        waitingSoundOn = settings.waitingSoundEnabled
        idleSoundOn = settings.idleSoundEnabled
        statusPinned = settings.statusCardPinned
        statusMini = settings.statusCardMini
        sessionWarnAt = SessionWarningPreference.get()
        updateCheckOn = settings.updateCheckEnabled

        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        super.init()

        // A session already idle when the app starts — at login, say — finished its turn before we
        // were here to see it. Without this baseline every start would announce it.
        if store.state.activity == .idle {
            lastNotifiedIdleAt = store.state.activityChangedUtc
        }

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

        // Always running, but the check does nothing unless switched on, so enabling it later
        // needs no restart and disabling it leaves nothing to tear down.
        updateTimer = Timer.scheduledTimer(withTimeInterval: Self.updateCheckInterval, repeats: true) { [weak self] _ in
            MainActor.assumeIsolated { self?.checkForUpdate() }
        }
        checkForUpdate()
    }

    public var state: VitalsState { store.state }

    /// Redraws the icon and tooltip for the current state, judged at `now`, and raises whatever a
    /// transition calls for.
    public func refresh(now: Date) {
        let state = store.state
        updateIcon(state, now: now)
        statusItem.button?.toolTip = Self.tooltip(for: state)

        if statusPinned {
            showStatusCard(state, now: now)
        }

        notifyIfWaiting(state)
        notifyIfIdle(state)
        warnIfSessionUsageHigh(state, now: now)
        onRefresh?()
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
    /// wake-ups a second for a still icon. It redraws the icon only — not `refresh`, which raises
    /// cards and must run on a transition, not a timer.
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

    // MARK: - Waiting and idle

    /// A card and/or a sound when Claude Code starts waiting on the user — the one state the user
    /// needs to act on, and the easiest to miss while looking at something else.
    ///
    /// Once per wait: keyed off `activityChangedUtc`, so a second prompt in the same session still
    /// gets its own because the timestamp moves.
    private func notifyIfWaiting(_ state: VitalsState) {
        guard state.activity == .waiting else {
            // Forget the last one, so returning to Waiting later announces again even in the
            // unlikely event the timestamp repeats.
            lastNotifiedWaitAt = nil

            // The prompt has been answered in the terminal, so the card is now wrong — take it
            // down rather than leave it counting out its lifetime.
            waitingCard?.dismiss()
            return
        }

        guard state.activityChangedUtc != lastNotifiedWaitAt else { return }
        lastNotifiedWaitAt = state.activityChangedUtc

        // The sound and the card are independent: either, both, or neither.
        if waitingSoundOn {
            WaitingSound.play(pet, alternate: soundAlternate(.waiting))
        }

        if notificationsOn {
            // The hook's own text names what is blocked, e.g. "Claude needs your permission to use
            // Bash"; WaitingPrompt falls back to something plain if it is absent.
            showWaitingCard(.from(state.waitingMessage))
        }
    }

    private func showWaitingCard(_ prompt: WaitingPrompt) {
        let card = waitingCard ?? makeCard { [weak self] in self?.bringClickTargetToFront() }
        waitingCard = card
        card.pet = pet
        card.show(prompt)
    }

    /// The idle counterpart of `notifyIfWaiting`: a sound and/or a card when a turn ends. Same
    /// transition rule, and the card comes down as soon as the next turn starts.
    private func notifyIfIdle(_ state: VitalsState) {
        guard state.activity == .idle else {
            lastNotifiedIdleAt = nil
            idleCard?.dismiss()
            return
        }

        guard state.activityChangedUtc != lastNotifiedIdleAt else { return }
        lastNotifiedIdleAt = state.activityChangedUtc

        if idleSoundOn {
            WaitingSound.play(pet, alternate: soundAlternate(.idle))
        }

        if idleCardOn {
            showIdleCard()
        }
    }

    private func showIdleCard() {
        let card = idleCard ?? makeCard { [weak self] in self?.bringClickTargetToFront() }
        idleCard = card
        card.pet = pet
        card.show(CardContent(
            state: "Idle",
            accent: IconRenderer.quotaColor(for: 0),
            title: "Your turn",
            detail: "Claude has finished and is waiting for your next message.",
            hint: nil,
            dog: .idle
        ))
    }

    /// The card dismisses itself on click; this adds the optional jump to the chosen app. Read at
    /// the moment of use, so a change applies to the next click.
    ///
    /// Raises the app, not a particular window: that would need the Accessibility permission.
    /// And only a running app — launching one the user has quit would be a surprise.
    private func bringClickTargetToFront() {
        guard let identifier = ClickTargetPreference.get() else { return }

        guard let running = NSRunningApplication.runningApplications(withBundleIdentifier: identifier).first,
              let url = running.bundleURL
        else {
            notice("That app is not running, so there was nothing to bring to the front.")
            return
        }

        // openApplication rather than running.activate(): from a background app, activate() is
        // often ignored on macOS 14 and later.
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = true
        NSWorkspace.shared.openApplication(at: url, configuration: configuration)
    }

    // MARK: - Usage warning

    /// A card the first time session usage passes the threshold. Like the other notifications
    /// this runs on every refresh, so it is gated by `sessionWarned` rather than by "usage is
    /// currently high".
    private func warnIfSessionUsageHigh(_ state: VitalsState, now: Date) {
        guard let used = state.session?.usedPercentage, used > Double(sessionWarnAt) else {
            sessionWarned = false
            return
        }

        guard !sessionWarned else { return }
        sessionWarned = true

        var detail = "Past your \(sessionWarnAt)% warning."
        if let minutes = state.session?.resetsInMinutes(at: now) {
            detail += "\nResets in \(Self.formatDuration(minutes))."
        }

        // Persistent: it stays until dismissed, and the other cards' dismissals cannot take it down.
        let card = usageCard ?? {
            let card = makeCard()
            card.persistent = true
            card.linkText = "Usage"
            card.onLink = { NSWorkspace.shared.open(Self.usageURL) }
            return card
        }()
        usageCard = card
        card.pet = pet
        card.show(CardContent(
            state: "Warning",
            accent: IconRenderer.quotaColor(for: used),
            title: "5h \(Int(used.rounded()))% used",
            detail: detail,
            hint: "5H",
            dog: .waiting,
            gauge: used
        ))
    }

    // MARK: - Status card

    /// The current status on the display card. Unpinned it is a snapshot that hides itself like
    /// the waiting card; pinned, `refresh` calls this on every state write and pose tick, so it
    /// stays current where the user dragged it.
    public func showStatusCard() {
        showStatusCard(store.state, now: Date())
    }

    private func showStatusCard(_ state: VitalsState, now: Date) {
        let dog = DogStates.pose(for: state, at: now)
        let session = state.session?.usedPercentage

        var detail: [String] = []
        if let resets = state.session?.resetsInMinutes(at: now) {
            detail.append("Resets in \(Self.formatDuration(resets))")
        }
        detail.append("Week: \(Self.formatWindow(state.week, now: now))")
        if let model = state.modelDisplayName {
            detail.append(model)
        }
        detail.append(notificationsOn ? "notify on" : "notify off")

        let card = statusCard ?? makeStatusCard()
        statusCard = card
        card.pinned = statusPinned
        card.mini = statusMini
        card.pet = pet
        if card.pinnedOrigin == nil, let saved = StatusCardPreference.position() {
            card.pinnedOrigin = Self.cocoaOrigin(saved, height: (statusMini ? CardPanel.miniSize : CardPanel.fullSize).height)
        }

        card.show(CardContent(
            state: Self.describe(dog),
            accent: CardContent.accent(for: dog),
            title: session.map { "5h \(Int($0.rounded()))% used" } ?? "No usage yet",
            detail: detail.joined(separator: "\n"),
            hint: session == nil ? nil : "5H",
            dog: dog,
            gauge: session ?? 0
        ))
    }

    private func makeStatusCard() -> CardPanel {
        let card = makeCard()
        card.onPinnedDoubleClick = { [weak self] in self?.bringClickTargetToFront() }

        // Closing a pinned card means "put it away": unpin, or the next refresh brings it back.
        card.onClose = { [weak self] in
            guard let self, self.statusPinned else { return }
            self.setStatusPinned(false)
        }

        // Saved as left, not clamped: it is kept on screen when next used, against whatever
        // displays are there then.
        card.onMoved = { [weak self, weak card] in
            guard let self, let card else { return }
            self.save(StatusCardPreference.trySavePosition(Self.storedPosition(card.frame)))
        }
        return card
    }

    /// Pins the status card on screen, or lets it go back to a self-hiding snapshot.
    public func setStatusPinned(_ pinned: Bool) {
        statusPinned = pinned
        if pinned {
            showStatusCard()
        } else if let card = statusCard {
            card.pinned = false
            card.dismiss()
        }
        save(StatusCardPreference.trySetPinned(pinned))
        onRefresh?()
    }

    /// Switches the status card between full and mini, redrawn at once if it is up.
    public func setStatusMini(_ mini: Bool) {
        statusMini = mini
        if statusCard?.isVisible == true {
            showStatusCard()
        }
        save(StatusCardPreference.trySetMini(mini))
        onRefresh?()
    }

    // MARK: - Settings the window changes

    public func cardOn(_ moment: NotifyMoment) -> Bool { moment == .idle ? idleCardOn : notificationsOn }

    public func soundOn(_ moment: NotifyMoment) -> Bool { moment == .idle ? idleSoundOn : waitingSoundOn }

    /// Whether the current pet uses its second sound at this moment: woof for the dog, meow for
    /// the cat, and so on. Read from disk because it is kept per pet.
    public func soundAlternate(_ moment: NotifyMoment) -> Bool { SoundPreference.usesAlternate(moment, pet) }

    /// Switching a card on shows it, so the choice is confirmed by the thing itself. Switching off
    /// is confirmed by nothing: a card saying "cards off" would be a small joke at the user's expense.
    public func setCard(_ moment: NotifyMoment, _ on: Bool) {
        if moment == .idle {
            idleCardOn = on
            save(NotificationPreference.trySetIdleCardEnabled(on))
            if on { showIdleCard() }
        } else {
            notificationsOn = on
            save(NotificationPreference.trySetEnabled(on))
            if on {
                showWaitingCard(WaitingPrompt(title: "Notifications on", detail: "You will see this card whenever Claude is waiting for you.", hint: nil))
            }
        }
        onRefresh?()
    }

    /// Switching on plays it, the way switching a card on shows the card.
    public func setSound(_ moment: NotifyMoment, _ on: Bool) {
        if moment == .idle { idleSoundOn = on } else { waitingSoundOn = on }
        save(SoundPreference.trySetEnabled(moment, on))
        if on {
            WaitingSound.play(pet, alternate: soundAlternate(moment))
        }
        onRefresh?()
    }

    /// Picks the current pet's sound for a moment and plays it, so the choice can be heard.
    public func setSoundAlternate(_ moment: NotifyMoment, _ alternate: Bool) {
        save(SoundPreference.trySetAlternate(moment, pet, alternate))
        WaitingSound.play(pet, alternate: alternate)
        onRefresh?()
    }

    /// Which pet. Takes effect at once: the choice is part of the icon's cache key, so the refresh
    /// redraws, and a pinned status card is redrawn by that same refresh.
    public func setPet(_ pet: Pet) {
        guard pet != self.pet else { return }
        self.pet = pet
        save(PetPreference.trySet(pet))
        refresh(now: Date())
    }

    public func setSessionWarnAt(_ percent: Int) {
        sessionWarnAt = min(max(percent, SessionWarningPreference.range.lowerBound), SessionWarningPreference.range.upperBound)
        // A new threshold is a new question: let the next refresh judge usage against it.
        sessionWarned = false
        save(SessionWarningPreference.trySet(sessionWarnAt))
        refresh(now: Date())
    }

    /// Says when a change will not survive a restart, rather than let someone discover it after one.
    private func save(_ persisted: Bool) {
        if !persisted {
            notice("That preference could not be saved, so it will not survive a restart.")
        }
    }

    /// The Mac's stand-in for the Windows balloon: a persistent card, so it waits to be read.
    public func notice(_ message: String) {
        let card = noticeCard ?? {
            let card = makeCard()
            card.persistent = true
            return card
        }()
        noticeCard = card
        card.pet = pet
        card.show(CardContent(state: "Notice", accent: CardPanel.mutedColor, title: "BorisCodeStatus", detail: message, hint: nil, dog: .idle, gauge: 0))
    }

    private func makeCard(onClick: (() -> Void)? = nil) -> CardPanel {
        let card = CardPanel()
        card.onClick = onClick
        return card
    }

    // MARK: - Update check

    /// Asks GitHub for the latest release and, if it has a Mac download, is newer than this copy
    /// and has not been announced yet, shows the update card. Does nothing unless switched on; one
    /// request at a time. A failed request is silent — the next tick tries again.
    private func checkForUpdate() {
        guard updateCheckOn, !checkingForUpdate else { return }
        checkingForUpdate = true

        Task { @MainActor [weak self] in
            let latest = await UpdateChecker.latestMacRelease()
            guard let self else { return }
            self.checkingForUpdate = false

            // The user may have switched the check off meanwhile.
            guard self.updateCheckOn, let latest, UpdateChecker.isNewer(latest, than: Self.version) else { return }

            let announced = latest.description
            guard UpdateCheckPreference.notifiedVersion() != announced else { return }

            // Recorded first: if it cannot be saved the card still shows, at worst again next start.
            UpdateCheckPreference.trySetNotifiedVersion(announced)
            self.showUpdateCard(announced)
        }
    }

    /// Persistent like the usage warning: an update is easy to dismiss unread if it fades.
    private func showUpdateCard(_ version: String) {
        let card = updateCard ?? {
            let card = makeCard()
            card.persistent = true
            card.linkText = "Releases"
            card.onLink = { NSWorkspace.shared.open(UpdateChecker.releasesPageURL) }
            return card
        }()
        updateCard = card
        card.pet = pet
        card.show(CardContent(
            state: "Update",
            accent: CardContent.accent(for: .working),
            title: "v\(version) is out",
            detail: "You are running v\(Self.version). The new release is on GitHub.",
            hint: nil,
            dog: .idle,
            gauge: 0
        ))
    }

    /// Switching on checks at once, so the choice is confirmed by the result.
    public func setUpdateCheck(_ on: Bool) {
        updateCheckOn = on
        save(UpdateCheckPreference.trySetEnabled(on))
        checkForUpdate()
        onRefresh?()
    }

    // MARK: - Hooks

    /// The helper inside this bundle. Under `swift run` there is no bundle, so this points at a
    /// path that does not exist and `HookPathGuard` refuses it.
    public static var hooksPath: String {
        Bundle.main.bundleURL.appending(path: "Contents/Helpers/\(ClaudeSettingsMerger.hooksExecutableName)").path(percentEncoded: false)
    }

    /// Merges this copy's hook registrations into `~/.claude/settings.json`. Run on every launch:
    /// it is idempotent, so it also repairs a stale path after the app has been moved or upgraded.
    ///
    /// At launch it is quiet about the ordinary refusals — a development build, a third-party
    /// statusLine — as Windows is, because the app starts at login and should not greet the user
    /// with a card. Two things are worth one: a translocated copy, which looks installed but cannot
    /// register, and a registration whose helper has gone, which fails silently on every event.
    /// From the settings window (`reporting`), every outcome is reported.
    public func registerHooks(reporting: Bool) {
        let path = Self.hooksPath
        let result = ClaudeSettingsMerger.merge(hooksPath: path)

        if reporting {
            if !result.warnings.isEmpty {
                notice(result.warnings.joined(separator: " "))
            } else {
                notice(result.changed
                    ? "Hooks registered in ~/.claude/settings.json. Restart Claude Code to pick them up."
                    : "Hooks were already registered correctly.")
            }
            onRefresh?()
            return
        }

        if path.contains("/AppTranslocation/"), let warning = result.warnings.first {
            notice(warning)
        } else if let registered = ClaudeSettingsMerger.registeredHooksPath(),
                  !FileManager.default.isExecutableFile(atPath: registered) {
            notice("Claude Code is set to run a BorisCodeStatus hook that no longer exists (\(registered)), so nothing reaches this app. " +
                "Open Settings, then Advanced, and choose Re-register hooks from an installed copy.")
        }
    }

    /// Removes our entries from `~/.claude/settings.json` — the step to take before dragging the
    /// app to the Bin, since a Mac app has no uninstaller to do it.
    public func unregisterHooks() {
        let result = ClaudeSettingsMerger.unregister()
        if !result.warnings.isEmpty {
            notice(result.warnings.joined(separator: " "))
        } else {
            notice(result.changed
                ? "Hooks removed from ~/.claude/settings.json. Quit BorisCodeStatus before moving it to the Bin: opening it again registers them again."
                : "No BorisCodeStatus hooks were registered.")
        }
        onRefresh?()
    }

    /// Where Claude Code currently runs our hook from, or nil if no entry of ours is registered.
    public var registeredHooksPath: String? { ClaudeSettingsMerger.registeredHooksPath() }

    // MARK: - Menu

    /// Kept short so it opens instantly; everything else lives in the settings window. The header
    /// is the version and links to the source — under the GPL, worth keeping one click away.
    private func buildMenu() -> NSMenu {
        let menu = NSMenu()

        let header = NSMenuItem(title: "BorisCodeStatus v\(Self.version)", action: #selector(openRepository), keyEquivalent: "")
        header.target = self
        menu.addItem(header)
        menu.addItem(.separator())

        let status = NSMenuItem(title: "Show Status Card", action: #selector(showStatusCardFromMenu), keyEquivalent: "")
        status.target = self
        menu.addItem(status)

        let settings = NSMenuItem(title: "Settings…", action: #selector(showSettingsFromMenu), keyEquivalent: ",")
        settings.target = self
        menu.addItem(settings)
        menu.addItem(.separator())

        let quit = NSMenuItem(title: "Quit BorisCodeStatus", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        menu.addItem(quit)
        return menu
    }

    @objc private func openRepository() {
        NSWorkspace.shared.open(Self.repositoryURL)
    }

    @objc private func showStatusCardFromMenu() {
        showStatusCard()
    }

    @objc private func showSettingsFromMenu() {
        onShowSettings?()
    }

    /// The bundle's version, which the build stamps from `<Version>` in Directory.Build.props.
    /// "dev" when run straight from `swift run`, which has no bundle.
    public static var version: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "dev"
    }

    // MARK: - Positions

    /// A card's frame as `StatusCardPreference` stores it: the top-left corner, measured down from
    /// the top of the main display, as Windows measures it.
    static func storedPosition(_ frame: CGRect) -> CardPosition {
        let top = NSScreen.screens.first?.frame.maxY ?? frame.maxY
        return CardPosition(x: Int(frame.minX.rounded()), y: Int((top - frame.maxY).rounded()))
    }

    /// The Cocoa frame origin for a stored position and a card `height`.
    static func cocoaOrigin(_ position: CardPosition, height: CGFloat) -> CGPoint {
        let top = NSScreen.screens.first?.frame.maxY ?? 0
        return CGPoint(x: CGFloat(position.x), y: top - CGFloat(position.y) - height)
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

    static func describe(_ dog: DogState) -> String {
        switch dog {
        case .working: "Working"
        case .waiting: "Waiting"
        case .idle: "Idle"
        case .sleeping: "Sleeping"
        }
    }

    static func formatWindow(_ window: RateLimitWindow?, now: Date) -> String {
        guard let used = window?.usedPercentage else { return "—" }
        let text = "\(used.formatted(.number.precision(.fractionLength(0...1))))% used"
        guard let minutes = window?.resetsInMinutes(at: now) else { return text }
        return "\(text), resets in \(formatDuration(minutes))"
    }

    public static func formatDuration(_ minutes: Int) -> String {
        switch minutes {
        case ..<60: "\(minutes)m"
        case ..<(60 * 24): "\(minutes / 60)h \(minutes % 60)m"
        default: "\(minutes / (60 * 24))d \(minutes % (60 * 24) / 60)h"
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
