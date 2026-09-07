// VRMCast frame bridge: Unity native plugin (macOS) that pushes BGRA frames into the sink stream of the
// "VRM Live Camera" Core Media I/O extension and drives the extension's activation from the host app.
//
// Build: Scripts/build-frame-bridge.sh  ->  Assets/Plugins/macOS/VRMCastFrameBridge.bundle
// Every entry point is plain C so Unity can DllImport it; nothing here depends on Unity headers.

#import <CoreMedia/CoreMedia.h>
#import <CoreMediaIO/CMIOHardware.h>
#import <CoreVideo/CoreVideo.h>
#import <Foundation/Foundation.h>
#import <SystemExtensions/SystemExtensions.h>

#include <atomic>
#include <cstring>
#include <mutex>
#include <string>
#include <vector>

// ---------------------------------------------------------------------------------------------------------- errors

extern "C" {
enum VRMCastVcamError {
    VRMCAST_VCAM_OK = 0,
    VRMCAST_VCAM_ERR_DEVICE_NOT_FOUND = 1,
    VRMCAST_VCAM_ERR_NO_SINK_STREAM = 2,
    VRMCAST_VCAM_ERR_QUEUE = 3,
    VRMCAST_VCAM_ERR_START_STREAM = 4,
    VRMCAST_VCAM_ERR_NOT_OPEN = 5,
    VRMCAST_VCAM_ERR_QUEUE_FULL = 6,
    VRMCAST_VCAM_ERR_PIXEL_BUFFER = 7,
    VRMCAST_VCAM_ERR_SAMPLE_BUFFER = 8,
    VRMCAST_VCAM_ERR_ENQUEUE = 9,
    VRMCAST_VCAM_ERR_BAD_ARGUMENT = 10,
};

enum VRMCastExtensionState {
    VRMCAST_EXT_UNKNOWN = 0,
    VRMCAST_EXT_NOT_INSTALLED = 1,
    VRMCAST_EXT_REQUESTING = 2,
    VRMCAST_EXT_AWAITING_APPROVAL = 3,
    VRMCAST_EXT_ENABLED = 4,
    VRMCAST_EXT_NEEDS_REBOOT = 5,
    VRMCAST_EXT_FAILED = 6,
    VRMCAST_EXT_UNINSTALLING = 7,
};
}

// ------------------------------------------------------------------------------------------------ CMIO utilities

namespace {

CMIOObjectPropertyAddress Address(CMIOObjectPropertySelector selector) {
    return CMIOObjectPropertyAddress{selector, kCMIOObjectPropertyScopeGlobal, kCMIOObjectPropertyElementMain};
}

template <typename T>
bool ReadArray(CMIOObjectID object, CMIOObjectPropertySelector selector, std::vector<T>& out) {
    CMIOObjectPropertyAddress address = Address(selector);
    UInt32 size = 0;
    if (CMIOObjectGetPropertyDataSize(object, &address, 0, nullptr, &size) != noErr || size == 0) return false;
    out.resize(size / sizeof(T));
    UInt32 used = 0;
    if (CMIOObjectGetPropertyData(object, &address, 0, nullptr, size, &used, out.data()) != noErr) return false;
    out.resize(used / sizeof(T));
    return true;
}

std::string ReadString(CMIOObjectID object, CMIOObjectPropertySelector selector) {
    CMIOObjectPropertyAddress address = Address(selector);
    CFStringRef value = nullptr;
    UInt32 used = 0;
    if (CMIOObjectGetPropertyData(object, &address, 0, nullptr, sizeof(value), &used, &value) != noErr || !value) return "";
    std::string result;
    char buffer[512];
    if (CFStringGetCString(value, buffer, sizeof(buffer), kCFStringEncodingUTF8)) result = buffer;
    CFRelease(value);
    return result;
}

CMIODeviceID FindDevice(const char* name) {
    std::vector<CMIODeviceID> devices;
    if (!ReadArray(kCMIOObjectSystemObject, kCMIOHardwarePropertyDevices, devices)) return 0;
    for (CMIODeviceID id : devices) {
        if (ReadString(id, kCMIOObjectPropertyName) == name) return id;
    }
    return 0;
}

CMIOStreamID FindSinkStream(CMIODeviceID device, const char* sinkName) {
    std::vector<CMIOStreamID> streams;
    if (!ReadArray(device, kCMIODevicePropertyStreams, streams)) return 0;
    for (CMIOStreamID id : streams) {
        if (ReadString(id, kCMIOObjectPropertyName) == sinkName) return id;
    }
    for (CMIOStreamID id : streams) {
        CMIOObjectPropertyAddress address = Address(kCMIOStreamPropertyDirection);
        UInt32 direction = 0, used = 0;
        if (CMIOObjectGetPropertyData(id, &address, 0, nullptr, sizeof(direction), &used, &direction) == noErr && direction == 1) return id;
    }
    return 0;
}

struct Bridge {
    std::mutex mutex;
    CMIODeviceID device = 0;
    CMIOStreamID stream = 0;
    CMSimpleQueueRef queue = nullptr;
    CVPixelBufferPoolRef pool = nullptr;
    CMFormatDescriptionRef format = nullptr;
    int width = 0, height = 0;
    std::atomic<long> sent{0};
    std::atomic<long> dropped{0};
    std::string lastError;

