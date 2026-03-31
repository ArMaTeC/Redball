#!/usr/bin/env python3
"""
Redball for Linux - GTK4/GNOME implementation
Main application entry point
"""

import sys
import gi

gi.require_version('Gtk', '4.0')
gi.require_version('Adw', '1')

from gi.repository import Gtk, Adw, GLib
from .window import RedballWindow
from .tray import TrayIndicator
from .keepawake import KeepAwakeEngine


class RedballApp(Adw.Application):
    """Main application class for Redball Linux"""
    
    def __init__(self):
        super().__init__(
            application_id='com.armatec.Redball',
            flags=Gio.ApplicationFlags.FLAGS_NONE
        )
        self.keep_awake = KeepAwakeEngine()
        self.tray = None
        
    def do_activate(self):
        """Activate the application"""
        # Create tray indicator (primary interface)
        if not self.tray:
            self.tray = TrayIndicator(self.keep_awake)
            self.tray.connect('show-preferences', self._on_show_preferences)
        
        # Show window if requested, otherwise just use tray
        win = self.get_active_window()
        if not win:
            win = RedballWindow(application=self, keep_awake=self.keep_awake)
        
        win.present()
    
    def _on_show_preferences(self, tray):
        """Show preferences window from tray"""
        win = self.get_active_window()
        if not win:
            win = RedballWindow(application=self, keep_awake=self.keep_awake)
        win.present()
        win.show_preferences()


def main(version):
    """Application entry point"""
    app = RedballApp()
    return app.run(sys.argv)
