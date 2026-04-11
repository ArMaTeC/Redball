import express from 'express';
import path from 'path';
import { spawn } from 'child_process';
import fs from 'fs';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

const app = express();
const PORT = process.env.PORT || 3500;

app.use(express.json());

// Serve static files from the React dist directory (built site)
app.use(express.static(path.join(__dirname, 'dist')));

// Also serve public assets
app.use('/assets', express.static(path.join(__dirname, 'public')));

// --- Admin APIs ---

app.post('/api/admin/build', (req, res) => {
  console.log('[SITE] Triggering build...');
  const buildProcess = spawn('npm', ['run', 'build'], { cwd: path.join(__dirname, '..') });

  res.json({ status: 'started', pid: buildProcess.pid });

  buildProcess.stdout.on('data', (data) => {
    fs.appendFileSync(path.join(__dirname, '..', 'build.log'), data);
  });
});

app.post('/api/admin/release', (req, res) => {
  console.log('[SITE] Triggering release...');
  const releaseProcess = spawn('npm', ['run', 'release'], { cwd: path.join(__dirname, '..') });

  res.json({ status: 'started', pid: releaseProcess.pid });

  releaseProcess.stdout.on('data', (data) => {
    fs.appendFileSync(path.join(__dirname, '..', 'build.log'), data);
  });
});

app.get('/api/admin/logs', (req, res) => {
  const logPath = path.join(__dirname, '..', 'build.log');
  if (fs.existsSync(logPath)) {
    const logs = fs.readFileSync(logPath, 'utf8');
    res.send(logs);
  } else {
    res.status(404).send('Log file not found');
  }
});

app.get('/api/admin/config', (req, res) => {
  const configPath = path.join(__dirname, '..', 'Redball.json');
  if (fs.existsSync(configPath)) {
    const config = fs.readFileSync(configPath, 'utf8');
    res.json(JSON.parse(config));
  } else {
    res.status(404).json({ error: 'Config file not found' });
  }
});

// --- Public Stats API ---
app.get('/api/stats', async (req, res) => {
  try {
    const response = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases');
    if (!response.ok) throw new Error(`GitHub API error: ${response.status}`);

    const ghReleases = await response.json();

    // Transform to our format
    const releases = ghReleases.map(r => {
      const files = r.assets.map(a => ({
        name: a.name,
        size: a.size,
        downloads: a.download_count,
        url: a.browser_download_url
      }));

      return {
        version: r.tag_name.replace(/^v/, ''),
        channel: r.prerelease ? 'beta' : 'stable',
        date: r.published_at,
        files,
        totalDownloads: r.assets.reduce((sum, a) => sum + a.download_count, 0)
      };
    });

    const totalDownloads = releases.reduce((sum, r) => sum + r.totalDownloads, 0);

    res.json({
      totalReleases: releases.length,
      totalDownloads,
      latestVersion: releases[0]?.version || 'none',
      releases
    });
  } catch (err) {
    console.error('[SITE] Error fetching stats:', err.message);
    res.status(500).json({ error: 'Failed to fetch stats' });
  }
});

// --- Latest Release API ---
app.get('/api/releases/latest', async (req, res) => {
  try {
    const response = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases/latest');
    if (!response.ok) throw new Error(`GitHub API error: ${response.status}`);

    const ghRelease = await response.json();
    const files = ghRelease.assets.map(a => ({
      name: a.name,
      size: a.size,
      downloads: a.download_count,
      url: a.browser_download_url
    }));

    res.json({
      version: ghRelease.tag_name.replace(/^v/, ''),
      channel: ghRelease.prerelease ? 'beta' : 'stable',
      date: ghRelease.published_at,
      files,
      totalDownloads: ghRelease.assets.reduce((sum, a) => sum + a.download_count, 0)
    });
  } catch (err) {
    console.error('[SITE] Error fetching latest:', err.message);
    res.status(500).json({ error: 'Failed to fetch latest release' });
  }
});

// Fallback to React app's index.html for SPA routing
app.use((req, res, next) => {
  if (req.path.startsWith('/api/')) return next();
  res.sendFile(path.join(__dirname, 'dist', 'index.html'));
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`[REDBALL SITE] Server running at http://localhost:${PORT}`);
  console.log(`[REDBALL SITE] Serving React app from: ${path.join(__dirname, 'dist')}`);
});
