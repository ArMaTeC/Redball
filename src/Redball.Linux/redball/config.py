"""
Configuration/Settings management for Redball Linux
Uses GSettings (dconf) for persistent storage
"""

import gi
gi.require_version('Gtk', '4.0')
gi.require_version('Adw', '1')

from gi.repository import Gio, GLib


class Config:
    """Configuration manager using GSettings"""
    
    SCHEMA_ID = 'com.armatec.Redball'
    
    def __init__(self):
        self.settings = None
        self._init_settings()
    
    def _init_settings(self):
        """Initialize GSettings schema"""
        try:
            self.settings = Gio.Settings.new(self.SCHEMA_ID)
        except Exception as e:
            print(f"Warning: Could not load GSettings schema: {e}")
            # Fallback to default values
            self.settings = None
    
    def get_keep_awake_enabled(self) -> bool:
        """Get keep-awake enabled state"""
        if self.settings:
            return self.settings.get_boolean('keep-awake-enabled')
        return False
    
    def set_keep_awake_enabled(self, enabled: bool):
        """Set keep-awake enabled state"""
        if self.settings:
            self.settings.set_boolean('keep-awake-enabled', enabled)
    
    def get_idle_threshold_minutes(self) -> int:
        """Get idle threshold in minutes"""
        if self.settings:
            return self.settings.get_int('idle-threshold-minutes')
        return 5
    
    def set_idle_threshold_minutes(self, minutes: int):
        """Set idle threshold in minutes"""
        if self.settings:
            self.settings.set_int('idle-threshold-minutes', minutes)
    
    def get_typething_enabled(self) -> bool:
        """Get TypeThing enabled state"""
        if self.settings:
            return self.settings.get_boolean('typething-enabled')
        return False
    
    def set_typething_enabled(self, enabled: bool):
        """Set TypeThing enabled state"""
        if self.settings:
            self.settings.set_boolean('typething-enabled', enabled)
    
    def get_typething_interval_seconds(self) -> int:
        """Get TypeThing interval in seconds"""
        if self.settings:
            return self.settings.get_int('typething-interval-seconds')
        return 300
    
    def set_typething_interval_seconds(self, seconds: int):
        """Set TypeThing interval in seconds"""
        if self.settings:
            self.settings.set_int('typething-interval-seconds', seconds)
    
    def get_pomodoro_enabled(self) -> bool:
        """Get Pomodoro timer enabled state"""
        if self.settings:
            return self.settings.get_boolean('pomodoro-enabled')
        return False
    
    def set_pomodoro_enabled(self, enabled: bool):
        """Set Pomodoro timer enabled state"""
        if self.settings:
            self.settings.set_boolean('pomodoro-enabled', enabled)
    
    def get_pomodoro_work_minutes(self) -> int:
        """Get Pomodoro work session length in minutes"""
        if self.settings:
            return self.settings.get_int('pomodoro-work-minutes')
        return 25
    
    def set_pomodoro_work_minutes(self, minutes: int):
        """Set Pomodoro work session length"""
        if self.settings:
            self.settings.set_int('pomodoro-work-minutes', minutes)
    
    def get_pomodoro_break_minutes(self) -> int:
        """Get Pomodoro break length in minutes"""
        if self.settings:
            return self.settings.get_int('pomodoro-break-minutes')
        return 5
    
    def set_pomodoro_break_minutes(self, minutes: int):
        """Set Pomodoro break length"""
        if self.settings:
            self.settings.set_int('pomodoro-break-minutes', minutes)
    
    def get_battery_aware(self) -> bool:
        """Get battery-aware mode state"""
        if self.settings:
            return self.settings.get_boolean('battery-aware')
        return True
    
    def set_battery_aware(self, enabled: bool):
        """Set battery-aware mode"""
        if self.settings:
            self.settings.set_boolean('battery-aware', enabled)
    
    def get_show_notifications(self) -> bool:
        """Get notification display state"""
        if self.settings:
            return self.settings.get_boolean('show-notifications')
        return True
    
    def set_show_notifications(self, enabled: bool):
        """Set notification display state"""
        if self.settings:
            self.settings.set_boolean('show-notifications', enabled)
    
    def get_mini_widget_enabled(self) -> bool:
        """Get mini widget enabled state"""
        if self.settings:
            return self.settings.get_boolean('mini-widget-enabled')
        return False
    
    def set_mini_widget_enabled(self, enabled: bool):
        """Set mini widget enabled state"""
        if self.settings:
            self.settings.set_boolean('mini-widget-enabled', enabled)
    
    def get_autostart(self) -> bool:
        """Get autostart on login state"""
        if self.settings:
            return self.settings.get_boolean('autostart')
        return False
    
    def set_autostart(self, enabled: bool):
        """Set autostart on login"""
        if self.settings:
            self.settings.set_boolean('autostart', enabled)
        
        # Also update desktop autostart
        self._update_desktop_autostart(enabled)
    
    def _update_desktop_autostart(self, enabled: bool):
        """Update desktop autostart file"""
        autostart_dir = GLib.get_user_config_dir() + '/autostart'
        desktop_file = autostart_dir + '/com.armatec.Redball.desktop'
        
        if enabled:
            # Create autostart entry
            import os
            os.makedirs(autostart_dir, exist_ok=True)
            content = f"""[Desktop Entry]
Name=Redball
Exec=redball
Type=Application
X-GNOME-Autostart-enabled=true
"""
            with open(desktop_file, 'w') as f:
                f.write(content)
        else:
            # Remove autostart entry
            try:
                import os
                os.remove(desktop_file)
            except FileNotFoundError:
                pass
    
    def bind_to_widget(self, key: str, widget):
        """Bind a setting key to a widget"""
        if self.settings:
            self.settings.bind(key, widget, 'active', Gio.SettingsBindFlags.DEFAULT)
