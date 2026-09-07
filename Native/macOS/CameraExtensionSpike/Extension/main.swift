import Foundation
import CoreMediaIO

// Entry point of the Camera Extension process. macOS launches it on demand (first client that opens
// the device, or the host connecting to the sink) and keeps it alive while the extension is enabled.
let providerSource = CameraExtensionProviderSource(clientQueue: nil)
CMIOExtensionProvider.startService(provider: providerSource.provider)
CFRunLoopRun()
