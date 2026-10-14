let socket;
let token = localStorage.getItem('redball_admin_token');
let buildRunning = false;
let liveStage = null;
let timerInterval;

// --- Toast Notifications ---
function showToast(message, type = 'info') {
    const toast = document.createElement('div');
    toast.style.cssText = `
        position: fixed;
        bottom: 24px;
        right: 24px;
        padding: 14px 24px;
        border-radius: 10px;
        font-weight: 500;
        z-index: 10000;
        animation: slideIn 0.3s ease;
        backdrop-filter: blur(10px);
        border: 1px solid rgba(148, 163, 184, 0.12);
        max-width: 320px;
        word-wrap: break-word;
    `;
    if (type === 'success') {
        toast.style.background = 'rgba(16, 185, 129, 0.15)';
        toast.style.color = '#34d399';
        toast.style.borderColor = 'rgba(16, 185, 129, 0.3)';
    } else if (type === 'error') {
        toast.style.background = 'rgba(239, 68, 68, 0.15)';
        toast.style.color = '#f87171';
        toast.style.borderColor = 'rgba(239, 68, 68, 0.3)';
    } else {
        toast.style.background = 'rgba(45, 127, 249, 0.15)';
        toast.style.color = '#5a9dff';
        toast.style.borderColor = 'rgba(45, 127, 249, 0.3)';
    }
    toast.textContent = message;
    document.body.appendChild(toast);
    setTimeout(() => {
        toast.style.animation = 'slideOut 0.3s ease forwards';
        setTimeout(() => toast.remove(), 300);
    }, 3000);
}

// Add slide animations
const style = document.createElement('style');
style.textContent = `
    @keyframes slideIn {
        from { transform: translateX(400px); opacity: 0; }
        to { transform: translateX(0); opacity: 1; }
    }
    @keyframes slideOut {
        from { transform: translateX(0); opacity: 1; }
        to { transform: translateX(400px); opacity: 0; }
    }
`;
document.head.appendChild(style);

// --- Sections & Deep Linking ---
const SECTION_LOADERS = {
    dashboard: fetchStats,
    releases: fetchStats,
    config: loadConfiguration,
    users: loadUsers,
    analytics: loadAnalytics
};

function activateNavButton(id, btn) {
    const target = btn || document.querySelector(`.nav-btn[data-section="${id}"]`);
    if (target) target.classList.add('active');
}

function showSection(id, btn, pushState = true) {
    const section = document.getElementById(id + '-section');
    if (!section) return;

    document.querySelectorAll('.section').forEach(s => s.classList.remove('active'));
    document.querySelectorAll('.nav-btn').forEach(b => b.classList.remove('active'));

    section.classList.add('active');
    activateNavButton(id, btn);

    if (pushState) {
        const url = new URL(window.location);
        url.searchParams.set('tab', id);
        window.history.pushState({}, '', url);
    }

    SECTION_LOADERS[id]?.();
    resetIdleTimer();
}

window.onpopstate = () => {
    const urlParams = new URLSearchParams(window.location.search);
    const tab = urlParams.get('tab') || 'dashboard';
    showSection(tab, null, false);
};

// --- Activity Tracking & Live Polling ---
let lastActivity = Date.now();
let isIdle = false;
let pollInterval = null;
const IDLE_THRESHOLD = 5 * 60 * 1000; // 5 minutes
const POLL_INTERVAL = 15 * 1000; // 15 seconds

function resetIdleTimer() {
    lastActivity = Date.now();
    if (isIdle) {
        isIdle = false;
        console.log('[System] Activity detected. Resuming telemetry.');
        startPolling();
        showToast('Activity detected. Live updates resumed.', 'info');
    }
}

function startPolling() {
    if (pollInterval) clearInterval(pollInterval);
    pollInterval = setInterval(() => {
        if (Date.now() - lastActivity > IDLE_THRESHOLD) {
            isIdle = true;
            clearInterval(pollInterval);
            pollInterval = null;
            console.log('[System] Idle detected. Pausing telemetry.');
            showToast('System idle. Pausing live updates.', 'info');
            return;
        }

        // Selective polling based on active tab
        const active = document.querySelector('.section.active');
        if (!active) return;

        const id = active.id.replace('-section', '');
        if (id === 'dashboard' || id === 'releases') fetchStats();
        if (id === 'analytics') loadAnalytics();
        if (id === 'build' && buildRunning) fetchStatus();
    }, POLL_INTERVAL);
}

// Listen for user interaction
['mousedown', 'keydown', 'scroll', 'touchstart'].forEach(type => {
    document.addEventListener(type, resetIdleTimer, { passive: true });
});

// --- Configuration ---
let currentConfig = {};
async function loadConfiguration() {
    try {
        const res = await fetch('/api/config', { headers: { 'Authorization': 'Bearer ' + token } });
        if (res.ok) {
            currentConfig = await res.json();
            renderConfig();
        }
    } catch (e) { console.error(e); }
}

// Template helpers — keep the ternaries out of renderConfig
const sel = (cond) => cond ? 'selected' : '';
const checked = (cond) => cond ? 'checked' : '';

