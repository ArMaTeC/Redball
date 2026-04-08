/**
 * Delta Patch Generator for Redball Update Server
 * 
 * Generates binary delta patches between file versions using
 * a simplified XOR+run-length encoding algorithm compatible
 * with the C# DeltaUpdateService client.
 */

const fs = require('fs');
const fsp = require('fs').promises;
const path = require('path');
const crypto = require('crypto');
const zlib = require('zlib');
const { promisify } = require('util');

const gzipCompress = promisify(zlib.gzip);
const gzipDecompress = promisify(zlib.gunzip);

/**
 * Compute SHA256 hash of data
 */
function computeHash(data) {
  return crypto.createHash('sha256').update(data).digest('hex').toUpperCase();
}

/**
 * Find common prefix length between two byte arrays
 */
function findCommonPrefix(oldData, newData) {
  const minLen = Math.min(oldData.length, newData.length);
  for (let i = 0; i < minLen; i++) {
    if (oldData[i] !== newData[i]) return i;
  }
  return minLen;
}

/**
 * Find common suffix length between two byte arrays
 */
function findCommonSuffix(oldData, newData, prefixLen) {
  const maxSuffix = Math.min(oldData.length - prefixLen, newData.length - prefixLen);
  for (let i = 1; i <= maxSuffix; i++) {
    if (oldData[oldData.length - i] !== newData[newData.length - i]) return i - 1;
  }
  return maxSuffix;
}

/**
 * Create a binary delta patch
 * Format compatible with C# DeltaUpdateService:
 * - int32: old file size
 * - int32: new file size
 * - int32: common prefix length
 * - int32: common suffix length
 * - int32: new data section length
 * - bytes: new data section
 */
async function createPatch(oldData, newData) {
  const oldSize = oldData.length;
  const newSize = newData.length;

  const commonPrefix = findCommonPrefix(oldData, newData);
  const commonSuffix = findCommonSuffix(oldData, newData, commonPrefix);

  const newStart = commonPrefix;
  const newLength = newSize - commonPrefix - commonSuffix;

  // Build patch data
  const header = Buffer.alloc(20); // 5 x int32
  header.writeInt32LE(oldSize, 0);
  header.writeInt32LE(newSize, 4);
  header.writeInt32LE(commonPrefix, 8);
  header.writeInt32LE(commonSuffix, 12);
  header.writeInt32LE(newLength, 16);

  // Extract new data section
  const newSection = newData.slice(newStart, newStart + newLength);

  // Combine header + new section
  const patchData = Buffer.concat([header, newSection]);

  // Compress
  const compressed = await gzipCompress(patchData, { level: 9 });

  return {
    data: compressed,
    oldFileHash: computeHash(oldData),
    newFileHash: computeHash(newData),
    oldFileSize: oldSize,
    newFileSize: newSize,
    patchSize: compressed.length,
    compressionRatio: compressed.length / newSize
  };
}

/**
 * Apply a delta patch (for testing/verification)
 */
async function applyPatch(oldData, patchData) {
  // Decompress
  const decompressed = await gzipDecompress(patchData);

  // Read header
  const oldSize = decompressed.readInt32LE(0);
  const newSize = decompressed.readInt32LE(4);
  const commonPrefix = decompressed.readInt32LE(8);
  const commonSuffix = decompressed.readInt32LE(12);
  const newDataLength = decompressed.readInt32LE(16);

  if (oldSize !== oldData.length) {
    throw new Error(`Old file size mismatch: expected ${oldSize}, got ${oldData.length}`);
  }

  // Reconstruct
  const result = Buffer.alloc(newSize);

  // Copy prefix
  oldData.copy(result, 0, 0, commonPrefix);

  // Copy new section
  const newSection = decompressed.slice(20, 20 + newDataLength);
  newSection.copy(result, commonPrefix);

  // Copy suffix
  const suffixStartOld = oldData.length - commonSuffix;
  const suffixStartNew = newSize - commonSuffix;
  oldData.copy(result, suffixStartNew, suffixStartOld);

  return result;
}

