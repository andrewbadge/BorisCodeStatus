import Foundation

/// Reads a timestamp that may be an ISO-8601 string or a numeric Unix epoch (seconds, or
/// milliseconds when the value is implausibly large), and writes ISO-8601.
///
/// The shape of `resets_at` is not contractually documented, so accept both rather than fail.
/// The ISO side also has to read what the Windows build writes: .NET emits seven fractional
/// digits (`2026-10-31T01:02:09.9912345+00:00`), more than `ISO8601DateFormatter` is documented
/// to accept, so the fraction is trimmed to milliseconds before parsing.
public enum FlexibleDate {
    /// Anything past this many seconds is far beyond year 2286 and is therefore milliseconds.
    private static let maxPlausibleSeconds: Double = 10_000_000_000

    public static func decode<K: CodingKey>(_ container: KeyedDecodingContainer<K>, _ key: K) -> Date? {
        if let number = try? container.decodeIfPresent(Double.self, forKey: key) {
            return fromEpoch(number)
        }
        if let text = try? container.decodeIfPresent(String.self, forKey: key) {
            return parse(text)
        }
        return nil
    }

    public static func parse(_ text: String) -> Date? {
        let trimmed = text.trimmingCharacters(in: .whitespaces)
        if trimmed.isEmpty {
            return nil
        }
        if let date = parseISO(trimmed) {
            return date
        }
        // Some producers stringify the epoch.
        return Double(trimmed).map(fromEpoch)
    }

    public static func format(_ date: Date) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }

    private static func fromEpoch(_ value: Double) -> Date {
        Date(timeIntervalSince1970: value > maxPlausibleSeconds ? value / 1000 : value)
    }

    private static func parseISO(_ text: String) -> Date? {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime]
        if let date = formatter.date(from: text) {
            return date
        }

        guard let dot = text.firstIndex(of: ".") else { return nil }
        let digits = text[text.index(after: dot)...].prefix { $0.isASCII && $0.isNumber }
        guard !digits.isEmpty else { return nil }

        // Keep the sub-millisecond part as a separate offset so seven-digit .NET timestamps
        // survive a round trip to within a millisecond rather than being rejected outright.
        let rest = text[digits.endIndex...]
        let millis = String(digits.prefix(3)).padding(toLength: 3, withPad: "0", startingAt: 0)
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        guard let date = formatter.date(from: text[..<dot] + "." + millis + rest) else { return nil }

        let extra = digits.dropFirst(3)
        guard !extra.isEmpty, let fraction = Double("0." + extra) else { return date }
        return date.addingTimeInterval(fraction / 1000)
    }
}