function renderConfig() {
    const root = document.getElementById('config-root');
    root.innerHTML = `
        <div class="config-group">
            <div class="group-header">
                <div class="group-icon">⚡</div>
                <div class="group-title">Pulse & Power</div>
            </div>
            <div class="config-row">
                <div class="config-field">
                    <label>Heartbeat Sync (Seconds)</label>
                    <input type="number" id="cfg-HeartbeatSeconds" value="${escapeHtml(currentConfig.HeartbeatSeconds)}">
                </div>
                <div class="config-field">
                    <label>Default Op Duration (Min)</label>
                    <input type="number" id="cfg-DefaultDuration" value="${escapeHtml(currentConfig.DefaultDuration)}">
                </div>
            </div>
            <div class="config-field">
                <label>Heartbeat Input Mode</label>
                <select id="cfg-HeartbeatInputMode">
                    <option value="F15" ${sel(currentConfig.HeartbeatInputMode === 'F15')}>F15 (Ghost Key)</option>
                    <option value="F24" ${sel(currentConfig.HeartbeatInputMode === 'F24')}>F24 (Phantom Key)</option>
                    <option value="Mouse" ${sel(currentConfig.HeartbeatInputMode === 'Mouse')}>Mouse Jiggle</option>
                </select>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-PreventDisplaySleep" ${checked(currentConfig.PreventDisplaySleep)}>
                    <span>Prevent System Hibernation</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-UseHeartbeatKeypress" ${checked(currentConfig.UseHeartbeatKeypress)}>
                    <span>Simulate User Activity</span>
                </label>
            </div>
        </div>

        <div class="config-group">
            <div class="group-header">
                <div class="group-icon">🧠</div>
                <div class="group-title">Intelligence Engine</div>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-TypeThingEnabled" ${checked(currentConfig.TypeThingEnabled)}>
                    <span>Enable Virtual Input Logic</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-TypeThingNotifications" ${checked(currentConfig.TypeThingNotifications)}>
                    <span>Virtual Input Notifications</span>
                </label>
            </div>
            <div class="config-row">
                <div class="config-field">
                    <label>Min Key Delay (ms)</label>
                    <input type="number" id="cfg-TypeThingMinDelayMs" value="${escapeHtml(currentConfig.TypeThingMinDelayMs)}">
                </div>
                <div class="config-field">
                    <label>Max Key Delay (ms)</label>
                    <input type="number" id="cfg-TypeThingMaxDelayMs" value="${escapeHtml(currentConfig.TypeThingMaxDelayMs)}">
                </div>
            </div>
            <div class="config-field">
                <label>Input Theme</label>
                <select id="cfg-TypeThingTheme">
                    <option value="dark" ${sel(currentConfig.TypeThingTheme === 'dark')}>Dark</option>
                    <option value="light" ${sel(currentConfig.TypeThingTheme === 'light')}>Light</option>
                </select>
            </div>
            <hr style="border: 1px solid var(--border); margin: 0.5rem 0;">
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-IdleDetection" ${checked(currentConfig.IdleDetection)}>
                    <span>Automatic Idle Migration</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-BatteryAware" ${checked(currentConfig.BatteryAware)}>
                    <span>Battery Mode Awareness</span>
                </label>
            </div>
            <div class="config-row">
                <div class="config-field">
                    <label>Idle Threshold (Sec)</label>
                    <input type="number" id="cfg-IdleThreshold" value="${escapeHtml(currentConfig.IdleThreshold)}">
                </div>
                <div class="config-field">
                    <label>Battery Threshold (%)</label>
                    <input type="number" id="cfg-BatteryThreshold" value="${escapeHtml(currentConfig.BatteryThreshold)}">
                </div>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-NetworkAware" ${checked(currentConfig.NetworkAware)}>
                    <span>Network Context Awareness</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-PresentationModeDetection" ${checked(currentConfig.PresentationModeDetection)}>
                    <span>Suppress in Presentation Mode</span>
                </label>
            </div>
        </div>

        <div class="config-group">
            <div class="group-header">
                <div class="group-icon">🌐</div>
                <div class="group-title">System Infrastructure</div>
            </div>
            <div class="config-row">
                <div class="config-field">
                    <label>GitHub Repository Owner</label>
                    <input type="text" id="cfg-UpdateRepoOwner" value="${escapeHtml(currentConfig.UpdateRepoOwner)}">
                </div>
                <div class="config-field">
                    <label>GitHub Repository Name</label>
                    <input type="text" id="cfg-UpdateRepoName" value="${escapeHtml(currentConfig.UpdateRepoName)}">
                </div>
            </div>
            <div class="config-field">
                <label>Update Channel</label>
                <select id="cfg-UpdateChannel">
                    <option value="stable" ${sel(currentConfig.UpdateChannel === 'stable')}>Stable (Production)</option>
                    <option value="beta" ${sel(currentConfig.UpdateChannel === 'beta')}>Beta (Preview)</option>
                    <option value="dev" ${sel(currentConfig.UpdateChannel === 'dev')}>Development</option>
                </select>
            </div>
            <label class="cb-field">
                <input type="checkbox" id="cfg-VerifyUpdateSignature" ${checked(currentConfig.VerifyUpdateSignature)}>
                <span>Strict Binary Signature Verification</span>
            </label>
        </div>

        <div class="config-group">
            <div class="group-header">
                <div class="group-icon">🖥️</div>
                <div class="group-title">Client Interface</div>
            </div>
            <div class="config-row">
                <div class="config-field">
                    <label>System Theme</label>
                    <select id="cfg-Theme">
                        <option value="System" ${sel(currentConfig.Theme === 'System')}>System Default</option>
                        <option value="Dark" ${sel(currentConfig.Theme === 'Dark')}>Dark Mode</option>
                        <option value="Light" ${sel(currentConfig.Theme === 'Light')}>Light Mode</option>
                    </select>
                </div>
                <div class="config-field">
                    <label>Locale</label>
                    <input type="text" id="cfg-Locale" value="${escapeHtml(currentConfig.Locale)}">
                </div>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-MinimizeToTray" ${checked(currentConfig.MinimizeToTray)}>
                    <span>Minimize to Tray</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-MinimizeOnStart" ${checked(currentConfig.MinimizeOnStart)}>
                    <span>Minimize on Start</span>
                </label>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-ShowNotifications" ${checked(currentConfig.ShowNotifications)}>
                    <span>Show Notifications</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-ShowBalloonOnStart" ${checked(currentConfig.ShowBalloonOnStart)}>
                    <span>Show Balloon on Start</span>
                </label>
            </div>
        </div>

        <div class="config-group">
            <div class="group-header">
                <div class="group-icon">📊</div>
                <div class="group-title">Telemetry & Diagnostics</div>
            </div>
            <div class="config-row">
                <label class="cb-field">
                    <input type="checkbox" id="cfg-VerboseLogging" ${checked(currentConfig.VerboseLogging)}>
                    <span>Verbose Logging</span>
                </label>
                <label class="cb-field">
                    <input type="checkbox" id="cfg-EnableTelemetry" ${checked(currentConfig.EnableTelemetry)}>
                    <span>Enable Telemetry</span>
                </label>
            </div>
            <label class="cb-field">
                <input type="checkbox" id="cfg-EnablePerformanceMetrics" ${checked(currentConfig.EnablePerformanceMetrics)}>
                <span>Collect Performance Metrics</span>
            </label>
            <div class="config-field">
                <label>Max Log Size (MB)</label>
                <input type="number" id="cfg-MaxLogSizeMB" value="${escapeHtml(currentConfig.MaxLogSizeMB)}">
            </div>
        </div>
    `;
}

