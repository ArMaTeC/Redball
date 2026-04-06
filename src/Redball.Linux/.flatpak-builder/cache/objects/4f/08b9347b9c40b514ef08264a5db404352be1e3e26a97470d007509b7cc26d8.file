"""
Timer functionality for Redball Linux
Includes session management for keep-awake tracking
"""

import gi
gi.require_version('Gtk', '4.0')

from gi.repository import GLib


class SessionTimer:
    """
    Simple session timer for tracking keep-awake sessions
    """
    
    def __init__(self):
        self.start_time = None
        self.end_time = None
        self.duration_seconds = 0
        self._timeout_id = None
    
    def start(self):
        """Start the session timer"""
        self.start_time = GLib.get_monotonic_time()
        self.end_time = None
        self.duration_seconds = 0
        
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
        self._timeout_id = GLib.timeout_add_seconds(1, self._update_duration)
    
    def stop(self):
        """Stop the session timer"""
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
            self._timeout_id = None
        
        self.end_time = GLib.get_monotonic_time()
        if self.start_time:
            elapsed = (self.end_time - self.start_time) / 1000000  # Convert from microseconds
            self.duration_seconds = int(elapsed)
    
    def _update_duration(self):
        """Update the duration counter"""
        if self.start_time:
            current = GLib.get_monotonic_time()
            elapsed = (current - self.start_time) / 1000000
            self.duration_seconds = int(elapsed)
        return True
    
    @property
    def formatted_duration(self) -> str:
        """Get formatted duration string (HH:MM:SS)"""
        hours = self.duration_seconds // 3600
        minutes = (self.duration_seconds % 3600) // 60
        seconds = self.duration_seconds % 60
        
        if hours > 0:
            return f"{hours}:{minutes:02d}:{seconds:02d}"
        else:
            return f"{minutes:02d}:{seconds:02d}"
    
    @property
    def is_active(self) -> bool:
        """Check if session is active"""
        return self._timeout_id is not None
