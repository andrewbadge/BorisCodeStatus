/// Who keeps you company in the menu bar. Presentation only: the pose rule is the same for every
/// pet. The raw values are the Windows build's names, as `settings.json` stores them.
public enum Pet: String, CaseIterable, Codable, Sendable {
    case dog = "Dog"
    case cat = "Cat"
    case bot = "Bot"
    case duck = "Duck"
    case goat = "Goat"
}