async function saveConfiguration() {
    const updates = {};
    const inputs = document.querySelectorAll('[id^="cfg-"]');
    inputs.forEach(input => {
        const key = input.id.replace('cfg-', '');
        if (input.type === 'checkbox') updates[key] = input.checked;
        else if (input.type === 'number') updates[key] = parseInt(input.value);
        else updates[key] = input.value;
    });

    try {
        const res = await fetch('/api/config', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify(updates)
        });
        if (res.ok) {
            showToast('Configuration synchronized successfully', 'success');
            loadConfiguration();
        } else {
            showToast('Failed to synchronize configuration', 'error');
        }
    } catch (e) {
        console.error(e);
        showToast('Network error during synchronization', 'error');
    }
}

// --- Auth ---
function checkAuth() {
    if (!token) {
        document.getElementById('login-overlay').classList.remove('hidden');
    } else {
        document.getElementById('login-overlay').classList.add('hidden');
        initSocket();
        startPolling();

        // Handle deep linking on startup
        const urlParams = new URLSearchParams(window.location.search);
        const tab = urlParams.get('tab') || 'dashboard';
        showSection(tab, null, false);
    }
}

async function handleLogin() {
    const user = document.getElementById('username').value;
    const pass = document.getElementById('password').value;
    const errEl = document.getElementById('login-error');
    errEl.classList.remove('show');

    try {
        const tdt = localStorage.getItem('redball_tdt_' + user);
        const res = await fetch('/api/auth/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: user, password: pass, tdt })
        });
        const data = await res.json();

        if (res.ok) {
            if (data.mfaRequired) {
                // Switch to MFA view
                tempToken = data.tempToken;
                document.getElementById('login-fields').classList.add('hidden');
                document.getElementById('mfa-fields').classList.remove('hidden');
            } else {
                loginSuccess(data);
            }
        } else {
            errEl.innerText = data.error || 'Invalid authorization key';
            errEl.classList.add('show');
        }
    } catch (e) {
        console.error(e);
        errEl.innerText = 'Network error';
        errEl.classList.add('show');
    }
}

async function handleMfaLogin() {
    const code = document.getElementById('mfa-token').value;
    const errEl = document.getElementById('login-error');

    try {
        const remember = document.getElementById('mfa-remember').checked;
        const res = await fetch('/api/auth/mfa/verify', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + tempToken,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ token: code, rememberDevice: remember })
        });
        const data = await res.json();

        if (res.ok) {
            if (data.tdt) {
                localStorage.setItem('redball_tdt_' + data.user.username, data.tdt);
            }
            loginSuccess(data);
        } else {
            errEl.innerText = data.error || 'Invalid MFA code';
            errEl.classList.add('show');
        }
    } catch (e) {
        console.error(e);
    }
}