    void ReleasePool() {
        if (pool) { CVPixelBufferPoolRelease(pool); pool = nullptr; }
        if (format) { CFRelease(format); format = nullptr; }
        width = height = 0;
    }

    bool EnsurePool(int w, int h) {
        if (pool && width == w && height == h) return true;
        ReleasePool();
        NSDictionary* attributes = @{
            (id)kCVPixelBufferWidthKey : @(w),
            (id)kCVPixelBufferHeightKey : @(h),
            (id)kCVPixelBufferPixelFormatTypeKey : @(kCVPixelFormatType_32BGRA),
            (id)kCVPixelBufferIOSurfacePropertiesKey : @{},
        };
        if (CVPixelBufferPoolCreate(kCFAllocatorDefault, nullptr, (__bridge CFDictionaryRef)attributes, &pool) != kCVReturnSuccess) return false;
        if (CMVideoFormatDescriptionCreate(kCFAllocatorDefault, kCVPixelFormatType_32BGRA, w, h, nullptr, &format) != noErr) {
            ReleasePool();
            return false;
        }
        width = w;
        height = h;
        return true;
    }

    void Close() {
        if (device && stream) CMIODeviceStopStream(device, stream);
        if (queue) { CFRelease(queue); queue = nullptr; }
        ReleasePool();
        device = 0;
        stream = 0;
    }
};

Bridge& G() {
    static Bridge bridge;
    return bridge;
}

}  // namespace

// --------------------------------------------------------------------------------------------- frame transport

extern "C" {

/// Looks up the device and its sink stream, copies the buffer queue and starts the sink. Safe to call again.
int vrmcast_vcam_open(const char* deviceName, const char* sinkStreamName) {
    if (!deviceName || !sinkStreamName) return VRMCAST_VCAM_ERR_BAD_ARGUMENT;
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    b.Close();

    CMIODeviceID device = FindDevice(deviceName);
    if (!device) { b.lastError = "device not found"; return VRMCAST_VCAM_ERR_DEVICE_NOT_FOUND; }
    CMIOStreamID stream = FindSinkStream(device, sinkStreamName);
    if (!stream) { b.lastError = "sink stream not found"; return VRMCAST_VCAM_ERR_NO_SINK_STREAM; }

    CMSimpleQueueRef queue = nullptr;
    OSStatus status = CMIOStreamCopyBufferQueue(stream, [](CMIOStreamID, void*, void*) {}, nullptr, &queue);
    if (status != noErr || !queue) { b.lastError = "CMIOStreamCopyBufferQueue " + std::to_string(status); return VRMCAST_VCAM_ERR_QUEUE; }

    status = CMIODeviceStartStream(device, stream);
    if (status != noErr) {
        CFRelease(queue);
        b.lastError = "CMIODeviceStartStream " + std::to_string(status);
        return VRMCAST_VCAM_ERR_START_STREAM;
    }
    b.device = device;
    b.stream = stream;
    b.queue = queue;
    b.sent = 0;
    b.dropped = 0;
    b.lastError.clear();
    return VRMCAST_VCAM_OK;
}

void vrmcast_vcam_close(void) {
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    b.Close();
}

int vrmcast_vcam_is_open(void) {
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    return b.queue != nullptr ? 1 : 0;
}

/// Sends one BGRA frame. `flipVertically` != 0 reverses the row order (Unity readbacks are bottom-up).
/// `timestampSeconds` <= 0 uses the host clock.
int vrmcast_vcam_send(const void* bgra, int width, int height, int bytesPerRow, int flipVertically, double timestampSeconds) {
    if (!bgra || width <= 0 || height <= 0 || bytesPerRow < width * 4) return VRMCAST_VCAM_ERR_BAD_ARGUMENT;
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    if (!b.queue) return VRMCAST_VCAM_ERR_NOT_OPEN;

    if (CMSimpleQueueGetCount(b.queue) >= CMSimpleQueueGetCapacity(b.queue)) {
        b.dropped++;
        return VRMCAST_VCAM_ERR_QUEUE_FULL;
    }
    if (!b.EnsurePool(width, height)) { b.lastError = "pixel buffer pool"; return VRMCAST_VCAM_ERR_PIXEL_BUFFER; }

    CVPixelBufferRef pixelBuffer = nullptr;
    if (CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, b.pool, &pixelBuffer) != kCVReturnSuccess || !pixelBuffer) {
        b.dropped++;
        return VRMCAST_VCAM_ERR_PIXEL_BUFFER;
    }
    CVPixelBufferLockBaseAddress(pixelBuffer, 0);
    uint8_t* dst = static_cast<uint8_t*>(CVPixelBufferGetBaseAddress(pixelBuffer));
    const size_t dstStride = CVPixelBufferGetBytesPerRow(pixelBuffer);
    const uint8_t* src = static_cast<const uint8_t*>(bgra);
    const size_t rowBytes = static_cast<size_t>(width) * 4;
    for (int y = 0; y < height; y++) {
        const int srcRow = flipVertically ? (height - 1 - y) : y;
        memcpy(dst + y * dstStride, src + static_cast<size_t>(srcRow) * bytesPerRow, rowBytes);
    }
    CVPixelBufferUnlockBaseAddress(pixelBuffer, 0);

    CMTime pts = timestampSeconds > 0 ? CMTimeMakeWithSeconds(timestampSeconds, 1000000000) : CMClockGetTime(CMClockGetHostTimeClock());
    CMSampleTimingInfo timing = {kCMTimeInvalid, pts, kCMTimeInvalid};
    CMSampleBufferRef sample = nullptr;
    OSStatus status = CMSampleBufferCreateForImageBuffer(kCFAllocatorDefault, pixelBuffer, true, nullptr, nullptr, b.format, &timing, &sample);
    CVPixelBufferRelease(pixelBuffer);
    if (status != noErr || !sample) { b.dropped++; return VRMCAST_VCAM_ERR_SAMPLE_BUFFER; }

    // The queue owns one retain of the sample buffer; the extension releases it after consumption.
    status = CMSimpleQueueEnqueue(b.queue, sample);
    if (status != noErr) {
        CFRelease(sample);
        b.dropped++;
        return VRMCAST_VCAM_ERR_ENQUEUE;
    }
    b.sent++;
    return VRMCAST_VCAM_OK;
}

