import Foundation
import CoreMediaIO
import CoreVideo
import IOKit.audio
import os.log

private let log = OSLog(subsystem: "VRMCast.CameraExtension", category: "Provider")

// MARK: - Device

/// One virtual camera device with two streams:
///  - a *source* stream that capture apps (OBS, FaceTime, QuickTime) read at a fixed 30 fps;
///  - a *sink* stream that the host app writes BGRA frames into.
///
/// A timer drives the source stream at exactly `frameRate`. On every tick it sends the most recent host
/// frame if one arrived within `hostFrameTimeout`, otherwise a fallback pattern generated here. That keeps
/// the cadence deterministic regardless of host jitter and gives clients a defined frame when the host
/// stops (PRD 20.2 "deterministic frame pacing", PRD 37.6 "defined fallback frame").
final class CameraExtensionDeviceSource: NSObject, CMIOExtensionDeviceSource {
    private(set) var device: CMIOExtensionDevice!

    private var sourceStream: CameraExtensionSourceStream!
    private var sinkStream: CameraExtensionSinkStream!
    private var videoDescription: CMFormatDescription!
    private var bufferPool: CVPixelBufferPool!
    private let painter = TestPatternPainter(width: Int(VirtualCameraConstants.width),
                                             height: Int(VirtualCameraConstants.height))

    private let timerQueue = DispatchQueue(label: "VRMCast.CameraExtension.timer",
                                           qos: .userInteractive,
                                           target: .global(qos: .userInteractive))
    private var timer: DispatchSourceTimer?
    private var sourceClients: UInt32 = 0
    private var frameIndex = 0

    private let stateLock = NSLock()
    private var latestHostPixelBuffer: CVPixelBuffer?
    private var latestHostFrameAt = Date.distantPast
    private var sinkClient: CMIOExtensionClient?
    private var sinkStreaming = false
    private var hostFramesReceived: UInt64 = 0
    private var fallbackFramesSent: UInt64 = 0

    init(localizedName: String) {
        super.init()

        device = CMIOExtensionDevice(localizedName: localizedName,
                                     deviceID: VirtualCameraConstants.deviceID,
                                     legacyDeviceID: nil,
                                     source: self)

        let dims = CMVideoDimensions(width: VirtualCameraConstants.width, height: VirtualCameraConstants.height)
        CMVideoFormatDescriptionCreate(allocator: kCFAllocatorDefault,
                                       codecType: VirtualCameraConstants.pixelFormat,
                                       width: dims.width,
                                       height: dims.height,
                                       extensions: nil,
                                       formatDescriptionOut: &videoDescription)

        // IOSurface-backed buffers so frames cross the XPC boundary to clients without a copy.
        let pixelBufferAttributes: NSDictionary = [
            kCVPixelBufferWidthKey: dims.width,
            kCVPixelBufferHeightKey: dims.height,
            kCVPixelBufferPixelFormatTypeKey: VirtualCameraConstants.pixelFormat,
            kCVPixelBufferIOSurfacePropertiesKey: [:],
        ]
        CVPixelBufferPoolCreate(kCFAllocatorDefault, nil, pixelBufferAttributes, &bufferPool)

        let format = CMIOExtensionStreamFormat(formatDescription: videoDescription,
                                               maxFrameDuration: VirtualCameraConstants.frameDuration,
                                               minFrameDuration: VirtualCameraConstants.frameDuration,
                                               validFrameDurations: nil)

        sourceStream = CameraExtensionSourceStream(localizedName: VirtualCameraConstants.sourceStreamName,
                                                   streamID: VirtualCameraConstants.sourceStreamID,
                                                   streamFormat: format,
                                                   device: device)
        sinkStream = CameraExtensionSinkStream(localizedName: VirtualCameraConstants.sinkStreamName,
                                               streamID: VirtualCameraConstants.sinkStreamID,
                                               streamFormat: format,
                                               device: device)
        do {
            try device.addStream(sourceStream.stream)
            try device.addStream(sinkStream.stream)
        } catch {
            fatalError("Failed to add streams: \(error.localizedDescription)")
        }
    }

    // MARK: CMIOExtensionDeviceSource

    var availableProperties: Set<CMIOExtensionProperty> {
        [.deviceTransportType, .deviceModel]
    }

