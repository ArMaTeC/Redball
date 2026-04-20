/**
 * Build orchestration routes
 * Handles starting, stopping, and monitoring builds
 */
const express = require('express');
const router = express.Router();

// Build state will be injected from server.js
let buildState = { status: 'idle', stage: null, progress: 0, log: [], pid: null, startTime: null, endTime: null };
let startBuildFn = null;
let stopBuildFn = null;

/**
 * Set the build state reference and control functions
 * @param {Object} state - The shared build state object
 * @param {Function} startFn - Function to start a build
 * @param {Function} stopFn - Function to stop a build
 */
function setBuildState(state, startFn, stopFn) {
    buildState = state;
    startBuildFn = startFn;
    stopBuildFn = stopFn;
}

// POST /api/build/start - Start a new build
router.post('/start', (req, res) => {
    if (buildState.status === 'running') {
        return res.status(409).json({ error: 'Build already in progress' });
    }
    if (startBuildFn) {
        startBuildFn();
    }
    res.json({ message: 'Build started', status: 'running' });
});

// POST /api/build/stop - Stop the current build
router.post('/stop', (req, res) => {
    if (stopBuildFn) {
        stopBuildFn();
    }
    res.json({ message: 'Build stopping', status: 'stopping' });
});

// GET /api/admin/status - Get current build status
router.get('/status', (req, res) => {
    res.json(buildState);
});

// Aliases for compatibility
router.post('/admin/build', (req, res) => {
    if (buildState.status === 'running') {
        return res.status(409).json({ error: 'Build already in progress' });
    }
    if (startBuildFn) {
        startBuildFn();
    }
    res.json({ message: 'Build started', status: 'running' });
});

module.exports = { router, setBuildState };
