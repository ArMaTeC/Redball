const express = require('express');
const { WebSocketServer } = require('ws');
const pty = require('node-pty');
const path = require('path');
const fs = require('fs');
const http = require('http');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const crypto = require('crypto');
const multer = require('multer');
const morgan = require('morgan');
const rateLimit = require('express-rate-limit');

const { generatePatches, formatBytes } = require('./lib/delta-patches');

const app = express();
app.set('trust proxy', 1); // Enable trust proxy for rate limiting (needed behind Cloudflare/reverse proxy)
const server = http.createServer(app);
const wss = new WebSocketServer({ server });

const PORT = process.env.PORT || 3500;
const PROJECT_ROOT = path.join(__dirname, '..');
const RELEASES_DIR = path.join(__dirname, 'releases');
const DATA_DIR = path.join(__dirname, 'data');
const DB_PATH = path.join(DATA_DIR, 'releases.json');
const LOGS_DIR = path.join(__dirname, 'logs');

// Build State & Auth Files (migrated from web-admin)
const BUILD_STATE_FILE = path.join(LOGS_DIR, 'build-state.json');
const AUTH_FILE = path.join(LOGS_DIR, 'auth.json');
const DOWNLOADS_DB = path.join(LOGS_DIR, 'downloads.json');

// Ensure logs dir exists
if (!fs.existsSync(LOGS_DIR)) fs.mkdirSync(LOGS_DIR, { recursive: true });




// GitHub cache configuration
const GITHUB_CACHE_TTL = 5 * 60 * 1000; // 5 minutes
let githubCache = {
  releases: null,
  latest: null,
  lastFetch: 0
};

// GitHub API fetch with caching - merged with local patch data
async function fetchGitHubReleases() {
  const now = Date.now();
  if (githubCache.releases && (now - githubCache.lastFetch) < GITHUB_CACHE_TTL) {
    return githubCache.releases;
  }

  let ghReleases = [];
  try {
    const response = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases');
    if (response.ok) {
      ghReleases = await response.json();
    } else {
      console.warn(`[GitHub] API returned ${response.status} - will use local releases only`);
    }
  } catch (err) {
    console.error('[GitHub] Failed to fetch releases:', err.message);
  }

  const db = loadDB();
  const localReleases = db.releases || [];

  // Transform GitHub releases and merge with local data
  const mappedGitHub = ghReleases.map(r => {
    const version = r.tag_name.replace(/^v/, '');
    const localRelease = localReleases.find(lr => lr.version === version);

    const files = r.assets.map(a => ({
      name: a.name,
      size: a.size,
      hash: localRelease?.files?.find(lf => lf.name === a.name)?.hash || '', 
      downloads: (localRelease?.files?.find(lf => lf.name === a.name)?.downloads || 0) + a.download_count,
      url: `/api/download/${version}/${a.name}`,
      sourceUrl: a.browser_download_url
    }));

    // Merge in local files that aren't on GitHub (e.g. manifest.json, patches)
    if (localRelease?.files) {
      for (const lf of localRelease.files) {
        if (!files.some(f => f.name === lf.name)) {
          files.push({
            ...lf,
            url: `/api/download/${version}/${lf.name}`,
            sourceUrl: `/downloads/${version}/${lf.name}`
          });
        }
      }
    }

    return {
      version,
      channel: r.prerelease ? 'beta' : 'stable',
      date: r.published_at,
      notes: r.body || '',
      files,
      totalDownloads: (localRelease?.totalDownloads || 0) + r.assets.reduce((sum, a) => sum + a.download_count, 0),
      githubUrl: r.html_url,
      patchInfo: localRelease?.patchInfo || null
    };
  });

  // Include local releases that aren't on GitHub yet
  const localOnly = localReleases
    .filter(lr => !mappedGitHub.some(gr => gr.version === lr.version))
    .map(lr => {
      const files = (lr.files || []).map(f => ({
        ...f,
        url: `/api/download/${lr.version}/${f.name}`,
        sourceUrl: `/downloads/${lr.version}/${f.name}`
      }));
      return {
        ...lr,
        files,
        githubUrl: null
      };
    });

  // Combine and sort by version (newest first)
  const combined = [...mappedGitHub, ...localOnly].sort((a, b) => compareVersions(b.version, a.version));

  githubCache = {
    releases: combined,
    latest: combined[0] || null,
    lastFetch: now
  };

  return combined;
}

async function fetchGitHubLatest() {
  // Always ensure releases are fetched/cached first
  const releases = await fetchGitHubReleases();
  return releases.length > 0 ? releases[0] : null;
}

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

// === Unified Auth & State Management (Migrated from web-admin) ===

const JWT_SECRET = loadOrCreateJwtSecret();

function loadOrCreateJwtSecret() {
  const secretFile = path.join(LOGS_DIR, '.jwt-secret');
  try {
    if (fs.existsSync(secretFile)) {
      return fs.readFileSync(secretFile, 'utf8').trim();
    }
  } catch (e) { }

  const secret = crypto.randomBytes(64).toString('hex');
  try {
    fs.writeFileSync(secretFile, secret, { mode: 0o600 });
  } catch (e) {
    console.error('[AUTH] Warning: Could not save JWT secret to file');
  }
  return secret;
}

