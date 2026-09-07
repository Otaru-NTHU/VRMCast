import Foundation
import CoreMedia
import CoreMediaIO
import CoreVideo

/// Pushes host-generated 1920×1080 BGRA frames into the extension's sink stream at 30 fps. This is the
/// exact path the Unity plugin will use later: IOSurface-backed CVPixelBuffer → CMSampleBuffer →
/// CMSimpleQueue obtained with CMIOStreamCopyBufferQueue.
final class SinkFrameSender {
    var onStatus: ((String) -> Void)?
    var onLog: ((String) -> Void)?

    private(set) var framesSent: UInt64 = 0
    private(set) var enqueueFailures: UInt64 = 0

    private let queue = DispatchQueue(label: "VRMCast.CameraSpike.sender", qos: .userInteractive)
    private var timer: DispatchSourceTimer?
    private var deviceID: CMIODeviceID = 0
    private var streamID: CMIOStreamID = 0
    private var bufferQueue: CMSimpleQueue?
    private var bufferPool: CVPixelBufferPool?
    private var formatDescription: CMFormatDescription?
    private var frameIndex = 0
    private var consecutiveFailures = 0
    private let painter = TestPatternPainter(width: Int(VirtualCameraConstants.width),
                                             height: Int(VirtualCameraConstants.height))

    var isRunning: Bool { timer != nil }

    func start() {
        queue.async { self.startOnQueue() }
    }

    func stop() {
        queue.async { self.stopOnQueue(reason: "stopped by user") }
    }

    private func startOnQueue() {
        guard timer == nil else { return }

        guard let device = CMIODeviceQuery.findDevice(named: VirtualCameraConstants.deviceName) else {
            let names = CMIODeviceQuery.allDevices().compactMap { CMIODeviceQuery.name(of: $0) }
            onStatus?("Device '\(VirtualCameraConstants.deviceName)' not found. Is the extension installed and approved?")
            onLog?("CMIO devices visible: \(names.isEmpty ? "(none)" : names.joined(separator: ", "))")
            return
        }
        guard let stream = CMIODeviceQuery.findSinkStream(of: device) else {
            onStatus?("Device found but it has no sink stream (old extension version still loaded?).")
            return
        }
        deviceID = device
        streamID = stream
        onLog?("Found device \(device) uid=\(CMIODeviceQuery.uid(of: device) ?? "?") sink stream \(stream)")

        var unmanagedQueue: Unmanaged<CMSimpleQueue>?
        let copyStatus = CMIOStreamCopyBufferQueue(stream, { _, _, _ in }, nil, &unmanagedQueue)
        guard copyStatus == noErr, let q = unmanagedQueue?.takeRetainedValue() else {
            onStatus?("CMIOStreamCopyBufferQueue failed (\(copyStatus)).")
            return
        }
        bufferQueue = q
        onLog?("Sink queue capacity: \(CMSimpleQueueGetCapacity(q))")

        let width = VirtualCameraConstants.width
        let height = VirtualCameraConstants.height
        var description: CMFormatDescription?
        CMVideoFormatDescriptionCreate(allocator: kCFAllocatorDefault,
                                       codecType: VirtualCameraConstants.pixelFormat,
                                       width: width, height: height,
                                       extensions: nil,
                                       formatDescriptionOut: &description)
        formatDescription = description

        let attributes: NSDictionary = [
            kCVPixelBufferWidthKey: width,
            kCVPixelBufferHeightKey: height,
            kCVPixelBufferPixelFormatTypeKey: VirtualCameraConstants.pixelFormat,
            kCVPixelBufferIOSurfacePropertiesKey: [:],
        ]
        var pool: CVPixelBufferPool?
        CVPixelBufferPoolCreate(kCFAllocatorDefault, nil, attributes, &pool)
        bufferPool = pool

        let startStatus = CMIODeviceStartStream(device, stream)
        guard startStatus == noErr else {
            onStatus?("CMIODeviceStartStream failed (\(startStatus)).")
            bufferQueue = nil
            return
        }

        framesSent = 0
        enqueueFailures = 0
        consecutiveFailures = 0
        frameIndex = 0

        let t = DispatchSource.makeTimerSource(flags: .strict, queue: queue)
        t.schedule(deadline: .now(), repeating: 1.0 / Double(VirtualCameraConstants.frameRate), leeway: .milliseconds(1))
        t.setEventHandler { [weak self] in self?.sendFrame() }
        timer = t
        t.resume()
        onStatus?("Sending host frames at \(VirtualCameraConstants.frameRate) fps.")
    }

    private func stopOnQueue(reason: String) {
        guard let t = timer else { return }
        t.cancel()
        timer = nil
        _ = CMIODeviceStopStream(deviceID, streamID)
        bufferQueue = nil
        bufferPool = nil
        onStatus?("Idle (\(reason)). The extension now shows its red fallback pattern.")
        onLog?("Sender stopped: \(reason); frames sent \(framesSent), enqueue failures \(enqueueFailures)")
    }

    private func sendFrame() {
        guard let q = bufferQueue, let pool = bufferPool, let description = formatDescription else { return }

        // The extension drains the queue at 30 fps; if it is full the extension has stalled or OBS is not
        // pulling. Drop instead of blocking so the host never stalls on the camera (PRD 20.2 bounded latency).
        if CMSimpleQueueGetCount(q) >= CMSimpleQueueGetCapacity(q) {
            recordFailure("queue full")
            return
        }

        var pixelBuffer: CVPixelBuffer?
        guard CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &pixelBuffer) == kCVReturnSuccess,
              let buffer = pixelBuffer else {
            recordFailure("pixel buffer pool exhausted")
            return
        }
        painter.paint(into: buffer, frameIndex: frameIndex, style: .hostFrame)
        frameIndex += 1

        let now = CMClockGetTime(CMClockGetHostTimeClock())
        var timing = CMSampleTimingInfo(duration: VirtualCameraConstants.frameDuration,
                                        presentationTimeStamp: now,
                                        decodeTimeStamp: .invalid)
        var sampleBuffer: CMSampleBuffer?
        let status = CMSampleBufferCreateForImageBuffer(allocator: kCFAllocatorDefault,
                                                        imageBuffer: buffer,
                                                        dataReady: true,
                                                        makeDataReadyCallback: nil,
                                                        refcon: nil,
                                                        formatDescription: description,
                                                        sampleTiming: &timing,
                                                        sampleBufferOut: &sampleBuffer)
        guard status == noErr, let sbuf = sampleBuffer else {
            recordFailure("CMSampleBufferCreateForImageBuffer \(status)")
            return
        }

        // The queue takes ownership of one retain; release it ourselves if the enqueue is refused.
        let element = Unmanaged.passRetained(sbuf).toOpaque()
        let enqueueStatus = CMSimpleQueueEnqueue(q, element: element)
        if enqueueStatus == noErr {
            framesSent += 1
            consecutiveFailures = 0
        } else {
            Unmanaged<CMSampleBuffer>.fromOpaque(element).release()
            recordFailure("CMSimpleQueueEnqueue \(enqueueStatus)")
        }
    }

    private func recordFailure(_ what: String) {
        enqueueFailures += 1
        consecutiveFailures += 1
        if consecutiveFailures == 1 || consecutiveFailures % 30 == 0 {
            onLog?("Frame dropped: \(what) (consecutive: \(consecutiveFailures))")
        }
        // Ten seconds without a single accepted frame means the extension is gone; stop cleanly.
        if consecutiveFailures >= Int(VirtualCameraConstants.frameRate) * 10 {
            stopOnQueue(reason: "extension stopped accepting frames")
        }
    }
}
