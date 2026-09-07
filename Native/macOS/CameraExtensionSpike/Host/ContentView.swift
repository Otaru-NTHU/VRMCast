import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var model: SpikeModel

    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("VRM Live Camera — Camera Extension spike")
                .font(.title2).bold()

            GroupBox("1. Extension") {
                VStack(alignment: .leading, spacing: 8) {
                    Text(model.extensionStatus)
                        .textSelection(.enabled)
                    HStack {
                        Button("Check Status") { model.checkStatus() }
                        Button("Install / Enable") { model.activate() }
                        Button("Uninstall") { model.deactivate() }
                    }
                    Text("After Install, macOS asks for approval in System Settings › General › Login Items & Extensions › Camera Extensions (macOS 15+) or System Settings › Privacy & Security (macOS 13–14).")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            GroupBox("2. Host frames through the sink stream") {
                VStack(alignment: .leading, spacing: 8) {
                    Text(model.senderStatus).textSelection(.enabled)
                    HStack {
                        Button("Start Sending 1080p30") { model.startSending() }
                        Button("Stop Sending") { model.stopSending() }
                        Spacer()
                        Text("sent \(model.framesSent) · enqueue failures \(model.enqueueFailures)")
                            .font(.system(.body, design: .monospaced))
                    }
                    Text("Blue lower band in OBS = frames from this app. Red lower band = the extension's own fallback pattern (host stopped or never started).")
                        .font(.footnote).foregroundStyle(.secondary)
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }

            GroupBox("Log") {
                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(alignment: .leading, spacing: 2) {
                            ForEach(Array(model.logLines.enumerated()), id: \.offset) { index, line in
                                Text(line)
                                    .font(.system(.caption, design: .monospaced))
                                    .textSelection(.enabled)
                                    .id(index)
                            }
                        }
                        .frame(maxWidth: .infinity, alignment: .leading)
                    }
                    .onChange(of: model.logLines.count) { count in
                        if count > 0 { proxy.scrollTo(count - 1, anchor: .bottom) }
                    }
                }
                .frame(minHeight: 160)
            }
        }
        .padding(16)
        .onAppear { model.checkStatus() }
    }
}
