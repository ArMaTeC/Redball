// Redball Browser Extension - Background Service Worker
// Handles communication with desktop app and manages keep-awake state

const REDBALL_API_URL = 'http://localhost:5000';
const CHECK_INTERVAL_MS = 30000; // Check connection every 30 seconds

class RedballBackgroundService {
    constructor() {
        this.isConnected = false;
        this.desktopState = null;
        this.keepAliveInterval = null;
        this.init();
    }

    async init() {
        console.log('[Redball Extension] Background service initializing...');
        
        // Set up alarm for periodic checks
        chrome.alarms.create('checkConnection', { periodInMinutes: 0.5 });
        chrome.alarms.create('keepAlive', { periodInMinutes: 1 });
        
        // Event listeners
        chrome.alarms.onAlarm.addListener((alarm) => this.handleAlarm(alarm));
        chrome.runtime.onMessage.addListener((message, sender, sendResponse) => 
            this.handleMessage(message, sender, sendResponse));
        
        // Initial connection check
        await this.checkConnection();
        
        console.log('[Redball Extension] Background service initialized');
    }

    async handleAlarm(alarm) {
        switch (alarm.name) {
            case 'checkConnection':
                await this.checkConnection();
                break;
            case 'keepAlive':
                await this.performKeepAlive();
                break;
        }
    }

    async handleMessage(message, sender, sendResponse) {
        console.log('[Redball Extension] Received message:', message);
        
        switch (message.type) {
            case 'GET_STATUS':
                const status = await this.getStatus();
                sendResponse({ success: true, data: status });
                break;
                
            case 'TOGGLE_KEEP_AWAKE':
                const result = await this.toggleKeepAwake();
                sendResponse({ success: true, data: result });
                break;
                
            case 'START_TIMED':
                const timedResult = await this.startTimed(message.minutes);
                sendResponse({ success: true, data: timedResult });
                break;
                
            case 'STOP_KEEP_AWAKE':
                const stopResult = await this.stopKeepAwake();
                sendResponse({ success: true, data: stopResult });
                break;
                
            default:
                sendResponse({ success: false, error: 'Unknown message type' });
        }
        
        return true; // Keep channel open for async
    }

    async checkConnection() {
        try {
            const response = await fetch(`${REDBALL_API_URL}/api/status`, {
                method: 'GET',
                headers: { 'Content-Type': 'application/json' }
            });
            
            if (response.ok) {
                const data = await response.json();
                this.isConnected = true;
                this.desktopState = data;
                
                // Update extension icon based on state
                this.updateIcon(data.isActive);
                
                console.log('[Redball Extension] Connected to desktop app');
            } else {
                this.isConnected = false;
                this.updateIcon(false);
            }
        } catch (error) {
            this.isConnected = false;
            this.updateIcon(false);
            console.log('[Redball Extension] Desktop app not available');
        }
    }

    async performKeepAlive() {
        // Only perform web-based keep-alive if desktop app is not running
        if (!this.isConnected) {
            // Web-based keep-alive could be implemented here
            // For now, we rely on the desktop app
        }
    }

    async getStatus() {
        if (!this.isConnected) {
            return { connected: false, error: 'Desktop app not running' };
        }
        
        try {
            const response = await fetch(`${REDBALL_API_URL}/api/status`);
            const data = await response.json();
            return { connected: true, ...data };
        } catch (error) {
            return { connected: false, error: error.message };
        }
    }

    async toggleKeepAwake() {
        if (!this.isConnected) {
            return { success: false, error: 'Desktop app not running' };
        }
        
        try {
            const response = await fetch(`${REDBALL_API_URL}/api/toggle`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            
            const data = await response.json();
            this.updateIcon(data.isActive);
            return { success: true, ...data };
        } catch (error) {
            return { success: false, error: error.message };
        }
    }

    async startTimed(minutes) {
        if (!this.isConnected) {
            return { success: false, error: 'Desktop app not running' };
        }
        
        try {
            const response = await fetch(`${REDBALL_API_URL}/api/timed`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ minutes })
            });
            
            const data = await response.json();
            return { success: true, ...data };
        } catch (error) {
            return { success: false, error: error.message };
        }
    }

    async stopKeepAwake() {
        if (!this.isConnected) {
            return { success: false, error: 'Desktop app not running' };
        }
        
        try {
            const response = await fetch(`${REDBALL_API_URL}/api/stop`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });
            
            const data = await response.json();
            this.updateIcon(false);
            return { success: true, ...data };
        } catch (error) {
            return { success: false, error: error.message };
        }
    }

    updateIcon(isActive) {
        const iconPath = isActive ? 'icons/active/' : 'icons/inactive/';
        
        chrome.action.setIcon({
            path: {
                '16': `${iconPath}icon16.png`,
                '32': `${iconPath}icon32.png`,
                '48': `${iconPath}icon48.png`,
                '128': `${iconPath}icon128.png`
            }
        });
        
        chrome.action.setBadgeText({
            text: isActive ? 'ON' : ''
        });
        
        chrome.action.setBadgeBackgroundColor({
            color: isActive ? '#00AA00' : '#888888'
        });
    }
}

// Initialize the background service
const redballService = new RedballBackgroundService();

// Handle extension installation
chrome.runtime.onInstalled.addListener((details) => {
    console.log('[Redball Extension] Installed:', details.reason);
    
    if (details.reason === 'install') {
        // First install - show welcome notification
        chrome.notifications.create({
            type: 'basic',
            iconUrl: 'icons/icon48.png',
            title: 'Redball Extension Installed',
            message: 'Click the extension icon to connect with your desktop Redball app.'
        });
    }
});