function loginSuccess(data) {
    token = data.token;
    currentUser = data.user;
    localStorage.setItem('redball_admin_token', token);
    document.getElementById('login-overlay').classList.add('hidden');
    initSocket();
    fetchStats();
    showToast(`Welcome back, ${currentUser.username}`, 'success');
}

function cancelMfa() {
    tempToken = null;
    document.getElementById('mfa-fields').classList.add('hidden');
    document.getElementById('login-fields').classList.remove('hidden');
    document.getElementById('login-error').classList.remove('show');
}

function handleLogout() {
    localStorage.removeItem('redball_admin_token');
    location.reload();
}

// --- Stats ---
async function fetchStats() {
    try {
        const res = await fetch('/api/stats', {
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (res.ok) {
            const data = await res.json();

            // Animate stat value updates
            const downloadsEl = document.getElementById('stat-downloads');
            const versionEl = document.getElementById('stat-version');

            if (downloadsEl.innerText !== data.totalDownloads.toLocaleString()) {
                downloadsEl.classList.add('updating');
                downloadsEl.innerText = data.totalDownloads.toLocaleString();
                setTimeout(() => downloadsEl.classList.remove('updating'), 800);
            }

            if (versionEl.innerText !== 'v' + data.latestVersion) {
                versionEl.classList.add('updating');
                versionEl.innerText = 'v' + data.latestVersion;
                setTimeout(() => versionEl.classList.remove('updating'), 800);
            }

            const tbody = document.getElementById('releases-body');
            tbody.innerHTML = data.releases.map(r => `
                <tr>
                    <td><span class="badge badge-ver">v${escapeHtml(r.version)}</span></td>
                    <td>${new Date(r.date).toLocaleDateString()}</td>
                    <td><span class="badge badge-dl">${r.totalDownloads.toLocaleString()} dl</span></td>
                    <td><span style="color: var(--success)">PROD</span></td>
                </tr>
            `).join('');
        }
    } catch (e) {
        console.error(e);
    }
}

// --- Keyboard ---
document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && !document.getElementById('login-overlay').classList.contains('hidden')) {
        if (!document.getElementById('mfa-fields').classList.contains('hidden')) {
            handleMfaLogin();
        } else {
            handleLogin();
        }
    }
});

// --- Socket & Build ---
function initSocket() {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    socket = new WebSocket(`${protocol}//${location.host}/ws?token=${token}`);

    socket.onopen = () => {
        document.getElementById('connection-dot').className = 'connection-dot connected';
        document.getElementById('connection-text').innerText = 'Live Link Established';
        // Sync current state
        fetchStatus();
    };

    socket.onclose = () => {
        document.getElementById('connection-dot').className = 'connection-dot disconnected';
        document.getElementById('connection-text').innerText = 'Link Connection Lost';
        setTimeout(initSocket, 3000);
    };

    socket.onmessage = (e) => {
        const msg = JSON.parse(e.data);
        handleMessage(msg);
    };
}

async function fetchStatus() {
    const res = await fetch('/api/build/status', {
        headers: { 'Authorization': 'Bearer ' + token }
    });
    if (res.ok) {
        const status = await res.json();
        syncState(status);
    }
}

// Show the latest meaningful build line as the "current operation"
function updateCurrentOp(rawLine) {
    const cleanLine = cleanAnsi(rawLine).trim();
    if (cleanLine && !cleanLine.startsWith('[') && cleanLine.length < 100 && !/^\([0-9.]+s\)$/.test(cleanLine)) {
        document.getElementById('current-op').innerText = cleanLine;
    }
}

function onBuildOutput(data) {
    appendLog(data.line);
    updatePipeline(data.stage, data.progress, buildRunning ? 'running' : 'idle', data.error);
    updateCurrentOp(data.line);
}

function onBuildOutputBatch(data) {
    // Server batches output bursts every ~100ms; render each line and
    // apply the stage/progress carried by the batch's last message
    (data.lines || []).forEach(l => appendLog(l));
    updatePipeline(data.stage, data.progress, buildRunning ? 'running' : 'idle', data.error);
    updateCurrentOp(data.lines?.[data.lines.length - 1] || '');
}

function onBuildComplete(data) {
    syncState({ status: data.status, progress: 100, stage: 'complete' });
    // Render the terminal state of the build that just ran this session
    updatePipeline(data.status === 'failed' ? liveStage : 'complete', 100, data.status);
    stopTimer();
    fetchStats(); // Update stats grid
}

const MESSAGE_HANDLERS = {
    'state': (d) => syncState(d),           // Initial state on WebSocket connect
    'build-started': (d) => syncState(d),
    'build-output': onBuildOutput,
    'build-output-batch': onBuildOutputBatch,
    'build-complete': onBuildComplete,
    'build-stopped': (d) => { syncState(d); stopTimer(); }
};

function handleMessage(msg) {
    MESSAGE_HANDLERS[msg.type]?.(msg.data);
}

