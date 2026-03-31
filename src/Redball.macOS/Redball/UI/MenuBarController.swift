import SwiftUI
import AppKit

/// Manages the menu bar status item and dropdown menu
class MenuBarController: ObservableObject {
    private var statusItem: NSStatusItem
    private var keepAwakeEngine: KeepAwakeEngine
    private var cancellables: Set<AnyCancellable> = []
    
    init(keepAwakeEngine: KeepAwakeEngine) {
        self.keepAwakeEngine = keepAwakeEngine
        
        // Create status item
        statusItem = NSStatusBar.shared.statusItem(withLength: NSStatusItem.variableLength)
        
        setupMenu()
        setupBindings()
    }
    
    private func setupMenu() {
        guard let button = statusItem.button else { return }
        
        button.image = NSImage(systemSymbolName: "circle.fill", accessibilityDescription: "Redball")
        button.action = #selector(toggleMenu)
        button.target = self
        
        // Build menu
        let menu = NSMenu()
        
        // Status header
        let statusItem = NSMenuItem(
            title: "Redball: Inactive",
            action: nil,
            keyEquivalent: ""
        )
        statusItem.tag = 100 // For updating later
        menu.addItem(statusItem)
        
        menu.addItem(NSMenuItem.separator())
        
        // Toggle action
        let toggleItem = NSMenuItem(
            title: "Start Keep-Awake",
            action: #selector(toggleKeepAwake),
            keyEquivalent: "k"
        )
        toggleItem.target = self
        menu.addItem(toggleItem)
        
        // Timed sessions submenu
        let timedMenu = NSMenu(title: "Timed Session")
        [30, 60, 120, 240].forEach { minutes in
            let item = NSMenuItem(
                title: "\(minutes) minutes",
                action: #selector(startTimed(_:)),
                keyEquivalent: ""
            )
            item.representedObject = minutes
            item.target = self
            timedMenu.addItem(item)
        }
        
        let timedItem = NSMenuItem(title: "Timed Session", action: nil, keyEquivalent: "")
        timedItem.submenu = timedMenu
        menu.addItem(timedItem)
        
        menu.addItem(NSMenuItem.separator())
        
        // Settings
        menu.addItem(NSMenuItem(
            title: "Preferences...",
            action: #selector(openSettings),
            keyEquivalent: ","
        )).target = self
        
        menu.addItem(NSMenuItem.separator())
        
        // Quit
        menu.addItem(NSMenuItem(
            title: "Quit Redball",
            action: #selector(quit),
            keyEquivalent: "q"
        )).target = self
        
        statusItem.menu = menu
    }
    
    private func setupBindings() {
        // Update UI when keep-awake state changes
        keepAwakeEngine.$isActive
            .receive(on: DispatchQueue.main)
            .sink { [weak self] isActive in
                self?.updateUI(isActive: isActive)
            }
            .store(in: &cancellables)
    }
    
    private func updateUI(isActive: Bool) {
        guard let button = statusItem.button,
              let menu = statusItem.menu else { return }
        
        // Update icon
        let symbolName = isActive ? "circle.fill" : "circle"
        let color = isActive ? NSColor.systemGreen : NSColor.systemGray
        
        if let image = NSImage(systemSymbolName: symbolName, accessibilityDescription: nil) {
            image.isTemplate = false
            button.image = image
            button.contentTintColor = color
        }
        
        // Update menu items
        if let statusItem = menu.item(withTag: 100) {
            statusItem.title = isActive ? "Redball: Active" : "Redball: Inactive"
        }
        
        if let toggleItem = menu.items.first(where: { $0.action == #selector(toggleKeepAwake) }) {
            toggleItem.title = isActive ? "Stop Keep-Awake" : "Start Keep-Awake"
        }
    }
    
    @objc private func toggleMenu() {
        // Menu shows automatically on click
    }
    
    @objc private func toggleKeepAwake() {
        keepAwakeEngine.toggle()
    }
    
    @objc private func startTimed(_ sender: NSMenuItem) {
        guard let minutes = sender.representedObject as? Int else { return }
        let duration = TimeInterval(minutes * 60)
        keepAwakeEngine.start(duration: duration)
    }
    
    @objc private func openSettings() {
        NSApp.sendAction(Selector(("showPreferencesWindow:")), to: nil, from: nil)
        NSApp.activate(ignoringOtherApps: true)
    }
    
    @objc private func quit() {
        keepAwakeEngine.stop()
        NSApp.terminate(nil)
    }
}
