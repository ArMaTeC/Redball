const test = require('node:test');
const assert = require('node:assert');
const http = require('node:http');
const { spawn } = require('node:child_process');
const path = require('node:path');

// Port for testing
const TEST_PORT = 3501;
let serverProcess;

test.before(async () => {
    // Start the server in a separate process
    serverProcess = spawn('node', ['server.js'], {
        cwd: path.join(__dirname, '..'),
        env: { ...process.env, PORT: TEST_PORT }
    });

    // Wait for server to start
    return new Promise((resolve, reject) => {
        let started = false;
        const timeout = setTimeout(() => {
            if (!started) {
                serverProcess.kill();
                reject(new Error('Server failed to start in time'));
            }
        }, 5000);

        const checkHealth = () => {
            http.get(`http://localhost:${TEST_PORT}/api/health`, (res) => {
                if (res.statusCode === 200) {
                    started = true;
                    clearTimeout(timeout);
                    resolve();
                } else {
                    setTimeout(checkHealth, 500);
                }
            }).on('error', () => {
                setTimeout(checkHealth, 500);
            });
        };
        checkHealth();
    });
});

test.after(() => {
    if (serverProcess) serverProcess.kill();
});

test('GET /api/health returns healthy status', async (t) => {
    return new Promise((resolve, reject) => {
        http.get(`http://localhost:${TEST_PORT}/api/health`, (res) => {
            assert.strictEqual(res.statusCode, 200);
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                const body = JSON.parse(data);
                assert.strictEqual(body.status, 'healthy');
                resolve();
            });
        }).on('error', reject);
    });
});

test('GET /api/releases returns an array', async (t) => {
    return new Promise((resolve, reject) => {
        http.get(`http://localhost:${TEST_PORT}/api/releases`, (res) => {
            assert.strictEqual(res.statusCode, 200);
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                const releases = JSON.parse(data);
                assert.ok(Array.isArray(releases));
                resolve();
            });
        }).on('error', reject);
    });
});

test('GET /api/stats returns server metrics', async (t) => {
    return new Promise((resolve, reject) => {
        http.get(`http://localhost:${TEST_PORT}/api/stats`, (res) => {
            assert.strictEqual(res.statusCode, 200);
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                const stats = JSON.parse(data);
                assert.ok(stats.totalDownloads !== undefined);
                assert.ok(stats.latestVersion !== undefined);
                resolve();
            });
        }).on('error', reject);
    });
});
