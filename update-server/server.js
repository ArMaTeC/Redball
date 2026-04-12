const express = require('express');
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const multer = require('multer');
const morgan = require('morgan');
const rateLimit = require('express-rate-limit');

const { generatePatches, formatBytes } = require('./lib/delta-patches');

const app = express();
const PORT = process.env.PORT || 3500;
const RELEASES_DIR = path.join(__dirname, 'releases');
const DATA_DIR = path.join(__dirname, 'data');
const DB_PATH = path.join(DATA_DIR, 'releases.json');

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

  try {
    const response = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases');
    if (!response.ok) throw new Error(`GitHub API error: ${response.status}`);

    const ghReleases = await response.json();
    const db = loadDB();

    // Transform GitHub format to our format and merge with local patch data
    const releases = ghReleases.map(r => {
      const version = r.tag_name.replace(/^v/, '');
      const localRelease = db.releases.find(lr => lr.version === version);

      // Start with GitHub assets
      const files = r.assets.map(a => ({
        name: a.name,
        size: a.size,
        hash: '', // GitHub doesn't provide hashes in the API
        downloads: a.download_count,
        url: a.browser_download_url
      }));

      // Merge with local patch files if available
      if (localRelease?.files) {
        const patchFiles = localRelease.files.filter(f => f.name.endsWith('.patch'));
        for (const patch of patchFiles) {
          const existing = files.findIndex(f => f.name === patch.name);
          if (existing >= 0) {
            files[existing] = { ...files[existing], ...patch };
          } else {
            files.push(patch);
          }
        }
      }

      return {
        version,
        channel: r.prerelease ? 'beta' : 'stable',
        date: r.published_at,
        notes: r.body || '',
        files,
        totalDownloads: r.assets.reduce((sum, a) => sum + a.download_count, 0),
        githubUrl: r.html_url,
        patchInfo: localRelease?.patchInfo || null
      };
    });

    githubCache = {
      releases,
      latest: releases[0] || null,
      lastFetch: now
    };

    return releases;
  } catch (err) {
    console.error('[GitHub] Failed to fetch releases:', err.message);
    // Return cached data even if stale
    if (githubCache.releases && githubCache.releases.length > 0) {
      console.log('[GitHub] Returning stale cached releases');
      return githubCache.releases;
    }
    // Fall back to local database releases
    const db = loadDB();
    if (db.releases && db.releases.length > 0) {
      console.log(`[GitHub] Falling back to local database with ${db.releases.length} releases`);
      return db.releases;
    }
    return [];
  }
}

