import Foundation
import CoreMediaIO

/// Thin wrappers over the CoreMediaIO C property API used to find the virtual camera and its sink
/// stream from the host process. The same lookup will be ported to the Objective-C++ Unity plugin.
enum CMIODeviceQuery {
    private static func address(_ selector: Int) -> CMIOObjectPropertyAddress {
        CMIOObjectPropertyAddress(mSelector: CMIOObjectPropertySelector(selector),
                                  mScope: CMIOObjectPropertyScope(kCMIOObjectPropertyScopeGlobal),
                                  mElement: CMIOObjectPropertyElement(kCMIOObjectPropertyElementMain))
    }

    private static func readArray<T: BinaryInteger>(_ object: CMIOObjectID, _ selector: Int) -> [T] {
        var addr = address(selector)
        var dataSize: UInt32 = 0
        guard CMIOObjectGetPropertyDataSize(object, &addr, 0, nil, &dataSize) == noErr, dataSize > 0 else { return [] }
        let count = Int(dataSize) / MemoryLayout<T>.size
        var result = [T](repeating: T(0), count: count)
        var dataUsed: UInt32 = 0
        let status = result.withUnsafeMutableBytes { raw in
            CMIOObjectGetPropertyData(object, &addr, 0, nil, dataSize, &dataUsed, raw.baseAddress!)
        }
        guard status == noErr else { return [] }
        return Array(result.prefix(Int(dataUsed) / MemoryLayout<T>.size))
    }

    private static func readString(_ object: CMIOObjectID, _ selector: Int) -> String? {
        var addr = address(selector)
        var value: Unmanaged<CFString>?
        let dataSize = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        var dataUsed: UInt32 = 0
        let status = withUnsafeMutablePointer(to: &value) { ptr in
            CMIOObjectGetPropertyData(object, &addr, 0, nil, dataSize, &dataUsed, ptr)
        }
        guard status == noErr, let cf = value else { return nil }
        return cf.takeRetainedValue() as String
    }

    static func allDevices() -> [CMIODeviceID] {
        readArray(CMIOObjectID(kCMIOObjectSystemObject), kCMIOHardwarePropertyDevices)
    }

    static func name(of object: CMIOObjectID) -> String? {
        readString(object, kCMIOObjectPropertyName)
    }

    static func uid(of device: CMIODeviceID) -> String? {
        readString(device, kCMIODevicePropertyDeviceUID)
    }

    static func streams(of device: CMIODeviceID) -> [CMIOStreamID] {
        readArray(device, kCMIODevicePropertyStreams)
    }

    /// 0 = output stream (device → host, i.e. what capture apps read), 1 = input stream (host → device).
    static func direction(of stream: CMIOStreamID) -> UInt32? {
        let values: [UInt32] = readArray(stream, kCMIOStreamPropertyDirection)
        return values.first
    }

    static func findDevice(named name: String) -> CMIODeviceID? {
        allDevices().first { self.name(of: $0) == name }
    }

    /// The sink stream is identified by name first and by direction as a fallback.
    static func findSinkStream(of device: CMIODeviceID) -> CMIOStreamID? {
        let streams = streams(of: device)
        if let byName = streams.first(where: { name(of: $0) == VirtualCameraConstants.sinkStreamName }) {
            return byName
        }
        return streams.first { direction(of: $0) == 1 }
    }
}
