import SwiftUI

@main
struct VRMCastCameraSpikeApp: App {
    @StateObject private var model = SpikeModel()

    var body: some Scene {
        WindowGroup("VRMCast Camera Extension Spike") {
            ContentView()
                .environmentObject(model)
                .frame(minWidth: 620, minHeight: 520)
        }
    }
}