long vrmcast_vcam_frames_sent(void) { return G().sent.load(); }
long vrmcast_vcam_frames_dropped(void) { return G().dropped.load(); }

int vrmcast_vcam_queue_count(void) {
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    return b.queue ? CMSimpleQueueGetCount(b.queue) : 0;
}

/// 1 when a CMIO device with this name exists (the extension is installed, approved and loaded).
int vrmcast_vcam_device_present(const char* deviceName) {
    return deviceName && FindDevice(deviceName) ? 1 : 0;
}

int vrmcast_vcam_last_error(char* buffer, int capacity) {
    if (!buffer || capacity <= 0) return 0;
    Bridge& b = G();
    std::lock_guard<std::mutex> lock(b.mutex);
    strncpy(buffer, b.lastError.c_str(), static_cast<size_t>(capacity) - 1);
    buffer[capacity - 1] = 0;
    return static_cast<int>(b.lastError.size());
}

}  // extern "C"

// ------------------------------------------------------------------------------------------ extension activation

@interface VRMCastExtensionRequester : NSObject <OSSystemExtensionRequestDelegate>
@property(atomic) int state;
@property(atomic, copy) NSString* message;
@property(atomic, copy) NSString* installedVersion;
@end

@implementation VRMCastExtensionRequester

- (OSSystemExtensionReplacementAction)request:(OSSystemExtensionRequest*)request
                  actionForReplacingExtension:(OSSystemExtensionProperties*)existing
                                withExtension:(OSSystemExtensionProperties*)ext {
    self.message = [NSString stringWithFormat:@"replacing %@ with %@", existing.bundleVersion, ext.bundleVersion];
    return OSSystemExtensionReplacementActionReplace;
}

- (void)requestNeedsUserApproval:(OSSystemExtensionRequest*)request {
    self.state = VRMCAST_EXT_AWAITING_APPROVAL;
    self.message = @"approval required in System Settings";
}

- (void)request:(OSSystemExtensionRequest*)request didFinishWithResult:(OSSystemExtensionRequestResult)result {
    if (result == OSSystemExtensionRequestWillCompleteAfterReboot) {
        self.state = VRMCAST_EXT_NEEDS_REBOOT;
        self.message = @"restart macOS to finish";
    } else {
        self.state = VRMCAST_EXT_ENABLED;
        self.message = @"";
    }
}

