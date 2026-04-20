/**
 * Authentication utilities for the update server
 * JWT secret management and admin user operations
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const { LOGS_DIR, AUTH_FILE } = require('../config');

const JWT_SECRET_FILE = path.join(LOGS_DIR, '.jwt-secret');

// Lazy-loaded JWT secret
let jwtSecretCache = null;

/**
 * Load or create JWT secret
 * @returns {string} JWT secret
 */
function loadOrCreateJwtSecret() {
    if (jwtSecretCache) return jwtSecretCache;

    try {
        if (fs.existsSync(JWT_SECRET_FILE)) {
            jwtSecretCache = fs.readFileSync(JWT_SECRET_FILE, 'utf8').trim();
            return jwtSecretCache;
        }
    } catch (e) { }

    const secret = crypto.randomBytes(64).toString('hex');
    try {
        fs.writeFileSync(JWT_SECRET_FILE, secret, { mode: 0o600 });
    } catch (e) {
        console.error('[AUTH] Warning: Could not save JWT secret to file');
    }
    jwtSecretCache = secret;
    return secret;
}

/**
 * Get JWT secret (lazy loaded)
 * @returns {string} JWT secret
 */
function getJwtSecret() {
    if (!jwtSecretCache) {
        jwtSecretCache = loadOrCreateJwtSecret();
    }
    return jwtSecretCache;
}

/**
 * Default admin user
 */
const DEFAULT_USER = {
    username: 'admin',
    passwordHash: '$2b$10$NH.q4MEoGZUuC4kHmv8B2uLh4OXdPVwa2Q/CKp/ORwSMG2XvhGQ8e'
};

/**
 * Load admin user from file or create default
 * @returns {{username: string, passwordHash: string}} Admin user
 */
function getAdminUser() {
    try {
        if (fs.existsSync(AUTH_FILE)) {
            const auth = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
            return auth.admin || DEFAULT_USER;
        }
    } catch (e) {
        console.error('[AUTH] Error loading auth file:', e.message);
    }
    saveAdminUser(DEFAULT_USER);
    return DEFAULT_USER;
}

/**
 * Save admin user to file
 * @param {{username: string, passwordHash: string}} user - User to save
 */
function saveAdminUser(user) {
    try {
        fs.writeFileSync(AUTH_FILE, JSON.stringify({ admin: user }, null, 2), { mode: 0o600 });
    } catch (e) {
        console.error('[AUTH] Error saving auth file:', e.message);
    }
}

/**
 * Generate JWT token for user
 * @param {string} username - Username to encode in token
 * @returns {string} JWT token
 */
function generateToken(username) {
    return jwt.sign({ username }, getJwtSecret(), { expiresIn: '24h' });
}

/**
 * Verify JWT token
 * @param {string} token - Token to verify
 * @returns {Object|null} Decoded token payload or null if invalid
 */
function verifyToken(token) {
    try {
        return jwt.verify(token, getJwtSecret());
    } catch (err) {
        return null;
    }
}

/**
 * Verify password against hash
 * @param {string} password - Plain text password
 * @param {string} hash - Bcrypt hash
 * @returns {Promise<boolean>} Whether password matches
 */
async function verifyPassword(password, hash) {
    return bcrypt.compare(password, hash);
}

module.exports = {
    getJwtSecret,
    getAdminUser,
    saveAdminUser,
    generateToken,
    verifyToken,
    verifyPassword,
    DEFAULT_USER
};
