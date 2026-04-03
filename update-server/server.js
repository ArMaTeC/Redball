const express = require('express');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const multer = require('multer');
const morgan = require('morgan');

const app = express();
const PORT = process.env.PORT || 3500;
const RELEASES_DIR = path.join(__dirname, 'releases');
const DATA_DIR = path.join(__dirname, 'data');
const DB_PATH = path.join(DATA_DIR, 'releases.json');

// Ensure directories exist
[RELEASES_DIR, DATA_DIR].forEach(dir => {
  if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
});

// === Database helpers ===
function loadDB() {
  if (!fs.existsSync(DB_PATH)) return { releases: [] };
  try {
    return JSON.parse(fs.readFileSync(DB_PATH, 'utf8'));
  } catch {
    return { releases: [] };
  }
}

function saveDB(db) {
  fs.writeFileSync(DB_PATH, JSON.stringify(db, null, 2));
}

function compareVersions(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const na = pa[i] || 0;
    const nb = pb[i] || 0;
    if (na > nb) return 1;
    if (na < nb) return -1;
  }
  return 0;
}

function sha256File(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', d => hash.update(d));
    stream.on('end', () => resolve(hash.digest('hex')));
    stream.on('error', reject);
  });
}

// === Middleware ===
app.use(morgan('short'));
app.use(express.json());
app.use(express.static(path.join(__dirname, 'public')));

// === Multer for file uploads ===
const storage = multer.diskStorage({
  destination: (req, file, cb) => {
    const version = req.params.version;
    const dir = path.join(RELEASES_DIR, version);
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    cb(null, dir);
  },
  filename: (req, file, cb) => {
    cb(null, file.originalname);
  }
});
const upload = multer({ storage, limits: { fileSize: 500 * 1024 * 1024 } }); // 500MB max

// ============================================================
// API Routes
// ============================================================

// --- List all releases ---
app.get('/api/releases', (req, res) => {
  const db = loadDB();
  const sorted = db.releases.sort((a, b) => compareVersions(b.version, a.version));
  res.json(sorted);
});

// --- Get latest release ---
app.get('/api/releases/latest', (req, res) => {
  const db = loadDB();
  const channel = req.query.channel || 'stable';
  const filtered = db.releases.filter(r =>
    channel === 'all' || r.channel === channel || (!r.channel && channel === 'stable')
  );
  if (filtered.length === 0) return res.status(404).json({ error: 'No releases found' });
  const sorted = filtered.sort((a, b) => compareVersions(b.version, a.version));
  res.json(sorted[0]);
});

// --- Get specific release ---
app.get('/api/releases/:version', (req, res) => {
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (!release) return res.status(404).json({ error: 'Release not found' });
  res.json(release);
});

// --- GitHub-compatible releases endpoint (for UpdateService compatibility) ---
app.get('/api/github/releases', (req, res) => {
  const db = loadDB();
  const sorted = db.releases.sort((a, b) => compareVersions(b.version, a.version));
  // Map to GitHub release format
  const ghReleases = sorted.map(r => ({
    tag_name: `v${r.version}`,
    name: `Redball v${r.version}`,
    body: r.notes || '',
    prerelease: r.channel !== 'stable',
    published_at: r.date || new Date().toISOString(),
    assets: (r.files || []).map(f => ({
      name: f.name,
      size: f.size,
      browser_download_url: `${req.protocol}://${req.get('host')}/downloads/${r.version}/${f.name}`,
      content_type: guessMimeType(f.name)
    }))
  }));
  res.json(ghReleases);
});

// --- Download a file ---
app.get('/downloads/:version/:filename', (req, res) => {
  const filePath = path.join(RELEASES_DIR, req.params.version, req.params.filename);
  if (!fs.existsSync(filePath)) return res.status(404).json({ error: 'File not found' });

  // Track download count
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (release) {
    const file = (release.files || []).find(f => f.name === req.params.filename);
    if (file) {
      file.downloads = (file.downloads || 0) + 1;
      release.totalDownloads = (release.totalDownloads || 0) + 1;
      saveDB(db);
    }
  }

  res.download(filePath);
});

// --- Get manifest.json for a release (differential updates) ---
app.get('/api/releases/:version/manifest', (req, res) => {
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (!release) return res.status(404).json({ error: 'Release not found' });

  // Build manifest.json compatible with UpdateService.cs
  const manifest = {
    version: release.version,
    files: (release.files || [])
      .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS')
      .map(f => ({
        name: f.name,
        hash: f.hash || '',
        size: f.size || 0,
        signature: f.signature || ''
      }))
  };
  res.json(manifest);
});

// --- Create a new release ---
app.post('/api/releases', (req, res) => {
  const { version, notes, channel } = req.body;
  if (!version) return res.status(400).json({ error: 'Version is required' });

  const db = loadDB();
  if (db.releases.find(r => r.version === version)) {
    return res.status(409).json({ error: 'Release already exists' });
  }

  const releaseDir = path.join(RELEASES_DIR, version);
  if (!fs.existsSync(releaseDir)) fs.mkdirSync(releaseDir, { recursive: true });

  const release = {
    version,
    notes: notes || '',
    channel: channel || 'stable',
    date: new Date().toISOString(),
    files: [],
    totalDownloads: 0
  };

  db.releases.push(release);
  saveDB(db);
  res.status(201).json(release);
});

