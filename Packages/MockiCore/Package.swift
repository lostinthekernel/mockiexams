// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "MockiCore",
    platforms: [.macOS(.v14)],
    products: [
        .library(name: "MockiCore", targets: ["MockiCore"]),
        .executable(name: "mockiexam", targets: ["mockiexam"]),
    ],
    targets: [
        .target(name: "MockiCore", resources: [.copy("Resources/mockiexam.schema.json")]),
        .executableTarget(name: "mockiexam", dependencies: ["MockiCore"]),
        .testTarget(name: "MockiCoreTests", dependencies: ["MockiCore"]),
    ]
)
