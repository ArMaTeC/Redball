/**
 * Authentication utilities for the update server
 * JWT secret management, User management, and MFA (TOTP)
 */
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const bcrypt = require('bcryptjs');
const jwt = require('jsonwebtoken');
const { authenticator } = require('otplib');
const qrcode = require('qrcode');
const { LOGS_DIR, AUTH_FILE } = require('../config');

const JWT_SECRET_FILE = path.join(LOGS_DIR, '.jwt-secret');

// Lazy-loaded JWT secret
let jwtSecretCache = null;

/**
 * Load or create JWT secret
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

function getJwtSecret() {
    if (!jwtSecretCache) jwtSecretCache = loadOrCreateJwtSecret();
    return jwtSecretCache;
}

/**
 * User Management
 */
const DEFAULT_USER = {
    username: 'admin',
    passwordHash: '$2b$10$vbyR65jpIlk9tN99d.p33u4A7mdwXcdarb.5iR72VlJScEF9CJaQi', // 'admin'
    role: 'admin',
    mfaEnabled: false,
    mfaSecret: null,
    createdAt: new Date().toISOString()
};

function loadAuthData() {
    try {
        if (fs.existsSync(AUTH_FILE)) {
            const data = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
            // Support legacy format or new format
            if (data.users) return data;
            if (data.admin) {
                const migrated = { users: [ { ...data.admin, role: 'admin', mfaEnabled: !!data.admin.mfaEnabled, createdAt: data.admin.createdAt || new Date().toISOString() } ] };
                saveAuthData(migrated);
                return migrated;
            }
        }
    } catch (e) {
        console.error('[AUTH] Error loading auth file:', e.message);
    }
    const initialData = { users: [DEFAULT_USER] };
    saveAuthData(initialData);
    return initialData;
}

function saveAuthData(data) {
    try {
        fs.writeFileSync(AUTH_FILE, JSON.stringify(data, null, 2), { mode: 0o600 });
    } catch (e) {
        console.error('[AUTH] Error saving auth file:', e.message);
    }
}

function getUsers() {
    return loadAuthData().users;
}

function getUserByUsername(username) {
    return getUsers().find(u => u.username === username);
}

async function addUser(username, password, role = 'viewer') {
    const data = loadAuthData();
    if (data.users.find(u => u.username === username)) throw new Error('User already exists');
    
    const passwordHash = await bcrypt.hash(password, 10);
    const newUser = {
        username,
        passwordHash,
        role,
        mfaEnabled: false,
        mfaSecret: null,
        createdAt: new Date().toISOString()
    };
    
    data.users.push(newUser);
    saveAuthData(data);
    return newUser;
}

async function updateUser(username, updates) {
    const data = loadAuthData();
    const idx = data.users.findIndex(u => u.username === username);
    if (idx === -1) throw new Error('User not found');
    
    if (updates.password) {
        updates.passwordHash = await bcrypt.hash(updates.password, 10);
        delete updates.password;
    }
    
    data.users[idx] = { ...data.users[idx], ...updates };
    saveAuthData(data);
    return data.users[idx];
}

function deleteUser(username) {
    const data = loadAuthData();
    data.users = data.users.filter(u => u.username !== username);
    saveAuthData(data);
}

/**
 * MFA (TOTP)
 */
function generateMfaSecret(username) {
    const secret = authenticator.generateSecret();
    const otpauth = authenticator.keyuri(username, 'Redball Admin', secret);
    return { secret, otpauth };
}

async function generateQrCode(otpauth) {
    return qrcode.toDataURL(otpauth);
}

function verifyMfaToken(token, secret) {
    return authenticator.check(token, secret);
}

/**
 * JWT
 */
function generateToken(user, mfaVerified = false) {
    return jwt.sign({ 
        username: user.username, 
        role: user.role,
        mfaRequired: user.mfaEnabled && !mfaVerified
    }, getJwtSecret(), { expiresIn: '24h' });
}

function verifyToken(token) {
    try {
        return jwt.verify(token, getJwtSecret());
    } catch (err) {
        return null;
    }
}

async function verifyPassword(password, hash) {
    return bcrypt.compare(password, hash);
}

/**
 * Trusted Device Tokens (TDT)
 * Allows bypassing MFA on remembered browsers for 30 days
 */
function generateTrustedDeviceToken(username) {
    return jwt.sign({ 
        username,
        type: 'trusted_device'
    }, getJwtSecret(), { expiresIn: '30d' });
}

function verifyTrustedDeviceToken(token, username) {
    if (!token) return false;
    try {
        const decoded = jwt.verify(token, getJwtSecret());
        return decoded.username === username && decoded.type === 'trusted_device';
    } catch (err) {
        return false;
    }
}

module.exports = {
    getJwtSecret,
    getUsers,
    getUserByUsername,
    addUser,
    updateUser,
    deleteUser,
    generateMfaSecret,
    generateQrCode,
    verifyMfaToken,
    generateToken,
    verifyToken,
    verifyPassword,
    generateTrustedDeviceToken,
    verifyTrustedDeviceToken,
    DEFAULT_USER
};
