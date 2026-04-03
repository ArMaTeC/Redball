"""
Main application window for Redball Linux
"""

import gi
gi.require_version('Gtk', '4.0')
gi.require_version('Adw', '1')

from gi.repository import Gtk, Adw, Gio, GLib


class RedballWindow(Adw.ApplicationWindow):
    """Main application window"""
    
    def __init__(self, **kwargs):
        self.keep_awake = kwargs.pop('keep_awake', None)
        super().__init__(**kwargs)
        
        self.set_default_size(400, 500)
        self.set_title("Redball")
        
        # Build UI
        self._build_ui()
        
        # Connect to keep-awake signals
        if self.keep_awake:
            self._update_status()
    
    def _build_ui(self):
        """Build the user interface"""
        # Main box
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        self.set_content(box)
        
        # Header bar
        header = Adw.HeaderBar()
        box.append(header)
        
        # Toast overlay for notifications
        toast_overlay = Adw.ToastOverlay()
        box.append(toast_overlay)
        self.toast_overlay = toast_overlay
        
        # Main content
        content_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        content_box.set_margin_top(12)
        content_box.set_margin_bottom(12)
        content_box.set_margin_start(12)
        content_box.set_margin_end(12)
        toast_overlay.set_child(content_box)
        
        # Status section
        status_group = Adw.PreferencesGroup(title="Status")
        content_box.append(status_group)
        
        # Keep-awake row
        self.keep_awake_row = Adw.ActionRow(
            title="Keep Awake",
            subtitle="Prevent screen from sleeping"
        )
        status_group.add(self.keep_awake_row)
        
        self.keep_awake_switch = Gtk.Switch()
        self.keep_awake_switch.set_valign(Gtk.Align.CENTER)
        self.keep_awake_switch.connect('notify::active', self._on_keep_awake_toggled)
        self.keep_awake_row.add_suffix(self.keep_awake_switch)
        
        # TypeThing section
        typething_group = Adw.PreferencesGroup(title="TypeThing")
        content_box.append(typething_group)
        
        self.typething_row = Adw.ActionRow(
            title="Enable TypeThing",
            subtitle="Automatic typing simulation"
        )
        typething_group.add(self.typething_row)
        
        self.typething_switch = Gtk.Switch()
        self.typething_switch.set_valign(Gtk.Align.CENTER)
        typething_group.add(self.typething_switch)
        
        # Timer settings
        timer_group = Adw.PreferencesGroup(title="Timer Settings")
        content_box.append(timer_group)
        
        self.interval_spin = Adw.SpinRow.new_with_range(60, 3600, 30)
        self.interval_spin.set_title("Interval (seconds)")
        self.interval_spin.set_value(300)
        timer_group.add(self.interval_spin)
        
        # Bottom buttons
        button_box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        button_box.set_halign(Gtk.Align.CENTER)
        button_box.set_margin_top(24)
        content_box.append(button_box)
        
        self.start_button = Gtk.Button(label="Start")
        self.start_button.add_css_class("suggested-action")
        self.start_button.connect('clicked', self._on_start_clicked)
        button_box.append(self.start_button)
        
        self.stop_button = Gtk.Button(label="Stop")
        self.stop_button.set_sensitive(False)
        self.stop_button.connect('clicked', self._on_stop_clicked)
        button_box.append(self.stop_button)
    
    def _on_keep_awake_toggled(self, switch, pspec):
        """Handle keep-awake toggle"""
        if switch.get_active():
            if self.keep_awake:
                self.keep_awake.start()
            self._show_toast("Keep-awake enabled")
        else:
            if self.keep_awake:
                self.keep_awake.stop()
            self._show_toast("Keep-awake disabled")
        self._update_status()
    
    def _on_start_clicked(self, button):
        """Handle start button"""
        if self.keep_awake and not self.keep_awake.is_active:
            self.keep_awake.start()
            self.keep_awake_switch.set_active(True)
        self.start_button.set_sensitive(False)
        self.stop_button.set_sensitive(True)
        self._show_toast("Session started")
    
    def _on_stop_clicked(self, button):
        """Handle stop button"""
        if self.keep_awake and self.keep_awake.is_active:
            self.keep_awake.stop()
            self.keep_awake_switch.set_active(False)
        self.start_button.set_sensitive(True)
        self.stop_button.set_sensitive(False)
        self._show_toast("Session stopped")
    
    def _update_status(self):
        """Update UI based on keep-awake status"""
        if self.keep_awake:
            is_active = self.keep_awake.is_active
            self.keep_awake_switch.set_active(is_active)
            self.start_button.set_sensitive(not is_active)
            self.stop_button.set_sensitive(is_active)
    
    def _show_toast(self, message):
        """Show a toast notification"""
        toast = Adw.Toast.new(message)
        toast.set_timeout(3)
        self.toast_overlay.add_toast(toast)
    
    def show_preferences(self):
        """Show preferences dialog"""
        # TODO: Implement preferences dialog
        self._show_toast("Preferences not yet implemented")
