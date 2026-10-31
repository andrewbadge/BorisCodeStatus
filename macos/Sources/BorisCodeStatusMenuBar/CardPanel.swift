import AppKit
import BorisCodeStatusCore

/// The "Claude is waiting for you" notification, drawn as the design's display card rather than a
/// system notification: a bezel around a 320×240 screen — the ESP32 panel's own size — with the
/// pet, a pixel-font heading and a bar. A Notification Center banner's layout belongs to macOS and
/// cannot carry any of that, which is the whole reason this is a window of its own. The Mac twin of
/// the Windows `WaitingCard`, with the same geometry.
///
/// It never takes focus. It appears while the user is typing somewhere else, and stealing the
/// keyboard would keep their keystrokes from the terminal where the prompt has to be answered. So
/// it is a non-activating panel that refuses key and main status — which macOS gives for free,
/// where Windows needed extra styles. A click dismisses it.
///
/// The bar counts down to the card hiding itself, and holds while the pointer is over the card.
///
/// Other instances are the idle card, the usage warning (`persistent`: stays until dismissed, with
/// buttons in the chin) and the status card (`pinned`: stays up and is dragged; `mini`: the small
/// one). `CardContent` carries what each says.
///
/// All geometry is in the design's pixels, which are points here: AppKit maps them to whole
/// device pixels on every display, so the sprite and the pixel font stay on the grid without the
/// DPI arithmetic Windows needs.
@MainActor
public final class CardPanel: NSPanel {
    static let lifetime: TimeInterval = 12

    /// The design's bang blinks at 2 fps: half a second on, half off.
    static let bangBlink: TimeInterval = 0.5

    // Design geometry. The screen is 320×240 inside a 3px border and a 14px frame, with a deeper
    // chin below, as sampled from the design's card.
    static let border: CGFloat = 3
    static let frameWidth: CGFloat = 14
    static let chin: CGFloat = 22
    static let screenWidth: CGFloat = 320
    static let screenHeight: CGFloat = 240
    static let inset: CGFloat = 16
    static let dogX: CGFloat = 40
    static let dogY: CGFloat = 77
    static let textX: CGFloat = 158
    static let headerY: CGFloat = 17
    static let titleY: CGFloat = 102
    static let titleLineHeight: CGFloat = 17
    static let barY: CGFloat = 218
    static let barWidth: CGFloat = 249
    static let barHeight: CGFloat = 6

    /// Between a card and the menu bar, the screen edge, and the card above it.
    static let gap: CGFloat = 12

    // The mini status card: two thirds of the full card's width, a fifth of its height, with a
    // thinner frame so the screen keeps most of what little room there is.
    static let miniWidth: CGFloat = 236
    static let miniHeight: CGFloat = 56
    static let miniFrame: CGFloat = 4

    static let fullSize = CGSize(width: screenWidth + 2 * (border + frameWidth), height: screenHeight + border + frameWidth + chin + border)
    static let miniSize = CGSize(width: miniWidth, height: miniHeight)

    static let borderColor = PixelColor(0x3A, 0x36, 0x2B)
    static let frameColor = PixelColor(0x0C, 0x0B, 0x08)
    static let screenColor = PixelColor(0x14, 0x12, 0x0C)
    static let mutedColor = PixelColor(0x9A, 0x93, 0x84)
    static let titleColor = SpriteData.palette[2]
    static let barTrackColor = PixelColor(0x2A, 0x27, 0x1F)

    /// The cards using the default corner, in the order they appeared, so two at once stack rather
    /// than cover each other. A card dragged somewhere of its own leaves the queue.
    private static var stacked: [CardPanel] = []

    private let cardView = CardView()
    private var ticker: Timer?
    private(set) var content = CardContent.from(.from(nil))
    private var remaining = lifetime
    private var lastTick = Date()
    private(set) var shownAt = Date()
    private var paintedWagFrame = 0

    /// Stays until dismissed rather than fading after `lifetime`, for a warning that must not be
    /// missed. Unlike a pinned card it still sits in the stack and is not draggable.
    public var persistent = false

