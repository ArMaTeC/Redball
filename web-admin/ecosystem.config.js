module.exports = {
  apps: [{
    name: 'redball-web',
    script: './server.js',
    cwd: '/root/Redball/web-admin',
    instances: 1,
    exec_mode: 'fork',
    env: {
      NODE_ENV: 'production',
      PORT: 3500
    },
    // Graceful shutdown and restart
    kill_timeout: 5000,
    listen_timeout: 10000,
    // Auto-restart on failure
    autorestart: true,
    max_restarts: 5,
    min_uptime: '10s',
    // Logging
    log_file: '/root/Redball/web-admin/logs/combined.log',
    out_file: '/root/Redball/web-admin/logs/out.log',
    err_file: '/root/Redball/web-admin/logs/err.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z',
    // Don't restart if already running (for build script)
    restart_delay: 3000
  }]
};
