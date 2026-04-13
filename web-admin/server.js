const express = require('express');
const { WebSocketServer } = require('ws');
const pty = require('node-pty');
const path = require('path');
const fs = require('fs');
const http = require('http');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');

const app = express();
const server = http.createServer(app);
const wss = new WebSocketServer({ server });

const PORT = process.env.PORT || 3000;
const PROJECT_ROOT = path.join(__dirname, '..');
const RELEASES_JSON = path.join(PROJECT_ROOT, 'update-server', 'data', 'releases.json');
const RELEASES_DIR = path.join(PROJECT_ROOT, 'update-server', 'releases');
const PUBLIC_DIR = path.join(PROJECT_ROOT, 'site', 'dist');
const BUILD_STATE_FILE = path.join(__dirname, 'logs', 'build-state.json');
const BUILD_LOG_FILE = path.join(__dirname, 'logs', 'build.log');
const AUTH_FILE = path.join(__dirname, 'logs', 'auth.json');
const DOWNLOADS_DB = path.join(__dirname, 'logs', 'downloads.json');

// Download tracking database
let downloadCounts = loadDownloadCounts();

function loadDownloadCounts() {
  try {
    if (fs.existsSync(DOWNLOADS_DB)) {
      return JSON.parse(fs.readFileSync(DOWNLOADS_DB, 'utf8'));
    }
  } catch (e) {
    console.error('[DOWNLOADS] Error loading download counts:', e.message);
  }
  return { files: {}, total: 0, byVersion: {} };
}

function saveDownloadCounts() {
  try {
    fs.writeFileSync(DOWNLOADS_DB, JSON.stringify(downloadCounts, null, 2));
  } catch (e) {
    console.error('[DOWNLOADS] Error saving download counts:', e.message);
  }
}

function trackDownload(version, filename) {
  const key = `${version}/${filename}`;
  if (!downloadCounts.files[key]) {
    downloadCounts.files[key] = 0;
  }
  downloadCounts.files[key]++;
  downloadCounts.total++;

  if (!downloadCounts.byVersion[version]) {
    downloadCounts.byVersion[version] = 0;
  }
  downloadCounts.byVersion[version]++;

  saveDownloadCounts();
  console.log(`[DOWNLOAD] ${key} (count: ${downloadCounts.files[key]})`);
  return downloadCounts.files[key];
}

// JWT Secret (generate random on first run)
const JWT_SECRET = loadOrCreateJwtSecret();

function loadOrCreateJwtSecret() {
  const secretFile = path.join(__dirname, 'logs', '.jwt-secret');
  try {
    if (fs.existsSync(secretFile)) {
      return fs.readFileSync(secretFile, 'utf8').trim();
    }
  } catch (e) { }

  // Generate new secret
  const secret = require('crypto').randomBytes(64).toString('hex');
  try {
    fs.writeFileSync(secretFile, secret, { mode: 0o600 });
  } catch (e) {
    console.error('[AUTH] Warning: Could not save JWT secret to file');
  }
  return secret;
}

// Load or create admin user
function getAdminUser() {
  const defaultUser = {
    username: 'admin',
    // Default password: 'redball2026' - change after first login
    passwordHash: '$2b$10$NH.q4MEoGZUuC4kHmv8B2uLh4OXdPVwa2Q/CKp/ORwSMG2XvhGQ8e'
  };

  try {
    if (fs.existsSync(AUTH_FILE)) {
      const auth = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
      return auth.admin || defaultUser;
    }
  } catch (e) {
    console.error('[AUTH] Error loading auth file:', e.message);
  }

  // Save default user
  saveAdminUser(defaultUser);
  return defaultUser;
}

function saveAdminUser(user) {
  try {
    fs.writeFileSync(AUTH_FILE, JSON.stringify({ admin: user }, null, 2), { mode: 0o600 });
  } catch (e) {
    console.error('[AUTH] Error saving auth file:', e.message);
  }
}

// Authentication middleware
function authenticateToken(req, res, next) {
  const authHeader = req.headers['authorization'];
  const token = authHeader && authHeader.split(' ')[1];

  if (!token) {
    return res.status(401).json({ error: 'Access denied. No token provided.' });
  }

  jwt.verify(token, JWT_SECRET, (err, user) => {
    if (err) {
      return res.status(403).json({ error: 'Invalid or expired token' });
    }
    req.user = user;
    next();
  });
}

// Health check endpoint
app.get('/api/health', (req, res) => {
  res.json({ 
    status: 'healthy', 
    server: 'web-admin', 
    timestamp: new Date().toISOString(),
    uptime: process.uptime()
  });
});

