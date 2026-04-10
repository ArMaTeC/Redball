#!/usr/bin/env node
/**
 * Cleanup script for update-server releases
 * Keeps only the last N versions and removes old ones
 * This prevents disk space exhaustion from accumulated releases
 */

const fs = require('fs');
const path = require('path');

const RELEASES_DIR = path.join(__dirname, '..', 'releases');
const MAX_VERSIONS_TO_KEEP = 6;

/**
 * Parse version string to array for comparison
 */
function parseVersion(version) {
  return version.split('.').map(n => parseInt(n, 10));
}

/**
 * Compare two versions (semver-style)
 * Returns: -1 if v1 < v2, 0 if equal, 1 if v1 > v2
 */
function compareVersions(v1, v2) {
  const a = parseVersion(v1);
  const b = parseVersion(v2);
  
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const av = a[i] || 0;
    const bv = b[i] || 0;
    if (av < bv) return -1;
    if (av > bv) return 1;
  }
  return 0;
}

/**
 * Get all release versions sorted newest first
 */
function getAllVersions() {
  if (!fs.existsSync(RELEASES_DIR)) {
    return [];
  }
  
  return fs.readdirSync(RELEASES_DIR)
    .filter(dir => {
      const dirPath = path.join(RELEASES_DIR, dir);
      return fs.statSync(dirPath).isDirectory() && /^\d+\.\d+\.\d+$/.test(dir);
    })
    .sort((a, b) => compareVersions(b, a)); // Newest first
}

/**
 * Remove a release directory recursively
 */
function removeRelease(version) {
  const dirPath = path.join(RELEASES_DIR, version);
  
  if (!fs.existsSync(dirPath)) {
    return false;
  }
  
  try {
    fs.rmSync(dirPath, { recursive: true, force: true });
    console.log(`[CLEANUP] Removed old release: ${version}`);
    return true;
  } catch (err) {
    console.error(`[CLEANUP] Failed to remove ${version}:`, err.message);
    return false;
  }
}

/**
 * Main cleanup function
 */
function cleanupReleases() {
  console.log('[CLEANUP] Starting releases cleanup...');
  
  const versions = getAllVersions();
  console.log(`[CLEANUP] Found ${versions.length} releases (keeping last ${MAX_VERSIONS_TO_KEEP})`);
  
  if (versions.length <= MAX_VERSIONS_TO_KEEP) {
    console.log('[CLEANUP] No cleanup needed');
    return;
  }
  
  const versionsToRemove = versions.slice(MAX_VERSIONS_TO_KEEP);
  let removedCount = 0;
  
  for (const version of versionsToRemove) {
    if (removeRelease(version)) {
      removedCount++;
    }
  }
  
  // Clean up releases.json database
  const dbPath = path.join(__dirname, '..', 'data', 'releases.json');
  if (fs.existsSync(dbPath)) {
    try {
      const db = JSON.parse(fs.readFileSync(dbPath, 'utf8'));
      const originalCount = db.releases?.length || 0;
      
      // Keep only releases that still exist on disk
      db.releases = (db.releases || []).filter(r => {
        const releaseDir = path.join(RELEASES_DIR, r.version);
        return fs.existsSync(releaseDir);
      });
      
      fs.writeFileSync(dbPath, JSON.stringify(db, null, 2));
      console.log(`[CLEANUP] Updated database: ${originalCount} → ${db.releases.length} releases`);
    } catch (err) {
      console.error('[CLEANUP] Failed to update database:', err.message);
    }
  }
  
  console.log(`[CLEANUP] Completed: removed ${removedCount} old releases`);
  console.log(`[CLEANUP] Remaining versions: ${versions.slice(0, MAX_VERSIONS_TO_KEEP).join(', ')}`);
}

// Run if called directly
if (require.main === module) {
  cleanupReleases();
}

module.exports = { cleanupReleases, getAllVersions, compareVersions, MAX_VERSIONS_TO_KEEP };
