// swift-tools-version:6.0
import PackageDescription

// The macOS port. See docs/macos-port.md for what it is and what it deliberately leaves out.
let package = Package(
    name: "BorisCodeStatus",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "BorisCodeStatusCore", targets: ["BorisCodeStatusCore"]),
        .executable(name: "BorisCodeStatusHooks", targets: ["BorisCodeStatusHooks"]),
    ],
    targets: [
        .target(name: "BorisCodeStatusCore"),
        .executableTarget(
            name: "BorisCodeStatusHooks",
            dependencies: ["BorisCodeStatusCore"]
        ),
        .testTarget(
            name: "BorisCodeStatusCoreTests",
            // The shared fixtures live in the repo-root fixtures/ folder so the C# tests read the
            // same files. SwiftPM cannot bundle resources from outside a target, so the tests find
            // them relative to their own source file instead.
            dependencies: ["BorisCodeStatusCore"]
        ),
    ]
)
