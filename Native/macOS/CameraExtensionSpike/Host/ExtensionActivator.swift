import Foundation
import SystemExtensions

/// Drives OSSystemExtensionRequest for the camera extension: install/replace, uninstall and a properties
/// query for the status line. Every outcome is reported as text (PRD 20.3: do not silently fail).
final class ExtensionActivator: NSObject, OSSystemExtensionRequestDelegate {
    var onStatus: ((String) -> Void)?
    var onLog: ((String) -> Void)?

    private var pendingRequests: [OSSystemExtensionRequest] = []

    func activate(extensionBundleID: String) {
        let request = OSSystemExtensionRequest.activationRequest(forExtensionWithIdentifier: extensionBundleID, queue: .main)
        submit(request, description: "activation")
    }

    func deactivate(extensionBundleID: String) {
        let request = OSSystemExtensionRequest.deactivationRequest(forExtensionWithIdentifier: extensionBundleID, queue: .main)
        submit(request, description: "deactivation")
    }

    func queryProperties(extensionBundleID: String) {
        let request = OSSystemExtensionRequest.propertiesRequest(forExtensionWithIdentifier: extensionBundleID, queue: .main)
        submit(request, description: "properties query")
    }

    private func submit(_ request: OSSystemExtensionRequest, description: String) {
        request.delegate = self
        pendingRequests.append(request)
        onLog?("Submitting \(description) request for \(request.identifier)")
        OSSystemExtensionManager.shared.submitRequest(request)
    }

    private func finish(_ request: OSSystemExtensionRequest) {
        pendingRequests.removeAll { $0 === request }
    }

    // MARK: OSSystemExtensionRequestDelegate

    func request(_ request: OSSystemExtensionRequest,
                 actionForReplacingExtension existing: OSSystemExtensionProperties,
                 withExtension ext: OSSystemExtensionProperties) -> OSSystemExtensionRequest.ReplacementAction {
        onLog?("Replacing extension \(existing.bundleShortVersion) (\(existing.bundleVersion)) with \(ext.bundleShortVersion) (\(ext.bundleVersion))")
        return .replace
    }

    func requestNeedsUserApproval(_ request: OSSystemExtensionRequest) {
        onStatus?("Waiting for approval — open System Settings and allow the VRMCast camera extension.")
        onLog?("Request needs user approval")
    }

    func request(_ request: OSSystemExtensionRequest, didFinishWithResult result: OSSystemExtensionRequest.Result) {
        finish(request)
        switch result {
        case .completed:
            onStatus?("Installed and enabled. 'VRM Live Camera' should now be listed in OBS › Video Capture Device.")
            onLog?("Request completed")
        case .willCompleteAfterReboot:
            onStatus?("Installed; macOS requires a reboot before the extension becomes active.")
            onLog?("Request will complete after reboot")
        @unknown default:
            onStatus?("Request finished with an unknown result (\(result.rawValue)).")
            onLog?("Unknown result \(result.rawValue)")
        }
    }

    func request(_ request: OSSystemExtensionRequest, didFailWithError error: Error) {
        finish(request)
        let nsError = error as NSError
        var hint = ""
        if nsError.domain == OSSystemExtensionErrorDomain, let code = OSSystemExtensionError.Code(rawValue: nsError.code) {
            switch code {
            case .extensionNotFound:
                hint = " (extension bundle missing: check Embed System Extensions build phase and bundle identifiers)"
            case .unsupportedParentBundleLocation:
                hint = " (app must run from /Applications, or run `systemextensionsctl developer on`)"
            case .validationFailed, .codeSignatureInvalid, .forbiddenBySystemPolicy:
                hint = " (signing/entitlements problem: set DEVELOPMENT_TEAM in Config/Signing.xcconfig)"
            case .authorizationRequired:
                hint = " (approve the extension in System Settings)"
            case .requestCanceled, .requestSuperseded:
                hint = " (request replaced by a newer one)"
            default:
                hint = ""
            }
        }
        onStatus?("Failed: \(error.localizedDescription)\(hint)")
        onLog?("Request failed: domain=\(nsError.domain) code=\(nsError.code) \(error.localizedDescription)")
    }

    func request(_ request: OSSystemExtensionRequest, foundProperties properties: [OSSystemExtensionProperties]) {
        finish(request)
        guard let props = properties.first else {
            onStatus?("Not installed.")
            onLog?("Properties query: no extension installed for \(request.identifier)")
            return
        }
        var state: [String] = []
        state.append(props.isEnabled ? "enabled" : "disabled")
        if props.isAwaitingUserApproval { state.append("awaiting approval") }
        if props.isUninstalling { state.append("uninstalling") }
        onStatus?("Installed \(props.bundleShortVersion) (\(props.bundleVersion)) — \(state.joined(separator: ", ")).")
        onLog?("Properties: \(properties.count) version(s) found; first is \(props.bundleVersion) \(state.joined(separator: ", "))")
    }
}
