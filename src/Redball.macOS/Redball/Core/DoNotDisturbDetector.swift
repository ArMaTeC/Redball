import Foundation
import CoreData

/// Detects macOS Do Not Disturb / Focus mode status
class DoNotDisturbDetector: ObservableObject {
    @Published var isDoNotDisturbEnabled: Bool = false
    
    private var timer: Timer?
    
    init() {
        // Check initial status
        updateStatus()
        
        // Monitor for changes
        timer = Timer.scheduledTimer(withTimeInterval: 5.0, repeats: true) { [weak self] _ in
            self?.updateStatus()
        }
    }
    
    deinit {
        timer?.invalidate()
    }
    
    private func updateStatus() {
        // Check DND status using notification center prefs
        let enabled = checkDoNotDisturbStatus()
        
        if enabled != isDoNotDisturbEnabled {
            isDoNotDisturbEnabled = enabled
            Logger.shared.info("Do Not Disturb status changed: \(enabled)")
        }
    }
    
    private func checkDoNotDisturbStatus() -> Bool {
        // Method 1: Check com.apple.notificationcenterui preferences
        if let prefs = UserDefaults(suiteName: "com.apple.notificationcenterui") {
            let dndEnabled = prefs.bool(forKey: "doNotDisturb")
            if dndEnabled {
                return true
            }
        }
        
        // Method 2: Check Focus modes (macOS 12+)
        if #available(macOS 12.0, *) {
            // Check if any Focus is active via private API
            // This requires entitlements in production
        }
        
        return false
    }
}
