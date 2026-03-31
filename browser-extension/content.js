// Redball Browser Extension - Content Script
// Injected into web pages to detect user activity and communicate with background

(function() {
    'use strict';

    console.log('[Redball Extension] Content script loaded');

    class RedballContentScript {
        constructor() {
            this.lastActivity = Date.now();
            this.isActive = false;
            this.init();
        }

        init() {
            // Track user activity
            this.trackActivity();
            
            // Listen for messages from background
            chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
                return this.handleMessage(message, sender, sendResponse);
            });
            
            // Notify background that content script is ready
            this.sendToBackground({ type: 'CONTENT_SCRIPT_READY' });
        }

        trackActivity() {
            const events = ['mousedown', 'keydown', 'scroll', 'touchstart'];
            
            events.forEach(event => {
                document.addEventListener(event, () => {
                    this.lastActivity = Date.now();
                    this.notifyActivity();
                }, { passive: true });
            });
            
            // Check for inactivity periodically
            setInterval(() => this.checkInactivity(), 60000); // Every minute
        }

        checkInactivity() {
            const idleTime = Date.now() - this.lastActivity;
            const IDLE_THRESHOLD = 5 * 60 * 1000; // 5 minutes
            
            if (idleTime > IDLE_THRESHOLD && this.isActive) {
                this.isActive = false;
                this.sendToBackground({
                    type: 'USER_INACTIVE',
                    idleTime: idleTime
                });
            } else if (idleTime < IDLE_THRESHOLD && !this.isActive) {
                this.isActive = true;
                this.sendToBackground({
                    type: 'USER_ACTIVE'
                });
            }
        }

        notifyActivity() {
            // Debounce activity notifications
            if (this.activityTimeout) {
                clearTimeout(this.activityTimeout);
            }
            
            this.activityTimeout = setTimeout(() => {
                this.sendToBackground({
                    type: 'USER_ACTIVITY',
                    timestamp: this.lastActivity
                });
            }, 1000);
        }

        handleMessage(message, sender, sendResponse) {
            switch (message.type) {
                case 'PING':
                    sendResponse({ success: true, lastActivity: this.lastActivity });
                    break;
                    
                case 'GET_ACTIVITY_STATUS':
                    const idleTime = Date.now() - this.lastActivity;
                    sendResponse({
                        success: true,
                        lastActivity: this.lastActivity,
                        idleTime: idleTime,
                        isActive: idleTime < 5 * 60 * 1000
                    });
                    break;
                    
                default:
                    sendResponse({ success: false, error: 'Unknown message type' });
            }
            
            return true;
        }

        sendToBackground(message) {
            try {
                chrome.runtime.sendMessage(message).catch(() => {
                    // Background may not be available - ignore
                });
            } catch (error) {
                // Extension context may be invalidated
            }
        }
    }

    // Initialize content script
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', () => new RedballContentScript());
    } else {
        new RedballContentScript();
    }
})();
