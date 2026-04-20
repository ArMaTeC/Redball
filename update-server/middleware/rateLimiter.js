/**
 * Rate limiting middleware configurations
 */
const rateLimit = require('express-rate-limit');
const {
    RATE_LIMIT_WINDOW_MS,
    RATE_LIMIT_MAX,
    AUTH_RATE_LIMIT_MAX,
    UPLOAD_RATE_LIMIT_WINDOW_MS,
    UPLOAD_RATE_LIMIT_MAX
} = require('../config');

// Standard API rate limiter: 100 requests per 15 minutes
const apiLimiter = rateLimit({
    windowMs: RATE_LIMIT_WINDOW_MS,
    max: RATE_LIMIT_MAX,
    message: { error: 'Too many requests, please try again later' },
    standardHeaders: true,
    legacyHeaders: false,
});

// Stricter auth rate limiter: 10 login attempts per 15 minutes
const authLimiter = rateLimit({
    windowMs: RATE_LIMIT_WINDOW_MS,
    max: AUTH_RATE_LIMIT_MAX,
    message: { error: 'Too many login attempts, please try again later' },
    standardHeaders: true,
    legacyHeaders: false,
});

// Upload rate limiter: 10 uploads per hour
const uploadLimiter = rateLimit({
    windowMs: UPLOAD_RATE_LIMIT_WINDOW_MS,
    max: UPLOAD_RATE_LIMIT_MAX,
    message: { error: 'Too many uploads, please try again later' },
});

module.exports = {
    apiLimiter,
    authLimiter,
    uploadLimiter
};
