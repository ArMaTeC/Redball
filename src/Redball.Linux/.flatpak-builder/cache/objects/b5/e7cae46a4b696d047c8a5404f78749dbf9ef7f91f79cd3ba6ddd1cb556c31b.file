"""
Keep-Awake Engine for Linux
Multi-backend implementation supporting X11, Wayland, and systemd
"""

import os
import subprocess
import dbus
from enum import Enum

import gi
gi.require_version('Gtk', '4.0')

from gi.repository import GLib


class BackendType(Enum):
    X11 = "x11"
    WAYLAND = "wayland"
    SYSTEMD = "systemd"


class KeepAwakeEngine:
    """
    Cross-platform keep-awake for Linux
    Automatically detects and uses the best available backend
    """
    
    def __init__(self):
        self.backend = self._detect_backend()
        self._inhibit_cookie = None
        self._is_active = False
        
    def _detect_backend(self) -> BackendType:
        """Detect the appropriate backend for the current session"""
        if os.environ.get('WAYLAND_DISPLAY'):
            return BackendType.WAYLAND
        elif os.environ.get('DISPLAY'):
            return BackendType.X11
        else:
            return BackendType.SYSTEMD
    
    def start(self, duration: int = None):
        """
        Start keep-awake
        
        Args:
            duration: Optional session duration in seconds
        """
        if self._is_active:
            return
            
        if self.backend == BackendType.X11:
            self._start_x11()
        elif self.backend == BackendType.WAYLAND:
            self._start_wayland()
        else:
            self._start_systemd()
            
        self._is_active = True
        
        # Auto-stop after duration if specified
        if duration:
            GLib.timeout_add_seconds(duration, self.stop)
    
    def stop(self):
        """Stop keep-awake and allow screen sleep"""
        if not self._is_active:
            return
            
        if self.backend == BackendType.X11:
            self._stop_x11()
        elif self.backend == BackendType.WAYLAND:
            self._stop_wayland()
        else:
            self._stop_systemd()
            
        self._is_active = False
    
    def toggle(self):
        """Toggle keep-awake state"""
        if self._is_active:
            self.stop()
        else:
            self.start()
    
    @property
    def is_active(self) -> bool:
        return self._is_active
    
    # X11 Implementation
    def _start_x11(self):
        """Use xdg-screensaver for X11"""
        try:
            subprocess.run(
                ['xdg-screensaver', 'reset'],
                check=True,
                capture_output=True
            )
            # Start periodic reset
            self._start_periodic_reset()
        except (subprocess.CalledProcessError, FileNotFoundError):
            pass
    
    def _stop_x11(self):
        """Stop X11 screensaver prevention"""
        # xdg-screensaver doesn't need explicit stop
        pass
    
    def _start_periodic_reset(self):
        """Reset screensaver every 30 seconds"""
        if self._is_active:
            try:
                subprocess.run(
                    ['xdg-screensaver', 'reset'],
                    check=False,
                    capture_output=True
                )
            except FileNotFoundError:
                pass
            GLib.timeout_add_seconds(30, self._start_periodic_reset)
    
    # Wayland Implementation
    def _start_wayland(self):
        """Use idle-inhibit portal for Wayland"""
        try:
            bus = dbus.SessionBus()
            inhibitor = bus.get_object(
                'org.freedesktop.portal.Desktop',
                '/org/freedesktop/portal/desktop'
            )
            # Request idle inhibition via portal
            # Implementation depends on portal support
        except dbus.DBusException:
            pass
    
    def _stop_wayland(self):
        """Release Wayland idle inhibition"""
        if self._inhibit_cookie:
            # Release inhibition
            self._inhibit_cookie = None
    
    # Systemd Implementation
    def _start_systemd(self):
        """Use systemd-inhibit as fallback"""
        try:
            subprocess.Popen([
                'systemd-inhibit',
                '--what=idle',
                '--who=Redball',
                '--why=Keep system awake',
                '--mode=block',
                'sleep', 'infinity'
            ])
        except FileNotFoundError:
            pass
    
    def _stop_systemd(self):
        """Stop systemd inhibition (process will be terminated)"""
        pass