    func deviceProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionDeviceProperties {
        let deviceProperties = CMIOExtensionDeviceProperties(dictionary: [:])
        if properties.contains(.deviceTransportType) {
            deviceProperties.transportType = kIOAudioDeviceTransportTypeVirtual
        }
        if properties.contains(.deviceModel) {
            deviceProperties.model = VirtualCameraConstants.deviceModel
        }
        return deviceProperties
    }

    func setDeviceProperties(_ deviceProperties: CMIOExtensionDeviceProperties) throws {
        // Nothing is settable.
    }

    // MARK: Source stream lifecycle (called by clients such as OBS)

    func startSourceStreaming() {
        sourceClients += 1
        os_log(.info, log: log, "Source stream started (clients: %u)", sourceClients)
        guard timer == nil else { return }

        let t = DispatchSource.makeTimerSource(flags: .strict, queue: timerQueue)
        let interval = 1.0 / Double(VirtualCameraConstants.frameRate)
        t.schedule(deadline: .now(), repeating: interval, leeway: .milliseconds(1))
        t.setEventHandler { [weak self] in self?.tick() }
        timer = t
        t.resume()
    }

    func stopSourceStreaming() {
        if sourceClients > 0 { sourceClients -= 1 }
        os_log(.info, log: log, "Source stream stopped (clients: %u)", sourceClients)
        guard sourceClients == 0, let t = timer else { return }
        t.cancel()
        timer = nil
    }

    private func tick() {
        let now = CMClockGetTime(CMClockGetHostTimeClock())
        let hostNanos = UInt64(max(0, now.seconds) * Double(NSEC_PER_SEC))

        stateLock.lock()
        let fresh = Date().timeIntervalSince(latestHostFrameAt) <= VirtualCameraConstants.hostFrameTimeout
        let hostBuffer = fresh ? latestHostPixelBuffer : nil
        stateLock.unlock()

        let pixelBuffer: CVPixelBuffer
        if let hostBuffer = hostBuffer {
            pixelBuffer = hostBuffer
        } else {
            var created: CVPixelBuffer?
            let status = CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, bufferPool, &created)
            guard status == kCVReturnSuccess, let buffer = created else {
                os_log(.error, log: log, "Pixel buffer pool exhausted (%d)", status)
                return
            }
            painter.paint(into: buffer, frameIndex: frameIndex, style: .extensionFallback)
            pixelBuffer = buffer
            fallbackFramesSent += 1
        }
        frameIndex += 1

        var timing = CMSampleTimingInfo(duration: VirtualCameraConstants.frameDuration,
                                        presentationTimeStamp: now,
                                        decodeTimeStamp: .invalid)
        var sampleBuffer: CMSampleBuffer?
        let err = CMSampleBufferCreateForImageBuffer(allocator: kCFAllocatorDefault,
                                                     imageBuffer: pixelBuffer,
                                                     dataReady: true,
                                                     makeDataReadyCallback: nil,
                                                     refcon: nil,
                                                     formatDescription: formatDescription(for: pixelBuffer),
                                                     sampleTiming: &timing,
                                                     sampleBufferOut: &sampleBuffer)
        guard err == noErr, let sbuf = sampleBuffer else {
            os_log(.error, log: log, "CMSampleBufferCreateForImageBuffer failed: %d", err)
            return
        }
        sourceStream.stream.send(sbuf, discontinuity: [], hostTimeInNanoseconds: hostNanos)
    }

    private func formatDescription(for pixelBuffer: CVPixelBuffer) -> CMFormatDescription {
        if CVPixelBufferGetWidth(pixelBuffer) == Int(VirtualCameraConstants.width),
           CVPixelBufferGetHeight(pixelBuffer) == Int(VirtualCameraConstants.height),
           CVPixelBufferGetPixelFormatType(pixelBuffer) == VirtualCameraConstants.pixelFormat {
            return videoDescription
        }
        var description: CMFormatDescription?
        CMVideoFormatDescriptionCreateForImageBuffer(allocator: kCFAllocatorDefault,
                                                     imageBuffer: pixelBuffer,
                                                     formatDescriptionOut: &description)
        return description ?? videoDescription
    }

    // MARK: Sink stream lifecycle (called when the host app starts/stops sending)

    func startSinkStreaming(client: CMIOExtensionClient) {
        stateLock.lock()
        sinkClient = client
        sinkStreaming = true
        stateLock.unlock()
        os_log(.info, log: log, "Sink stream started by host")
        consumeNextHostBuffer()
    }

    func stopSinkStreaming() {
        stateLock.lock()
        sinkStreaming = false
        sinkClient = nil
        latestHostPixelBuffer = nil
        latestHostFrameAt = .distantPast
        stateLock.unlock()
        os_log(.info, log: log, "Sink stream stopped (host frames received: %llu, fallback frames: %llu)",
               hostFramesReceived, fallbackFramesSent)
    }

    private func consumeNextHostBuffer() {
        stateLock.lock()
        let streaming = sinkStreaming
        let client = sinkClient
        stateLock.unlock()
        guard streaming, let client = client else { return }

        sinkStream.stream.consumeSampleBuffer(from: client) { [weak self] sbuf, sequenceNumber, _, _, error in
            guard let self = self else { return }
            if let sbuf = sbuf, let pixelBuffer = CMSampleBufferGetImageBuffer(sbuf) {
                self.stateLock.lock()
                self.latestHostPixelBuffer = pixelBuffer
                self.latestHostFrameAt = Date()
                self.hostFramesReceived += 1
                self.stateLock.unlock()

                // Tell the host which buffer was consumed and when; the host can use this to pace itself.
                let now = CMClockGetTime(CMClockGetHostTimeClock())
                let output = CMIOExtensionScheduledOutput(sequenceNumber: sequenceNumber,
                                                          hostTimeInNanoseconds: UInt64(max(0, now.seconds) * Double(NSEC_PER_SEC)))
                self.sinkStream.stream.notifyScheduledOutputChanged(output)
                self.consumeNextHostBuffer()
            } else {
                if let error = error {
                    os_log(.error, log: log, "consumeSampleBuffer failed: %{public}@", error.localizedDescription)
                }
                // Back off briefly so a persistent error cannot spin the process.
                self.timerQueue.asyncAfter(deadline: .now() + .milliseconds(20)) { self.consumeNextHostBuffer() }
            }
        }
    }
}

