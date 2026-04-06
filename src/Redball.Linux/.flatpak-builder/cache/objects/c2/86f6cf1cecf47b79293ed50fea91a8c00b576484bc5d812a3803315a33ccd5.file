"""
Preferences dialog for Redball Linux
"""

import gi
gi.require_version('Gtk', '4.0')
gi.require_version('Adw', '1')

from gi.repository import Gtk, Adw


class PreferencesWindow(Adw.PreferencesWindow):
    """
    Preferences dialog with settings pages
    """
    
    def __init__(self, parent=None, config=None):
        super().__init__(transient_for=parent)
        
        self.config = config
        self.set_default_size(600, 500)
        self.set_title('Preferences')
        
        self._build_ui()
    
    def _build_ui(self):
        """Build preferences UI"""
        # General settings page
        general_page = Adw.PreferencesPage(title='General')
        self.add(general_page)
        
        # Keep-Awake group
        keep_awake_group = Adw.PreferencesGroup(title='Keep-Awake Settings')
        general_page.add(keep_awake_group)
        
        # Idle threshold
        idle_row = Adw.SpinRow.new_with_range(1, 60, 1)
        idle_row.set_title('Idle Threshold')
        idle_row.set_subtitle('Minutes before keep-awake activates')
        keep_awake_group.add(idle_row)
        
        if self.config:
            idle_row.set_value(self.config.get_idle_threshold_minutes())
            idle_row.connect('changed', self._on_idle_changed)
        
        # Battery aware
        battery_row = Adw.SwitchRow()
        battery_row.set_title('Battery Aware')
        battery_row.set_subtitle('Disable when running on battery')
        keep_awake_group.add(battery_row)
        
        if self.config:
            battery_row.set_active(self.config.get_battery_aware())
            self.config.bind_to_widget('battery-aware', battery_row)
        
        # TypeThing group
        typething_group = Adw.PreferencesGroup(title='TypeThing Settings')
        general_page.add(typething_group)
        
        # Interval
        interval_row = Adw.SpinRow.new_with_range(60, 3600, 30)
        interval_row.set_title('Typing Interval')
        interval_row.set_subtitle('Seconds between automatic keypresses')
        typething_group.add(interval_row)
        
        if self.config:
            interval_row.set_value(self.config.get_typething_interval_seconds())
            interval_row.connect('changed', self._on_interval_changed)
        
        # Startup group
        startup_group = Adw.PreferencesGroup(title='Startup')
        general_page.add(startup_group)
        
        # Autostart
        autostart_row = Adw.SwitchRow()
        autostart_row.set_title('Autostart')
        autostart_row.set_subtitle('Start Redball on login')
        startup_group.add(autostart_row)
        
        if self.config:
            autostart_row.set_active(self.config.get_autostart())
            self.config.bind_to_widget('autostart', autostart_row)
    
    def _on_idle_changed(self, row):
        """Handle idle threshold change"""
        if self.config:
            self.config.set_idle_threshold_minutes(int(row.get_value()))
    
    def _on_interval_changed(self, row):
        """Handle interval change"""
        if self.config:
            self.config.set_typething_interval_seconds(int(row.get_value()))