function updateLastStatus(status) {
    const el = document.getElementById('last-status');
    if (status === 'success') {
        el.innerText = 'SUCCESS';
        el.style.color = 'var(--success)';
    } else if (status === 'failed') {
        el.innerText = 'FAILED';
        el.style.color = 'var(--danger)';
    }
}

function syncState(state) {
    const isRunning = state.status === 'running';
    buildRunning = isRunning;

    document.getElementById('stat-active').innerText = isRunning ? '1' : '0';
    document.getElementById('start-btn').disabled = isRunning;
    document.getElementById('stop-btn').disabled = !isRunning;

    document.getElementById('initial-controls').style.display = isRunning ? 'none' : 'block';
    document.getElementById('active-controls').style.display = isRunning ? 'block' : 'none';

    // Pipeline only reflects a live run; stale/persisted state stays neutral
    if (isRunning && state.stage) {
        liveStage = state.stage;
        updatePipeline(state.stage, state.progress, state.status, state.error);
    } else {
        resetPipeline();
    }
    updateLastStatus(state.status);
    syncTimerAndTerminal(state, isRunning);
}

function syncTimerAndTerminal(state, isRunning) {
    if (isRunning) {
        if (state.log) {
            const terminal = document.getElementById('terminal-out');
            terminal.innerHTML = '';
            state.log.forEach(l => appendLog(l.message));
        }
        startTimer(state.startTime);
    } else {
        stopTimer();
        if (state.stage === 'complete' || state.status === 'success') {
            document.getElementById('current-op').innerText = 'System idle. Readiness 100%';
        }
    }
}

function resetPipeline() {
    document.querySelectorAll('.pipeline-step').forEach(s => s.classList.remove('active', 'complete', 'error'));
    document.getElementById('progress-percent').innerText = '0%';
    const fill = document.getElementById('progress-fill');
    fill.style.width = '0%';
    fill.classList.remove('running');
    fill.style.background = 'linear-gradient(90deg, var(--accent), #10b981)';
    document.getElementById('pipeline-progress').style.width = '0%';
}

function updatePipeline(stage, progress, status, hasError) {
    const steps = Array.from(document.querySelectorAll('.pipeline-step'));
    const stageIndex = steps.findIndex(s => s.dataset.stage === stage);

    document.getElementById('progress-percent').innerText = Math.round(progress) + '%';
    const fill = document.getElementById('progress-fill');
    fill.style.width = progress + '%';
    document.getElementById('pipeline-progress').style.width = progress + '%';

    // Add/remove running class for liquid wave animation
    if (status === 'running') {
        fill.classList.add('running');
    } else {
        fill.classList.remove('running');
    }

    if (status === 'failed') {
        fill.style.background = 'var(--danger)';
    } else {
        fill.style.background = 'linear-gradient(90deg, var(--accent), #10b981)';
    }

    steps.forEach((step, i) => applyStepState(step, i, stageIndex, stage, status, progress, hasError));
}

function applyStepState(step, i, stageIndex, stage, status, progress, hasError) {
    step.classList.remove('active', 'complete', 'error');

    if (stage === 'complete' && status !== 'failed') {
        step.classList.add('complete');
        return;
    }

    // Unknown stage (e.g. stale persisted state) -> leave all steps neutral
    if (stageIndex === -1) return;

    if (i < stageIndex) {
        step.classList.add('complete');
    } else if (i === stageIndex) {
        applyCurrentStepState(step, status, progress, hasError);
    }
}

function applyCurrentStepState(step, status, progress, hasError) {
    if (status === 'failed' || (hasError && status !== 'success')) {
        step.classList.add('error');
    } else if (status === 'success' || progress === 100) {
        step.classList.add('complete');
    } else {
        step.classList.add('active');
    }
}