function getAdminUser() {
  const defaultUser = {
    username: 'admin',
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

function authenticateToken(req, res, next) {
  const authHeader = req.headers['authorization'];
  const token = authHeader && authHeader.split(' ')[1];
  if (!token) return res.status(401).json({ error: 'Access denied. No token provided.' });

  jwt.verify(token, JWT_SECRET, (err, user) => {
    if (err) return res.status(403).json({ error: 'Invalid or expired token' });
    req.user = user;
    next();
  });
}

function getDirSize(dirPath) {
  if (!fs.existsSync(dirPath)) return 0;
  let size = 0;
  try {
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
  } catch (e) { }
  return size;
}


let buildState = loadBuildState();
function loadBuildState() {
  const defaultState = { status: 'idle', stage: null, progress: 0, log: [], pid: null, startTime: null, endTime: null };
  try {
    if (fs.existsSync(BUILD_STATE_FILE)) {
      const saved = JSON.parse(fs.readFileSync(BUILD_STATE_FILE, 'utf8'));
      if (saved.status === 'running') {
        if (saved.stage === 'restart' || saved.stage === 'health') {
          saved.status = 'success';
          saved.progress = 100;
          saved.endTime = Date.now();
          saved.log.push({ 
            timestamp: Date.now(), 
            message: '--- Build completed successfully after planned server restart ---' 
          });
        } else {
          saved.status = 'failed';
          saved.log.push({ 
            timestamp: Date.now(), 
            message: '--- Server was restarted unexpectedly during build ---' 
          });
        }
      }
      return saved;
    }
  } catch (e) { }
  return defaultState;
}

function saveBuildState() {
  try {
    fs.writeFileSync(BUILD_STATE_FILE, JSON.stringify(buildState, null, 2));
  } catch (e) { }
}

let downloadCounts = loadDownloadCounts();
function loadDownloadCounts() {
  try {
    if (fs.existsSync(DOWNLOADS_DB)) return JSON.parse(fs.readFileSync(DOWNLOADS_DB, 'utf8'));
  } catch (e) { }
  return { files: {}, total: 0, byVersion: {} };
}

function saveDownloadCounts() {
  try {
    fs.writeFileSync(DOWNLOADS_DB, JSON.stringify(downloadCounts, null, 2));
  } catch (e) { }
}

function trackDownload(version, filename) {
  const key = `${version}/${filename}`;
  if (!downloadCounts.files[key]) downloadCounts.files[key] = 0;
  downloadCounts.files[key]++;
  downloadCounts.total++;
  if (!downloadCounts.byVersion[version]) downloadCounts.byVersion[version] = 0;
  downloadCounts.byVersion[version]++;
  saveDownloadCounts();
  return downloadCounts.files[key];
}


// === Security Middleware ===
// Rate limiting for API endpoints
const apiLimiter = rateLimit({
  windowMs: 15 * 60 * 1000, // 15 minutes
  max: 100, // limit each IP to 100 requests per windowMs
  message: { error: 'Too many requests, please try again later' },
  standardHeaders: true,
  legacyHeaders: false,
});

// Stricter rate limiting for upload endpoints
const uploadLimiter = rateLimit({
  windowMs: 60 * 60 * 1000, // 1 hour
  max: 10, // limit each IP to 10 uploads per hour
  message: { error: 'Too many uploads, please try again later' },
});

// Only use morgan logging if TTY is available (not in background)
if (process.stdin.isTTY) {
  app.use(morgan('short'));
}

app.use(express.json({ limit: '10mb' })); // Limit JSON payload size
app.use(express.static(path.join(__dirname, 'public')));

// SECURITY: Validate file extensions for uploads
const ALLOWED_EXTENSIONS = ['.zip', '.msi', '.exe', '.patch', '.json', '.md', '.txt', '.bmp', '.png', '.jpg', '.ico', '.dll', '.nsi', '.config'];
const ALLOWED_MIME_TYPES = [
  'application/zip',
  'application/x-msi',
  'application/x-msdownload',
  'application/octet-stream',
  'application/json',
  'text/plain',
  'text/markdown'
];

function validateFileUpload(file) {
  const ext = path.extname(file.originalname).toLowerCase();
  if (!ALLOWED_EXTENSIONS.includes(ext)) {
    return { valid: false, error: `File extension not allowed: ${ext}` };
  }
  return { valid: true };
}

// === Multer for file uploads with security validation ===
const storage = multer.diskStorage({
  destination: (req, file, cb) => {
    // Support both URL params (/api/releases/:version/upload) and body/query (/api/publish)
    const version = req.params.version || req.body?.version || req.query?.version;
    if (!version) {
      return cb(new Error('Version is required'));
    }
    // SECURITY: Sanitise version path to prevent path traversal
    const sanitisedVersion = version.replace(/[^a-zA-Z0-9._-]/g, '');
    if (sanitisedVersion !== version) {
      return cb(new Error('Invalid version format'));
    }
    const dir = path.join(RELEASES_DIR, sanitisedVersion);
    // SECURITY: Ensure path is within RELEASES_DIR
    const resolvedPath = path.resolve(dir);
    const resolvedReleasesDir = path.resolve(RELEASES_DIR);
    if (!resolvedPath.startsWith(resolvedReleasesDir)) {
      return cb(new Error('Path traversal detected'));
    }
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
    cb(null, dir);
  },
  filename: (req, file, cb) => {
    // SECURITY: Sanitise filename to prevent path traversal
    const sanitisedName = path.basename(file.originalname).replace(/[^a-zA-Z0-9._-]/g, '_');
    cb(null, sanitisedName);
  }
});

// SECURITY: File filter to validate uploads
const fileFilter = (req, file, cb) => {
  const validation = validateFileUpload(file);
  if (!validation.valid) {
    return cb(new Error(validation.error), false);
  }
  cb(null, true);
};

const upload = multer({
  storage,
  fileFilter,
  limits: {
    fileSize: 500 * 1024 * 1024, // 500MB max
    files: 200 // max 200 files per upload
  }
});

// ============================================================
// API Routes
// ============================================================

// Apply API rate limiting to all API routes
app.use('/api/', apiLimiter);

// --- List all releases (from GitHub with cache) ---
app.get('/api/releases', async (req, res) => {
  try {
    const releases = await fetchGitHubReleases();
    res.json(releases);
  } catch (err) {
    console.error('[API] Error fetching releases:', err);
    res.status(500).json({ error: 'Failed to fetch releases' });
  }
});

// --- Get latest release (from GitHub with cache) ---
app.get('/api/releases/latest', async (req, res) => {
  try {
    const channel = req.query.channel || 'stable';
    const latest = await fetchGitHubLatest();

    if (!latest) {
      return res.status(404).json({ error: 'No releases found' });
    }

    // Filter by channel if needed
    if (channel !== 'all' && latest.channel !== channel) {
      // If latest doesn't match channel, fetch all and filter
      const all = await fetchGitHubReleases();
      const filtered = all.filter(r => r.channel === channel);
      if (filtered.length === 0) {
        return res.status(404).json({ error: 'No releases found for channel' });
      }
      return res.json(filtered[0]);
    }

    res.json(latest);
  } catch (err) {
    console.error('[API] Error fetching latest:', err);
    res.status(500).json({ error: 'Failed to fetch latest release' });
  }
});

// --- Get specific release ---
app.get('/api/releases/:version', (req, res) => {
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (!release) return res.status(404).json({ error: 'Release not found' });
  res.json(release);
});

// --- GitHub-compatible releases endpoint (fetches from GitHub API with cache) ---
app.get('/api/github/releases', async (req, res) => {
  try {
    const releases = await fetchGitHubReleases();
    // Map to GitHub release format
    const ghReleases = releases.map(r => ({
      tag_name: `v${r.version}`,
      name: `Redball v${r.version}`,
      body: r.notes,
      prerelease: r.channel !== 'stable',
      draft: false,
      published_at: r.date,
      assets: (r.files || [])
        .filter(f => !f.name.endsWith('.msi') && f.name !== 'SHA256SUMS')
        .map(f => {
          // Patch files are in the patches subdirectory
          const isPatch = f.name.endsWith('.patch');
          const downloadUrl = isPatch
            ? `${req.protocol}://${req.get('host')}/downloads/${r.version}/patches/${f.name}`
            : (f.url || `${req.protocol}://${req.get('host')}/downloads/${r.version}/${f.name}`);
          return {
            name: f.name,
            size: f.size,
            browser_download_url: downloadUrl,
            content_type: guessMimeType(f.name),
            downloads: f.downloads || 0,
            patch_for: f.patchFor || null  // Include patch metadata if available
          };
        }),
      html_url: r.githubUrl
    }));
    res.json(ghReleases);
  } catch (err) {
    console.error('[API] Error fetching GitHub releases:', err);
    res.status(500).json({ error: 'Failed to fetch releases' });
  }
});

// --- Download a file ---
app.get('/downloads/:version/:filename', (req, res) => {
  // SECURITY: Sanitize filename to prevent path traversal
  const sanitizedFilename = path.basename(req.params.filename);
  if (sanitizedFilename !== req.params.filename) {
    console.warn(`[SECURITY] Path traversal attempt blocked: ${req.params.filename}`);
    return res.status(400).json({ error: 'Invalid filename' });
  }

  const filePath = path.join(RELEASES_DIR, req.params.version, sanitizedFilename);

  // SECURITY: Verify resolved path is within allowed directory
  const resolvedPath = path.resolve(filePath);
  const allowedDir = path.resolve(path.join(RELEASES_DIR, req.params.version));
  if (!resolvedPath.startsWith(allowedDir)) {
    console.warn(`[SECURITY] Path escape attempt blocked: ${req.params.filename}`);
    return res.status(400).json({ error: 'Invalid path' });
  }

  if (!fs.existsSync(filePath)) return res.status(404).json({ error: 'File not found' });

  // Track download count
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (release) {
    const file = (release.files || []).find(f => f.name === sanitizedFilename);
    if (file) {
      file.downloads = (file.downloads || 0) + 1;
      release.totalDownloads = (release.totalDownloads || 0) + 1;
      saveDB(db);
    }
  }

  res.download(filePath);
});

// --- Get manifest.json for a release (differential updates) ---
app.get('/api/releases/:version/manifest', async (req, res) => {
  const db = loadDB();
  const release = db.releases.find(r => r.version === req.params.version);
  if (!release) return res.status(404).json({ error: 'Release not found' });

  // Build manifest.json compatible with UpdateService.cs
  const manifest = {
    version: release.version,
    files: (release.files || [])
      .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS' && !f.name.endsWith('.patch'))
      .map(f => ({
        name: f.name,
        hash: f.hash || '',
        size: f.size || 0,
        signature: f.signature || ''
      }))
  };

  // Add patch information if available
  const patchesDir = path.join(RELEASES_DIR, req.params.version, 'patches');
  if (fs.existsSync(patchesDir)) {
    const patchManifestPath = path.join(patchesDir, 'patch-manifest.json');
    if (fs.existsSync(patchManifestPath)) {
      try {
        const patchManifest = JSON.parse(fs.readFileSync(patchManifestPath, 'utf8'));
        manifest.fromVersion = patchManifest.fromVersion;
        manifest.patches = patchManifest.patches.map(p => ({
          file: p.file,
          patchFile: p.patchFile,
          patchSize: p.patchSize,
          originalSize: p.originalSize,
          savings: p.savings
        }));
        manifest.totalSavings = patchManifest.totalSavings;
      } catch {
        // ignore parse errors
      }
    }
  }

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

  // Invalidate cache
  githubCache.releases = null;
  githubCache.lastFetch = 0;

  res.status(201).json(release);
});

// --- Upload files to a release ---
app.post('/api/releases/:version/upload', uploadLimiter, upload.array('files', 5), async (req, res) => {
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
  
  // Invalidate cache
  githubCache.releases = null;
  githubCache.lastFetch = 0;

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
app.post('/api/publish', uploadLimiter, (req, res, next) => {
  upload.array('files', 200)(req, res, (err) => {
    if (err) {
      console.error('[Upload Error]', err.message);
      return res.status(400).json({ error: 'File upload failed: ' + err.message });
    }
    next();
  });
}, async (req, res) => {
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

  // Auto-generate manifest.json in the release directory if it doesn't exist
  // We prefer the detailed manifest generated by the build script
  const manifestPath = path.join(releaseDir, 'manifest.json');
  if (!fs.existsSync(manifestPath)) {
    const manifest = {
      version,
      files: release.files
        .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS')
        .map(f => ({ name: f.name, hash: f.hash, size: f.size, signature: '' }))
    };
    fs.writeFileSync(manifestPath, JSON.stringify(manifest, null, 2));
  }

  // Auto-generate delta patches from previous version
  generatePatchesForRelease(version);

  // Invalidate cache
  githubCache.releases = null;
  githubCache.lastFetch = 0;

  res.status(201).json({ published: version, files: release.files.length });
});

// --- Server stats (Unified for public and admin) ---
app.get('/api/download/:version/:filename', async (req, res) => {
  const { version, filename } = req.params;
  
  // 1. Track the download in our local DB
  trackDownload(version, filename);
  
  // 2. Find the source URL
  const releases = await fetchGitHubReleases();
  const release = releases.find(r => r.version === version);
  const file = release?.files?.find(f => f.name === filename);
  
  const sourceUrl = file?.sourceUrl || `/downloads/${version}/${filename}`;
  
  // 3. Redirect to the actual file
  if (sourceUrl.startsWith('http')) {
    res.redirect(sourceUrl);
  } else {
    // If it's a local path, ensure it starts with /downloads
    const redirectUrl = sourceUrl.startsWith('/downloads') ? sourceUrl : `/downloads/${sourceUrl.replace(/^\.\//, '')}`;
    res.redirect(redirectUrl);
  }
});

app.get('/api/stats', async (req, res) => {
  try {
    const releases = await fetchGitHubReleases();
    const totalReleases = releases.length;
    const latestVersion = releases.length > 0 ? releases[0].version : '0.0.0';
    
    // Detailed file-level stats
    const byFile = {};
    for (const r of releases) {
      if (!r.files) continue;
      for (const f of r.files) {
        if (!byFile[f.name]) {
          byFile[f.name] = { downloads: 0, versions: [] };
        }
        byFile[f.name].downloads += (f.downloads || 0);
        if (!byFile[f.name].versions.includes(r.version)) {
          byFile[f.name].versions.push(r.version);
        }
      }
    }

    // Add local download tracking data if available
    for (const [key, count] of Object.entries(downloadCounts.files || {})) {
      const filename = key.split('/').pop();
      if (byFile[filename]) {
        byFile[filename].downloads += count;
      } else {
        byFile[filename] = { downloads: count, versions: [key.split('/')[0]] };
      }
    }

    const totalDownloads = Object.values(byFile).reduce((sum, f) => sum + f.downloads, 0);

    res.json({
      totalReleases,
      totalDownloads,
      latestVersion,
      byFile,
      releases,
      activeBuilds: buildState.status === 'running' ? 1 : 0
    });
  } catch (err) {
    console.error('[API] Error fetching stats:', err);
    res.status(500).json({ error: 'Failed to fetch stats' });
  }
});



// --- System Information endpoint ---
app.get('/api/system/config', authenticateToken, (req, res) => {
  const db = loadDB();
  const releasesSize = getDirSize(RELEASES_DIR);
  const logsSize = getDirSize(LOGS_DIR);
  
  res.json({
    hostname: require('os').hostname(),
    platform: require('os').platform(),
    nodeVersion: process.version,
    uptime: process.uptime(),
    webPort: process.env.WEB_PORT || 3000,
    updatePort: PORT,
    env: process.env.NODE_ENV || 'development',
    releaseCount: db.releases?.length || 0,
    dataDir: DATA_DIR,
    releasesDir: RELEASES_DIR,
    projectRoot: PROJECT_ROOT,
    diskUsage: {
      releases: releasesSize,
      logs: logsSize,
      total: releasesSize + logsSize
    },
    metrics: {
      freeMem: require('os').freemem(),
      totalMem: require('os').totalmem(),
      loadAvg: require('os').loadavg(),
      processMemory: process.memoryUsage(),
      cpuCount: require('os').cpus().length
    }
  });
});


// Default client configuration
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

const CLIENT_CONFIG_PATH = path.join(DATA_DIR, 'client-config.json');

function loadClientConfig() {
  if (!fs.existsSync(CLIENT_CONFIG_PATH)) {
    return { ...DEFAULT_CLIENT_CONFIG };
  }
  try {
    const data = JSON.parse(fs.readFileSync(CLIENT_CONFIG_PATH, 'utf8'));
    return { ...DEFAULT_CLIENT_CONFIG, ...data };
  } catch (err) {
    console.error('[Config] Failed to load client config:', err.message);
    return { ...DEFAULT_CLIENT_CONFIG };
  }
}

function saveClientConfig(config) {
  try {
    fs.writeFileSync(CLIENT_CONFIG_PATH, JSON.stringify(config, null, 2));
    return true;
  } catch (err) {
    console.error('[Config] Failed to save client config:', err.message);
    return false;
  }
}

// --- Get default client configuration ---
app.get('/api/config', (req, res) => {
  const config = loadClientConfig();
  res.json(config);
});

// --- Update default client configuration ---
app.post('/api/config', (req, res) => {
  const currentConfig = loadClientConfig();
  const newConfig = { ...currentConfig, ...req.body };

  if (saveClientConfig(newConfig)) {
    res.json({ success: true, config: newConfig });
  } else {
    res.status(500).json({ error: 'Failed to save configuration' });
  }
});

// --- Get delta patches for a version ---
app.get('/api/releases/:version/patches', (req, res) => {
  const patchesDir = path.join(RELEASES_DIR, req.params.version, 'patches');

  if (!fs.existsSync(patchesDir)) {
    return res.json({ patches: [], fromVersion: null });
  }

  // Read patch manifest if exists
  const manifestPath = path.join(patchesDir, 'patch-manifest.json');
  let manifest = null;
  if (fs.existsSync(manifestPath)) {
    try {
      manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
    } catch {
      // ignore
    }
  }

  // Get all patch files
  const patches = [];
  const files = fs.readdirSync(patchesDir);
  for (const file of files) {
    if (file.endsWith('.patch')) {
      const stat = fs.statSync(path.join(patchesDir, file));
      const patchInfo = manifest?.patches?.find(p => p.patchFile === file);
      patches.push({
        name: file,
        size: stat.size,
        targetFile: file.replace('.patch', ''),
        ...patchInfo
      });
    }
  }

  res.json({
    version: req.params.version,
    fromVersion: manifest?.fromVersion || null,
    patches,
    totalSavings: manifest?.totalSavings || 0
  });
});

// --- Download a patch file ---
app.get('/downloads/:version/patches/:filename', (req, res) => {
  // SECURITY: Sanitize filename to prevent path traversal
  const sanitizedFilename = path.basename(req.params.filename);
  if (sanitizedFilename !== req.params.filename) {
    Logger.Warning("UpdateServer", `Path traversal attempt blocked: ${req.params.filename}`);
    return res.status(400).json({ error: 'Invalid filename' });
  }

  const filePath = path.join(RELEASES_DIR, req.params.version, 'patches', sanitizedFilename);

  // SECURITY: Verify resolved path is within allowed directory
  const resolvedPath = path.resolve(filePath);
  const allowedDir = path.resolve(path.join(RELEASES_DIR, req.params.version, 'patches'));
  if (!resolvedPath.startsWith(allowedDir)) {
    Logger.Warning("UpdateServer", `Path escape attempt blocked: ${req.params.filename}`);
    return res.status(400).json({ error: 'Invalid path' });
  }

  if (!fs.existsSync(filePath)) return res.status(404).json({ error: 'Patch not found' });

  res.download(filePath);
});

// --- Trigger patch generation for all versions ---
app.post('/api/admin/generate-patches', async (req, res) => {
  // Run in background
  res.json({ message: 'Patch generation started in background' });

  try {
    const { generateAllPatches } = require('./lib/delta-patches');
    await generateAllPatches(RELEASES_DIR);
    console.log('[PATCH] Background patch generation completed');
  } catch (err) {
    console.error('[PATCH] Background generation failed:', err);
  }
});

// === Helper: Generate patches for a release ===
async function generatePatchesForRelease(newVersion) {
  try {
    // Find previous version
    const versions = fs.readdirSync(RELEASES_DIR)
      .filter(v => fs.statSync(path.join(RELEASES_DIR, v)).isDirectory())
      .sort(compareVersions);

    const newIndex = versions.indexOf(newVersion);
    if (newIndex <= 0) {
      console.log(`[PATCH] No previous version found for ${newVersion}`);
      return;
    }

    const oldVersion = versions[newIndex - 1];
    console.log(`[PATCH] Generating patches: ${oldVersion} → ${newVersion}`);

    const oldDir = path.join(RELEASES_DIR, oldVersion);
    const newDir = path.join(RELEASES_DIR, newVersion);
    const patchesDir = path.join(newDir, 'patches');

    const results = await generatePatches(oldDir, newDir, patchesDir);

    console.log(`[PATCH] Generated ${results.generated.length} patches (${formatBytes(results.totalSavings)} saved)`);

    // Save manifest
    const manifest = {
      fromVersion: oldVersion,
      toVersion: newVersion,
      generatedAt: new Date().toISOString(),
      patches: results.generated,
      skipped: results.skipped,
      totalSavings: results.totalSavings,
      totalOriginalSize: results.totalOriginalSize
    };

    fs.writeFileSync(
      path.join(patchesDir, 'patch-manifest.json'),
      JSON.stringify(manifest, null, 2)
    );

    // Update release files in database with patch info
    const db = loadDB();
    const release = db.releases.find(r => r.version === newVersion);
    if (release) {
      // Add patch files to release
      for (const patch of results.generated) {
        const patchFilePath = path.join(patchesDir, patch.patchFile);
        if (fs.existsSync(patchFilePath)) {
          const stat = fs.statSync(patchFilePath);
          const hash = await sha256File(patchFilePath);

          // Check if already added
          const existing = release.files.findIndex(f => f.name === patch.patchFile);
          const patchEntry = {
            name: patch.patchFile,
            size: stat.size,
            hash: hash,
            downloads: 0,
            uploadedAt: new Date().toISOString(),
            patchFor: patch.file,
            originalSize: patch.originalSize,
            savings: patch.savings
          };

          if (existing >= 0) {
            release.files[existing] = patchEntry;
          } else {
            release.files.push(patchEntry);
          }
        }
      }

      release.patchInfo = {
        fromVersion: oldVersion,
        generatedAt: manifest.generatedAt,
        totalPatches: results.generated.length,
        totalSavings: results.totalSavings
      };

      saveDB(db);
    }

  } catch (err) {
    console.error('[PATCH] Error generating patches:', err);
  }
}

// === Helper: Compare versions ===
function compareVersions(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const na = pa[i] || 0;
    const nb = pb[i] || 0;
    if (na !== nb) return na - nb;
  }
  return 0;
}

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

// --- Check for update with delta patch support and fallback to full installer ---
app.get('/api/check-update', async (req, res) => {
  const { currentVersion, channel = 'stable' } = req.query;

  if (!currentVersion) {
    return res.status(400).json({ error: 'currentVersion is required' });
  }

  try {
    const releases = await fetchGitHubReleases();
    const latest = releases.find(r => channel === 'all' || r.channel === channel);

    if (!latest) {
      return res.json({
        updateAvailable: false,
        message: 'No releases found for channel: ' + channel
      });
    }

    // Check if update is needed
    const versionCompare = compareVersions(latest.version, currentVersion);
    if (versionCompare <= 0) {
      return res.json({
        updateAvailable: false,
        currentVersion,
        latestVersion: latest.version,
        message: 'Already on latest version'
      });
    }

    // Check if delta patch is available
    const patchesDir = path.join(RELEASES_DIR, latest.version, 'patches');
    const patchManifestPath = path.join(patchesDir, 'patch-manifest.json');
    let patchAvailable = false;
    let patchInfo = null;

    if (fs.existsSync(patchManifestPath)) {
      try {
        const patchManifest = JSON.parse(fs.readFileSync(patchManifestPath, 'utf8'));
        // Check if patch is from this specific version
        if (patchManifest.fromVersion === currentVersion) {
          patchAvailable = true;
          patchInfo = {
            fromVersion: patchManifest.fromVersion,
            toVersion: patchManifest.toVersion,
            totalSavings: patchManifest.totalSavings,
            patches: patchManifest.patches
          };
        }
      } catch {
        // ignore parse errors
      }
    }

    // Build response
    const response = {
      updateAvailable: true,
      currentVersion,
      latestVersion: latest.version,
      channel: latest.channel,
      releaseDate: latest.date,
      releaseNotes: latest.notes,
      downloadUrl: latest.files.find(f => f.name.endsWith('.exe') || f.name.endsWith('.zip'))?.url || null,
      githubUrl: latest.githubUrl,
      patchAvailable,
      updateMethod: patchAvailable ? 'delta' : 'full',
      message: patchAvailable
        ? `Delta update available: ${currentVersion} → ${latest.version}`
        : `Full installer update: ${currentVersion} → ${latest.version}`
    };

    if (patchAvailable && patchInfo) {
      response.patchInfo = patchInfo;
      response.patchDownloadUrl = `${req.protocol}://${req.get('host')}/downloads/${latest.version}/patches/`;
    }

    // Include full installer info as fallback for silent update
    const installerFile = latest.files.find(f => f.name.includes('Setup.exe') || f.name.endsWith('.exe'));
    if (installerFile) {
      response.fullInstaller = {
        name: installerFile.name,
        size: installerFile.size,
        url: installerFile.url,
        hash: installerFile.hash || ''
      };
      response.silentUpdateSupported = installerFile.name.includes('Setup.exe');
    }

    res.json(response);

  } catch (err) {
    console.error('[API] Error checking for update:', err);
    res.status(500).json({ error: 'Failed to check for updates' });
  }
});

// === Unified Build & Admin Logic (Migrated from web-admin) ===

function broadcast(msg) {
  wss.clients.forEach(client => {
    if (client.readyState === 1) { // OPEN
      client.send(JSON.stringify(msg));
    }
  });
}

wss.on('connection', (ws) => {
  console.log('[WS] New client connected');
  ws.send(JSON.stringify({ type: 'state', data: buildState }));

  ws.on('message', (message) => {
    try {
      const data = JSON.parse(message);
      if (data.action === 'start-build') {
        if (buildState.status !== 'running') {
          startBuild();
        }
      } else if (data.action === 'stop-build') {
        stopBuild();
      }
    } catch (e) {
      console.error('[WS] Message error:', e);
    }
  });

  ws.on('close', () => console.log('[WS] Client disconnected'));
});


// Admin Auth Routes
app.post('/api/auth/login', async (req, res) => {
  const { username, password } = req.body;
  const admin = getAdminUser();

  if (username === admin.username && await bcrypt.compare(password, admin.passwordHash)) {
    const token = jwt.sign({ username }, JWT_SECRET, { expiresIn: '24h' });
    res.json({ token, user: { username } });
  } else {
    res.status(401).json({ error: 'Invalid credentials' });
  }
});

app.get('/api/auth/me', authenticateToken, (req, res) => {
  res.json({ user: req.user });
});


// Build Trigger (Aliased for compatibility)
app.post(['/api/admin/build', '/api/build/start'], authenticateToken, (req, res) => {
  if (buildState.status === 'running') {
    return res.status(409).json({ error: 'Build already in progress' });
  }
  
  startBuild();
  res.json({ message: 'Build started', status: 'running' });
});

app.post('/api/build/stop', authenticateToken, (req, res) => {
  stopBuild();
  res.json({ message: 'Build stopping', status: 'stopping' });
});


app.get('/api/admin/status', authenticateToken, (req, res) => {
  res.json(buildState);
});

async function getSystemStats() {
  const releases = await fetchGitHubReleases();
  return {
    totalDownloads: downloadCounts.total || 0,
    latestVersion: releases[0]?.version || '0.0.0',
    totalReleases: releases.length,
    activeBuilds: buildState.status === 'running' ? 1 : 0
  };
}

// Note: Consolidated into public /api/stats above


// --- Build Orchestration Logic ---
function parseBuildStage(line) {
  const stages = [
    { name: 'setup', pattern: /Phase 1|Checking build dependencies|Repairing missing.*Wine .NET SDK/i, progress: 3 },
    { name: 'version', pattern: /Phase 0|Bumping version/i, progress: 6 },
    { name: 'restore', pattern: /Restoring NuGet packages/i, progress: 12 },
    { name: 'wpf-build', pattern: /Building WPF Application|Compiling WPF project/i, progress: 25 },
    { name: 'service-build', pattern: /Building Redball Service/i, progress: 45 },
    { name: 'manifest', pattern: /Generating manifest\.json/i, progress: 55 },
    { name: 'zip-bundle', pattern: /Creating portable ZIP package/i, progress: 62 },
    { name: 'installer', pattern: /Building NSIS Installer|Creating Windows installer package/i, progress: 75 },
    { name: 'update-server', pattern: /Phase 4|Update Server/i, progress: 82 },
    { name: 'publish', pattern: /Phase 5|Publish to Update Server/i, progress: 88 },
    { name: 'github', pattern: /Phase 6|Publish to GitHub/i, progress: 94 },
    { name: 'restart', pattern: /Phase 7|Restarting update server/i, progress: 97 },
    { name: 'health', pattern: /Phase 9|Final health status check/i, progress: 99 },
    { name: 'complete', pattern: /AUTO-RELEASE COMPLETED/i, progress: 100 }
  ];


  for (const s of stages) {
    if (s.pattern.test(line)) return s;
  }
  if (/(?<!0\s)(error|failed|abort)/i.test(line)) return { name: 'error', progress: buildState.progress };
  return null;
}

function startBuild() {
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
    env: { ...process.env, FORCE_COLOR: '1', BUILD_BY_SERVICE: '1' }
  });

  let restartNeeded = false;
  buildState.pid = ptyProcess.pid;

  ptyProcess.onData((data) => {
    const rawContent = data.toString();
    if (!rawContent.trim()) return;

    // Split into individual lines to ensure we don't skip stages if they arrive in a single burst
    // Also handle \r (carriage return) which is often used for in-place progress updates
    const lines = rawContent.split(/\r?\n|\r/);
    
    for (let line of lines) {
      if (!line.trim()) continue;
      
      // Strip ANSI codes before parsing stage to ensure regex matches correctly
      const cleanLine = line.replace(/\u001b\[[0-9;?]*[a-zA-Z]/g, '').trim();
      if (!cleanLine) continue;

      // Ignore noise lines like MSBuild progress timer (1.4s) or left-over ANSI fragments
      if (/^\([0-9.]+s\)$/.test(cleanLine) || /^\[[0-9;?]*[a-zA-Z]/.test(cleanLine)) continue;

      buildState.log.push({ timestamp: Date.now(), message: line });
      const stage = parseBuildStage(cleanLine);
      if (stage) {
        buildState.stage = stage.name;
        buildState.progress = stage.progress;
      }
      
      if (buildState.log.length > 5000) buildState.log = buildState.log.slice(-2500);
      if (buildState.log.length % 50 === 0 || stage) saveBuildState();
      
      if (cleanLine.includes('[SIGNAL] RESTART_NEEDED')) {
        restartNeeded = true;
      }

      broadcast({
        type: 'build-output',
        data: { 
          line, 
          stage: buildState.stage, 
          progress: buildState.progress 
        }
      });
    }
  });

  ptyProcess.onExit(({ exitCode }) => {
    buildState.status = exitCode === 0 ? 'success' : 'failed';
    buildState.endTime = Date.now();
    buildState.progress = exitCode === 0 ? 100 : buildState.progress;
    buildState.pid = null;
    saveBuildState();

    broadcast({
      type: 'build-complete',
      data: { status: buildState.status, exitCode, duration: buildState.endTime - buildState.startTime }
    });

    if (exitCode === 0 && restartNeeded) {
      console.log('[BUILD] Restart signaled, scheduling self-restart in 5s...');
      buildState.log.push({ timestamp: Date.now(), message: '[SYSTEM] Restarting server to apply updates...' });
      saveBuildState();
      
      const { exec } = require('child_process');
      setTimeout(() => {
        exec('pm2 restart redball-update-server', (err) => {
          if (err) {
            console.error('[BUILD] Self-restart failed, exiting instead:', err);
            process.exit(0);
          }
        });
      }, 5000);
    }
  });
}function stopBuild() {
  if (buildState.pid) {
    try {
      process.kill(buildState.pid, 'SIGTERM');
      buildState.status = 'stopped';
      buildState.endTime = Date.now();
      saveBuildState();
      broadcast({ type: 'build-stopped', data: buildState });
    } catch (e) {
      console.error('[BUILD] Error stopping build:', e.message);
    }
  }
}

// --- Health Check ---
app.get('/api/health', (req, res) => {
  res.json({
    status: 'healthy',
    server: 'unified-redball-server',
    timestamp: new Date().toISOString(),
    uptime: process.uptime(),
    version: process.env.npm_package_version || '1.0.0'
  });
});

// === Unified Static Serving ===
const ADMIN_PUBLIC = path.join(PROJECT_ROOT, 'web-admin', 'public');
const SITE_PUBLIC = path.join(PROJECT_ROOT, 'site', 'dist');

app.use('/admin', express.static(ADMIN_PUBLIC, { index: 'admin.html' }));
app.use('/downloads', express.static(RELEASES_DIR));
app.use(express.static(SITE_PUBLIC, { index: 'index.html' }));

// SPA Fallbacks
app.get(/^\/admin\/(.*)/, (req, res) => res.sendFile(path.join(ADMIN_PUBLIC, 'admin.html')));
app.get(/^(?!\/api|\/downloads).*/, (req, res, next) => {
  res.sendFile(path.join(SITE_PUBLIC, 'index.html'));
});

// --- Scheduled Cleanup Job ---
const { cleanupReleases } = require('./scripts/cleanup-releases');
const CLEANUP_INTERVAL = 24 * 60 * 60 * 1000;

function scheduleCleanup() {
  console.log('[SCHEDULER] Release cleanup scheduled');
  setTimeout(() => { try { cleanupReleases(); } catch (e) { } }, 5000);
  setInterval(() => { try { cleanupReleases(); } catch (e) { } }, CLEANUP_INTERVAL);
}


// === Start ===
server.listen(PORT, '0.0.0.0', () => {
  console.log(`\n  ╔══════════════════════════════════════════════════╗`);
  console.log(`  ║  Redball Unified Server (Admin + Update)          ║`);
  console.log(`  ╠══════════════════════════════════════════════════╣`);
  console.log(`  ║  http://0.0.0.0:${PORT}                            ║`);
  console.log(`  ║  Dashboard: http://localhost:${PORT}/admin       ║`);
  console.log(`  ╚══════════════════════════════════════════════════╝\n`);

  scheduleCleanup();
});