- (void)request:(OSSystemExtensionRequest*)request didFailWithError:(NSError*)error {
    self.state = VRMCAST_EXT_FAILED;
    self.message = [NSString stringWithFormat:@"%@ (%ld)", error.localizedDescription, (long)error.code];
}

- (void)request:(OSSystemExtensionRequest*)request foundProperties:(NSArray<OSSystemExtensionProperties*>*)properties {
    if (properties.count == 0) {
        self.state = VRMCAST_EXT_NOT_INSTALLED;
        self.message = @"";
        self.installedVersion = @"";
        return;
    }
    OSSystemExtensionProperties* p = properties.firstObject;
    self.installedVersion = p.bundleShortVersion ?: @"";
    if (p.isUninstalling) {
        self.state = VRMCAST_EXT_UNINSTALLING;
    } else if (p.isAwaitingUserApproval) {
        self.state = VRMCAST_EXT_AWAITING_APPROVAL;
        self.message = @"approval required in System Settings";
    } else if (p.isEnabled) {
        self.state = VRMCAST_EXT_ENABLED;
        self.message = @"";
    } else {
        self.state = VRMCAST_EXT_NOT_INSTALLED;
    }
}

@end

namespace {
VRMCastExtensionRequester* Requester() {
    static VRMCastExtensionRequester* requester = nil;
    static dispatch_once_t once;
    dispatch_once(&once, ^{
        requester = [VRMCastExtensionRequester new];
        requester.state = VRMCAST_EXT_UNKNOWN;
        requester.message = @"";
        requester.installedVersion = @"";
    });
    return requester;
}

void Submit(OSSystemExtensionRequest* request) {
    VRMCastExtensionRequester* r = Requester();
    request.delegate = r;
    r.state = VRMCAST_EXT_REQUESTING;
    r.message = @"";
    [[OSSystemExtensionManager sharedManager] submitRequest:request];
}
}  // namespace

extern "C" {

/// Asks macOS to install/enable the extension with this bundle identifier (must live inside this app bundle).
void vrmcast_ext_activate(const char* bundleIdentifier) {
    if (!bundleIdentifier) return;
    NSString* identifier = [NSString stringWithUTF8String:bundleIdentifier];
    Submit([OSSystemExtensionRequest activationRequestForExtension:identifier queue:dispatch_get_main_queue()]);
}

void vrmcast_ext_deactivate(const char* bundleIdentifier) {
    if (!bundleIdentifier) return;
    NSString* identifier = [NSString stringWithUTF8String:bundleIdentifier];
    Submit([OSSystemExtensionRequest deactivationRequestForExtension:identifier queue:dispatch_get_main_queue()]);
}

/// Refreshes the state asynchronously; poll vrmcast_ext_state afterwards.
void vrmcast_ext_query(const char* bundleIdentifier) {
    if (!bundleIdentifier) return;
    NSString* identifier = [NSString stringWithUTF8String:bundleIdentifier];
    if (@available(macOS 12.0, *)) {
        Submit([OSSystemExtensionRequest propertiesRequestForExtension:identifier queue:dispatch_get_main_queue()]);
    } else {
        Requester().state = VRMCAST_EXT_UNKNOWN;
    }
}

int vrmcast_ext_state(void) { return Requester().state; }

int vrmcast_ext_message(char* buffer, int capacity) {
    if (!buffer || capacity <= 0) return 0;
    NSString* message = Requester().message ?: @"";
    strncpy(buffer, message.UTF8String, static_cast<size_t>(capacity) - 1);
    buffer[capacity - 1] = 0;
    return static_cast<int>(strlen(buffer));
}

int vrmcast_ext_installed_version(char* buffer, int capacity) {
    if (!buffer || capacity <= 0) return 0;
    NSString* version = Requester().installedVersion ?: @"";
    strncpy(buffer, version.UTF8String, static_cast<size_t>(capacity) - 1);
    buffer[capacity - 1] = 0;
    return static_cast<int>(strlen(buffer));
}

/// Returns the app's own bundle identifier (the extension's identifier is this plus ".Camera").
int vrmcast_host_bundle_identifier(char* buffer, int capacity) {
    if (!buffer || capacity <= 0) return 0;
    NSString* identifier = [NSBundle mainBundle].bundleIdentifier ?: @"";
    strncpy(buffer, identifier.UTF8String, static_cast<size_t>(capacity) - 1);
    buffer[capacity - 1] = 0;
    return static_cast<int>(strlen(buffer));
}

}  // extern "C"
