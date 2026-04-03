"""
System tray indicator for Redball Linux
Uses AppIndicator3 or fallback to StatusIcon
"""

from gi.repository import Gtk, GLib, GObject
import gi
gi.require_version('Gtk', '4.0')


class TrayIndicator(GObject.Object):
    """
    System tray indicator
    Provides menu and status display
    """
    
    __gsignals__ = {
        'show-preferences': (GObject.SignalFlags.RUN_FIRST, None, ()),
        'quit': (GObject.SignalFlags.RUN_FIRST, None, ()),
    }
    
    def __init__(self, keep_awake=None):
        super().__init__()
        
        self.keep_awake = keep_awake
        self.menu = None
        self.indicator = None
        
        self._create_menu()
        self._setup_indicator()
    
    def _create_menu(self):
        """Create the tray menu"""
        self.menu = Gio.Menu()
        
        # Status section
        self.status_item = Gio.MenuItem.new("Status: Inactive", None)
        self.menu.append_item(self.status_item)
        
        self.menu.append_item(Gio.MenuItem.new_separator())
        
        # Toggle keep-awake
        toggle_item = Gio.MenuItem.new("Enable Keep-Awake", "app.toggle-keep-awake")
        self.menu.append_item(toggle_item)
        
        # Toggle TypeThing
        typething_item = Gio.MenuItem.new("Enable TypeThing", "app.toggle-typething")
        self.menu.append_item(typething_item)
        
        self.menu.append_item(Gio.MenuItem.new_separator())
        
        # Preferences
        prefs_item = Gio.MenuItem.new("Preferences", "app.preferences")
        self.menu.append_item(prefs_item)
        
        # About
        about_item = Gio.MenuItem.new("About", "app.about")
        self.menu.append_item(about_item)
        
        self.menu.append_item(Gio.MenuItem.new_separator())
        
        # Quit
        quit_item = Gio.MenuItem.new("Quit", "app.quit")
        self.menu.append_item(quit_item)
    
    def _setup_indicator(self):
        """Setup the tray indicator"""
        try:
            # Try to use AppIndicator (requires gir1.2-appindicator3-0.1)
            gi.require_version('AppIndicator3', '0.1')
            from gi.repository import AppIndicator3
            
            self.indicator = AppIndicator3.Indicator.new(
                'com.armatec.Redball',
                'com.armatec.Redball',
                AppIndicator3.IndicatorCategory.APPLICATION_STATUS
            )
            self.indicator.set_status(AppIndicator3.IndicatorStatus.ACTIVE)
            self.indicator.set_menu(self.menu)
            
        except (ValueError, ImportError):
            # Fallback: Use a simple window or skip tray
            print("AppIndicator not available, using fallback")
            self.indicator = None
    
    def set_active(self, active: bool):
        """Update tray status"""
        if active:
            self.status_item.set_label("Status: Active")
        else:
            self.status_item.set_label("Status: Inactive")
    
    def show(self):
        """Show the tray indicator"""
        pass  # Indicator is always shown when created
    
    def hide(self):
        """Hide the tray indicator"""
        if self.indicator:
            try:
                self.indicator.set_status(AppIndicator3.IndicatorStatus.PASSIVE)
            except:
                pass
