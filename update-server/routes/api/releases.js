/**
 * Release management routes
 * Handles listing, creating, uploading, and managing releases
 */
const express = require('express');
const path = require('path');
const fs = require('fs');
const router = express.Router();
const { loadDB, saveDB, compareVersions } = require('../../lib/db');
const config = require('../../config');
const { RELEASES_DIR } = config;

// Shared githubCache reference - will be injected
let githubCache = { releases: null, latest: null, lastFetch: 0 };

/**
 * Set the shared githubCache reference
 * @param {Object} cache - The github cache object from server.js
 */
function setGithubCache(cache) {
    githubCache = cache;
}

// GitHub API fetch with caching
const GITHUB_CACHE_TTL = config.GITHUB_CACHE_TTL;

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
                        sourceUrl: null
                    });
                }
            }
        }

        return {
            version,
            notes: r.body || '',
            date: r.published_at || r.created_at,
            channel: r.prerelease ? 'beta' : 'stable',
            files,
            totalDownloads: files.reduce((sum, f) => sum + (f.downloads || 0), 0),
            githubUrl: r.html_url
        };
    });

    // Find local-only releases (not on GitHub)
    const localOnly = localReleases.filter(lr => !mappedGitHub.some(mr => mr.version === lr.version))
        .map(r => ({
            ...r,
            files: (r.files || []).map(f => ({
                ...f,
                url: `/api/download/${r.version}/${f.name}`,
                sourceUrl: null
            })),
            githubUrl: null
        }));

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
    const releases = await fetchGitHubReleases();
    return releases.length > 0 ? releases[0] : null;
}

// GET /api/releases - List all releases
router.get('/', async (req, res) => {
    try {
        const releases = await fetchGitHubReleases();
        res.json(releases);
    } catch (err) {
        console.error('[API] Error fetching releases:', err);
        res.status(500).json({ error: 'Failed to fetch releases' });
    }
});

// GET /api/releases/latest - Get latest release
router.get('/latest', async (req, res) => {
    try {
        const channel = req.query.channel || 'stable';
        const latest = await fetchGitHubLatest();

        if (!latest) {
            return res.status(404).json({ error: 'No releases found' });
        }

        // Filter by channel if needed
        if (channel !== 'all' && latest.channel !== channel) {
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

// GET /api/releases/:version - Get specific release
router.get('/:version', (req, res) => {
    const db = loadDB();
    const release = db.releases.find(r => r.version === req.params.version);
    if (!release) return res.status(404).json({ error: 'Release not found' });
    res.json(release);
});

// POST /api/releases - Create a new release
router.post('/', (req, res) => {
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

// DELETE /api/releases/:version - Delete a release
router.delete('/:version', (req, res) => {
    const db = loadDB();
    const idx = db.releases.findIndex(r => r.version === req.params.version);
    if (idx < 0) return res.status(404).json({ error: 'Release not found' });

    // Remove files from disk
    const release = db.releases[idx];
    const releaseDir = path.join(RELEASES_DIR, req.params.version);
    if (fs.existsSync(releaseDir)) {
        try {
            fs.rmSync(releaseDir, { recursive: true, force: true });
        } catch (e) {
            console.error('[RELEASE] Failed to delete release directory:', e.message);
        }
    }

    db.releases.splice(idx, 1);
    saveDB(db);

    // Invalidate cache
    githubCache.releases = null;
    githubCache.lastFetch = 0;

    res.json({ message: 'Release deleted', version: req.params.version });
});

// GET /api/github/releases - GitHub-compatible releases endpoint
router.get('/github/releases', async (req, res) => {
    try {
        const releases = await fetchGitHubReleases();
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
                        patch_for: f.patchFor || null
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

function guessMimeType(filename) {
    const ext = path.extname(filename).toLowerCase();
    const types = {
        '.zip': 'application/zip',
        '.msi': 'application/x-msi',
        '.exe': 'application/x-msdownload',
        '.json': 'application/json',
        '.patch': 'application/octet-stream',
        '.md': 'text/markdown',
        '.txt': 'text/plain'
    };
    return types[ext] || 'application/octet-stream';
}

module.exports = { router, setGithubCache, fetchGitHubReleases, fetchGitHubLatest };