// MARK: - Source stream

final class CameraExtensionSourceStream: NSObject, CMIOExtensionStreamSource {
    private(set) var stream: CMIOExtensionStream!
    let device: CMIOExtensionDevice
    private let streamFormat: CMIOExtensionStreamFormat

    init(localizedName: String, streamID: UUID, streamFormat: CMIOExtensionStreamFormat, device: CMIOExtensionDevice) {
        self.device = device
        self.streamFormat = streamFormat
        super.init()
        stream = CMIOExtensionStream(localizedName: localizedName,
                                     streamID: streamID,
                                     direction: .source,
                                     clockType: .hostTime,
                                     source: self)
    }

    var formats: [CMIOExtensionStreamFormat] { [streamFormat] }

    var activeFormatIndex: Int = 0 {
        didSet {
            if activeFormatIndex >= 1 {
                os_log(.error, log: log, "Invalid source format index %d", activeFormatIndex)
                activeFormatIndex = 0
            }
        }
    }

    var availableProperties: Set<CMIOExtensionProperty> {
        [.streamActiveFormatIndex, .streamFrameDuration]
    }

    func streamProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionStreamProperties {
        let streamProperties = CMIOExtensionStreamProperties(dictionary: [:])
        if properties.contains(.streamActiveFormatIndex) {
            streamProperties.activeFormatIndex = 0
        }
        if properties.contains(.streamFrameDuration) {
            streamProperties.frameDuration = VirtualCameraConstants.frameDuration
        }
        return streamProperties
    }

    func setStreamProperties(_ streamProperties: CMIOExtensionStreamProperties) throws {
        if let index = streamProperties.activeFormatIndex {
            activeFormatIndex = index
        }
    }

    func authorizedToStartStream(for client: CMIOExtensionClient) -> Bool {
        // Every client may read the camera; TCC already gated camera access on the client side.
        true
    }

    func startStream() throws {
        guard let deviceSource = device.source as? CameraExtensionDeviceSource else {
            fatalError("Unexpected device source")
        }
        deviceSource.startSourceStreaming()
    }

    func stopStream() throws {
        guard let deviceSource = device.source as? CameraExtensionDeviceSource else {
            fatalError("Unexpected device source")
        }
        deviceSource.stopSourceStreaming()
    }
}

// MARK: - Sink stream

final class CameraExtensionSinkStream: NSObject, CMIOExtensionStreamSource {
    private(set) var stream: CMIOExtensionStream!
    let device: CMIOExtensionDevice
    private let streamFormat: CMIOExtensionStreamFormat
    private var client: CMIOExtensionClient?

