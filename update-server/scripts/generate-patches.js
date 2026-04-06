#!/usr/bin/env node
/**
 * Generate delta patches for all existing releases
 * 
 * Usage: node scripts/generate-patches.js
 */

const { generateAllPatches } = require('../lib/delta-patches');
const path = require('path');

const RELEASES_DIR = path.join(__dirname, '..', 'releases');

console.log('╔══════════════════════════════════════════════════╗');
console.log('║  Redball Delta Patch Generator                  ║');
console.log('╚══════════════════════════════════════════════════╝\n');

console.log(`Releases directory: ${RELEASES_DIR}\n`);

(async () => {
  try {
    await generateAllPatches(RELEASES_DIR);
    console.log('\n✓ Patch generation completed successfully');
  } catch (err) {
    console.error('\n✗ Patch generation failed:', err.message);
    process.exit(1);
  }
})();
