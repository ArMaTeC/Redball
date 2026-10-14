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

function clientIp(req) {
    const ip = req.headers['x-forwarded-for'] || req.socket.remoteAddress || '127.0.0.1';
    return ip.split(',')[0].trim();
}

function parseUserAgent(ua) {
    const parser = new UAParser(ua);
    return {
        browser: parser.getBrowser().name || 'Unknown',
        os: parser.getOS().name || 'Unknown',
        device: parser.getDevice().type || 'desktop'
    };
}

function lookupGeo(ip) {
    const geo = geoip.lookup(ip);
    return geo ? { country: geo.country, city: geo.city, region: geo.region } : null;
}

function buildEvent(req, type, metadata) {
    const ip = clientIp(req);
    return {
        ts: Date.now(),
        ip,
        type,
        path: req.path || '/',
        ua: parseUserAgent(req.headers['user-agent'] || ''),
        geo: lookupGeo(ip),
        session: metadata.sessionId || req.query?.sid || null,
        ...metadata
    };
}

/**
 * Track a visitor event
 */
function trackEvent(req, type = 'view', metadata = {}) {
    try {
        fs.appendFileSync(ANALYTICS_FILE, JSON.stringify(buildEvent(req, type, metadata)) + '\n');
    } catch (err) {
        console.error('[Analytics] Tracking failed:', err.message);
    }
}

function newStats() {
    return {
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
}

function tally(bucket, key) {
    bucket[key] = (bucket[key] || 0) + 1;
}

function updateSession(sessions, event) {
    if (!sessions[event.session]) {
        sessions[event.session] = { start: event.ts, end: event.ts, count: 0 };
    }
    const s = sessions[event.session];
    s.start = Math.min(s.start, event.ts);
    s.end = Math.max(s.end, event.ts);
    s.count++;
}

function aggregateEvent(stats, sessions, event) {
    stats.totalVisits++;
    stats.uniqueVisitors.add(event.ip);

    const date = new Date(event.ts).toISOString().split('T')[0];
    stats.trends[date] = (stats.trends[date] || 0) + 1;

    if (event.type === 'download') {
        stats.downloads++;
        stats.downloadTrends[date] = (stats.downloadTrends[date] || 0) + 1;
    }

    if (event.geo?.country) {
        tally(stats.geoDistribution, event.geo.country);
    }

    if (event.ua) {
        tally(stats.browsers, event.ua.browser);
        tally(stats.os, event.ua.os);
    }

    if (event.session) updateSession(sessions, event);
}

/**
 * Get aggregated analytics for a date range
 */
async function getAnalytics(days = 30) {
    if (!fs.existsSync(ANALYTICS_FILE)) return null;

    const cutoff = Date.now() - (days * 24 * 60 * 60 * 1000);
    const stats = newStats();
    const sessions = {}; // sessionId -> { start, end, count }

    for (const line of fs.readFileSync(ANALYTICS_FILE, 'utf8').split('\n')) {
        if (!line.trim()) continue;
        const event = JSON.parse(line);
        if (event.ts < cutoff) continue;
        aggregateEvent(stats, sessions, event);
    }

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
