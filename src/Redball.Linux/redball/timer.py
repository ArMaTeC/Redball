"""
Timer functionality for Redball Linux
Includes Pomodoro timer and session management
"""

import gi
gi.require_version('Gtk', '4.0')

from gi.repository import GLib, GObject
from enum import Enum


class TimerState(Enum):
    """Timer states"""
    STOPPED = "stopped"
    RUNNING = "running"
    PAUSED = "paused"
    COMPLETED = "completed"


class TimerMode(Enum):
    """Timer modes"""
    WORK = "work"
    BREAK = "break"
    LONG_BREAK = "long_break"


class PomodoroTimer(GObject.Object):
    """
    Pomodoro timer with work/break cycles
    Emits signals for state changes
    """
    
    __gsignals__ = {
        'tick': (GObject.SignalFlags.RUN_FIRST, None, (int,)),
        'completed': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
        'state-changed': (GObject.SignalFlags.RUN_FIRST, None, (str,)),
    }
    
    def __init__(self):
        super().__init__()
        
        self.state = TimerState.STOPPED
        self.mode = TimerMode.WORK
        self.remaining_seconds = 0
        self.total_seconds = 0
        
        self.work_minutes = 25
        self.break_minutes = 5
        self.long_break_minutes = 15
        self.work_sessions_before_long_break = 4
        self.work_sessions_completed = 0
        
        self._timeout_id = None
    
    def start_work(self):
        """Start a work session"""
        self.mode = TimerMode.WORK
        self.total_seconds = self.work_minutes * 60
        self.remaining_seconds = self.total_seconds
        self.state = TimerState.RUNNING
        self._start_timer()
        self.emit('state-changed', self.state.value)
    
    def start_break(self):
        """Start a break session"""
        if self.work_sessions_completed >= self.work_sessions_before_long_break:
            self.mode = TimerMode.LONG_BREAK
            self.total_seconds = self.long_break_minutes * 60
            self.work_sessions_completed = 0
        else:
            self.mode = TimerMode.BREAK
            self.total_seconds = self.break_minutes * 60
        
        self.remaining_seconds = self.total_seconds
        self.state = TimerState.RUNNING
        self._start_timer()
        self.emit('state-changed', self.state.value)
    
    def pause(self):
        """Pause the timer"""
        if self.state == TimerState.RUNNING:
            self._stop_timer()
            self.state = TimerState.PAUSED
            self.emit('state-changed', self.state.value)
    
    def resume(self):
        """Resume a paused timer"""
        if self.state == TimerState.PAUSED:
            self.state = TimerState.RUNNING
            self._start_timer()
            self.emit('state-changed', self.state.value)
    
    def stop(self):
        """Stop the timer"""
        self._stop_timer()
        self.state = TimerState.STOPPED
        self.remaining_seconds = 0
        self.emit('state-changed', self.state.value)
    
    def _start_timer(self):
        """Start the GLib timeout"""
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
        self._timeout_id = GLib.timeout_add_seconds(1, self._on_tick)
    
    def _stop_timer(self):
        """Stop the GLib timeout"""
        if self._timeout_id:
            GLib.source_remove(self._timeout_id)
            self._timeout_id = None
    
    def _on_tick(self):
        """Handle timer tick"""
        if self.remaining_seconds > 0:
            self.remaining_seconds -= 1
            self.emit('tick', self.remaining_seconds)
            return True  # Continue timer
        else:
            # Timer completed
            self._on_completed()
            return False  # Stop timer
    
    def _on_completed(self):
        """Handle timer completion"""
        self.state = TimerState.COMPLETED
        self._stop_timer()
        
        if self.mode == TimerMode.WORK:
            self.work_sessions_completed += 1
            self.emit('completed', 'work')
        else:
            self.emit('completed', 'break')
    
    @property
    def progress(self) -> float:
        """Get timer progress as percentage (0-1)"""
        if self.total_seconds == 0:
            return 0.0
        return 1.0 - (self.remaining_seconds / self.total_seconds)
    
    @property
    def formatted_time(self) -> str:
        """Get formatted time string (MM:SS)"""
        minutes = self.remaining_seconds // 60
        seconds = self.remaining_seconds % 60
        return f"{minutes:02d}:{seconds:02d}"
    
    @property
    def is_running(self) -> bool:
        """Check if timer is running"""
        return self.state == TimerState.RUNNING


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
