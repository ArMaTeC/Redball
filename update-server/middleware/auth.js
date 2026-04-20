/**
 * Authentication middleware
 * Verifies JWT tokens for protected routes
 */
const { verifyToken } = require('../lib/auth');

/**
 * Express middleware to authenticate JWT token
 * @param {Object} req - Express request object
 * @param {Object} res - Express response object
 * @param {Function} next - Express next function
 */
function authenticateToken(req, res, next) {
    const authHeader = req.headers['authorization'];
    const token = authHeader && authHeader.split(' ')[1];
    if (!token) return res.status(401).json({ error: 'Access denied. No token provided.' });

    const user = verifyToken(token);
    if (!user) return res.status(403).json({ error: 'Invalid or expired token' });
    
    req.user = user;
    next();
}

module.exports = {
    authenticateToken
};