    init(localizedName: String, streamID: UUID, streamFormat: CMIOExtensionStreamFormat, device: CMIOExtensionDevice) {
        self.device = device
        self.streamFormat = streamFormat
        super.init()
        stream = CMIOExtensionStream(localizedName: localizedName,
                                     streamID: streamID,
                                     direction: .sink,
                                     clockType: .hostTime,
                                     source: self)
    }

    var formats: [CMIOExtensionStreamFormat] { [streamFormat] }

    var activeFormatIndex: Int = 0 {
        didSet {
            if activeFormatIndex >= 1 {
                os_log(.error, log: log, "Invalid sink format index %d", activeFormatIndex)
                activeFormatIndex = 0
            }
        }
    }

    var availableProperties: Set<CMIOExtensionProperty> {
        [.streamActiveFormatIndex, .streamFrameDuration, .streamSinkBufferQueueSize,
         .streamSinkBuffersRequiredForStartup, .streamSinkBufferUnderrunCount, .streamSinkEndOfData]
    }

    func streamProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionStreamProperties {
        let streamProperties = CMIOExtensionStreamProperties(dictionary: [:])
        if properties.contains(.streamActiveFormatIndex) {
            streamProperties.activeFormatIndex = 0
        }
        if properties.contains(.streamFrameDuration) {
            streamProperties.frameDuration = VirtualCameraConstants.frameDuration
        }
        if properties.contains(.streamSinkBufferQueueSize) {
            // Small queue: the host should never be more than a few frames ahead of the camera clock.
            streamProperties.sinkBufferQueueSize = 4
        }
        if properties.contains(.streamSinkBuffersRequiredForStartup) {
            streamProperties.sinkBuffersRequiredForStartup = 1
        }
        if properties.contains(.streamSinkBufferUnderrunCount) {
            streamProperties.sinkBufferUnderrunCount = 0
        }
        if properties.contains(.streamSinkEndOfData) {
            streamProperties.sinkEndOfData = 0
        }
        return streamProperties
    }

    func setStreamProperties(_ streamProperties: CMIOExtensionStreamProperties) throws {
        if let index = streamProperties.activeFormatIndex {
            activeFormatIndex = index
        }
    }

    func authorizedToStartStream(for client: CMIOExtensionClient) -> Bool {
        // Only one writer at a time. A stricter build could check client.signingID against the host.
        self.client = client
        return true
    }

    func startStream() throws {
        guard let deviceSource = device.source as? CameraExtensionDeviceSource, let client = client else {
            fatalError("Sink started without a client")
        }
        deviceSource.startSinkStreaming(client: client)
    }

    func stopStream() throws {
        guard let deviceSource = device.source as? CameraExtensionDeviceSource else {
            fatalError("Unexpected device source")
        }
        deviceSource.stopSinkStreaming()
        client = nil
    }
}

// MARK: - Provider

final class CameraExtensionProviderSource: NSObject, CMIOExtensionProviderSource {
    private(set) var provider: CMIOExtensionProvider!
    private var deviceSource: CameraExtensionDeviceSource!

    init(clientQueue: DispatchQueue?) {
        super.init()
        provider = CMIOExtensionProvider(source: self, clientQueue: clientQueue)
        deviceSource = CameraExtensionDeviceSource(localizedName: VirtualCameraConstants.deviceName)
        do {
            try provider.addDevice(deviceSource.device)
        } catch {
            fatalError("Failed to add device: \(error.localizedDescription)")
        }
        os_log(.info, log: log, "Provider started, device %{public}@ published", VirtualCameraConstants.deviceName)
    }

    func connect(to client: CMIOExtensionClient) throws {
        os_log(.info, log: log, "Client connected: %{public}@", client.signingID ?? "unknown")
    }

    func disconnect(from client: CMIOExtensionClient) {
        os_log(.info, log: log, "Client disconnected: %{public}@", client.signingID ?? "unknown")
    }

    var availableProperties: Set<CMIOExtensionProperty> {
        [.providerManufacturer]
    }

    func providerProperties(forProperties properties: Set<CMIOExtensionProperty>) throws -> CMIOExtensionProviderProperties {
        let providerProperties = CMIOExtensionProviderProperties(dictionary: [:])
        if properties.contains(.providerManufacturer) {
            providerProperties.manufacturer = VirtualCameraConstants.manufacturer
        }
        return providerProperties
    }

    func setProviderProperties(_ providerProperties: CMIOExtensionProviderProperties) throws {
        // Nothing is settable.
    }
}