async function fetchGitHubLatest() {
  const now = Date.now();
  if (githubCache.latest && (now - githubCache.lastFetch) < GITHUB_CACHE_TTL) {
    return githubCache.latest;
  }

  try {
    const response = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases/latest');
    if (!response.ok) throw new Error(`GitHub API error: ${response.status}`);

    const r = await response.json();

    const latest = {
      version: r.tag_name.replace(/^v/, ''),
      channel: r.prerelease ? 'beta' : 'stable',
      date: r.published_at,
      notes: r.body || '',
      files: r.assets.map(a => ({
        name: a.name,
        size: a.size,
        hash: '',
        downloads: a.download_count,
        url: a.browser_download_url
      })),
      totalDownloads: r.assets.reduce((sum, a) => sum + a.download_count, 0),
      githubUrl: r.html_url
    };

    githubCache.latest = latest;
    githubCache.lastFetch = now;

    return latest;
  } catch (err) {
    console.error('[GitHub] Failed to fetch latest:', err.message);
    // Return cached data even if stale
    if (githubCache.latest) {
      console.log('[GitHub] Returning stale cached latest release');
      return githubCache.latest;
    }
    // Fall back to local database - return the most recent release
    const db = loadDB();
    if (db.releases && db.releases.length > 0) {
      // Sort by version and return the latest
      const sorted = [...db.releases].sort((a, b) => compareVersions(b.version, a.version));
      console.log(`[GitHub] Falling back to local database latest: ${sorted[0].version}`);
      return sorted[0];
    }
    return null;
  }
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
const ALLOWED_EXTENSIONS = ['.zip', '.msi', '.exe', '.patch', '.json', '.md', '.txt'];
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
    files: 5 // max 5 files per upload
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
      published_at: r.date,
      assets: (r.files || [])
        .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS' && !f.name.startsWith('patches/'))
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
  upload.array('files', 5)(req, res, (err) => {
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

  // Auto-generate manifest.json in the release directory
  const manifest = {
    version,
    files: release.files
      .filter(f => !f.name.endsWith('.msi') && f.name !== 'manifest.json' && f.name !== 'SHA256SUMS')
      .map(f => ({ name: f.name, hash: f.hash, size: f.size, signature: '' }))
  };
  fs.writeFileSync(path.join(releaseDir, 'manifest.json'), JSON.stringify(manifest, null, 2));

  // Auto-generate delta patches from previous version
  generatePatchesForRelease(version);

  res.status(201).json({ published: version, files: release.files.length });
});

// --- Server stats (from GitHub with cache) ---
app.get('/api/stats', async (req, res) => {
  try {
    const releases = await fetchGitHubReleases();
    const totalReleases = releases.length;
    const totalFiles = releases.reduce((sum, r) => sum + (r.files?.length || 0), 0);
    const totalDownloads = releases.reduce((sum, r) => sum + (r.totalDownloads || 0), 0);
    const latestVersion = releases.length > 0 ? releases[0].version : 'none';
    res.json({ totalReleases, totalFiles, totalDownloads, latestVersion, releases });
  } catch (err) {
    console.error('[API] Error fetching stats:', err);
    res.status(500).json({ error: 'Failed to fetch stats' });
  }
});

// --- Health check endpoint ---
app.get('/api/health', (req, res) => {
  res.json({
    status: 'healthy',
    server: 'update-server',
    timestamp: new Date().toISOString(),
    uptime: process.uptime(),
    version: process.env.npm_package_version || '1.0.0'
  });
});

// --- System Information endpoint ---
app.get('/api/system/config', (req, res) => {
  const db = loadDB();
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
    releasesDir: RELEASES_DIR
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

// --- Trigger Build Engine ---
app.post('/api/admin/build', (req, res) => {
  // Simulates an async build trigger. 
  // In a true environment, this spawns `pwsh ./scripts/build.ps1`
  // and pipes output to a WebSocket.
  console.log('[BUILD] Simulated build trigger initiated.');
  setTimeout(() => {
    console.log('[BUILD] Simulated build trigger finished.');
  }, 3000);
  res.status(202).json({ message: 'Build started', status: 'processing' });
});

// === SPA fallback (catch-all for non-API routes) ===
app.use((req, res, next) => {
  if (req.path.startsWith('/api/') || req.path.startsWith('/downloads/')) {
    return next();
  }
  res.sendFile(path.join(__dirname, 'public', 'index.html'));
});

// === Scheduled Cleanup Job ===
const { cleanupReleases } = require('./scripts/cleanup-releases');

// Run cleanup every 24 hours
const CLEANUP_INTERVAL = 24 * 60 * 60 * 1000; // 24 hours

function scheduleCleanup() {
  console.log('[SCHEDULER] Release cleanup scheduled (runs every 24 hours, keeps last 6 versions)');

  // Run immediately on startup
  setTimeout(() => {
    try {
      cleanupReleases();
    } catch (err) {
      console.error('[SCHEDULER] Cleanup failed:', err);
    }
  }, 5000); // 5 seconds after startup

  // Schedule recurring cleanup
  setInterval(() => {
    try {
      cleanupReleases();
    } catch (err) {
      console.error('[SCHEDULER] Scheduled cleanup failed:', err);
    }
  }, CLEANUP_INTERVAL);
}

// === Start ===
app.listen(PORT, '0.0.0.0', () => {
  console.log(`\n  ╔══════════════════════════════════════════════════╗`);
  console.log(`  ║  Redball Update Server                            ║`);
  console.log(`  ╠══════════════════════════════════════════════════╣`);
  console.log(`  ║  http://0.0.0.0:${PORT}                            ║`);
  console.log(`  ║  Releases: ${RELEASES_DIR}`);
  console.log(`  ╚══════════════════════════════════════════════════╝\n`);

  // Start scheduled cleanup
  scheduleCleanup();
});
