/// Who keeps you company in the menu bar. Presentation only: the pose rule is the same for every
/// pet. The dog is the default; choosing another arrives with the settings window.
public enum Pet: String, CaseIterable, Sendable {
    case dog = "Dog"
    case cat = "Cat"
    case bot = "Bot"
    case duck = "Duck"
    case goat = "Goat"
}
