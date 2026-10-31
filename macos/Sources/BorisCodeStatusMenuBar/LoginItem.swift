import BorisCodeStatusCore
import Foundation
import ServiceManagement

/// Open at Login, through `SMAppService.mainApp`: the app appears under System Settings → General →
/// Login Items, where the user can switch it off without this app's help. The Mac form of the
/// Windows Run key.
@MainActor
public enum LoginItem {
    public static var status: SMAppService.Status { SMAppService.mainApp.status }

    public static var isEnabled: Bool { status == .enabled }

    /// Turned off in System Settings, or blocked by a management profile, and waiting for the user
    /// to allow it there.
    public static var needsApproval: Bool { status == .requiresApproval }

    /// Turns it on or off. Throws what `SMAppService` throws, for the caller to report.
    public static func set(_ enabled: Bool) throws {
        if enabled {
            try SMAppService.mainApp.register()
        } else {
            try SMAppService.mainApp.unregister()
        }
    }

    /// On the first launch of an installed copy, turns Open at Login on, as the Windows installer
    /// does — once. Recorded in `settings.json` only when it worked, so a failure is retried on
    /// the next launch, and never again after it has worked, so turning it off sticks.
    ///
    /// Not from a translocated copy or a build folder: `helperPath`, this bundle's hook helper, is
    /// judged by `HookPathGuard` exactly as the hooks are, because macOS would otherwise start a
    /// path that is gone by the next login.
    public static func setUpOnce(helperPath: String) {
        guard !LoginItemPreference.isSetUp(), HookPathGuard.problem(with: helperPath) == nil else { return }

        if isEnabled || (try? set(true)) != nil {
            LoginItemPreference.trySetSetUp()
        }
    }

    public static func openSystemSettings() {
        SMAppService.openSystemSettingsLoginItems()
    }
}
