import Foundation
import IOKit.pwr_mgt

/// Manages system keep-awake using IOKit power assertions
class KeepAwakeEngine: ObservableObject {
    @Published var isActive: Bool = false
    @Published var currentSessionDuration: TimeInterval = 0
    
    private var assertionID: IOPMAssertionID = 0
    private var timer: Timer?
    private var sessionStartTime: Date?
    
    private let assertionName = "Redball Keep Awake" as CFString
    
    /// Starts keep-awake with optional duration limit
    func start(duration: TimeInterval? = nil) {
        guard !isActive else { return }
        
        // Create power assertion to prevent sleep
        let result = IOPMAssertionCreateWithName(
            kIOPMAssertionTypeNoDisplaySleep as CFString,
            IOPMAssertionLevel(kIOPMAssertionLevelOn),
            assertionName,
            &assertionID
        )
        
        guard result == kIOReturnSuccess else {
            Logger.shared.error("Failed to create power assertion")
            return
        }
        
        isActive = true
        sessionStartTime = Date()
        
        // Start duration timer
        timer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
            self?.updateDuration()
        }
        
        Logger.shared.info("Keep-awake started")
        
        // Handle timed session
        if let duration = duration {
            DispatchQueue.main.asyncAfter(deadline: .now() + duration) { [weak self] in
                self?.stop()
            }
        }
    }
    
    /// Stops keep-awake
    func stop() {
        guard isActive else { return }
        
        // Release power assertion
        if assertionID != 0 {
            IOPMAssertionRelease(assertionID)
            assertionID = 0
        }
        
        timer?.invalidate()
        timer = nil
        
        isActive = false
        currentSessionDuration = 0
        sessionStartTime = nil
        
        Logger.shared.info("Keep-awake stopped")
    }
    
    /// Toggles keep-awake state
    func toggle() {
        if isActive {
            stop()
        } else {
            start()
        }
    }
    
    private func updateDuration() {
        guard let startTime = sessionStartTime else { return }
        currentSessionDuration = Date().timeIntervalSince(startTime)
    }
}
