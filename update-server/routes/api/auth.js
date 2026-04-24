/**
 * Authentication & User Management routes
 */
const express = require('express');
const router = express.Router();
const { 
    getUserByUsername, 
    verifyPassword, 
    generateToken, 
    generateMfaSecret, 
    generateQrCode,
    verifyMfaToken,
    updateUser,
    getUsers,
    addUser,
    deleteUser,
    generateTrustedDeviceToken,
    verifyTrustedDeviceToken
} = require('../../lib/auth');
const { authenticateToken, requireAdmin } = require('../../middleware/auth');
const { authLimiter } = require('../../middleware/rateLimiter');

// POST /api/auth/login - Authenticate and get token
router.post('/login', authLimiter, async (req, res) => {
    const { username, password, tdt } = req.body;
    if (!username || !password) {
        return res.status(400).json({ error: 'Username and password are required' });
    }

    const user = getUserByUsername(username);
    if (user && await verifyPassword(password, user.passwordHash)) {
        if (user.mfaEnabled) {
            // Check if device is trusted (bypass MFA)
            if (verifyTrustedDeviceToken(tdt, username)) {
                const token = generateToken(user, true);
                return res.json({ token, user: { username: user.username, role: user.role } });
            }

            // Return temporary token that only allows MFA verification
            const mfaToken = generateToken(user, false);
            return res.json({ mfaRequired: true, tempToken: mfaToken });
        }
        
        const token = generateToken(user, true);
        res.json({ token, user: { username: user.username, role: user.role } });
    } else {
        res.status(401).json({ error: 'Invalid credentials' });
    }
});

// POST /api/auth/mfa/verify - Verify MFA during login
router.post('/mfa/verify', authLimiter, authenticateToken, async (req, res) => {
    const { token: mfaCode, rememberDevice } = req.body;
    if (!mfaCode) return res.status(400).json({ error: 'MFA code is required' });

    const user = getUserByUsername(req.user.username);
    if (!user || !user.mfaSecret) return res.status(400).json({ error: 'MFA not configured' });

    if (verifyMfaToken(mfaCode, user.mfaSecret)) {
        const token = generateToken(user, true);
        const response = { token, user: { username: user.username, role: user.role } };
        
        // Generate Trusted Device Token if requested
        if (rememberDevice) {
            response.tdt = generateTrustedDeviceToken(user.username);
        }
        
        res.json(response);
    } else {
        res.status(401).json({ error: 'Invalid MFA code' });
    }
});

// GET /api/auth/me - Get current user info
router.get('/me', authenticateToken, (req, res) => {
    res.json({ user: req.user });
});

// --- MFA Setup (Requires authentication) ---

// POST /api/auth/mfa/setup - Initiate MFA setup
router.post('/mfa/setup', authenticateToken, async (req, res) => {
    const { secret, otpauth } = generateMfaSecret(req.user.username);
    const qrCode = await generateQrCode(otpauth);
    
    // Store secret temporarily in memory or just return it for the client to send back
    res.json({ secret, qrCode });
});

// POST /api/auth/mfa/verify-setup - Finalize MFA setup
router.post('/mfa/verify-setup', authenticateToken, async (req, res) => {
    const { secret, token: mfaCode } = req.body;
    if (!secret || !mfaCode) return res.status(400).json({ error: 'Secret and code are required' });

    if (verifyMfaToken(mfaCode, secret)) {
        await updateUser(req.user.username, { mfaEnabled: true, mfaSecret: secret });
        res.json({ message: 'MFA enabled successfully' });
    } else {
        res.status(401).json({ error: 'Invalid MFA code' });
    }
});

// POST /api/auth/mfa/disable - Disable MFA
router.post('/mfa/disable', authenticateToken, async (req, res) => {
    const { token: mfaCode } = req.body;
    const user = getUserByUsername(req.user.username);
    
    if (user.mfaEnabled) {
        if (!mfaCode || !verifyMfaToken(mfaCode, user.mfaSecret)) {
            return res.status(401).json({ error: 'Invalid MFA code' });
        }
    }

    await updateUser(req.user.username, { mfaEnabled: false, mfaSecret: null });
    res.json({ message: 'MFA disabled successfully' });
});

// POST /api/auth/change-password - Change own password
router.post('/change-password', authenticateToken, async (req, res) => {
    const { currentPassword, newPassword } = req.body;
    if (!currentPassword || !newPassword) return res.status(400).json({ error: 'Current and new password are required' });

    const user = getUserByUsername(req.user.username);
    if (!user || !(await verifyPassword(currentPassword, user.passwordHash))) {
        return res.status(401).json({ error: 'Incorrect current password' });
    }

    await updateUser(req.user.username, { password: newPassword });
    res.json({ message: 'Password updated successfully' });
});

// --- User Management (Admin only) ---

// GET /api/auth/users - List all users
router.get('/users', authenticateToken, requireAdmin, async (req, res) => {
    try {
        const users = await getUsers();
        res.json(users);
    } catch (e) {
        res.status(500).json({ error: 'Failed to retrieve users' });
    }
});

// POST /api/auth/users - Add a new user
router.post('/users', authenticateToken, requireAdmin, async (req, res) => {
    const { username, password, role } = req.body;
    try {
        const user = await addUser(username, password, role);
        res.status(201).json({ 
            username: user.username, 
            role: user.role, 
            mfaEnabled: user.mfaEnabled,
            createdAt: user.createdAt
        });
    } catch (e) {
        res.status(400).json({ error: e.message });
    }
});

// DELETE /api/auth/users/:username - Delete a user
router.delete('/users/:username', authenticateToken, requireAdmin, (req, res) => {
    const { username } = req.params;
    if (username === req.user.username) return res.status(400).json({ error: 'Cannot delete yourself' });
    
    deleteUser(username);
    res.json({ message: 'User deleted' });
});

module.exports = router;