// Build state
let buildState = loadBuildState();

function loadBuildState() {
  const defaultState = {
    status: 'idle',
    stage: null,
    progress: 0,
    log: [],
    pid: null,
    startTime: null,
    endTime: null
  };

  try {
    if (fs.existsSync(BUILD_STATE_FILE)) {
      const saved = JSON.parse(fs.readFileSync(BUILD_STATE_FILE, 'utf8'));
      // If build was running when server stopped, mark it as failed
      if (saved.status === 'running') {
        saved.status = 'failed';
        saved.endTime = Date.now();
      }
      return { ...defaultState, ...saved };
    }
  } catch (e) {
    console.error('[BUILD] Error loading build state:', e.message);
  }
  return defaultState;
}

function saveBuildState() {
  try {
    fs.writeFileSync(BUILD_STATE_FILE, JSON.stringify(buildState, null, 2));
  } catch (e) {
    console.error('[BUILD] Error saving build state:', e.message);
  }
}

// Middleware
app.use(express.json());

// Security headers - allow Google Fonts and essential resources
app.use((req, res, next) => {
  res.setHeader('Content-Security-Policy', "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; connect-src 'self' wss: https:; img-src 'self' data: blob:; font-src 'self' https://fonts.gstatic.com;");
  next();
});

// Auth endpoints
app.post('/api/auth/login', async (req, res) => {
  const { username, password } = req.body;

  if (!username || !password) {
    return res.status(400).json({ error: 'Username and password required' });
  }

  const admin = getAdminUser();

  if (username !== admin.username) {
    return res.status(401).json({ error: 'Invalid credentials' });
  }

  const validPassword = await bcrypt.compare(password, admin.passwordHash);
  if (!validPassword) {
    return res.status(401).json({ error: 'Invalid credentials' });
  }

  // Generate JWT token
  const token = jwt.sign(
    { username: admin.username, role: 'admin' },
    JWT_SECRET,
    { expiresIn: '24h' }
  );

  res.json({
    token,
    user: { username: admin.username, role: 'admin' }
  });
});

app.post('/api/auth/change-password', authenticateToken, async (req, res) => {
  const { currentPassword, newPassword } = req.body;

  if (!currentPassword || !newPassword) {
    return res.status(400).json({ error: 'Current and new password required' });
  }

  if (newPassword.length < 8) {
    return res.status(400).json({ error: 'Password must be at least 8 characters' });
  }

  const admin = getAdminUser();

  const validPassword = await bcrypt.compare(currentPassword, admin.passwordHash);
  if (!validPassword) {
    return res.status(401).json({ error: 'Current password is incorrect' });
  }

  // Hash new password
  const newHash = await bcrypt.hash(newPassword, 10);
  admin.passwordHash = newHash;
  saveAdminUser(admin);

  res.json({ success: true, message: 'Password changed successfully' });
});

app.get('/api/auth/me', authenticateToken, (req, res) => {
  res.json({ user: req.user });
});

