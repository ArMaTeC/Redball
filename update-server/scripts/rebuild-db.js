#!/usr/bin/env node
/**
 * Rebuild releases database from filesystem
 * Scans releases directory and reads release.json metadata
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const RELEASES_DIR = path.join(__dirname, '..', 'releases');
const DATA_DIR = path.join(__dirname, '..', 'data');
const DB_PATH = path.join(DATA_DIR, 'releases.json');

function sha256File(filePath) {
  return new Promise((resolve, reject) => {
    const hash = crypto.createHash('sha256');
    const stream = fs.createReadStream(filePath);
    stream.on('data', d => hash.update(d));
    stream.on('end', () => resolve(hash.digest('hex')));
    stream.on('error', reject);
  });
}

async function scanReleases() {
  console.log('Scanning releases directory:', RELEASES_DIR);
  
  if (!fs.existsSync(RELEASES_DIR)) {
    console.error('Releases directory not found');
    process.exit(1);
  }

  const releases = [];
  const versions = fs.readdirSync(RELEASES_DIR).filter(f => {
    const stat = fs.statSync(path.join(RELEASES_DIR, f));
    return stat.isDirectory();
  });

  for (const version of versions) {
    const versionDir = path.join(RELEASES_DIR, version);
    const releaseJsonPath = path.join(versionDir, 'release.json');
    
    let metadata = {
      version,
      channel: 'stable',
      date: new Date().toISOString(),
      notes: ''
    };

    // Read release.json if it exists
    if (fs.existsSync(releaseJsonPath)) {
      try {
        const releaseJson = JSON.parse(fs.readFileSync(releaseJsonPath, 'utf8'));
        metadata = { ...metadata, ...releaseJson };
      } catch (err) {
        console.warn(`Failed to parse release.json for ${version}:`, err.message);
      }
    }

    // Scan files
    const files = [];
    const fileList = fs.readdirSync(versionDir).filter(f => {
      return f !== 'release.json' && f !== 'manifest.json';
    });

    for (const filename of fileList) {
      const filePath = path.join(versionDir, filename);
      const stat = fs.statSync(filePath);
      
      if (stat.isFile()) {
        const hash = await sha256File(filePath);
        files.push({
          name: filename,
          size: stat.size,
          hash,
          downloads: 0
        });
      }
    }

    releases.push({
      version: metadata.version,
      channel: metadata.channel || 'stable',
      date: metadata.date,
      notes: metadata.notes || '',
      files,
      totalDownloads: 0
    });

    console.log(`✓ ${version} (${metadata.channel}) - ${files.length} files`);
  }

  return releases;
}

async function main() {
  console.log('Rebuilding releases database...\n');

  const releases = await scanReleases();

  // Ensure data directory exists
  if (!fs.existsSync(DATA_DIR)) {
    fs.mkdirSync(DATA_DIR, { recursive: true });
  }

  // Save database
  const db = { releases };
  fs.writeFileSync(DB_PATH, JSON.stringify(db, null, 2));

  console.log(`\n✓ Database rebuilt: ${DB_PATH}`);
  console.log(`  Total releases: ${releases.length}`);
  console.log(`  Channels: ${[...new Set(releases.map(r => r.channel))].join(', ')}`);
}

main().catch(err => {
  console.error('Error:', err);
  process.exit(1);
});
