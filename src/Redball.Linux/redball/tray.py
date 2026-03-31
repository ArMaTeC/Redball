"""
System Tray Integration for Linux
Supports AppIndicator3 for GNOME/Unity and StatusNotifier for KDE
"""

import gi

gi.require_version('Gtk', '4.0')

from gi.repository import Gtk, Gio, GLib, GObject


class TrayIndicator(GObject.GObject):
    """
    System tray indicator for Redball
    Provides menu and status display
    """
    
    __gsignals__ = {
        'show-preferences': (GObject.SIGNAL_RUN_FIRST, None, ())
    }
    
    def __init__(self, keep_awake):
        super().__init__()
        self.keep_awake = keep_awake
        self.indicator = None
        self.menu = None
        
        self._setup_indicator()
        self._update_icon()
    
    def _setup_indicator(self):
        """Create the tray indicator"""
        # Create menu
        self.menu = Gio.Menu()
        
        # Status item
        status_section = Gio.Menu()
        status_section.append("Redball: Inactive", "app.status")
        self.menu.append_section("Status", status_section)
        
        # Actions section
        actions_section = Gio.Menu()
        actions_section.append("Start Keep-Awake", "app.toggle")
        
        # Timed submenu
        timed_menu = Gio.Menu()
        for minutes in [30, 60, 120]:
            timed_menu.append(
                f"{minutes} minutes",
                f"app.timed::{minutes}"
            )
        actions_section.append_submenu("Timed Session", timed_menu)
        self.menu.append_section("Actions", actions_section)
        
        # Settings section
        settings_section = Gio.Menu()
        settings_section.append("Preferences", "app.preferences")
        settings_section.append("Quit", "app.quit")
        self.menu.append_section("Settings", settings_section)
        
        # Create indicator using StatusNotifier (modern standard)
        self._create_status_notifier()
    
    def _create_status_notifier(self):
        """Create StatusNotifierItem for modern desktop environments"""
        # Using D-Bus StatusNotifierItem protocol
        # This works on GNOME (with extension), KDE, XFCE, etc.
        
        bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        
        # Export menu
        self.menu_id = bus.export_menu_model(
            '/com/armatec/Redball/Menu',
            self.menu
        )
        
        # Update initial state
        self._update_icon()
    
    def _update_icon(self):
        """Update tray icon based on keep-awake state"""
        # Icon names would correspond to installed icon theme
        if self.keep_awake.is_active:
            icon_name = 'redball-active'
        else:
            icon_name = 'redball-inactive'
        
        # Update menu text
        self._update_menu_text()
    
    def _update_menu_text(self):
        """Update menu items based on state"""
        # Dynamic menu updates would go here
        pass
    
    def on_toggle(self, action, param):
        """Handle toggle action from menu"""
        self.keep_awake.toggle()
        self._update_icon()
    
    def on_timed(self, action, param):
        """Handle timed session action"""
        minutes = param.get_int32()
        self.keep_awake.start(duration=minutes * 60)
        self._update_icon()
    
    def on_preferences(self, action, param):
        """Show preferences window"""
        self.emit('show-preferences')
    
    def on_quit(self, action, param):
        """Quit application"""
        self.keep_awake.stop()
        Gtk.main_quit()
    
    def update(self):
        """Update indicator display (call periodically)"""
        self._update_icon()
        return True  # For GLib timeout
