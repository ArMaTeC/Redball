/**
 * Analytics Engine for Redball Update Server
 * Tracks visits, downloads, and user engagement
 */
const fs = require('fs');
const path = require('path');
const geoip = require('geoip-lite');
const UAParser = require('ua-parser-js');
const { LOGS_DIR } = require('../config');

const ANALYTICS_FILE = path.join(LOGS_DIR, 'analytics.jsonl');

/**
 * Track a visitor event
 */
function trackEvent(req, type = 'view', metadata = {}) {
    try {
        const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress || '127.0.0.1';
        const cleanIp = ip.split(',')[0].trim();
        const ua = req.headers['user-agent'] || '';
        const parser = new UAParser(ua);
        const geo = geoip.lookup(cleanIp);
        
        const event = {
            ts: Date.now(),
            ip: cleanIp,
            type,
            path: req.path || '/',
            ua: {
                browser: parser.getBrowser().name || 'Unknown',
                os: parser.getOS().name || 'Unknown',
                device: parser.getDevice().type || 'desktop'
            },
            geo: geo ? {
                country: geo.country,
                city: geo.city,
                region: geo.region
            } : null,
            session: metadata.sessionId || req.query?.sid || null,
            ...metadata
        };
        
        fs.appendFileSync(ANALYTICS_FILE, JSON.stringify(event) + '\n');
    } catch (err) {
        console.error('[Analytics] Tracking failed:', err.message);
    }
}

/**
 * Get aggregated analytics for a date range
 */
async function getAnalytics(days = 30) {
    if (!fs.existsSync(ANALYTICS_FILE)) return null;

    const now = Date.now();
    const cutoff = now - (days * 24 * 60 * 60 * 1000);
    
    const stats = {
        totalVisits: 0,
        uniqueVisitors: new Set(),
        newVisits: 0,
        returningVisits: 0,
        downloads: 0,
        avgTimeOnSite: 0,
        geoDistribution: {},
        browsers: {},
        os: {},
        trends: {}, // daily visits
        downloadTrends: {},
        recentEvents: []
    };

    const sessions = {}; // sessionId -> { start, end, events }

    // Read file line by line
    const data = fs.readFileSync(ANALYTICS_FILE, 'utf8').split('\n');
    
    for (const line of data) {
        if (!line.trim()) continue;
        const event = JSON.parse(line);
        if (event.ts < cutoff) continue;

        stats.totalVisits++;
        stats.uniqueVisitors.add(event.ip);

        // Daily trend
        const date = new Date(event.ts).toISOString().split('T')[0];
        stats.trends[date] = (stats.trends[date] || 0) + 1;

        if (event.type === 'download') {
            stats.downloads++;
            stats.downloadTrends[date] = (stats.downloadTrends[date] || 0) + 1;
        }

        // Geo
        if (event.geo && event.geo.country) {
            stats.geoDistribution[event.geo.country] = (stats.geoDistribution[event.geo.country] || 0) + 1;
        }

        // UA
        if (event.ua) {
            stats.browsers[event.ua.browser] = (stats.browsers[event.ua.browser] || 0) + 1;
            stats.os[event.ua.os] = (stats.os[event.ua.os] || 0) + 1;
        }

        // Session tracking
        if (event.session) {
            if (!sessions[event.session]) {
                sessions[event.session] = { start: event.ts, end: event.ts, count: 0 };
            }
            sessions[event.session].start = Math.min(sessions[event.session].start, event.ts);
            sessions[event.session].end = Math.max(sessions[event.session].end, event.ts);
            sessions[event.session].count++;
        }
    }

    // Calculate session stats
    const sessionList = Object.values(sessions);
    if (sessionList.length > 0) {
        const totalDuration = sessionList.reduce((sum, s) => sum + (s.end - s.start), 0);
        stats.avgTimeOnSite = totalDuration / sessionList.length / 1000; // in seconds
    }

    // Rough estimation of new vs returning based on IP/Session history (simplified)
    // In a real app, you'd store persistent visitor IDs in a DB
    stats.uniqueVisitorsCount = stats.uniqueVisitors.size;
    delete stats.uniqueVisitors;

    return stats;
}

module.exports = { trackEvent, getAnalytics };
