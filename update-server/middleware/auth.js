/**
 * Authentication & Authorization middleware
 */
const { verifyToken } = require('../lib/auth');

/**
 * Express middleware to authenticate JWT token
 */
function authenticateToken(req, res, next) {
    const authHeader = req.headers['authorization'];
    const token = authHeader && authHeader.split(' ')[1];
    
    if (!token) return res.status(401).json({ error: 'Access denied. No token provided.' });

    const user = verifyToken(token);
    if (!user) return res.status(403).json({ error: 'Invalid or expired token' });
    
    req.user = user;

    // If MFA is required but this token hasn't verified it yet, 
    // only allow access to the MFA verification endpoint
    if (user.mfaRequired && req.path !== '/mfa/verify') {
        return res.status(403).json({ error: 'MFA verification required', mfaRequired: true });
    }

    next();
}

/**
 * Middleware to require admin role
 */
function requireAdmin(req, res, next) {
    if (req.user && req.user.role === 'admin') {
        next();
    } else {
        res.status(403).json({ error: 'Access denied. Administrator privileges required.' });
    }
}

module.exports = {
    authenticateToken,
    requireAdmin
};