/**
 * Find matching file in old version for versioned filenames
 * Handles patterns like Redball-2.1.450-Setup.exe -> Redball-2.1.452-Setup.exe
 */
function findMatchingOldFile(oldVersionDir, newVersionDir, newFilePath) {
  const newFileName = path.basename(newFilePath);

  // Check for exact match first
  const exactMatch = path.join(oldVersionDir, path.relative(newVersionDir, newFilePath));
  if (fs.existsSync(exactMatch)) {
    return exactMatch;
  }

  // Pattern: Redball-X.Y.Z-Setup.exe -> Redball-A.B.C-Setup.exe
  // Extract base name by removing version pattern
  const versionPattern = /Redball-\d+\.\d+\.\d+(-Setup\.exe|-[a-zA-Z]+\.zip|\.exe)$/;

  if (versionPattern.test(newFileName)) {
    // Get the file type suffix (e.g., -Setup.exe, .zip)
    const suffixMatch = newFileName.match(/-\d+\.\d+\.\d+(-.*)$/);
    if (suffixMatch) {
      const suffix = suffixMatch[1]; // e.g., -Setup.exe

      // Get all files in old version
      const oldFiles = getAllFiles(oldVersionDir);

      for (const oldFile of oldFiles) {
        const oldFileName = path.basename(oldFile);
        // Check if old file ends with same suffix
        if (oldFileName.endsWith(suffix)) {
          return oldFile;
        }
      }
    }
  }

  return null;
}

/**
 * Generate patches between two release versions
 */
async function generatePatches(oldVersionDir, newVersionDir, patchesDir, options = {}) {
  const {
    minSavingsPercent = 10,  // Minimum savings to generate patch
    maxPatchSizeRatio = 0.9  // Skip if patch is >90% of full file
  } = options;

  const results = {
    generated: [],
    skipped: [],
    errors: [],
    totalSavings: 0,
    totalOriginalSize: 0
  };

  if (!fs.existsSync(patchesDir)) {
    fs.mkdirSync(patchesDir, { recursive: true });
  }

  // Get all files in new version
  const newFiles = getAllFiles(newVersionDir);

  for (const newFile of newFiles) {
    let relativePath = '';
    try {
      relativePath = path.relative(newVersionDir, newFile);
      const oldFile = path.join(oldVersionDir, relativePath);

      // ASYNC: Use non-blocking file read
      const newData = await fsp.readFile(newFile);
      results.totalOriginalSize += newData.length;

      // Skip very small files
      if (newData.length < 1024) {
        results.skipped.push({ file: relativePath, reason: 'too_small' });
        continue;
      }

      // If old file doesn't exist at exact path, try to find matching versioned file
      let actualOldFile = oldFile;
      if (!fs.existsSync(oldFile)) {
        actualOldFile = findMatchingOldFile(oldVersionDir, newVersionDir, newFile);
        if (!actualOldFile) {
          results.skipped.push({ file: relativePath, reason: 'new_file' });
          continue;
        }
      }

      // ASYNC: Use non-blocking file read for old data too
      const oldData = await fsp.readFile(actualOldFile);

      // Check if files are identical
      if (computeHash(oldData) === computeHash(newData)) {
        results.skipped.push({ file: relativePath, reason: 'unchanged' });
        continue;
      }

      // Generate patch
      console.log(`[PATCH] Generating patch for ${relativePath}...`);
      const patch = await createPatch(oldData, newData);

      // Check if patch is worthwhile
      const savingsPercent = ((newData.length - patch.patchSize) / newData.length) * 100;

      if (savingsPercent < minSavingsPercent || patch.patchSize > newData.length * maxPatchSizeRatio) {
        results.skipped.push({
          file: relativePath,
          reason: 'not_worthwhile',
          savings: savingsPercent.toFixed(1) + '%'
        });
        continue;
      }

      // Verify patch works
      const testResult = await applyPatch(oldData, patch.data);
      if (computeHash(testResult) !== patch.newFileHash) {
        results.errors.push({ file: relativePath, error: 'patch_verification_failed' });
        continue;
      }

      // Save patch
      const patchFileName = `${path.basename(relativePath)}.patch`;
      const patchPath = path.join(patchesDir, patchFileName);

      // Create subdirectory if needed
      const patchSubDir = path.dirname(patchPath);
      if (!fs.existsSync(patchSubDir)) {
        fs.mkdirSync(patchSubDir, { recursive: true });
      }

      // ASYNC: Use non-blocking file write
      await fsp.writeFile(patchPath, patch.data);

      const savings = newData.length - patch.patchSize;
      results.totalSavings += savings;

      results.generated.push({
        file: relativePath,
        patchFile: patchFileName,
        patchSize: patch.patchSize,
        originalSize: newData.length,
        savings: savings,
        savingsPercent: savingsPercent.toFixed(1) + '%'
      });

      console.log(`[PATCH] ✓ ${relativePath}: ${savingsPercent.toFixed(1)}% savings (${formatBytes(savings)} saved)`);

    } catch (err) {
      results.errors.push({ file: relativePath || newFile, error: err.message });
      console.error(`[PATCH] ✗ Error patching ${relativePath || newFile}: ${err.message}`);
    }
  }

  return results;
}