    /// A pinned card stays up until unpinned, and is dragged rather than clicked: it does not count
    /// down, and grabbing it anywhere moves it. Still never takes focus.
    public var pinned = false

    /// Where a pinned card goes when it is next shown, before being kept on screen, in Cocoa
    /// screen coordinates (the frame's origin). Nil puts it in the corner like the waiting card.
    public var pinnedOrigin: CGPoint?

    /// The small status card: pet, state, 5-hour figure and gauge only. Applied at the next show.
    public var mini = false

    /// Who to draw: their portrait on the waiting card, their menu-bar sprites elsewhere.
    public var pet: Pet = .cat

    /// The optional link button's label; only a persistent card shows buttons.
    public var linkText: String?

    /// A click anywhere on a card that is neither pinned nor persistent, after it has hidden itself.
    public var onClick: (() -> Void)?
    /// A double-click on a pinned card — a single press is the start of a drag.
    public var onPinnedDoubleClick: (() -> Void)?
    /// The close button, after the card has hidden itself.
    public var onClose: (() -> Void)?
    /// The link button on a persistent card, after the card has hidden itself.
    public var onLink: (() -> Void)?
    /// The end of a drag of a pinned card.
    public var onMoved: (() -> Void)?

    public init() {
        super.init(contentRect: CGRect(origin: .zero, size: Self.fullSize), styleMask: [.borderless, .nonactivatingPanel], backing: .buffered, defer: true)
        level = .floating
        isFloatingPanel = true
        hidesOnDeactivate = false
        becomesKeyOnlyIfNeeded = true
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        isReleasedWhenClosed = false
        animationBehavior = .utilityWindow
        // On every Space and over full-screen apps: a prompt is just as urgent there.
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        title = "BorisCodeStatus — Claude is waiting"

        cardView.card = self
        contentView = cardView
    }

    public override var canBecomeKey: Bool { false }
    public override var canBecomeMain: Bool { false }

    /// Shows the waiting card, or refreshes it in place if it is already up.
    public func show(_ prompt: WaitingPrompt) {
        show(CardContent.from(prompt))
    }

    /// Shows the card with any content, or refreshes it in place if it is already up.
    public func show(_ content: CardContent) {
        self.content = content
        remaining = Self.lifetime
        lastTick = Date()
        shownAt = lastTick

        if pinned, pinnedOrigin != nil {
            Self.stacked.removeAll { $0 === self }
        } else if !Self.stacked.contains(where: { $0 === self }) {
            Self.stacked.append(self)
        }

        relayout()
        if !isVisible {
            orderFrontRegardless()
        }

        // The ticker drives the countdown and the bang's blink; a pinned card has neither, and
        // would otherwise repaint 20 times a second for as long as it is up. The goat's tongue is
        // the exception: it keeps the ticker, and `tick` repaints a pinned card only on a new frame.
        if pinned, pet != .goat {
            stopTicker()
        } else if ticker == nil {
            let timer = Timer(timeInterval: 0.05, repeats: true) { [weak self] _ in
                MainActor.assumeIsolated { self?.tick() }
            }
            RunLoop.main.add(timer, forMode: .common)
            ticker = timer
        }

        cardView.needsDisplay = true
    }

    /// Hides the card. Safe to call when it is not showing.
    public func dismiss() {
        stopTicker()
        guard isVisible else { return }
        orderOut(nil)

        // A card leaving the queue lets the ones below it move up into the gap.
        if let index = Self.stacked.firstIndex(where: { $0 === self }) {
            Self.stacked.remove(at: index)
            for card in Self.stacked where card.isVisible {
                card.relayout()
            }
        }
    }

    private func stopTicker() {
        ticker?.invalidate()
        ticker = nil
    }

