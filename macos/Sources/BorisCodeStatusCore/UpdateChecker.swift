import Foundation

/// A release number, "major.minor.patch". Compared numerically, so 1.10.0 is newer than 1.9.0.
public struct ReleaseVersion: Comparable, Hashable, Sendable, CustomStringConvertible {
    public var major: Int
    public var minor: Int
    public var patch: Int

    public init(_ major: Int, _ minor: Int, _ patch: Int = 0) {
        self.major = major
        self.minor = minor
        self.patch = patch
    }

    /// "v1.7.0", "1.7.0" or "1.7" to a version; anything else, including a pre-release suffix or a
    /// fourth part, is nil. The same rule as the Windows build's `UpdateChecker.ParseTag`.
    public init?(tag: String?) {
        guard var text = tag?.trimmingCharacters(in: .whitespacesAndNewlines), !text.isEmpty else { return nil }
        if text.first == "v" || text.first == "V" {
            text.removeFirst()
        }

        // ASCII digits only: Int() alone would also take a sign, and isNumber alone other scripts' digits.
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        let numbers = parts.compactMap { $0.allSatisfy { $0.isASCII && $0.isNumber } ? Int($0) : nil }
        guard (2...3).contains(parts.count), numbers.count == parts.count else { return nil }

        self.init(numbers[0], numbers[1], numbers.count == 3 ? numbers[2] : 0)
    }

    public static func < (lhs: ReleaseVersion, rhs: ReleaseVersion) -> Bool {
        (lhs.major, lhs.minor, lhs.patch) < (rhs.major, rhs.minor, rhs.patch)
    }

    public var description: String { "\(major).\(minor).\(patch)" }
}

/// Asks GitHub for the newest published release. Opt-in (see `UpdateCheckPreference`) and
/// anonymous: no token, no account, and nothing about this install in the request — only a fixed
/// User-Agent, which GitHub's API requires. `/releases/latest` already skips drafts and
/// pre-releases.
///
/// Unlike Windows, a release counts only if it carries a Mac download (`macAssetSuffix`): the same
/// tag ships both builds, and announcing a release that has only an MSI would send a Mac user to
/// nothing they can install.
///
/// Nothing here throws: any failure is simply "no answer", and the next check tries again.
public enum UpdateChecker {
    public static let latestReleaseURL = URL(string: "https://api.github.com/repos/andrewbadge/BorisCodeStatus/releases/latest")!

    /// Where the card's link goes.
    public static let releasesPageURL = URL(string: "https://github.com/andrewbadge/BorisCodeStatus/releases")!

    /// How the Release workflow names the Mac download: `BorisCodeStatus-<version>-macOS.dmg`.
    public static let macAssetSuffix = "-macOS.dmg"

    public static func macAssetName(for version: String) -> String {
        "BorisCodeStatus-\(version)\(macAssetSuffix)"
    }

    /// The request, kept apart from sending it so a test can check exactly what leaves the machine.
    public static var request: URLRequest {
        var request = URLRequest(url: latestReleaseURL, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 15)
        request.setValue("BorisCodeStatus", forHTTPHeaderField: "User-Agent")
        request.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        // No cookies either way: the request must carry nothing about this install.
        request.httpShouldHandleCookies = false
        return request
    }

    /// The newest release's version if it has a Mac download, or nil.
    public static func latestMacRelease(session: URLSession = .shared) async -> ReleaseVersion? {
        guard let (data, response) = try? await session.data(for: request),
              (response as? HTTPURLResponse)?.statusCode == 200
        else { return nil }
        return macRelease(from: data)
    }

    /// The release a `/releases/latest` body describes, if it has a usable tag and a Mac asset.
    public static func macRelease(from body: Data) -> ReleaseVersion? {
        guard let root = try? JSONSerialization.jsonObject(with: body) as? [String: Any],
              let version = ReleaseVersion(tag: root["tag_name"] as? String),
              let assets = root["assets"] as? [[String: Any]],
              assets.contains(where: { ($0["name"] as? String)?.hasSuffix(macAssetSuffix) == true })
        else { return nil }
        return version
    }

    /// Whether `latest` is newer than the running `current` ("1.7.0"; "dev" or junk is never older).
    public static func isNewer(_ latest: ReleaseVersion, than current: String?) -> Bool {
        guard let running = ReleaseVersion(tag: current) else { return false }
        return latest > running
    }
}