/**
 * Get all files in directory recursively
 */
function getAllFiles(dir, files = []) {
  if (!fs.existsSync(dir)) return files;

  const entries = fs.readdirSync(dir, { withFileTypes: true });

  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      getAllFiles(fullPath, files);
    } else {
      files.push(fullPath);
    }
  }

  return files;
}

/**
 * Format bytes to human readable
 */
function formatBytes(bytes) {
  if (bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

/**
 * Generate patches for all consecutive versions
 */
async function generateAllPatches(releasesDir) {
  const versions = fs.readdirSync(releasesDir)
    .filter(v => fs.statSync(path.join(releasesDir, v)).isDirectory())
    .sort(compareVersions);

  if (versions.length < 2) {
    console.log('[PATCH] Need at least 2 versions to generate patches');
    return;
  }

  console.log(`[PATCH] Found ${versions.length} versions: ${versions.join(', ')}`);

  // Generate patches between consecutive versions
  for (let i = 0; i < versions.length - 1; i++) {
    const oldVersion = versions[i];
    const newVersion = versions[i + 1];

    console.log(`\n[PATCH] Generating patches: ${oldVersion} → ${newVersion}`);

    const oldDir = path.join(releasesDir, oldVersion);
    const newDir = path.join(releasesDir, newVersion);
    const patchesDir = path.join(releasesDir, newVersion, 'patches');

    const results = await generatePatches(oldDir, newDir, patchesDir);

    console.log(`[PATCH] Generated ${results.generated.length} patches`);
    console.log(`[PATCH] Skipped ${results.skipped.length} files`);
    console.log(`[PATCH] Total savings: ${formatBytes(results.totalSavings)}`);

    // Save patch manifest
    const manifest = {
      fromVersion: oldVersion,
      toVersion: newVersion,
      generatedAt: new Date().toISOString(),
      patches: results.generated,
      skipped: results.skipped.filter(s => s.reason !== 'unchanged'),
      totalSavings: results.totalSavings,
      totalOriginalSize: results.totalOriginalSize
    };

    fs.writeFileSync(
      path.join(patchesDir, 'patch-manifest.json'),
      JSON.stringify(manifest, null, 2)
    );
  }
}

/**
 * Compare version strings (for sorting)
 */
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

module.exports = {
  createPatch,
  applyPatch,
  generatePatches,
  generateAllPatches,
  computeHash,
  formatBytes
};