    private func tick() {
        let now = Date()

        // Paused while hovered: someone reading the card should not have it vanish mid-sentence.
        // A pinned or persistent card does not count down at all.
        if !pinned, !persistent, !frame.contains(NSEvent.mouseLocation) {
            remaining -= now.timeIntervalSince(lastTick)
        }
        lastTick = now

        if remaining <= 0 {
            dismiss()
            return
        }

        if pinned {
            // Only the goat gets here (see `show`), and it needs a repaint only on a new frame.
            let frame = wagFrame
            guard frame != paintedWagFrame else { return }
            paintedWagFrame = frame
        }

        cardView.needsDisplay = true
    }

    /// The goat's tongue frame right now; 0 for every pet that does not wag.
    var wagFrame: Int { pet == .goat ? PetSprites.wagFrame(at: Date()) : 0 }

    /// The fraction of the countdown left, for the bar.
    var remainingFraction: Double { min(max(remaining / Self.lifetime, 0), 1) }

    /// Sizes the card and puts it at the top right of the main display, under the menu bar and
    /// beside the status item. Earlier cards keep the top; later ones queue below them.
    func relayout() {
        let size = mini ? Self.miniSize : Self.fullSize
        guard let screen = NSScreen.main ?? NSScreen.screens.first else { return }
        let area = screen.visibleFrame

        var origin = CGPoint(x: area.maxX - size.width - Self.gap, y: area.maxY - size.height - Self.gap)
        if let slot = Self.stacked.firstIndex(where: { $0 === self }), slot > 0 {
            origin.y -= Self.stacked.prefix(slot).reduce(0) { $0 + $1.frame.height + Self.gap }
            origin = Self.keepOnScreen(CGRect(origin: origin, size: size), area)
        }

        if pinned, isVisible || pinnedOrigin != nil {
            // Already up: stay where it was dragged. Otherwise: where it was saved. Either way kept
            // inside the visible area of the display nearest to it.
            let wanted = CGRect(origin: isVisible ? frame.origin : pinnedOrigin!, size: size)
            origin = Self.keepOnScreen(wanted, Self.visibleFrame(nearest: wanted))
        }

        setFrame(CGRect(origin: origin, size: size), display: false)
    }

    /// Moves a card fully inside a visible area — the display it was saved on may have been
    /// unplugged, rearranged, or changed resolution since. Pure, so it can be tested.
    nonisolated static func keepOnScreen(_ card: CGRect, _ area: CGRect) -> CGPoint {
        CGPoint(
            x: max(area.minX, min(card.minX, area.maxX - card.width)),
            y: max(area.minY, min(card.minY, area.maxY - card.height))
        )
    }

    private static func visibleFrame(nearest rect: CGRect) -> CGRect {
        let screens = NSScreen.screens
        let best = screens.max { $0.frame.intersection(rect).area < $1.frame.intersection(rect).area }
        if let best, best.frame.intersects(rect) {
            return best.visibleFrame
        }

        // Off every display: the one whose centre is closest.
        let centre = CGPoint(x: rect.midX, y: rect.midY)
        return screens.min { $0.frame.distance(to: centre) < $1.frame.distance(to: centre) }?.visibleFrame
            ?? NSScreen.main?.visibleFrame ?? rect
    }

    // MARK: - Pointer, from CardView

    fileprivate func clicked(at point: CGPoint, count: Int) {
        if cardView.closeBox.contains(point) {
            dismiss()
            onClose?()
            return
        }

        if pinned {
            if count == 2 {
                onPinnedDoubleClick?()
            }
            return
        }

        // A persistent card is dismissed by its buttons (or the cross), not by a click anywhere: a
        // warning should not vanish because it was clicked by accident.
        if persistent {
            let (dismissBox, linkBox) = cardView.buttonBoxes
            if dismissBox?.contains(point) == true {
                dismiss()
            } else if linkBox?.contains(point) == true {
                dismiss()
                onLink?()
            }
            return
        }

        dismiss()
        onClick?()
    }

    fileprivate func dragged(by delta: CGPoint) {
        setFrameOrigin(CGPoint(x: frame.origin.x + delta.x, y: frame.origin.y + delta.y))
    }

    fileprivate func dragEnded() {
        pinnedOrigin = frame.origin
        onMoved?()
    }
}

