const express = require('express');
const { WebSocketServer } = require('ws');
const pty = require('node-pty');
const path = require('path');
const fs = require('fs');
const { spawn } = require('child_process');
const http = require('http');

const app = express();
const server = http.createServer(app);
const wss = new WebSocketServer({ server });

const PORT = process.env.PORT || 3500;
const PROJECT_ROOT = path.join(__dirname, '..');
const RELEASES_JSON = path.join(PROJECT_ROOT, 'update-server', 'data', 'releases.json');

// Build state
let buildState = {
  status: 'idle', // idle, running, success, failed
  stage: null,
  progress: 0,
  log: [],
  pid: null,
  startTime: null,
  endTime: null
};

// Middleware
app.use(express.json());

// API: Get download stats
app.get('/api/stats', (req, res) => {
  try {
    const stats = getDownloadStats();
    res.json(stats);
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// API: Get current build status
app.get('/api/build/status', (req, res) => {
  res.json(buildState);
});

// API: Get build log
app.get('/api/build/log', (req, res) => {
  res.json({ log: buildState.log });
});

// API: Start build
app.post('/api/build/start', (req, res) => {
  if (buildState.status === 'running') {
    return res.status(409).json({ error: 'Build already in progress' });
  }

  const buildType = req.body.type || 'windows';
  startBuild(buildType);
  res.json({ status: 'started', type: buildType });
});

// API: Stop build
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

// API: Get releases
app.get('/api/releases', (req, res) => {
  try {
    if (fs.existsSync(RELEASES_JSON)) {
      const data = JSON.parse(fs.readFileSync(RELEASES_JSON, 'utf8'));
      res.json(data.releases || []);
    } else {
      res.json([]);
    }
  } catch (err) {
    res.status(500).json({ error: err.message });
  }
});

// WebSocket handling
wss.on('connection', (ws) => {
  console.log('[WS] Client connected');

  // Send current build state
  ws.send(JSON.stringify({
    type: 'state',
    data: buildState
  }));

  ws.on('message', (message) => {
    try {
      const data = JSON.parse(message);

      if (data.action === 'start-build') {
        if (buildState.status !== 'running') {
          startBuild(data.buildType || 'windows');
        }
      } else if (data.action === 'stop-build') {
        if (buildState.pid) {
          process.kill(buildState.pid, 'SIGTERM');
        }
      }
    } catch (err) {
      console.error('[WS] Error:', err);
    }
  });

  ws.on('close', () => {
    console.log('[WS] Client disconnected');
  });
});

function broadcast(message) {
  wss.clients.forEach(client => {
    if (client.readyState === 1) {
      client.send(JSON.stringify(message));
    }
  });
}

function getDownloadStats() {
  const stats = {
    totalDownloads: 0,
    totalReleases: 0,
    latestVersion: null,
    releases: [],
    byFile: {}
  };

  if (fs.existsSync(RELEASES_JSON)) {
    const data = JSON.parse(fs.readFileSync(RELEASES_JSON, 'utf8'));
    stats.releases = data.releases || [];
    stats.totalReleases = stats.releases.length;

    if (stats.releases.length > 0) {
      const sorted = [...stats.releases].sort((a, b) => {
        const va = a.version.split('.').map(Number);
        const vb = b.version.split('.').map(Number);
        for (let i = 0; i < 3; i++) {
          if (va[i] !== vb[i]) return vb[i] - va[i];
        }
        return 0;
      });
      stats.latestVersion = sorted[0].version;

      stats.releases.forEach(release => {
        const releaseDownloads = release.totalDownloads || 0;
        stats.totalDownloads += releaseDownloads;

        if (release.files) {
          release.files.forEach(file => {
            const fileName = file.name;
            if (!stats.byFile[fileName]) {
              stats.byFile[fileName] = { downloads: 0, versions: [] };
            }
            stats.byFile[fileName].downloads += file.downloads || 0;
            stats.byFile[fileName].versions.push(release.version);
          });
        }
      });
    }
  }

  return stats;
}

function parseBuildStage(line) {
  const stages = [
    { name: 'setup', pattern: /Phase 1.*Setup/i, progress: 5 },
    { name: 'version', pattern: /Phase 2.*Version/i, progress: 10 },
    { name: 'windows-build', pattern: /Phase 3.*Windows Build/i, progress: 30 },
    { name: 'update-server', pattern: /Phase 4.*Update Server/i, progress: 60 },
    { name: 'publish', pattern: /Phase 5.*Publish/i, progress: 80 },
    { name: 'github', pattern: /Phase 6.*GitHub/i, progress: 90 },
    { name: 'restart', pattern: /Phase 7.*Restart/i, progress: 95 },
    { name: 'complete', pattern: /AUTO-RELEASE COMPLETED/i, progress: 100 }
  ];

  for (const stage of stages) {
    if (stage.pattern.test(line)) {
      return stage;
    }
  }

  // Check for error patterns
  if (/error|failed|abort/i.test(line)) {
    return { name: 'error', progress: buildState.progress };
  }

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
    env: process.env
  });

  buildState.pid = ptyProcess.pid;

  ptyProcess.onData((data) => {
    const lines = data.toString().split('\n');

    lines.forEach(line => {
      if (line.trim()) {
        buildState.log.push({
          timestamp: Date.now(),
          message: line
        });

        // Parse build stage
        const stage = parseBuildStage(line);
        if (stage) {
          buildState.stage = stage.name;
          buildState.progress = stage.progress;
        }

        // Keep log size manageable
        if (buildState.log.length > 10000) {
          buildState.log = buildState.log.slice(-5000);
        }

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

// Static files (after API routes)
app.use(express.static(path.join(__dirname, 'public')));

// SPA fallback (must be last)
app.get(/.*/, (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

server.listen(PORT, '0.0.0.0', () => {
  console.log(`[REDBALL ADMIN] Server running at http://localhost:${PORT}`);
});
