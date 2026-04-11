const express = require('express');
const { WebSocketServer } = require('ws');
const pty = require('node-pty');
const path = require('path');
const fs = require('fs');
const http = require('http');

const app = express();
const server = http.createServer(app);
const wss = new WebSocketServer({ server });

const PORT = process.env.PORT || 3500;
const PROJECT_ROOT = path.join(__dirname, '..');
const RELEASES_JSON = path.join(PROJECT_ROOT, 'update-server', 'data', 'releases.json');
const RELEASES_DIR = path.join(PROJECT_ROOT, 'update-server', 'releases');
const PUBLIC_DIR = path.join(__dirname, 'public');

// Build state
let buildState = {
  status: 'idle',
  stage: null,
  progress: 0,
  log: [],
  pid: null,
  startTime: null,
  endTime: null
};

// Middleware
app.use(express.json());

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

app.post('/api/build/start', (req, res) => {
  if (buildState.status === 'running') {
    return res.status(409).json({ error: 'Build already in progress' });
  }
  startBuild(req.body.type || 'windows');
  res.json({ status: 'started' });
});

app.post('/api/build/stop', (req, res) => {
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

// Static files
app.use('/admin', express.static(PUBLIC_DIR, { index: 'admin.html' }));
app.use(express.static(PUBLIC_DIR, { index: 'marketing.html' }));

// Fallbacks
app.get('/admin', (req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'admin.html'));
});

app.get(/\/admin\/.*/, (req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'admin.html'));
});

app.get(/.*/, (req, res) => {
  res.sendFile(path.join(PUBLIC_DIR, 'marketing.html'));
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
            fileList.push({
              name: file,
              size: fileStat.size,
              downloads: 0 // We don't track downloads per file locally
            });

            if (!stats.byFile[file]) {
              stats.byFile[file] = { downloads: 0, versions: [] };
            }
            stats.byFile[file].versions.push(version);
          }
        });

        releaseMap.set(version, {
          version,
          channel: 'stable',
          date: stat.mtime.toISOString(),
          files: fileList,
          totalDownloads: 0
        });
      }
    });
  }

  // Merge with releases.json data if available
  if (fs.existsSync(RELEASES_JSON)) {
    try {
      const data = JSON.parse(fs.readFileSync(RELEASES_JSON, 'utf8'));
      (data.releases || []).forEach(r => {
        if (releaseMap.has(r.version)) {
          const existing = releaseMap.get(r.version);
          existing.channel = r.channel || existing.channel;
          existing.date = r.date || existing.date;
          existing.totalDownloads = r.totalDownloads || 0;
          if (r.files) {
            r.files.forEach(f => {
              if (stats.byFile[f.name]) {
                stats.byFile[f.name].downloads = f.downloads || 0;
              }
            });
          }
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
    stats.releases.forEach(r => {
      stats.totalDownloads += r.totalDownloads || 0;
    });
  }

  return stats;
}

function parseBuildStage(line) {
  const stages = [
    { name: 'setup', pattern: /Phase 1|Checking build dependencies/i, progress: 5 },
    { name: 'version', pattern: /Phase 0|Bumping version/i, progress: 10 },
    { name: 'windows-build', pattern: /Phase 1.*Building|Building Windows/i, progress: 30 },
    { name: 'update-server', pattern: /Phase 4|Update Server/i, progress: 60 },
    { name: 'publish', pattern: /Phase 5|Publish to Update Server/i, progress: 80 },
    { name: 'github', pattern: /Phase 6|Publish to GitHub/i, progress: 90 },
    { name: 'restart', pattern: /Phase 7|Restarting update server/i, progress: 95 },
    { name: 'complete', pattern: /AUTO-RELEASE COMPLETED/i, progress: 100 }
  ];

  for (const s of stages) {
    if (s.pattern.test(line)) return s;
  }
  if (/error|failed|abort/i.test(line)) return { name: 'error', progress: buildState.progress };
  return null;
}

function startBuild(buildType = 'windows') {
  buildState = {
    status: 'running',
    stage: 'setup',
    progress: 0,
    log: [],
    pid: null,
    startTime: Date.now(),
    endTime: null
  };

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
        }

        if (buildState.log.length > 5000) buildState.log = buildState.log.slice(-2500);

        broadcast({
          type: 'build-output',
          data: { line, stage: buildState.stage, progress: buildState.progress }
        });
      }
    });
  });

  ptyProcess.onExit(({ exitCode }) => {
    buildState.status = exitCode === 0 ? 'success' : 'failed';
    buildState.endTime = Date.now();
    buildState.progress = exitCode === 0 ? 100 : buildState.progress;
    buildState.pid = null;

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