/// Draws a `CardPanel` and turns the pointer into its clicks and drags. Flipped, so y runs down
/// as the design's coordinates and the sprite rows do.
@MainActor
final class CardView: NSView {
    weak var card: CardPanel?
    private var closeHot = false
    private var dragStart: CGPoint?
    private var dragMoved = false

    override var isFlipped: Bool { true }
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }

    override func updateTrackingAreas() {
        super.updateTrackingAreas()
        trackingAreas.forEach(removeTrackingArea)
        addTrackingArea(NSTrackingArea(rect: bounds, options: [.mouseMoved, .mouseEnteredAndExited, .activeAlways, .inVisibleRect, .cursorUpdate], owner: self))
    }

    // MARK: Geometry

    private var mini: Bool { card?.mini ?? false }

    var screenRect: CGRect {
        if mini {
            let edge = CardPanel.border + CardPanel.miniFrame
            return bounds.insetBy(dx: edge, dy: edge)
        }
        let edge = CardPanel.border + CardPanel.frameWidth
        return CGRect(x: edge, y: edge, width: CardPanel.screenWidth, height: CardPanel.screenHeight)
    }

    /// The close button's hit area: a small square in the screen's top-right corner, clear of the
    /// header text. On the mini card the 5-hour figure is drawn to leave room for it.
    var closeBox: CGRect {
        let size: CGFloat = 11
        let inset = mini ? CardPanel.border + CardPanel.miniFrame + 1 : CardPanel.border + CardPanel.frameWidth + 3
        return CGRect(x: bounds.width - inset - size, y: inset, width: size, height: size)
    }

    /// The buttons in the chin under the screen, right to left: Dismiss, then the link if there is
    /// one. Nil on a card that is not persistent, and on the mini card, which has no chin.
    var buttonBoxes: (dismiss: CGRect?, link: CGRect?) {
        guard let card, card.persistent, !card.mini else { return (nil, nil) }

        let top = CardPanel.border + CardPanel.frameWidth + CardPanel.screenHeight + 4
        let height = CardPanel.chin - 8
        let right = bounds.width - (CardPanel.border + CardPanel.frameWidth)

        let dismissWidth = PixelFont.measure("Dismiss", pixel: 1, tracking: 1, bold: true) + 16
        let dismiss = CGRect(x: right - dismissWidth, y: top, width: dismissWidth, height: height)

        var link: CGRect?
        if let text = card.linkText {
            let width = PixelFont.measure(text, pixel: 1, tracking: 1, bold: true) + 16
            link = CGRect(x: dismiss.minX - 8 - width, y: top, width: width, height: height)
        }
        return (dismiss, link)
    }

    // MARK: Pointer

    override func mouseMoved(with event: NSEvent) {
        setCloseHot(closeBox.contains(convert(event.locationInWindow, from: nil)))
    }

    override func mouseExited(with event: NSEvent) {
        setCloseHot(false)
    }

    override func cursorUpdate(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        (card?.pinned == true && !closeBox.contains(point) ? NSCursor.openHand : NSCursor.pointingHand).set()
    }

    private func setCloseHot(_ hot: Bool) {
        guard hot != closeHot else { return }
        closeHot = hot
        setNeedsDisplay(closeBox)
    }

    override func mouseDown(with event: NSEvent) {
        let point = convert(event.locationInWindow, from: nil)
        dragMoved = false
        dragStart = card?.pinned == true && !closeBox.contains(point) ? NSEvent.mouseLocation : nil
        if dragStart != nil {
            NSCursor.closedHand.set()
        }
    }

    /// Moved by hand rather than with `isMovableByWindowBackground`, so a press that does not move
    /// still arrives as a click (a double-click on a pinned card brings the chosen app forward).
    override func mouseDragged(with event: NSEvent) {
        guard let start = dragStart, let card else { return }
        let now = NSEvent.mouseLocation
        card.dragged(by: CGPoint(x: now.x - start.x, y: now.y - start.y))
        dragStart = now
        dragMoved = true
    }

    override func mouseUp(with event: NSEvent) {
        defer { dragStart = nil }
        guard let card else { return }

        if dragMoved {
            NSCursor.openHand.set()
            card.dragEnded()
            return
        }

        let point = convert(event.locationInWindow, from: nil)
        guard bounds.contains(point) else { return }
        card.clicked(at: point, count: event.clickCount)
    }

    // MARK: Drawing

    override func draw(_ dirtyRect: NSRect) {
        guard let card, let context = NSGraphicsContext.current?.cgContext else { return }
        context.setShouldAntialias(false)
        context.interpolationQuality = .none

        // Bezel: the border, then the frame inside it, then the screen, all with stepped corners.
        fill(context, Self.notched(bounds, step: 2), CardPanel.borderColor)
        fill(context, Self.notched(bounds.insetBy(dx: CardPanel.border, dy: CardPanel.border), step: 1), CardPanel.frameColor)
        let screen = screenRect
        fill(context, screen, CardPanel.screenColor)

        if card.mini {
            drawMini(context, card, screen)
            drawCloseButton(context)
            return
        }

        let content = card.content

        // Header: where this came from on the left, the state on the right in its accent colour.
        PixelFont.draw("Claude Code", in: context, x: screen.minX + CardPanel.inset, y: screen.minY + CardPanel.headerY, pixel: 1, tracking: 3, color: cg(CardPanel.mutedColor))
        let stateWidth = PixelFont.measure(content.state, pixel: 1, tracking: 3, bold: true)
        PixelFont.draw(content.state, in: context, x: screen.maxX - CardPanel.inset - stateWidth, y: screen.minY + CardPanel.headerY, pixel: 1, tracking: 3, color: cg(content.accent), bold: true)

        drawPet(context, card, x: screen.minX + CardPanel.dogX, y: screen.minY + CardPanel.dogY)

        // Heading, wrapped to the text column; the design breaks "PERMISSION NEEDED" in two.
        let textX = screen.minX + CardPanel.textX
        let textWidth = screen.maxX - CardPanel.inset - textX
        var y = screen.minY + CardPanel.titleY
        for line in PixelFont.wrap(content.title.uppercased(), maxWidth: textWidth, pixel: 1, tracking: 3, bold: true) {
            PixelFont.draw(line, in: context, x: textX, y: y, pixel: 1, tracking: 3, color: cg(CardPanel.titleColor), bold: true)
            y += CardPanel.titleLineHeight
        }

        // Detail: the hook's own sentence, trimmed with an ellipsis rather than overrunning the bar.
        let detailTop = y - CardPanel.titleLineHeight + CGFloat(PixelFont.glyphHeight) + 14
        let detailBounds = CGRect(x: textX, y: detailTop, width: textWidth, height: screen.minY + CardPanel.barY - 12 - detailTop)
        drawDetail(content.detail, in: detailBounds)

        drawBar(context, card, screen)
        drawCloseButton(context)
        drawButtons(context)
    }

    private func drawDetail(_ text: String, in rect: CGRect) {
        guard rect.height > 0 else { return }
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakMode = .byWordWrapping
        let attributes: [NSAttributedString.Key: Any] = [
            .font: NSFont.monospacedSystemFont(ofSize: 11, weight: .regular),
            .foregroundColor: ns(CardPanel.mutedColor),
            .paragraphStyle: paragraph,
        ]
        NSAttributedString(string: text, attributes: attributes)
            .draw(with: rect, options: [.usesLineFragmentOrigin, .truncatesLastVisibleLine])
    }

    /// The mini card: the menu-bar sprite, the state in its accent, the 5-hour figure, and the
    /// gauge. No detail text — at this size it would be unreadable, and the full card is a setting away.
    private func drawMini(_ context: CGContext, _ card: CardPanel, _ screen: CGRect) {
        let content = card.content
        let cell: CGFloat = 2
        let size = CGFloat(PetSprites.size) * cell
        let dogX = screen.minX + 5
        drawTraySprite(context, card, content.dog ?? .waiting, x: dogX, y: screen.minY + ((screen.height - size) / 2).rounded(.down), cell: cell)

        let textX = dogX + size + 8
        let textY = screen.minY + 8
        PixelFont.draw(content.state, in: context, x: textX, y: textY, pixel: 1, tracking: 1, color: cg(content.accent), bold: true)

        let figure = content.gauge.flatMap { used in content.hint == nil ? nil : "5H \(Int(used.rounded()))%" } ?? "5H -"
        let figureWidth = PixelFont.measure(figure, pixel: 1, tracking: 1, bold: true)
        PixelFont.draw(figure, in: context, x: screen.maxX - 16 - figureWidth, y: textY, pixel: 1, tracking: 1, color: cg(CardPanel.titleColor), bold: true)

        let bar = CGRect(x: textX, y: screen.maxY - 12, width: screen.maxX - 8 - textX, height: 4)
        fill(context, bar, CardPanel.barTrackColor)
        let used = content.gauge ?? 0
        let filled = (bar.width * min(max(used / 100, 0), 1)).rounded()
        if filled > 0 {
            fill(context, CGRect(x: bar.minX, y: bar.minY, width: filled, height: bar.height), IconRenderer.quotaColor(for: used))
        }
    }

    /// The pet: the large portrait while waiting, otherwise the menu-bar sprite at six points a cell.
    private func drawPet(_ context: CGContext, _ card: CardPanel, x: CGFloat, y: CGFloat) {
        if card.content.dog == .sleeping, card.pet == .cat {
            // 22×14 at five points is 110×70: centred on the tray sprite's 96×96 square.
            drawGrid(context, SpriteData.catSleepingPortrait, SpriteData.catPalette, x: x - 7, y: y + 13, cell: 5)
            return
        }

        if let pose = card.content.dog {
            drawTraySprite(context, card, pose, x: x, y: y, cell: 6)
            return
        }

        if let (grid, palette) = PetSprites.portrait(card.pet, frame: card.wagFrame) {
            // 18 wide at five points is 90×100 for 20 rows, the dog portrait's footprint at four.
            // The duck's 23 rows are centred on that footprint rather than hung from its top.
            drawGrid(context, grid, palette, x: x, y: y - (CGFloat(grid.count - 20) * 5 / 2).rounded(), cell: 5)
            return
        }

        // Driven off time since the card appeared rather than a frame counter, so the blink keeps
        // its rhythm whatever the tick rate.
        let bangVisible = Int(Date().timeIntervalSince(card.shownAt) / CardPanel.bangBlink) % 2 == 0
        drawGrid(context, SpriteData.waitingPortrait, SpriteData.palette, x: x, y: y, cell: 4) { index in
            index != SpriteData.bangIndex || bangVisible
        }
    }

    private func drawTraySprite(_ context: CGContext, _ card: CardPanel, _ pose: DogState, x: CGFloat, y: CGFloat, cell: CGFloat) {
        let (grid, palette) = PetSprites.tray(pose, card.pet, frame: card.wagFrame)
        drawGrid(context, grid, palette, x: x, y: y, cell: cell)
    }

    /// Blits a palette grid as solid squares of `cell` points.
    private func drawGrid(_ context: CGContext, _ grid: [String], _ palette: [PixelColor], x: CGFloat, y: CGFloat, cell: CGFloat, include: (Int) -> Bool = { _ in true }) {
        for (row, line) in grid.enumerated() {
            for (column, character) in line.enumerated() {
                let index = PetSprites.index(of: character)
                guard index > 0, index < palette.count, include(index) else { continue }
                fill(context, CGRect(x: x + CGFloat(column) * cell, y: y + CGFloat(row) * cell, width: cell, height: cell), palette[index])
            }
        }
    }

    /// The countdown bar, draining right to left — or, on the status card, the session gauge in its
    /// quota colour — with the hint beside it.
    private func drawBar(_ context: CGContext, _ card: CardPanel, _ screen: CGRect) {
        let content = card.content
        let bar = CGRect(x: screen.minX + CardPanel.inset, y: screen.minY + CardPanel.barY, width: CardPanel.barWidth, height: CardPanel.barHeight)
        fill(context, bar, CardPanel.barTrackColor)

        let fraction = content.gauge.map { min(max($0 / 100, 0), 1) } ?? card.remainingFraction
        let filled = (bar.width * fraction).rounded()
        if filled > 0 {
            let color = content.gauge.map(IconRenderer.quotaColor(for:)) ?? content.accent
            fill(context, CGRect(x: bar.minX, y: bar.minY, width: filled, height: bar.height), color)
        }

        if let hint = content.hint {
            let width = PixelFont.measure(hint, pixel: 1, tracking: 1)
            let hintY = (bar.midY - CGFloat(PixelFont.glyphHeight) / 2).rounded(.down)
            PixelFont.draw(hint, in: context, x: screen.maxX - CardPanel.inset - width, y: hintY, pixel: 1, tracking: 1, color: cg(CardPanel.mutedColor))
        }
    }

    private func drawButtons(_ context: CGContext) {
        let (dismiss, link) = buttonBoxes
        for (box, text) in [(dismiss, "Dismiss"), (link, card?.linkText ?? "")] {
            guard let box else { continue }
            context.setStrokeColor(cg(CardPanel.mutedColor))
            context.setLineWidth(1)
            context.stroke(box.insetBy(dx: 0.5, dy: 0.5))

            let width = PixelFont.measure(text, pixel: 1, tracking: 1, bold: true)
            PixelFont.draw(text, in: context, x: (box.minX + (box.width - width) / 2).rounded(.down), y: (box.minY + (box.height - CGFloat(PixelFont.glyphHeight)) / 2).rounded(.down), pixel: 1, tracking: 1, color: cg(CardPanel.titleColor), bold: true)
        }
    }

    /// A 5×5 pixel-art cross, barely there until hovered: muted and mostly transparent at rest,
    /// the full title colour under the pointer.
    private func drawCloseButton(_ context: CGContext) {
        let box = closeBox
        let x = (box.minX + (box.width - 5) / 2).rounded(.down)
        let y = (box.minY + (box.height - 5) / 2).rounded(.down)
        var color = closeHot ? CardPanel.titleColor : CardPanel.mutedColor
        if !closeHot { color.alpha = 0x60 }

        for i in 0..<5 {
            fill(context, CGRect(x: x + CGFloat(i), y: y + CGFloat(i), width: 1, height: 1), color)
            fill(context, CGRect(x: x + CGFloat(4 - i), y: y + CGFloat(i), width: 1, height: 1), color)
        }
    }

    // MARK: Helpers

    /// Stepped corners rather than an antialiased radius: a two-step notch reads as the pixel-art
    /// rounded corner the rest of the card already speaks.
    static func notched(_ rect: CGRect, step: CGFloat) -> CGPath {
        let path = CGMutablePath()
        path.addRect(rect.insetBy(dx: 2 * step, dy: 0))
        path.addRect(rect.insetBy(dx: step, dy: step))
        path.addRect(rect.insetBy(dx: 0, dy: 2 * step))
        return path
    }

    private func fill(_ context: CGContext, _ rect: CGRect, _ color: PixelColor) {
        context.setFillColor(cg(color))
        context.fill(rect)
    }

    private func fill(_ context: CGContext, _ path: CGPath, _ color: PixelColor) {
        context.setFillColor(cg(color))
        context.addPath(path)
        context.fillPath(using: .winding)
    }

    private func cg(_ color: PixelColor) -> CGColor { IconRenderer.cgColor(color) }

    private func ns(_ color: PixelColor) -> NSColor { NSColor(cgColor: cg(color)) ?? .gray }
}

private extension CGRect {
    var area: CGFloat { isNull ? 0 : width * height }

    func distance(to point: CGPoint) -> CGFloat {
        hypot(midX - point.x, midY - point.y)
    }
}
