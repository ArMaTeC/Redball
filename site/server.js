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

// Serve static files from the update-server public directory (full marketing site)
app.use(express.static(path.join(__dirname, '..', 'update-server', 'public')));

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

// Fallback to index.html for SPA
app.use((req, res, next) => {
  if (req.path.startsWith('/api/')) return next();
  res.sendFile(path.join(__dirname, '..', 'update-server', 'public', 'index.html'));
});

app.listen(PORT, '0.0.0.0', () => {
  console.log(`[REDBALL SITE] Server running at http://localhost:${PORT}`);
});