// --- Upload files to a release ---
app.post('/api/releases/:version/upload', upload.array('files', 50), async (req, res) => {
  const db = loadDB();
  let release = db.releases.find(r => r.version === req.params.version);

  // Auto-create release if it doesn't exist
  if (!release) {
    release = {
      version: req.params.version,
      notes: '',
      channel: 'stable',
      date: new Date().toISOString(),
      files: [],
      totalDownloads: 0
    };
    db.releases.push(release);
  }

  if (!req.files || req.files.length === 0) {
    return res.status(400).json({ error: 'No files uploaded' });
  }

  for (const file of req.files) {
    const hash = await sha256File(file.path);
    const existing = release.files.findIndex(f => f.name === file.originalname);
    const fileEntry = {
      name: file.originalname,
      size: file.size,
      hash,
      downloads: 0,
      uploadedAt: new Date().toISOString()
    };

    if (existing >= 0) {
      release.files[existing] = fileEntry;
    } else {
      release.files.push(fileEntry);
    }
  }

  saveDB(db);
  res.json({ uploaded: req.files.length, release });
});

// --- Delete a release ---
app.delete('/api/releases/:version', (req, res) => {
  const db = loadDB();
  const idx = db.releases.findIndex(r => r.version === req.params.version);
  if (idx < 0) return res.status(404).json({ error: 'Release not found' });

  db.releases.splice(idx, 1);
  saveDB(db);

  // Remove files
  const releaseDir = path.join(RELEASES_DIR, req.params.version);
  if (fs.existsSync(releaseDir)) {
    fs.rmSync(releaseDir, { recursive: true, force: true });
  }

  res.json({ deleted: req.params.version });
});

// --- Update release notes ---
app.patch('/api/releases/:version', (req, res) => {
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (!release) return res.status(404).json({ error: 'Release not found' });

  if (req.body.notes !== undefined) release.notes = req.body.notes;
  if (req.body.channel !== undefined) release.channel = req.body.channel;
  saveDB(db);
  res.json(release);
});

// --- Publish from build output (used by build script) ---
app.post('/api/publish', upload.array('files', 50), async (req, res) => {
  const version = req.body.version || req.query.version;
  if (!version) return res.status(400).json({ error: 'Version is required' });

  const db = loadDB();
  let release = db.releases.find(r => r.version === version);

  if (!release) {
    release = {
      version,
      notes: req.body.notes || '',
      channel: req.body.channel || 'stable',
      date: new Date().toISOString(),
      files: [],
      totalDownloads: 0
    };
    db.releases.push(release);
  }

  // Move uploaded files to release directory
  const releaseDir = path.join(RELEASES_DIR, version);
  if (!fs.existsSync(releaseDir)) fs.mkdirSync(releaseDir, { recursive: true });

  for (const file of (req.files || [])) {
    const destPath = path.join(releaseDir, file.originalname);
    if (file.path !== destPath) {
      fs.copyFileSync(file.path, destPath);
    }
    const hash = await sha256File(destPath);
    const existing = release.files.findIndex(f => f.name === file.originalname);
    const fileEntry = {
      name: file.originalname,
      size: file.size,
      hash,
      downloads: 0,
      uploadedAt: new Date().toISOString()
    };
    if (existing >= 0) release.files[existing] = fileEntry;
    else release.files.push(fileEntry);
  }

  saveDB(db);

  // Auto-generate manifest.json in the release directory
  const manifest = {
    version,
    files: release.files
      .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS')
      .map(f => ({ name: f.name, hash: f.hash, size: f.size, signature: '' }))
  };
  fs.writeFileSync(path.join(releaseDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

  res.status(201).json({ published: version, files: release.files.length });
});

// --- Server stats ---
app.get('/api/stats', (req, res) => {
  const db = loadDB();
  const totalReleases = db.releases.length;
  const totalFiles = db.releases.reduce((sum, r) => sum + (r.files?.length || 0), 0);
  const totalDownloads = db.releases.reduce((sum, r) => sum + (r.totalDownloads || 0), 0);
  const latestVersion = db.releases.length > 0
    ? db.releases.sort((a, b) => compareVersions(b.version, a.version))[0].version
    : 'none';
  res.json({ totalReleases, totalFiles, totalDownloads, latestVersion });
});

// === Helpers ===
function guessMimeType(filename) {
  const ext = path.extname(filename).toLowerCase();
  const types = {
    '.msi': 'application/x-msi',
    '.exe': 'application/x-msdownload',
    '.zip': 'application/zip',
    '.json': 'application/json',
    '.dll': 'application/octet-stream',
    '.pdb': 'application/octet-stream',
    '.md': 'text/markdown',
    '.txt': 'text/plain',
    '.rtf': 'application/rtf',
    '.ico': 'image/x-icon',
    '.cer': 'application/x-x509-ca-cert',
  };
  return types[ext] || 'application/octet-stream';
}

// === SPA fallback ===
app.get('*', (req, res) => {
  res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

// === Start ===
app.listen(PORT, '0.0.0.0', () => {
  console.log(`\n  ╔══════════════════════════════════════════════════╗`);
  console.log(`  ║  Redball Update Server                            ║`);
  console.log(`  ╠══════════════════════════════════════════════════╣`);
  console.log(`  ║  http://0.0.0.0:${PORT}                            ║`);
  console.log(`  ║  Releases: ${RELEASES_DIR}`);
  console.log(`  ╚══════════════════════════════════════════════════╝\n`);
});
