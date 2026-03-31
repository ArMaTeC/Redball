// Redball Extension Popup Script
// Handles UI interactions and communicates with background service

document.addEventListener('DOMContentLoaded', async () => {
    const ui = {
        loadingPanel: document.getElementById('loading-panel'),
        connectedPanel: document.getElementById('connected-panel'),
        disconnectedPanel: document.getElementById('disconnected-panel'),
        connectionStatus: document.getElementById('connection-status'),
        keepAwakeStatus: document.getElementById('keep-awake-status'),
        sessionTime: document.getElementById('session-time'),
        toggleBtn: document.getElementById('toggle-btn'),
        timerBtns: document.querySelectorAll('.timer-btn'),
        retryBtn: document.getElementById('retry-btn'),
        heartbeatMode: document.getElementById('heartbeat-mode'),
        displayStatus: document.getElementById('display-status'),
        batteryStatus: document.getElementById('battery-status')
    };

    let statusCheckInterval = null;

    // Initialize
    async function init() {
        showPanel('loading');
        await checkStatus();
        
        // Set up periodic status checks
        statusCheckInterval = setInterval(checkStatus, 5000);
    }

    // Check connection and status
    async function checkStatus() {
        try {
            const response = await sendMessage({ type: 'GET_STATUS' });
            
            if (response.success && response.data.connected) {
                showPanel('connected');
                updateConnectionStatus(true);
                updateUI(response.data);
            } else {
                showPanel('disconnected');
                updateConnectionStatus(false);
            }
        } catch (error) {
            showPanel('disconnected');
            updateConnectionStatus(false);
        }
    }

    // Update connection status indicator
    function updateConnectionStatus(connected) {
        ui.connectionStatus.className = `status ${connected ? 'connected' : 'disconnected'}`;
        ui.connectionStatus.title = connected ? 'Connected to desktop app' : 'Desktop app not running';
    }

    // Update UI with status data
    function updateUI(data) {
        // Update keep-awake status
        const isActive = data.isActive || false;
        ui.keepAwakeStatus.className = `status-indicator ${isActive ? 'active' : ''}`;
        ui.keepAwakeStatus.querySelector('.status-text').textContent = isActive ? 'Active' : 'Inactive';
        
        // Update session time
        if (isActive && data.sessionDuration) {
            ui.sessionTime.textContent = formatDuration(data.sessionDuration);
            ui.sessionTime.classList.add('active');
        } else {
            ui.sessionTime.textContent = '--:--';
            ui.sessionTime.classList.remove('active');
        }
        
        // Update toggle button
        updateToggleButton(isActive);
        
        // Update info section if available
        if (data.heartbeatMode) {
            ui.heartbeatMode.textContent = data.heartbeatMode;
        }
        if (data.preventDisplaySleep !== undefined) {
            ui.displayStatus.textContent = data.preventDisplaySleep ? 'Prevented' : 'Allowed';
        }
        if (data.batteryAware !== undefined) {
            ui.batteryStatus.textContent = data.batteryAware ? 'Enabled' : 'Disabled';
        }
    }

    // Update toggle button appearance
    function updateToggleButton(isActive) {
        const btn = ui.toggleBtn;
        if (isActive) {
            btn.innerHTML = '<span class="btn-icon">⏹</span><span class="btn-text">Stop Keep-Awake</span>';
            btn.classList.remove('btn-primary');
            btn.classList.add('btn-secondary');
        } else {
            btn.innerHTML = '<span class="btn-icon">🚀</span><span class="btn-text">Start Keep-Awake</span>';
            btn.classList.remove('btn-secondary');
            btn.classList.add('btn-primary');
        }
    }

    // Show appropriate panel
    function showPanel(panelName) {
        ui.loadingPanel.classList.add('hidden');
        ui.connectedPanel.classList.add('hidden');
        ui.disconnectedPanel.classList.add('hidden');
        
        switch (panelName) {
            case 'loading':
                ui.loadingPanel.classList.remove('hidden');
                break;
            case 'connected':
                ui.connectedPanel.classList.remove('hidden');
                break;
            case 'disconnected':
                ui.disconnectedPanel.classList.remove('hidden');
                break;
        }
    }

    // Format duration as MM:SS or HH:MM:SS
    function formatDuration(totalSeconds) {
        const hours = Math.floor(totalSeconds / 3600);
        const minutes = Math.floor((totalSeconds % 3600) / 60);
        const seconds = totalSeconds % 60;
        
        if (hours > 0) {
            return `${hours}:${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}`;
        }
        return `${minutes}:${seconds.toString().padStart(2, '0')}`;
    }

    // Send message to background script
    function sendMessage(message) {
        return new Promise((resolve, reject) => {
            chrome.runtime.sendMessage(message, (response) => {
                if (chrome.runtime.lastError) {
                    reject(chrome.runtime.lastError);
                } else {
                    resolve(response);
                }
            });
        });
    }

    // Event listeners
    ui.toggleBtn.addEventListener('click', async () => {
        try {
            ui.toggleBtn.disabled = true;
            const response = await sendMessage({ type: 'TOGGLE_KEEP_AWAKE' });
            
            if (response.success) {
                updateUI(response.data);
            }
        } catch (error) {
            console.error('Toggle failed:', error);
        } finally {
            ui.toggleBtn.disabled = false;
        }
    });

    ui.timerBtns.forEach(btn => {
        btn.addEventListener('click', async () => {
            const minutes = parseInt(btn.dataset.minutes);
            try {
                btn.disabled = true;
                const response = await sendMessage({ 
                    type: 'START_TIMED', 
                    minutes 
                });
                
                if (response.success) {
                    updateUI(response.data);
                }
            } catch (error) {
                console.error('Timed start failed:', error);
            } finally {
                btn.disabled = false;
            }
        });
    });

    ui.retryBtn.addEventListener('click', async () => {
        showPanel('loading');
        await checkStatus();
    });

    // Settings and help links
    document.getElementById('settings-link').addEventListener('click', (e) => {
        e.preventDefault();
        chrome.runtime.openOptionsPage?.() || window.open('options.html');
    });

    document.getElementById('help-link').addEventListener('click', (e) => {
        e.preventDefault();
        chrome.tabs.create({ url: 'https://github.com/ArMaTeC/Redball/wiki' });
    });

    // Cleanup on unload
    window.addEventListener('unload', () => {
        if (statusCheckInterval) {
            clearInterval(statusCheckInterval);
        }
    });

    // Start
    init();
});
