"""
Status indicator widget for Redball Linux
Displays current status and timer
"""

import gi
gi.require_version('Gtk', '4.0')
gi.require_version('Adw', '1')

from gi.repository import Gtk, Adw, GLib


class StatusIndicator(Adw.Bin):
    """
    Widget showing current keep-awake status
    Includes animated indicator and time display
    """
    
    def __init__(self):
        super().__init__()
        
        self._build_ui()
        self._active = False
        
        # Animation
        self._pulse = 0
        self._timeout_id = None
    
    def _build_ui(self):
        """Build the widget UI"""
        box = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=12)
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        box.set_margin_start(12)
        box.set_margin_end(12)
        self.set_child(box)
        
        # Status icon
        self.icon = Gtk.Image.new_from_icon_name('media-playback-stop')
        self.icon.set_pixel_size(32)
        box.append(self.icon)
        
        # Status text
        text_box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL)
        box.append(text_box)
        
        self.status_label = Gtk.Label()
        self.status_label.add_css_class('title-2')
        self.status_label.set_xalign(0)
        text_box.append(self.status_label)
        
        self.time_label = Gtk.Label()
        self.time_label.add_css_class('caption')
        self.time_label.set_xalign(0)
        text_box.append(self.time_label)
        
        self.set_active(False)
    
    def set_active(self, active: bool):
        """Set the active state"""
        self._active = active
        
        if active:
            self.icon.set_from_icon_name('media-playback-start')
            self.icon.add_css_class('accent')
            self.status_label.set_text('Keep-Awake Active')
            self._start_animation()
        else:
            self.icon.set_from_icon_name('media-playback-stop')
            self.icon.remove_css_class('accent')
            self.status_label.set_text('Keep-Awake Inactive')
            self.time_label.set_text('')
            self._stop_animation()
    
    def set_time(self, time_str: str):
        """Update the time display"""
        if self._active:
            self.time_label.set_text(f'Session: {time_str}')
    
    def _start_animation(self):
        """Start pulsing animation"""
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
        self._timeout_id = GLib.timeout_add(500, self._animate)
    
    def _stop_animation(self):
        """Stop animation"""
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
            self._timeout_id = None
    
    def _animate(self):
        """Animate the indicator"""
        self._pulse = (self._pulse + 1) % 2
        opacity = 1.0 if self._pulse == 0 else 0.7
        self.icon.set_opacity(opacity)
        return True
