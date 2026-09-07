import Foundation
import CoreMedia
import CoreVideo

/// Values shared by the host app and the Camera Extension. They are compiled into both targets so the
/// two sides can never disagree about the device name, frame geometry or stream identity.
enum VirtualCameraConstants {
    /// Name shown by OBS, FaceTime and every other AVFoundation client (PRD 20.1).
    static let deviceName = "VRM Live Camera"
    static let manufacturer = "VRMCast"
    static let deviceModel = "VRM Live Camera (software)"

    /// The stream capture apps read from.
    static let sourceStreamName = "VRM Live Camera Video"
    /// The stream the host app writes frames into. Capture apps ignore it.
    static let sinkStreamName = "VRM Live Camera Sink"

    static let width: Int32 = 1920
    static let height: Int32 = 1080
    static let frameRate: Int32 = 30
    static let frameDuration = CMTime(value: 1, timescale: frameRate)
    static let pixelFormat = kCVPixelFormatType_32BGRA

    /// Fixed identifiers so OBS keeps its device selection across host relaunches and extension
    /// updates (PRD 37.6: restarting OBS must not require reinstalling the extension).
    static let deviceID = UUID(uuidString: "D2C1F4B8-7A3E-4E5B-9C21-6E0F5A2B8D31")!
    static let sourceStreamID = UUID(uuidString: "E3D2A5C9-8B4F-4F6C-8D32-7F1A6B3C9E42")!
    static let sinkStreamID = UUID(uuidString: "F4E3B6DA-9C50-4A7D-9E43-802B7C4DAF53")!

    /// After this long without a host frame the extension shows its own fallback pattern instead of
    /// freezing on the last frame (PRD 43 item 10, PRD 37.6 "defined fallback frame").
    static let hostFrameTimeout: TimeInterval = 0.5

    /// The extension's bundle identifier is always the host's identifier plus this suffix.
    static let extensionBundleIDSuffix = ".Camera"
}
