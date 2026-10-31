import Foundation
import Testing
@testable import BorisCodeStatusCore

struct UpdateCheckerTests {
    @Test(arguments: [("v1.8.0", "1.8.0"), ("1.8.0", "1.8.0"), ("V2.0.1", "2.0.1"), (" v1.8 ", "1.8.0")])
    func parsesReleaseTags(tag: String, expected: String) {
        #expect(ReleaseVersion(tag: tag)?.description == expected)
    }

    @Test(arguments: [nil, "", "latest", "v1.8.0-beta.1", "v1.2.3.4", "v1..2", "1.٣.0"] as [String?])
    func ignoresTagsItCannotUse(tag: String?) {
        #expect(ReleaseVersion(tag: tag) == nil)
    }

    @Test(arguments: [
        ("1.8.0", "1.7.0", true), ("1.7.1", "1.7.0", true), ("2.0.0", "1.99.99", true),
        ("1.7.0", "1.7.0", false), ("1.6.0", "1.7.0", false), ("1.10.0", "1.9.0", true),
        ("1.7", "1.7.0", false), ("1.8.0", "dev", false),
    ])
    func comparesNumericallyNotAsText(latest: String, current: String, newer: Bool) throws {
        #expect(UpdateChecker.isNewer(try #require(ReleaseVersion(tag: latest)), than: current) == newer)
    }

    @Test func aReleaseWithAMacDownloadCounts() {
        let body = Data("""
        { "tag_name": "v1.9.0", "assets": [ { "name": "BorisCodeStatus-1.9.0.msi" }, { "name": "BorisCodeStatus-1.9.0-macOS.dmg" } ] }
        """.utf8)
        #expect(UpdateChecker.macRelease(from: body) == ReleaseVersion(1, 9, 0))
        #expect(UpdateChecker.macAssetName(for: "1.9.0") == "BorisCodeStatus-1.9.0-macOS.dmg")
    }

    /// The same tag ships both builds; a Windows-only release is nothing a Mac user can install.
    @Test(arguments: [
        #"{ "tag_name": "v1.9.0", "assets": [ { "name": "BorisCodeStatus-1.9.0.msi" } ] }"#,
        #"{ "tag_name": "v1.9.0", "assets": [] }"#,
        #"{ "tag_name": "v1.9.0" }"#,
        #"{ "tag_name": 5, "assets": [ { "name": "BorisCodeStatus-5-macOS.dmg" } ] }"#,
        "not json",
    ])
    func anythingElseIsNoAnswer(body: String) {
        #expect(UpdateChecker.macRelease(from: Data(body.utf8)) == nil)
    }

    @Test func theRequestSaysNothingAboutThisInstall() {
        let request = UpdateChecker.request
        #expect(request.url == UpdateChecker.latestReleaseURL)
        #expect(request.url?.host == "api.github.com")
        #expect(request.value(forHTTPHeaderField: "User-Agent") == "BorisCodeStatus")
        #expect(request.value(forHTTPHeaderField: "Authorization") == nil)
        #expect(!request.httpShouldHandleCookies)
    }
}