function cleanAnsi(text) {
    if (typeof text !== 'string') return text;
    return text
        .replace(/[\u001b\u009b][[()#;?]*(?:[0-9]{1,4}(?:;[0-9]{0,4})*)?[0-9A-ORZcf-nqry=><]/g, '') // CSI
        .replace(/\u001b\].*?(\u001b\\|\x07)/g, '') // OSC
        .replace(/\u001b[PX^_].*?\u001b\\/g, '') // DCS, SOS, PM, APC
        .replace(/\u001b./g, '') // Other Esc
        .replace(/[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]/g, ''); // Control chars
}

// Escape a value for safe interpolation into HTML markup or attributes
function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// Filter whole-line ANSI fragments only — a prefix like [STEP] must survive
function isNoiseLine(cleanLine) {
    return !cleanLine || /^\([0-9.]+s\)$/.test(cleanLine) || /^\[[0-9;?]+[a-zA-Z]$/.test(cleanLine);
}

function classifyLogLine(cleanLine) {
    if (cleanLine.includes('ERROR') || cleanLine.includes('failed')) return ' error';
    if (cleanLine.includes('OK') || cleanLine.includes('success')) return ' success';
    if (cleanLine.includes('Phase')) return ' phase';
    if (cleanLine.startsWith('↳') || cleanLine.startsWith('  [')) return ' dim';
    return '';
}

function appendLog(line) {
    const terminal = document.getElementById('terminal-out');
    const div = document.createElement('div');

    // Comprehensive ANSI cleaning
    const cleanLine = cleanAnsi(line).trim();
    if (isNoiseLine(cleanLine)) return;

    div.className = 't-line new' + classifyLogLine(cleanLine);

    const time = new Date().toLocaleTimeString('en-GB', { hour12: false });
    div.innerHTML = `<span class="t-line time">[${time}]</span>${escapeHtml(cleanLine)}`;

    terminal.appendChild(div);
    // Cap terminal size so a long build can't grow the DOM unboundedly
    while (terminal.childElementCount > 2000) terminal.removeChild(terminal.firstElementChild);
    terminal.scrollTop = terminal.scrollHeight;

    // Remove animation class after animation completes
    setTimeout(() => div.classList.remove('new'), 500);
}

async function startBuild() {
    document.getElementById('terminal-out').innerHTML = '';
    fetch('/api/build/start', {
        method: 'POST',
        headers: { 'Authorization': 'Bearer ' + token }
    });
}

async function stopBuild() {
    fetch('/api/build/stop', {
        method: 'POST',
        headers: { 'Authorization': 'Bearer ' + token }
    });
}

function startTimer(startTime) {
    clearInterval(timerInterval);
    const start = startTime || Date.now();
    timerInterval = setInterval(() => {
        const elapsed = Date.now() - start;
        const m = Math.floor(elapsed / 60000);
        const s = Math.floor((elapsed % 60000) / 1000);
        document.getElementById('build-timer').innerText = `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
    }, 1000);
}

function stopTimer() {
    clearInterval(timerInterval);
}

// --- Identity & Access ---
async function loadUsers() {
    try {
        const res = await fetch('/api/auth/users', { headers: { 'Authorization': 'Bearer ' + token } });
        if (res.ok) {
            const users = await res.json();
            console.log('[Users] Loaded:', users.length);
            const tbody = document.getElementById('users-table-body');
            if (tbody) {
                tbody.innerHTML = users.map(u => `
                    <tr>
                        <td>
                            <div style="display: flex; align-items: center; gap: 0.8rem">
                                <div style="width: 32px; height: 32px; border-radius: 50%; background: var(--accent-glow); display: flex; align-items: center; justify-content: center; font-size: 0.8rem; font-weight: 800">
                                    ${escapeHtml(u.username[0].toUpperCase())}
                                </div>
                                <span>${escapeHtml(u.username)}</span>
                            </div>
                        </td>
                        <td><span class="badge" style="background: ${u.role === 'admin' ? 'rgba(0, 217, 163, 0.1)' : 'rgba(255,255,255,0.05)'}; color: ${u.role === 'admin' ? 'var(--accent)' : 'var(--text-secondary)'}">${u.role.toUpperCase()}</span></td>
                        <td><span class="badge" style="background: ${u.mfaEnabled ? 'rgba(16, 185, 129, 0.1)' : 'rgba(239, 68, 68, 0.1)'}; color: ${u.mfaEnabled ? 'var(--success)' : 'var(--error)'}">${u.mfaEnabled ? 'ENFORCED' : 'UNSECURED'}</span></td>
                        <td>${new Date(u.createdAt).toLocaleDateString()}</td>
                        <td>
                            <button class="btn btn-sm" data-action="delete-user" data-username="${escapeHtml(u.username)}" style="padding: 0.4rem; background: transparent; border-color: rgba(239, 68, 68, 0.3); color: var(--error); width: auto;">
                                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18M19 6v14a2 2 0 01-2 2H7a2 2 0 01-2-2V6m3 0V4a2 2 0 012-2h4a2 2 0 012 2v2"/></svg>
                            </button>
                        </td>
                    </tr>
                `).join('');
            }

            // Update current user MFA status in the side panel
            if (currentUser) {
                const self = users.find(u => u.username === currentUser.username);
                if (self) {
                    console.log('[MFA] Status for', self.username, ':', self.mfaEnabled);
                    const statusVal = document.getElementById('mfa-status-value');
                    const statusLbl = document.getElementById('mfa-status-label');
                    const statusBtn = document.getElementById('mfa-setup-btn');

                    if (self.mfaEnabled) {
                        statusVal.innerText = 'Active';
                        statusVal.style.color = 'var(--success)';
                        statusLbl.innerText = 'Security Verified';
                        statusBtn.innerText = 'Disable MFA Protocol';
                        statusBtn.onclick = disableMfa;
                    } else {
                        statusVal.innerText = 'Inactive';
                        statusVal.style.color = 'var(--error)';
                        statusLbl.innerText = 'Multi-Factor Status';
                        statusBtn.innerText = 'Setup MFA Protocol';
                        statusBtn.onclick = initMfaSetup;
                    }
                } else {
                    console.warn('[MFA] Current user not found in user list:', currentUser.username);
                }
            }
        } else {
            console.error('[Users] Failed to load:', res.status);
            if (res.status === 401 || res.status === 403) {
                showToast('Session expired or access denied', 'error');
            }
        }
    } catch (e) {
        console.error('[Users] Error:', e);
    }
}

let mfaSecret = null;
async function initMfaSetup() {
    try {
        const res = await fetch('/api/auth/mfa/setup', {
            method: 'POST',
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (res.ok) {
            const data = await res.json();
            mfaSecret = data.secret;
            document.getElementById('mfa-qr-img').src = data.qrCode;
            document.getElementById('mfa-qr-container').classList.remove('hidden');
            document.getElementById('mfa-setup-controls').classList.add('hidden');
            showToast('MFA Secret generated. Scan the QR code.', 'success');
        } else {
            const data = await res.json();
            showToast(data.error || 'Failed to initialize MFA setup', 'error');
        }
    } catch (e) {
        console.error('[MFA Setup Error]', e);
        showToast('Network error during MFA setup', 'error');
    }
}

function cancelMfaSetup() {
    document.getElementById('mfa-qr-container').classList.add('hidden');
    document.getElementById('mfa-setup-controls').classList.remove('hidden');
    mfaSecret = null;
}

async function verifyMfaSetup() {
    const code = document.getElementById('mfa-setup-token').value;
    try {
        const res = await fetch('/api/auth/mfa/verify-setup', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ secret: mfaSecret, token: code })
        });
        if (res.ok) {
            showToast('MFA enabled successfully', 'success');
            cancelMfaSetup();
            loadUsers();
        } else {
            showToast('Invalid MFA code', 'error');
        }
    } catch (e) {
        console.error(e);
    }
}

async function disableMfa() {
    if (!confirm('Are you sure you want to disable MFA? This will reduce your account security.')) return;
    const code = prompt('Enter current 6-digit MFA code to confirm:');
    if (!code) return;

    try {
        const res = await fetch('/api/auth/mfa/disable', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ token: code })
        });
        if (res.ok) {
            showToast('MFA disabled', 'success');
            loadUsers();
        } else {
            const data = await res.json();
            showToast(data.error || 'Failed to disable MFA', 'error');
        }
    } catch (e) {
        console.error(e);
    }
}

async function changePassword() {
    const currentPassword = prompt('Enter CURRENT access key:');
    if (!currentPassword) return;
    const newPassword = prompt('Enter NEW access key:');
    if (!newPassword) return;
    const confirmPass = prompt('Confirm NEW access key:');
    if (newPassword !== confirmPass) {
        showToast('Passwords do not match', 'error');
        return;
    }

    try {
        const res = await fetch('/api/auth/change-password', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ currentPassword, newPassword })
        });
        if (res.ok) {
            showToast('Access key updated successfully', 'success');
        } else {
            const data = await res.json();
            showToast(data.error || 'Failed to update access key', 'error');
        }
    } catch (e) {
        console.error(e);
        showToast('Network error', 'error');
    }
}

async function deleteUser(username) {
    if (username === currentUser.username) {
        showToast('Cannot delete self', 'error');
        return;
    }
    if (!confirm(`Are you sure you want to revoke access for ${username}?`)) return;
    try {
        const res = await fetch(`/api/auth/users/${encodeURIComponent(username)}`, {
            method: 'DELETE',
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (res.ok) {
            showToast('Identity revoked', 'success');
            loadUsers();
        }
    } catch (e) {
        console.error(e);
    }
}

function showAddUserModal() {
    const username = prompt('Enter username:');
    if (!username) return;
    const password = prompt('Enter temporary password:');
    if (!password) return;
    const role = prompt('Enter role (admin/viewer):', 'viewer');

    addUser(username, password, role);
}

async function addUser(username, password, role) {
    try {
        const res = await fetch('/api/auth/users', {
            method: 'POST',
            headers: {
                'Authorization': 'Bearer ' + token,
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ username, password, role })
        });
        if (res.ok) {
            showToast('Identity created', 'success');
            loadUsers();
        } else {
            const data = await res.json();
            showToast(data.error || 'Failed to create identity', 'error');
        }
    } catch (e) {
        console.error(e);
    }
}

// --- Analytics ---
let trendChart = null;
let geoChart = null;

async function loadAnalytics() {
    const range = document.getElementById('analytics-range').value;
    try {
        const res = await fetch(`/api/analytics/stats?days=${range}`, {
            headers: { 'Authorization': 'Bearer ' + token }
        });
        if (res.ok) {
            const data = await res.json();
            updateAnalyticsUI(data);
        }
    } catch (e) {
        console.error('[Analytics] Fetch failed:', e);
        showToast('Failed to load analytics cluster', 'error');
    }
}

const fmtNum = (v) => (v || 0).toLocaleString();

function updateAnalyticsUI(data) {
    document.getElementById('ana-total-visits').innerText = fmtNum(data.totalVisits);
    document.getElementById('ana-unique-visitors').innerText = fmtNum(data.uniqueVisitorsCount);
    document.getElementById('ana-downloads').innerText = fmtNum(data.downloads);
    document.getElementById('ana-avg-time').innerText = Math.round(data.avgTimeOnSite || 0) + 's';

    // Trends List
    document.getElementById('ana-visits-trend').innerText = `Active trends in last ${document.getElementById('analytics-range').value} days`;

    // Render Charts
    renderTrendChart(data.trends || {}, data.downloadTrends || {});
    renderGeoChart(data.geoDistribution || {});

    // Mini Lists
    renderMiniList('browser-list', data.browsers || {});
    renderMiniList('os-list', data.os || {});
    renderMiniList('geo-list', data.geoDistribution || {});
}

function renderMiniList(id, stats) {
    const el = document.getElementById(id);
    const sorted = Object.entries(stats).sort((a, b) => b[1] - a[1]).slice(0, 5);
    if (sorted.length === 0) {
        el.innerHTML = '<div style="opacity: 0.3; font-size: 0.8rem;">No data available</div>';
        return;
    }
    el.innerHTML = sorted.map(([label, value]) => `
        <div class="mini-item">
            <span class="mini-label">${escapeHtml(label)}</span>
            <span class="mini-value">${value.toLocaleString()}</span>
        </div>
    `).join('');
}

function renderTrendChart(trends, dlTrends) {
    if (typeof Chart === 'undefined') return;
    const ctx = document.getElementById('trendChart').getContext('2d');
    const labels = Object.keys(trends).sort();
    const visitData = labels.map(l => trends[l]);
    const downloadData = labels.map(l => dlTrends[l] || 0);

    if (trendChart) trendChart.destroy();

    trendChart = new Chart(ctx, {
        type: 'line',
        data: {
            labels: labels,
            datasets: [
                {
                    label: 'Site Visits',
                    data: visitData,
                    borderColor: '#00d9a3',
                    backgroundColor: 'rgba(0, 217, 163, 0.1)',
                    fill: true,
                    tension: 0.4
                },
                {
                    label: 'Downloads',
                    data: downloadData,
                    borderColor: '#3b82f6',
                    backgroundColor: 'rgba(59, 130, 246, 0.1)',
                    fill: true,
                    tension: 0.4
                }
            ]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { labels: { color: '#94a3b8', font: { family: 'Outfit' } } }
            },
            scales: {
                x: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#94a3b8' } },
                y: { grid: { color: 'rgba(255,255,255,0.05)' }, ticks: { color: '#94a3b8' } }
            }
        }
    });
}

function renderGeoChart(geo) {
    if (typeof Chart === 'undefined') return;
    const ctx = document.getElementById('geoChart').getContext('2d');
    const labels = Object.keys(geo);
    const values = Object.values(geo);

    if (geoChart) geoChart.destroy();

    if (labels.length === 0) {
        // Placeholder
        return;
    }

    geoChart = new Chart(ctx, {
        type: 'doughnut',
        data: {
            labels: labels,
            datasets: [{
                data: values,
                backgroundColor: [
                    '#00d9a3', '#3b82f6', '#f59e0b', '#ef4444', '#8b5cf6', '#ec4899'
                ],
                borderWidth: 0
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: { position: 'bottom', labels: { color: '#94a3b8', font: { family: 'Outfit' } } }
            }
        }
    });
}

// --- UI Event Wiring (replaces inline handlers; required by CSP script-src-attr 'none') ---
const ACTIONS = {
    'login': handleLogin,
    'mfa-login': handleMfaLogin,
    'cancel-mfa': cancelMfa,
    'logout': handleLogout,
    'start-build': startBuild,
    'stop-build': stopBuild,
    'add-user': showAddUserModal,
    'change-password': changePassword,
    'verify-mfa-setup': verifyMfaSetup,
    'cancel-mfa-setup': cancelMfaSetup,
    'save-config': saveConfiguration
};

document.addEventListener('click', (e) => {
    const navBtn = e.target.closest('.nav-btn[data-section]');
    if (navBtn) {
        showSection(navBtn.dataset.section, navBtn);
        return;
    }
    const actionEl = e.target.closest('[data-action]');
    if (!actionEl) return;
    const action = actionEl.dataset.action;
    if (action === 'delete-user') {
        deleteUser(actionEl.dataset.username);
    } else if (ACTIONS[action]) {
        ACTIONS[action]();
    }
});

const analyticsRange = document.getElementById('analytics-range');
if (analyticsRange) analyticsRange.addEventListener('change', loadAnalytics);

// Default MFA button handler (loadUsers() reassigns .onclick based on MFA state)
const mfaSetupBtn = document.getElementById('mfa-setup-btn');
if (mfaSetupBtn) mfaSetupBtn.onclick = initMfaSetup;

// --- Startup ---
let currentUser = null;
let tempToken = null;

const savedToken = localStorage.getItem('redball_admin_token');
if (savedToken) {
    token = savedToken;
    fetch('/api/auth/me', {
        headers: { 'Authorization': 'Bearer ' + token }
    }).then(res => {
        if (res.ok) return res.json();
        else throw new Error('Token expired');
    }).then(data => {
        currentUser = data.user;
        checkAuth();
    }).catch(() => {
        token = null;
        localStorage.removeItem('redball_admin_token');
        checkAuth();
    });
} else {
    checkAuth();
}

setInterval(() => {
    const timeEl = document.getElementById('live-time');
    if (timeEl) timeEl.innerText = 'LIVE: ' + new Date().toLocaleTimeString();
}, 1000);
