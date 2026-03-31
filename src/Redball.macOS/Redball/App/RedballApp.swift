import SwiftUI

@main
struct RedballApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate
    
    var body: some Scene {
        Settings {
            SettingsView()
        }
    }
}

class AppDelegate: NSObject, NSApplicationDelegate {
    var menuBarController: MenuBarController?
    var keepAwakeEngine: KeepAwakeEngine?
    
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Initialize core services
        keepAwakeEngine = KeepAwakeEngine()
        menuBarController = MenuBarController(keepAwakeEngine: keepAwakeEngine!)
        
        // Hide dock icon (menu bar app)
        NSApp.setActivationPolicy(.accessory)
        
        // Load configuration
        ConfigStorage.shared.loadConfig()
        
        Logger.shared.info("Redball macOS started")
    }
    
    func applicationWillTerminate(_ notification: Notification) {
        // Cleanup
        keepAwakeEngine?.stop()
        Logger.shared.info("Redball macOS stopped")
    }
}
