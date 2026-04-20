/**
 * Configuration constants for the update server
 */
const path = require('path');
const fs = require('fs');

// Paths
const PROJECT_ROOT = path.join(__dirname, '..', '..');
const SERVER_ROOT = path.join(__dirname, '..');
const RELEASES_DIR = path.join(SERVER_ROOT, 'releases');
const DATA_DIR = path.join(SERVER_ROOT, 'data');
const DB_PATH = path.join(DATA_DIR, 'releases.json');
const LOGS_DIR = path.join(SERVER_ROOT, 'logs');

// Build State & Auth Files
const BUILD_STATE_FILE = path.join(LOGS_DIR, 'build-state.json');
const AUTH_FILE = path.join(LOGS_DIR, 'auth.json');
const DOWNLOADS_DB = path.join(LOGS_DIR, 'downloads.json');

// Ensure directories exist
[RELEASES_DIR, DATA_DIR, LOGS_DIR].forEach(dir => {
    if (!fs.existsSync(dir)) fs.mkdirSync(dir, { recursive: true });
});

// Server settings
const PORT = process.env.PORT || 3500;

// Cache TTLs
const GITHUB_CACHE_TTL = 5 * 60 * 1000; // 5 minutes
const DIR_SIZE_CACHE_TTL = 60 * 1000; // 60 seconds

// Broadcast throttling
const BROADCAST_BATCH_INTERVAL = 100; // ms

// Build state debouncing
const BUILD_STATE_DEBOUNCE_MS = 500;

// Cleanup interval
const CLEANUP_INTERVAL = 24 * 60 * 60 * 1000; // 24 hours

// WebSocket settings
const WS_HEARTBEAT_INTERVAL = 30000; // 30 seconds

// Rate limiting
const RATE_LIMIT_WINDOW_MS = 15 * 60 * 1000; // 15 minutes
const RATE_LIMIT_MAX = 100;
const AUTH_RATE_LIMIT_MAX = 10;
const UPLOAD_RATE_LIMIT_WINDOW_MS = 60 * 60 * 1000; // 1 hour
const UPLOAD_RATE_LIMIT_MAX = 10;

// JSON payload limit
const JSON_PAYLOAD_LIMIT = '10mb';

module.exports = {
    PROJECT_ROOT,
    SERVER_ROOT,
    RELEASES_DIR,
    DATA_DIR,
    DB_PATH,
    LOGS_DIR,
    BUILD_STATE_FILE,
    AUTH_FILE,
    DOWNLOADS_DB,
    PORT,
    GITHUB_CACHE_TTL,
    DIR_SIZE_CACHE_TTL,
    BROADCAST_BATCH_INTERVAL,
    BUILD_STATE_DEBOUNCE_MS,
    CLEANUP_INTERVAL,
    WS_HEARTBEAT_INTERVAL,
    RATE_LIMIT_WINDOW_MS,
    RATE_LIMIT_MAX,
    AUTH_RATE_LIMIT_MAX,
    UPLOAD_RATE_LIMIT_WINDOW_MS,
    UPLOAD_RATE_LIMIT_MAX,
    JSON_PAYLOAD_LIMIT
};
