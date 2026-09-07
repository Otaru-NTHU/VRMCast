import Foundation
import Combine

/// Glue between the UI, the extension activator and the sink frame sender. Everything user-visible is
/// a plain string so the tester can copy it into a bug report verbatim.
@MainActor
final class SpikeModel: ObservableObject {
    @Published var extensionStatus = "Unknown — press Check Status"
    @Published var senderStatus = "Idle"
    @Published var framesSent: UInt64 = 0
    @Published var enqueueFailures: UInt64 = 0
    @Published var logLines: [String] = []

    let activator = ExtensionActivator()
    let sender = SinkFrameSender()

    private var cancellables = Set<AnyCancellable>()
    private var statsTimer: Timer?

    var extensionBundleID: String {
        (Bundle.main.bundleIdentifier ?? "unknown") + VirtualCameraConstants.extensionBundleIDSuffix
    }

    init() {
        activator.onStatus = { [weak self] text in
            Task { @MainActor in self?.extensionStatus = text }
        }
        activator.onLog = { [weak self] line in
            Task { @MainActor in self?.append(line) }
        }
        sender.onStatus = { [weak self] text in
            Task { @MainActor in self?.senderStatus = text }
        }
        sender.onLog = { [weak self] line in
            Task { @MainActor in self?.append(line) }
        }
        statsTimer = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                self.framesSent = self.sender.framesSent
                self.enqueueFailures = self.sender.enqueueFailures
            }
        }
        append("Host bundle: \(Bundle.main.bundleIdentifier ?? "?") — extension: \(extensionBundleID)")
        append("Run from /Applications, or enable `systemextensionsctl developer on` (see Docs/VirtualCamera.md).")
    }

    func checkStatus() { activator.queryProperties(extensionBundleID: extensionBundleID) }
    func activate() { activator.activate(extensionBundleID: extensionBundleID) }
    func deactivate() {
        stopSending()
        activator.deactivate(extensionBundleID: extensionBundleID)
    }
    func startSending() { sender.start() }
    func stopSending() { sender.stop() }

    func append(_ line: String) {
        let stamp = ISO8601DateFormatter().string(from: Date())
        logLines.append("[\(stamp)] \(line)")
        if logLines.count > 400 { logLines.removeFirst(logLines.count - 400) }
    }
}