// API Routes
app.get('/api/stats', (req, res) => {
  try {
    const stats = getDownloadStats();
    res.json(stats);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

app.get('/api/build/status', (req, res) => {
  res.json(buildState);
});

app.get('/api/build/log', (req, res) => {
  res.json({ log: buildState.log });
});

app.post('/api/build/start', authenticateToken, (req, res) => {
  if (buildState.status === 'running') {
    return res.status(409).json({ error: 'Build already in progress' });
  }
  startBuild(req.body.type || 'windows');
  res.json({ status: 'started' });
});

app.post('/api/build/stop', authenticateToken, (req, res) => {
  if (buildState.pid) {
    try {
      process.kill(buildState.pid, 'SIGTERM');
      buildState.status = 'stopped';
      buildState.endTime = Date.now();
      broadcast({ type: 'build-stopped' });
      res.json({ status: 'stopped' });
    } catch (err) {
      res.status(500).json({ error: err.message });
    }
  } else {
    res.status(400).json({ error: 'No build running' });
  }
});

app.get('/api/releases', (req, res) => {
  try {
    const stats = getDownloadStats();
    res.json(stats.releases || []);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// Download tracking endpoint
app.get('/api/download/:version/:file', (req, res) => {
  const { version, file } = req.params;
  const filePath = path.join(RELEASES_DIR, version, file);

  // Validate path to prevent directory traversal
  if (!filePath.startsWith(RELEASES_DIR)) {
    return res.status(403).json({ error: 'Invalid path' });
  }

  if (!fs.existsSync(filePath)) {
    return res.status(404).json({ error: 'File not found' });
  }

  // Track the download
  trackDownload(version, file);

  // Serve the file
  res.setHeader('Content-Disposition', `attachment; filename="${file}"`);
  res.sendFile(filePath);
});

// System Config endpoints
app.get('/api/system/config', authenticateToken, (req, res) => {
  try {
    // Calculate disk usage
    const getDirSize = (dirPath) => {
      if (!fs.existsSync(dirPath)) return 0;
      let size = 0;
      const files = fs.readdirSync(dirPath);
      for (const file of files) {
        const filePath = path.join(dirPath, file);
        const stat = fs.statSync(filePath);
        if (stat.isDirectory()) {
          size += getDirSize(filePath);
        } else {
          size += stat.size;
        }
      }
      return size;
    };

    const releaseCount = fs.existsSync(RELEASES_DIR)
      ? fs.readdirSync(RELEASES_DIR).filter(d => /^\d+\.\d+\.\d+$/.test(d)).length
      : 0;

    res.json({
      hostname: require('os').hostname(),
      platform: require('os').platform(),
      nodeVersion: process.version,
      uptime: process.uptime(),
      env: process.env.NODE_ENV || 'development',
      webPort: PORT,
      updatePort: 3500,
      projectRoot: PROJECT_ROOT,
      releasesDir: RELEASES_DIR,
      releaseCount: releaseCount,
      diskUsage: {
        releases: getDirSize(RELEASES_DIR),
        logs: getDirSize(path.join(__dirname, 'logs')),
        total: getDirSize(RELEASES_DIR) + getDirSize(path.join(__dirname, 'logs'))
      }
    });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

app.post('/api/system/clear-logs', authenticateToken, (req, res) => {
  try {
    const logsDir = path.join(__dirname, 'logs');
    if (fs.existsSync(logsDir)) {
      const files = fs.readdirSync(logsDir);
      for (const file of files) {
        const filePath = path.join(logsDir, file);
        const stat = fs.statSync(filePath);
        if (stat.isFile() && (file.endsWith('.log') || file.endsWith('.json'))) {
          // Don't delete auth or secret files
          if (!file.startsWith('.') && file !== 'auth.json' && file !== 'downloads.json') {
            fs.truncateSync(filePath, 0);
          }
        }
      }
    }
    // Reset build state log
    buildState.log = [];
    saveBuildState();
    res.json({ success: true });
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

app.post('/api/system/restart', authenticateToken, (req, res) => {
  res.json({ success: true, message: 'Server restarting...' });
  setTimeout(() => process.exit(0), 1000);
});

// Application Configuration endpoints
const DEFAULT_CONFIG_PATH = path.join(__dirname, 'logs', 'default-client-config.json');

const DEFAULT_CLIENT_CONFIG = {
  // Keep-Alive Settings
  HeartbeatSeconds: 30,
  DefaultDuration: 480,
  HeartbeatInputMode: 'F15',
  PreventDisplaySleep: true,
  UseHeartbeatKeypress: true,

  // TypeThing Settings
  TypeThingEnabled: true,
  TypeThingNotifications: true,
  TypeThingMinDelayMs: 50,
  TypeThingMaxDelayMs: 150,
  TypeThingTheme: 'dark',

  // Smart Features
  BatteryAware: true,
  BatteryThreshold: 20,
  NetworkAware: true,
  IdleDetection: true,
  IdleThreshold: 300,
  PresentationModeDetection: true,

  // Update Settings
  UpdateRepoOwner: 'ArMaTeC',
  UpdateRepoName: 'Redball',
  UpdateChannel: 'stable',
  VerifyUpdateSignature: true,

  // UI Settings
  Theme: 'System',
  Locale: 'en-GB',
  MinimizeToTray: true,
  MinimizeOnStart: false,
  ShowNotifications: true,
  ShowBalloonOnStart: true,

  // Logging & Debug
  VerboseLogging: false,
  EnableTelemetry: false,
  EnablePerformanceMetrics: false,
  MaxLogSizeMB: 10
};

function loadClientConfig() {
  if (!fs.existsSync(DEFAULT_CONFIG_PATH)) {
    return { ...DEFAULT_CLIENT_CONFIG };
  }
  try {
    const data = JSON.parse(fs.readFileSync(DEFAULT_CONFIG_PATH, 'utf8'));
    return { ...DEFAULT_CLIENT_CONFIG, ...data };
  } catch (err) {
    console.error('[Config] Failed to load client config:', err.message);
    return { ...DEFAULT_CLIENT_CONFIG };
  }
}

function saveClientConfig(config) {
  try {
    fs.writeFileSync(DEFAULT_CONFIG_PATH, JSON.stringify(config, null, 2));
    return true;
  } catch (err) {
    console.error('[Config] Failed to save client config:', err.message);
    return false;
  }
}

// Public endpoint for clients to get default config (no auth required)
app.get('/api/defaults', (req, res) => {
  const config = loadClientConfig();
  res.json(config);
});

// Admin endpoints (require auth)
app.get('/api/config', authenticateToken, (req, res) => {
  const config = loadClientConfig();
  res.json(config);
});

app.post('/api/config', authenticateToken, (req, res) => {
  const currentConfig = loadClientConfig();
  const newConfig = { ...currentConfig, ...req.body };

  if (saveClientConfig(newConfig)) {
    res.json({ success: true, config: newConfig });
  } else {
    res.status(500).json({ error: 'Failed to save configuration' });
  }
});

// Static files - Serve React app from site/dist
app.use('/admin', express.static(path.join(__dirname, 'public'), { index: 'admin.html' }));
app.use('/releases', express.static(RELEASES_DIR));
app.use(express.static(PUBLIC_DIR, { index: 'index.html' }));

// Fallbacks
app.get('/admin', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'admin.html'));
});

app.get(/\/admin\/.*/, (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'admin.html'));
});

// SPA fallback for React app
app.get(/.*/, (req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'index.html'));
});

// WebSocket
wss.on('connection', (ws) => {
  console.log('[WS] Client connected');

  ws.send(JSON.stringify({ type: 'state', data: buildState }));

  ws.on('message', (message) => {
    try {
      const data = JSON.parse(message);
      if (data.action === 'start-build' && buildState.status !== 'running') {
        startBuild(data.buildType || 'windows');
      } else if (data.action === 'stop-build' && buildState.pid) {
        process.kill(buildState.pid, 'SIGTERM');
      }
    } catch (err) {
      console.error('[WS] Error:', err);
    }
  });

  ws.on('close', () => console.log('[WS] Client disconnected'));
});

function broadcast(msg) {
  wss.clients.forEach(c => {
    if (c.readyState === 1) c.send(JSON.stringify(msg));
  });
}

function getDownloadStats() {
  const stats = { totalDownloads: 0, totalReleases: 0, latestVersion: null, releases: [], byFile: {} };
  const releaseMap = new Map();

  // Reload download counts to get latest values
  downloadCounts = loadDownloadCounts();

  // Scan releases directory
  if (fs.existsSync(RELEASES_DIR)) {
    const versionDirs = fs.readdirSync(RELEASES_DIR)
      .filter(d => /^\d+\.\d+\.\d+$/.test(d))
      .sort((a, b) => {
        const va = a.split('.').map(Number);
        const vb = b.split('.').map(Number);
        for (let i = 0; i < 3; i++) if (va[i] !== vb[i]) return vb[i] - va[i];
        return 0;
      });

    versionDirs.forEach(version => {
      const versionPath = path.join(RELEASES_DIR, version);
      const stat = fs.statSync(versionPath);
      if (stat.isDirectory()) {
        const files = fs.readdirSync(versionPath);
        const fileList = [];
        let totalDownloads = 0;

        files.forEach(file => {
          const filePath = path.join(versionPath, file);
          const fileStat = fs.statSync(filePath);
          if (fileStat.isFile()) {
            const downloadKey = `${version}/${file}`;
            const fileDownloads = downloadCounts.files[downloadKey] || 0;
            fileList.push({
              name: file,
              size: fileStat.size,
              downloads: fileDownloads,
              url: `/api/download/${version}/${file}`
            });

            if (!stats.byFile[file]) {
              stats.byFile[file] = { downloads: 0, versions: [] };
            }
            stats.byFile[file].downloads += fileDownloads;
            stats.byFile[file].versions.push(version);
          }
        });

        const versionTotal = downloadCounts.byVersion[version] || 0;
        releaseMap.set(version, {
          version,
          channel: 'stable',
          date: stat.mtime.toISOString(),
          files: fileList,
          totalDownloads: versionTotal
        });
      }
    });
  }

  // Merge with releases.json data if available (but preserve actual download counts)
  if (fs.existsSync(RELEASES_JSON)) {
    try {
      const data = JSON.parse(fs.readFileSync(RELEASES_JSON, 'utf8'));
      (data.releases || []).forEach(r => {
        if (releaseMap.has(r.version)) {
          const existing = releaseMap.get(r.version);
          existing.channel = r.channel || existing.channel;
          existing.date = r.date || existing.date;
          // Don't overwrite totalDownloads - keep the actual tracked value
          // existing.totalDownloads is already set from downloadCounts.byVersion
        } else {
          releaseMap.set(r.version, r);
        }
      });
    } catch (e) {
      console.error('[STATS] Error reading releases.json:', e.message);
    }
  }

  stats.releases = Array.from(releaseMap.values());
  stats.totalReleases = stats.releases.length;

  if (stats.releases.length > 0) {
    stats.latestVersion = stats.releases[0].version;
  }

  // Use actual download count from database
  stats.totalDownloads = downloadCounts.total || 0;

  return stats;
}

function parseBuildStage(line) {
  const stages = [
    { name: 'setup', pattern: /starting full auto-release|checking build dependencies/i, progress: 5 },
    { name: 'version', pattern: /phase 0: bumping version|version set\/bumped/i, progress: 10 },
    { name: 'restore', pattern: /phase 1: restoring and building|restoring and building all components/i, progress: 20 },
    { name: 'wpf-build', pattern: /building windows artifacts|wpf compile/i, progress: 40 },
    { name: 'service-build', pattern: /wpf publish|building service/i, progress: 55 },
    { name: 'installer', pattern: /nsis installer|finalizing windows artifacts/i, progress: 68 },
    { name: 'publish', pattern: /phase 5: publishing to update-server|published to update-server/i, progress: 78 },
    { name: 'github', pattern: /phase 6: publishing to github|published to github/i, progress: 90 },
    { name: 'restart', pattern: /phase 7: restarting update server|update server restart process/i, progress: 95 },
    { name: 'health', pattern: /phase 9: final health status check/i, progress: 98 },
    { name: 'complete', pattern: /auto-release completed/i, progress: 100 }
  ];

  for (const s of stages) {
    if (s.pattern.test(line)) return s;
  }
  
  if (/error|failed|abort|unexpected failure/i.test(line)) {
      return { name: buildState.stage, progress: buildState.progress, status: 'failed' };
  }
  return null;
}

function startBuild(buildType = 'windows') {
  // Check if another build is already running externally
  if (buildState.status === 'running' && buildState.pid) {
    try {
      process.kill(buildState.pid, 0); // Check if process exists
      console.log('[BUILD] Build already running with PID:', buildState.pid);
      return; // Build is still running
    } catch (e) {
      // Process doesn't exist, reset state
      console.log('[BUILD] Stale build detected, resetting state');
    }
  }

  buildState = {
    status: 'running',
    stage: 'setup',
    progress: 0,
    log: [],
    pid: null,
    startTime: Date.now(),
    endTime: null
  };
  saveBuildState();

  broadcast({ type: 'build-started', data: buildState });

  const scriptPath = path.join(PROJECT_ROOT, 'scripts', 'build.sh');
  const ptyProcess = pty.spawn('bash', [scriptPath, 'auto-release'], {
    name: 'xterm-color',
    cols: 120,
    rows: 30,
    cwd: PROJECT_ROOT,
    env: { ...process.env, FORCE_COLOR: '1' }
  });

  buildState.pid = ptyProcess.pid;

  ptyProcess.onData((data) => {
    const lines = data.toString().split('\n');

    lines.forEach(line => {
      if (line.trim()) {
        buildState.log.push({ timestamp: Date.now(), message: line });

        const stage = parseBuildStage(line);
        if (stage) {
          buildState.stage = stage.name;
          buildState.progress = stage.progress;
          if (stage.status) buildState.status = stage.status;
        }

        if (buildState.log.length > 5000) buildState.log = buildState.log.slice(-2500);

        // Save state periodically (every ~5 seconds or on stage change)
        if (buildState.log.length % 50 === 0 || stage) {
          saveBuildState();
        }

        broadcast({
          type: 'build-output',
          data: { line, stage: buildState.stage, progress: buildState.progress, status: buildState.status }
        });
      }
    });
  });

  ptyProcess.onExit(({ exitCode }) => {
    buildState.status = exitCode === 0 ? 'success' : 'failed';
    buildState.endTime = Date.now();
    buildState.progress = exitCode === 0 ? 100 : buildState.progress;
    buildState.pid = null;
    saveBuildState();

    broadcast({
      type: 'build-complete',
      data: {
        status: buildState.status,
        exitCode,
        duration: buildState.endTime - buildState.startTime
      }
    });
  });
}

server.listen(PORT, '0.0.0.0', () => {
  console.log(`[REDBALL] Server running at http://localhost:${PORT}`);
  console.log(`[REDBALL] Marketing: /, Admin: /admin`);
});
