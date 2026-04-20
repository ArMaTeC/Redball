/**
 * Database operations for the update server
 * Manages releases.json and downloads.json
 */
const fs = require('fs');
const { DB_PATH, DOWNLOADS_DB } = require('../config');

/**
 * Load the releases database
 * @returns {{releases: Array}} Database object
 */
function loadDB() {
    if (!fs.existsSync(DB_PATH)) return { releases: [] };
    try {
        return JSON.parse(fs.readFileSync(DB_PATH, 'utf8'));
    } catch {
        return { releases: [] };
    }
}

/**
 * Save the releases database
 * @param {*} db - Database object to save
 */
function saveDB(db) {
    fs.writeFileSync(DB_PATH, JSON.stringify(db, null, 2));
}

/**
 * Compare two semantic versions
 * @param {string} a - Version A (e.g., "2.1.0")
 * @param {string} b - Version B (e.g., "2.1.1")
 * @returns {number} -1 if a < b, 1 if a > b, 0 if equal
 */
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

// Download counts cache
let downloadCountsCache = null;

/**
 * Load download counts from database
 * @returns {{files: Object, total: number, byVersion: Object}}
 */
function loadDownloadCounts() {
    if (downloadCountsCache) return downloadCountsCache;
    try {
        if (fs.existsSync(DOWNLOADS_DB)) {
            downloadCountsCache = JSON.parse(fs.readFileSync(DOWNLOADS_DB, 'utf8'));
            return downloadCountsCache;
        }
    } catch (e) {
        console.error('[Downloads] Failed to load download counts:', e.message);
    }
    return { files: {}, total: 0, byVersion: {} };
}

/**
 * Save download counts to database
 */
function saveDownloadCounts() {
    if (!downloadCountsCache) return;
    try {
        fs.writeFileSync(DOWNLOADS_DB, JSON.stringify(downloadCountsCache, null, 2));
    } catch (e) {
        console.error('[Downloads] Failed to save download counts:', e.message);
    }
}

/**
 * Track a download in the database
 * @param {string} version - Release version
 * @param {string} filename - Downloaded file name
 * @returns {number} New download count for this file
 */
function trackDownload(version, filename) {
    if (!downloadCountsCache) {
        downloadCountsCache = loadDownloadCounts();
    }
    const key = `${version}/${filename}`;
    if (!downloadCountsCache.files[key]) downloadCountsCache.files[key] = 0;
    downloadCountsCache.files[key]++;
    downloadCountsCache.total++;
    if (!downloadCountsCache.byVersion[version]) downloadCountsCache.byVersion[version] = 0;
    downloadCountsCache.byVersion[version]++;
    saveDownloadCounts();
    return downloadCountsCache.files[key];
}

/**
 * Get current download counts
 * @returns {{files: Object, total: number, byVersion: Object}}
 */
function getDownloadCounts() {
    if (!downloadCountsCache) {
        downloadCountsCache = loadDownloadCounts();
    }
    return downloadCountsCache;
}

module.exports = {
    loadDB,
    saveDB,
    compareVersions,
    loadDownloadCounts,
    saveDownloadCounts,
    trackDownload,
    getDownloadCounts
};
