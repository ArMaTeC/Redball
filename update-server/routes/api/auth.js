/**
 * Authentication routes
 * Handles login, token verification, and user info
 */
const express = require('express');
const router = express.Router();
const { getAdminUser, verifyPassword, generateToken } = require('../../lib/auth');

// POST /api/auth/login - Authenticate and get token
router.post('/login', async (req, res) => {
    const { username, password } = req.body;
    if (!username || typeof username !== 'string' || !password || typeof password !== 'string') {
        return res.status(400).json({ error: 'Username and password are required' });
    }

    const admin = getAdminUser();
    if (username === admin.username && await verifyPassword(password, admin.passwordHash)) {
        const token = generateToken(username);
        res.json({ token });
    } else {
        res.status(401).json({ error: 'Invalid credentials' });
    }
});

// GET /api/auth/me - Get current user info (requires valid token)
router.get('/me', (req, res) => {
    // This route is protected by authenticateToken middleware
    res.json({ user: req.user });
});

module.exports = router;
