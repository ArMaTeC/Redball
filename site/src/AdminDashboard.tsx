import React, { useState, useEffect, useCallback } from 'react';
import {
  BarChart3,
  Settings,
  Package,
  Terminal,
  Play,
  Plus,
  Clock,
  Search,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';


interface Release {
  version: string;
  date: string;
  totalDownloads: number;
  files: Array<{
    name: string;
    size: number;
    downloads: number;
  }>;
  channel?: string;
}

interface Stats {
  totalDownloads: number;
  totalReleases: number;
  totalFiles: number;
  latestVersion: string;
  releases: Release[];
}

const formatNumber = (num: number): string => {
  if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
  if (num >= 1000) return (num / 1000).toFixed(1) + 'k';
  return num.toString();
};

const formatBytes = (bytes: number): string => {
  if (bytes === 0) return '0 Bytes';
  const k = 1024;
  const sizes = ['Bytes', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
};

export const AdminDashboard: React.FC = () => {
  const [activeTab, setActiveTab] = useState('overview');
  const [buildLogs, setBuildLogs] = useState<string[]>(['[IDLE] System ready for build...']);
  const [isBuilding, setIsBuilding] = useState(false);
  const [stats, setStats] = useState<Stats | null>(null);
  const [statsLoading, setStatsLoading] = useState(true);
  const wsRef = React.useRef<WebSocket | null>(null);

  const fetchLogs = async () => {
    try {
      const token = localStorage.getItem('token');
      const res = await fetch('/api/build/log', {
        headers: token ? { 'Authorization': `Bearer ${token}` } : {}
      });
      if (res.ok) {
        const text = await res.text();
        setBuildLogs(text.split('\n').filter(Boolean).slice(-50)); // Last 50 lines
      }
    } catch (e) { console.error('Failed to fetch logs', e); }
  };

  // Setup WebSocket for real-time build output
  useEffect(() => {
    const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
    const token = localStorage.getItem('token') || '';
    const wsUrl = `${protocol}//${window.location.host}/ws?token=${encodeURIComponent(token)}`;
    const ws = new WebSocket(wsUrl);
    wsRef.current = ws;

    ws.onopen = () => {
      console.log('[WebSocket] Connected');
    };

    ws.onmessage = (event) => {
      try {
        const msg = JSON.parse(event.data);
        if (msg.type === 'build-output' && msg.data?.line) {
          setBuildLogs(prev => [...prev.slice(-199), msg.data.line]);
        } else if (msg.type === 'build-started') {
          setIsBuilding(true);
          setBuildLogs(['[BUILD] Build engine starting...']);
        } else if (msg.type === 'build-complete') {
          setIsBuilding(false);
          const duration = msg.data?.duration?.toFixed(1) || '?';
          setBuildLogs(prev => [...prev, `[SUCCESS] Build completed in ${duration}s`]);
        }
      } catch (e) {
        console.error('[WebSocket] Failed to parse message:', e);
      }
    };

    ws.onerror = (err) => {
      console.error('[WebSocket] Error:', err);
    };

    ws.onclose = () => {
      console.log('[WebSocket] Disconnected');
    };

    return () => {
      ws.close();
    };
  }, []);

  useEffect(() => {
    fetchStats();
    if (activeTab === 'build') {
      fetchLogs();
    }
  }, [activeTab]);

  const fetchStats = async () => {
    try {
      const res = await fetch('/api/stats');
      if (res.ok) {
        const data = await res.json();
        setStats(data);
      }
    } catch (e) {
      console.error('Failed to fetch stats', e);
    } finally {
      setStatsLoading(false);
    }
  };

  const runBuild = async () => {
    setIsBuilding(true);
    setBuildLogs(['[BUILD] Starting Redball build pipeline...']);
    try {
      const token = localStorage.getItem('token');
      const res = await fetch('/api/build/start', {
        method: 'POST',
        headers: token ? { 'Authorization': `Bearer ${token}` } : {}
      });
      if (!res.ok) {
        throw new Error(`HTTP ${res.status}`);
      }
    } catch (err) {
      setBuildLogs(prev => [...prev, `[ERROR] Failed to trigger build: ${err}`]);
      setIsBuilding(false);
    }
  };


  return (
    <div className="admin-container" style={{ display: 'flex', height: '100vh', background: '#050505' }}>
      {/* Sidebar */}
      <aside style={{
        width: '280px',
        borderRight: '1px solid var(--border-glass)',
        padding: '30px',
        display: 'flex',
        flexDirection: 'column',
        gap: '40px'
      }}>
        <div className="logo" style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div style={{ width: '32px', height: '32px', background: 'var(--primary)', borderRadius: '8px' }}></div>
          <span style={{ fontWeight: '700', fontSize: '1.25rem' }}>Redball<span style={{ color: 'var(--text-dim)' }}>_Admin</span></span>
        </div>

        <nav style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
          <NavItem active={activeTab === 'overview'} onClick={() => setActiveTab('overview')} icon={<BarChart3 size={20} />} label="Overview" />
          <NavItem active={activeTab === 'build'} onClick={() => setActiveTab('build')} icon={<Terminal size={20} />} label="Build Engine" />
          <NavItem active={activeTab === 'releases'} onClick={() => setActiveTab('releases')} icon={<Package size={20} />} label="Releases" />
          <NavItem active={activeTab === 'config'} onClick={() => setActiveTab('config')} icon={<Settings size={20} />} label="System Config" />
        </nav>

        <div style={{ marginTop: 'auto' }}>
          <div className="glass-card" style={{ padding: '16px', borderRadius: '12px', fontSize: '0.875rem' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '8px', color: '#4ade80' }}>
              <div style={{ width: '8px', height: '8px', background: '#4ade80', borderRadius: '50%' }}></div>
              Nodes Online
            </div>
            <p style={{ margin: 0 }}>Update Server: v3.1.2</p>
          </div>
        </div>
      </aside>

      {/* Main Content */}
      <main style={{ flex: 1, padding: '40px', overflowY: 'auto' }}>
        <header style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '40px' }}>
          <h2 style={{ fontSize: '2rem' }}>{activeTab.charAt(0).toUpperCase() + activeTab.slice(1)}</h2>
          <div style={{ display: 'flex', gap: '12px' }}>
            <div className="search-bar" style={{
              background: 'rgba(255,255,255,0.05)',
              borderRadius: '8px',
              padding: '8px 16px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              border: '1px solid var(--border-glass)'
            }}>
              <Search size={18} color="var(--text-dim)" />
              <input type="text" placeholder="Search resources..." style={{ background: 'none', border: 'none', color: 'white', outline: 'none' }} />
            </div>
            <button className="btn-primary" onClick={runBuild} disabled={isBuilding}>
              {isBuilding ? <Plus className="animate-spin" size={18} /> : <Play size={18} />}
              Trigger Build
            </button>
          </div>
        </header>

        {activeTab === 'overview' && (
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '24px' }}>
            <StatCard
              label="Total Downloads"
              value={statsLoading ? '...' : formatNumber(stats?.totalDownloads || 0)}
              trend="+12%"
            />
            <StatCard
              label="Total Releases"
              value={statsLoading ? '...' : (stats?.totalReleases || 0).toString()}
              trend="GitHub + Local"
            />
            <StatCard
              label="Latest Version"
              value={statsLoading ? '...' : (stats?.latestVersion || 'N/A')}
              trend="Current"
            />

            <div className="glass-card" style={{ gridColumn: 'span 2', padding: '30px', height: '300px' }}>
              <h3 style={{ marginBottom: '20px' }}>Deployment History</h3>
              {/* Chart Placeholder */}
              <div style={{ width: '100%', height: '200px', display: 'flex', alignItems: 'flex-end', gap: '12px' }}>
                {[40, 60, 45, 90, 65, 80, 50, 70].map((h, i) => (
                  <div key={i} style={{ flex: 1, height: `${h}%`, background: 'linear-gradient(to top, var(--primary), var(--primary-glow))', borderRadius: '4px 4px 0 0' }}></div>
                ))}
              </div>
            </div>

            <div className="glass-card" style={{ padding: '30px' }}>
              <h3 style={{ marginBottom: '20px' }}>Recent Logs</h3>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
                <LogItem type="success" msg="Build #472 published" time="12m ago" />
                <LogItem type="error" msg="RDP service failed" time="45m ago" />
                <LogItem type="info" msg="Config updated by admin" time="1h ago" />
              </div>
            </div>
          </div>
        )}

        {activeTab === 'build' && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '24px' }}>
            <div className="glass-card" style={{ padding: '30px' }}>
              <h3>Build Console</h3>
              <div style={{
                background: '#000',
                borderRadius: '8px',
                padding: '20px',
                height: '400px',
                fontFamily: 'monospace',
                overflowY: 'auto',
                marginTop: '16px',
                border: '1px solid #333'
              }}>
                {buildLogs.map((log, i) => (
                  <div key={i} style={{ color: log.includes('SUCCESS') ? '#4ade80' : log.includes('ERR') ? '#f87171' : '#fff', marginBottom: '4px' }}>
                    {log}
                  </div>
                ))}
                {isBuilding && (
                  <div style={{ color: 'var(--primary)', animation: 'pulse 1.5s infinite' }}>_ Building...</div>
                )}
              </div>
            </div>
            <div style={{ display: 'flex', gap: '16px' }}>
              <button className="btn-secondary" style={{ flex: 1 }}>Full Log History</button>
              <button className="btn-secondary" style={{ flex: 1 }}>Clear Artifacts</button>
              <button className="btn-primary" style={{ flex: 1 }}>Publish to Release Channel</button>
            </div>
          </div>
        )}

        {activeTab === 'releases' && (
          <div style={{ padding: '20px' }}>
            <h2 style={{ marginBottom: '24px' }}>Releases</h2>
            {statsLoading ? (
              <p style={{ color: 'var(--text-dim)' }}>Loading releases...</p>
            ) : stats?.releases?.length === 0 ? (
              <p style={{ color: 'var(--text-dim)' }}>No releases found</p>
            ) : (
              <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
                {stats?.releases?.map((release) => (
                  <div key={release.version} className="glass-card" style={{ padding: '20px' }}>
                    <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '12px' }}>
                      <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                        <h3 style={{ margin: 0 }}>v{release.version}</h3>
                        {release.channel === 'beta' && (
                          <span style={{
                            background: 'rgba(251, 191, 36, 0.2)',
                            color: '#fbbf24',
                            padding: '2px 8px',
                            borderRadius: '4px',
                            fontSize: '0.75rem'
                          }}>
                            BETA
                          </span>
                        )}
                      </div>
                      <span style={{ color: 'var(--text-dim)', fontSize: '0.875rem' }}>
                        {new Date(release.date).toLocaleDateString()}
                      </span>
                    </div>
                    <div style={{ display: 'flex', gap: '24px', marginBottom: '12px', fontSize: '0.875rem' }}>
                      <span>{formatNumber(release.totalDownloads)} downloads</span>
                      <span>{release.files?.length || 0} files</span>
                    </div>
                    {release.files && release.files.length > 0 && (
                      <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
                        {release.files.map((file) => (
                          <div
                            key={file.name}
                            style={{
                              display: 'flex',
                              justifyContent: 'space-between',
                              alignItems: 'center',
                              padding: '8px 12px',
                              background: 'rgba(255,255,255,0.03)',
                              borderRadius: '6px'
                            }}
                          >
                            <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
                              <span style={{ fontSize: '0.875rem' }}>{file.name}</span>
                              <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>
                                {formatBytes(file.size)}
                              </span>
                            </div>
                            <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                              <span style={{ fontSize: '0.75rem', color: 'var(--text-dim)' }}>
                                {formatNumber(file.downloads || 0)} dl
                              </span>
                              <a
                                href={`/downloads/${release.version}/${file.name}`}
                                className="btn-primary"
                                style={{
                                  padding: '4px 12px',
                                  fontSize: '0.75rem',
                                  textDecoration: 'none'
                                }}
                              >
                                Download
                              </a>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </div>
        )}

        {activeTab === 'config' && <SystemConfigPanel />}
      </main>
    </div>
  );
};

const NavItem: React.FC<{ active: boolean, onClick: () => void, icon: React.ReactNode, label: string }> = ({ active, onClick, icon, label }) => (
  <div
    onClick={onClick}
    style={{
      padding: '12px 16px',
      borderRadius: '8px',
      display: 'flex',
      alignItems: 'center',
      gap: '12px',
      cursor: 'pointer',
      background: active ? 'rgba(255, 51, 51, 0.1)' : 'transparent',
      color: active ? 'var(--primary)' : 'var(--text-dim)',
      transition: 'all 0.2s ease'
    }}
  >
    {icon}
    <span style={{ fontWeight: active ? '600' : '400' }}>{label}</span>
  </div>
);

const StatCard: React.FC<{ label: string, value: string, trend: string }> = ({ label, value, trend }) => (
  <div className="glass-card" style={{ padding: '24px' }}>
    <p style={{ fontSize: '0.875rem', marginBottom: '8px' }}>{label}</p>
    <div style={{ display: 'flex', alignItems: 'baseline', gap: '8px' }}>
      <h4 style={{ fontSize: '1.75rem', margin: 0 }}>{value}</h4>
      <span style={{ fontSize: '0.75rem', color: trend.startsWith('+') ? '#4ade80' : '#f87171' }}>{trend}</span>
    </div>
  </div>
);

const LogItem: React.FC<{ type: 'success' | 'error' | 'info', msg: string, time: string }> = ({ type, msg, time }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: '12px', fontSize: '0.875rem' }}>
    {type === 'success' && <CheckCircle2 size={16} color="#4ade80" />}
    {type === 'error' && <AlertCircle size={16} color="#f87171" />}
    {type === 'info' && <Clock size={16} color="var(--text-dim)" />}
    <span style={{ flex: 1 }}>{msg}</span>
    <span style={{ color: 'var(--text-dim)', fontSize: '0.75rem' }}>{time}</span>
  </div>
);

interface ServerInfo {
  hostname?: string;
  platform?: string;
  nodeVersion?: string;
  uptime?: number;
  webPort?: number;
  updatePort?: number;
  env?: string;
  releaseCount?: number;
}

interface Config {
  [key: string]: unknown;
}

const LoginPanel: React.FC<{ onLogin: () => void }> = ({ onLogin }) => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');
    try {
      const res = await fetch('/api/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ username, password })
      });
      const data = await res.json();
      if (res.ok && data.token) {
        localStorage.setItem('token', data.token);
        onLogin();
      } else {
        setError(data.error || 'Login failed');
      }
    } catch {
      setError('Network error');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="glass-card" style={{ maxWidth: '400px', margin: '100px auto', padding: '40px' }}>
      <h2 style={{ marginBottom: '24px', textAlign: 'center' }}>Admin Login</h2>
      <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
        <label htmlFor="login-username" style={{ fontSize: '13px', color: 'var(--text-dim)' }}>Username</label>
        <input
          id="login-username"
          type="text"
          placeholder="Username"
          aria-label="Username"
          autoComplete="username"
          value={username}
          onChange={(e) => setUsername(e.target.value)}
          style={{ padding: '12px', borderRadius: '8px', border: '1px solid var(--border)', background: 'var(--surface)', color: 'var(--fg)' }}
        />
        <label htmlFor="login-password" style={{ fontSize: '13px', color: 'var(--text-dim)' }}>Password</label>
        <input
          id="login-password"
          type="password"
          placeholder="Password"
          aria-label="Password"
          autoComplete="current-password"
          value={password}
          onChange={(e) => setPassword(e.target.value)}
          style={{ padding: '12px', borderRadius: '8px', border: '1px solid var(--border)', background: 'var(--surface)', color: 'var(--fg)' }}
        />
        {error && <div style={{ color: '#ff6b6b', fontSize: '14px' }}>{error}</div>}
        <button
          type="submit"
          disabled={loading}
          style={{ padding: '12px', borderRadius: '8px', border: 'none', background: 'var(--accent)', color: 'white', cursor: loading ? 'not-allowed' : 'pointer', opacity: loading ? 0.7 : 1 }}
        >
          {loading ? 'Logging in...' : 'Login'}
        </button>
      </form>
    </div>
  );
};

const SystemConfigPanel: React.FC = () => {
  const [serverInfo, setServerInfo] = useState<ServerInfo | null>(null);
  const [config, setConfig] = useState<Config | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [isAuthenticated, setIsAuthenticated] = useState(!!localStorage.getItem('token'));

  const getAuthHeaders = (): Record<string, string> => {
    const token = localStorage.getItem('token');
    return token ? { 'Authorization': `Bearer ${token}` } : {};
  };

  const fetchConfig = useCallback(async () => {
    try {
      const [serverRes, configRes] = await Promise.all([
        fetch('/api/system/config', { headers: getAuthHeaders() }),
        fetch('/api/config', { headers: getAuthHeaders() })
      ]);
      if (serverRes.status === 401 || configRes.status === 401) {
        setIsAuthenticated(false);
        localStorage.removeItem('token');
        return;
      }
      if (serverRes.ok) setServerInfo(await serverRes.json());
      if (configRes.ok) setConfig(await configRes.json());
    } catch (e) {
      console.error('Failed to fetch config:', e);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchConfig();
  }, [fetchConfig]);

  const saveConfig = useCallback(async () => {
    setSaving(true);
    try {
      const res = await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json', ...getAuthHeaders() } as Record<string, string>,
        body: JSON.stringify(config)
      });
      if (res.ok) {
        alert('Configuration saved successfully!');
      } else if (res.status === 401) {
        alert('Authentication required. Please log in again.');
      } else {
        alert('Failed to save configuration');
      }
    } catch {
      alert('Error saving configuration');
    } finally {
      setSaving(false);
    }
  }, [config]);

  const updateConfigField = (field: string, value: unknown) => {
    setConfig((prev: Config | null) => prev ? { ...prev, [field]: value } : { [field]: value });
  };

  if (loading) {
    return (
      <div style={{ padding: '40px', textAlign: 'center', color: 'var(--text-dim)' }}>
        Loading configuration...
      </div>
    );
  }

  if (!isAuthenticated) {
    return <LoginPanel onLogin={() => { setIsAuthenticated(true); fetchConfig(); }} />;
  }

  return (
    <div style={{ padding: '20px', display: 'flex', flexDirection: 'column', gap: '24px' }}>
      {/* Server Info */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>Server Information</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))', gap: '16px' }}>
          <InfoItem label="Hostname" value={serverInfo?.hostname ?? '-'} />
          <InfoItem label="Platform" value={serverInfo?.platform ?? '-'} />
          <InfoItem label="Node.js Version" value={serverInfo?.nodeVersion ?? '-'} />
          <InfoItem label="Uptime" value={formatUptime(serverInfo?.uptime)} />
          <InfoItem label="Web Admin Port" value={serverInfo?.webPort ?? '-'} />
          <InfoItem label="Update Server Port" value={serverInfo?.updatePort ?? '-'} />
          <InfoItem label="Environment" value={serverInfo?.env ?? '-'} />
          <InfoItem label="Release Count" value={serverInfo?.releaseCount ?? '-'} />
        </div>
      </div>

      {/* Keep-Alive Settings */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>Keep-Alive Settings</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigNumberInput
            label="Heartbeat Interval (seconds)"
            value={config?.HeartbeatSeconds}
            onChange={(v) => updateConfigField('HeartbeatSeconds', v)}
            min={1}
            max={300}
          />
          <ConfigNumberInput
            label="Default Duration (minutes)"
            value={config?.DefaultDuration}
            onChange={(v) => updateConfigField('DefaultDuration', v)}
            min={1}
            max={1440}
          />
          <ConfigSelectInput
            label="Heartbeat Input Mode"
            value={config?.HeartbeatInputMode}
            onChange={(v) => updateConfigField('HeartbeatInputMode', v)}
            options={[{ value: 'F15', label: 'F15 Key' }, { value: 'Shift', label: 'Shift Key' }, { value: 'Mouse', label: 'Mouse Move' }]}
          />
          <ConfigCheckboxInput
            label="Prevent Display Sleep"
            checked={config?.PreventDisplaySleep}
            onChange={(v) => updateConfigField('PreventDisplaySleep', v)}
          />
          <ConfigCheckboxInput
            label="Use Heartbeat Keypress"
            checked={config?.UseHeartbeatKeypress}
            onChange={(v) => updateConfigField('UseHeartbeatKeypress', v)}
          />
        </div>
      </div>

      {/* TypeThing Settings */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>TypeThing Settings</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigCheckboxInput
            label="Enable TypeThing"
            checked={config?.TypeThingEnabled}
            onChange={(v) => updateConfigField('TypeThingEnabled', v)}
          />
          <ConfigCheckboxInput
            label="Show Notifications"
            checked={config?.TypeThingNotifications}
            onChange={(v) => updateConfigField('TypeThingNotifications', v)}
          />
          <ConfigNumberInput
            label="Min Delay (ms)"
            value={config?.TypeThingMinDelayMs}
            onChange={(v) => updateConfigField('TypeThingMinDelayMs', v)}
            min={10}
            max={1000}
          />
          <ConfigNumberInput
            label="Max Delay (ms)"
            value={config?.TypeThingMaxDelayMs}
            onChange={(v) => updateConfigField('TypeThingMaxDelayMs', v)}
            min={10}
            max={2000}
          />
          <ConfigSelectInput
            label="Theme"
            value={config?.TypeThingTheme}
            onChange={(v) => updateConfigField('TypeThingTheme', v)}
            options={[{ value: 'dark', label: 'Dark' }, { value: 'light', label: 'Light' }, { value: 'system', label: 'System' }]}
          />
        </div>
      </div>

      {/* Smart Features */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>Smart Features</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigCheckboxInput
            label="Battery Aware"
            checked={config?.BatteryAware}
            onChange={(v) => updateConfigField('BatteryAware', v)}
          />
          <ConfigNumberInput
            label="Battery Threshold (%)"
            value={config?.BatteryThreshold}
            onChange={(v) => updateConfigField('BatteryThreshold', v)}
            min={5}
            max={50}
          />
          <ConfigCheckboxInput
            label="Network Aware"
            checked={config?.NetworkAware}
            onChange={(v) => updateConfigField('NetworkAware', v)}
          />
          <ConfigCheckboxInput
            label="Idle Detection"
            checked={config?.IdleDetection}
            onChange={(v) => updateConfigField('IdleDetection', v)}
          />
          <ConfigNumberInput
            label="Idle Threshold (seconds)"
            value={config?.IdleThreshold}
            onChange={(v) => updateConfigField('IdleThreshold', v)}
            min={10}
            max={3600}
          />
          <ConfigCheckboxInput
            label="Presentation Mode Detection"
            checked={config?.PresentationModeDetection}
            onChange={(v) => updateConfigField('PresentationModeDetection', v)}
          />
        </div>
      </div>

      {/* Update Settings */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>Update Settings</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigTextInput
            label="Repository Owner"
            value={config?.UpdateRepoOwner}
            onChange={(v) => updateConfigField('UpdateRepoOwner', v)}
          />
          <ConfigTextInput
            label="Repository Name"
            value={config?.UpdateRepoName}
            onChange={(v) => updateConfigField('UpdateRepoName', v)}
          />
          <ConfigSelectInput
            label="Update Channel"
            value={config?.UpdateChannel}
            onChange={(v) => updateConfigField('UpdateChannel', v)}
            options={[{ value: 'stable', label: 'Stable' }, { value: 'beta', label: 'Beta' }, { value: 'dev', label: 'Development' }]}
          />
          <ConfigCheckboxInput
            label="Verify Update Signatures"
            checked={config?.VerifyUpdateSignature}
            onChange={(v) => updateConfigField('VerifyUpdateSignature', v)}
          />
        </div>
      </div>

      {/* UI Settings */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>UI Settings</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigSelectInput
            label="Theme"
            value={config?.Theme}
            onChange={(v) => updateConfigField('Theme', v)}
            options={[
              { value: 'System', label: 'System Default' },
              { value: 'Dark', label: 'Dark' },
              { value: 'Light', label: 'Light' },
              { value: 'Midnight Blue', label: 'Midnight Blue' },
              { value: 'Forest Green', label: 'Forest Green' },
              { value: 'Ocean Blue', label: 'Ocean Blue' },
              { value: 'Sunset Orange', label: 'Sunset Orange' },
              { value: 'Royal Purple', label: 'Royal Purple' },
              { value: 'Slate Grey', label: 'Slate Grey' },
              { value: 'Rose Gold', label: 'Rose Gold' },
              { value: 'Cyberpunk', label: 'Cyberpunk' },
              { value: 'Coffee', label: 'Coffee' },
              { value: 'Arctic Frost', label: 'Arctic Frost' }
            ]}
          />
          <ConfigSelectInput
            label="Locale"
            value={config?.Locale}
            onChange={(v) => updateConfigField('Locale', v)}
            options={[{ value: 'en', label: 'English' }, { value: 'en-GB', label: 'English (UK)' }]}
          />
          <ConfigCheckboxInput
            label="Minimise to Tray"
            checked={config?.MinimizeToTray}
            onChange={(v) => updateConfigField('MinimizeToTray', v)}
          />
          <ConfigCheckboxInput
            label="Minimise on Start"
            checked={config?.MinimizeOnStart}
            onChange={(v) => updateConfigField('MinimizeOnStart', v)}
          />
          <ConfigCheckboxInput
            label="Show Notifications"
            checked={config?.ShowNotifications}
            onChange={(v) => updateConfigField('ShowNotifications', v)}
          />
          <ConfigCheckboxInput
            label="Show Balloon on Start"
            checked={config?.ShowBalloonOnStart}
            onChange={(v) => updateConfigField('ShowBalloonOnStart', v)}
          />
        </div>
      </div>

      {/* Logging & Debug */}
      <div className="glass-card" style={{ padding: '24px' }}>
        <h3 style={{ marginBottom: '20px' }}>Logging & Debug</h3>
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(280px, 1fr))', gap: '16px' }}>
          <ConfigCheckboxInput
            label="Verbose Logging"
            checked={config?.VerboseLogging}
            onChange={(v) => updateConfigField('VerboseLogging', v)}
          />
          <ConfigCheckboxInput
            label="Enable Telemetry"
            checked={config?.EnableTelemetry}
            onChange={(v) => updateConfigField('EnableTelemetry', v)}
          />
          <ConfigCheckboxInput
            label="Enable Performance Metrics"
            checked={config?.EnablePerformanceMetrics}
            onChange={(v) => updateConfigField('EnablePerformanceMetrics', v)}
          />
          <ConfigNumberInput
            label="Max Log Size (MB)"
            value={config?.MaxLogSizeMB}
            onChange={(v) => updateConfigField('MaxLogSizeMB', v)}
            min={1}
            max={100}
          />
        </div>
      </div>

      {/* Save Button */}
      <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '12px' }}>
        <button
          className="btn-secondary"
          onClick={async () => {
            if (confirm('Reset all settings to factory defaults? This cannot be undone.')) {
              try {
                const res = await fetch('/api/config', {
                  method: 'POST',
                  headers: { 'Content-Type': 'application/json', ...getAuthHeaders() },
                  body: JSON.stringify({})
                });
                if (res.ok) {
                  alert('Configuration reset to defaults');
                  fetchConfig();
                } else if (res.status === 401) {
                  alert('Authentication required. Please log in again.');
                } else {
                  alert('Failed to reset configuration');
                }
              } catch {
                alert('Error resetting configuration');
              }
            }
          }}
          disabled={saving}
        >
          Reset to Defaults
        </button>
        <button
          className="btn-secondary"
          onClick={fetchConfig}
          disabled={saving}
        >
          Discard Changes
        </button>
        <button
          className="btn-primary"
          onClick={saveConfig}
          disabled={saving}
          style={{ minWidth: '140px' }}
        >
          {saving ? 'Saving...' : 'Save Configuration'}
        </button>
      </div>
    </div>
  );
};

const InfoItem: React.FC<{ label: string, value: string | number }> = ({ label, value }) => (
  <div>
    <div style={{ fontSize: '0.75rem', color: 'var(--text-dim)', marginBottom: '4px' }}>{label}</div>
    <div style={{ fontSize: '1rem', fontWeight: 500 }}>{value || '-'}</div>
  </div>
);

const ConfigTextInput: React.FC<{ label: string, value: unknown, onChange: (value: string) => void }> = ({ label, value, onChange }) => (
  <div>
    <label style={{ display: 'block', fontSize: '0.875rem', color: 'var(--text-dim)', marginBottom: '8px' }}>{label}</label>
    <input
      type="text"
      value={String(value || '')}
      onChange={(e) => onChange(e.target.value)}
      style={{
        width: '100%',
        background: 'rgba(255,255,255,0.05)',
        border: '1px solid var(--border-glass)',
        borderRadius: '6px',
        padding: '10px 12px',
        color: 'white',
        fontSize: '0.875rem',
        outline: 'none'
      }}
    />
  </div>
);

const ConfigNumberInput: React.FC<{ label: string, value: unknown, onChange: (value: number) => void, min?: number, max?: number }> = ({ label, value, onChange, min, max }) => (
  <div>
    <label style={{ display: 'block', fontSize: '0.875rem', color: 'var(--text-dim)', marginBottom: '8px' }}>{label}</label>
    <input
      type="number"
      value={Number(value || 0)}
      min={min}
      max={max}
      onChange={(e) => onChange(parseInt(e.target.value) || 0)}
      style={{
        width: '100%',
        background: 'rgba(255,255,255,0.05)',
        border: '1px solid var(--border-glass)',
        borderRadius: '6px',
        padding: '10px 12px',
        color: 'white',
        fontSize: '0.875rem',
        outline: 'none'
      }}
    />
  </div>
);

const ConfigSelectInput: React.FC<{ label: string, value: unknown, onChange: (value: string) => void, options: { value: string, label: string }[] }> = ({ label, value, onChange, options }) => (
  <div>
    <label style={{ display: 'block', fontSize: '0.875rem', color: 'var(--text-dim)', marginBottom: '8px' }}>{label}</label>
    <select
      value={String(value || '')}
      onChange={(e) => onChange(e.target.value)}
      style={{
        width: '100%',
        background: 'rgba(255,255,255,0.05)',
        border: '1px solid var(--border-glass)',
        borderRadius: '6px',
        padding: '10px 12px',
        color: 'white',
        fontSize: '0.875rem',
        outline: 'none',
        cursor: 'pointer'
      }}
    >
      {options.map(opt => (
        <option key={opt.value} value={opt.value} style={{ background: '#1a1a1a' }}>{opt.label}</option>
      ))}
    </select>
  </div>
);

const ConfigCheckboxInput: React.FC<{ label: string, checked: unknown, onChange: (checked: boolean) => void }> = ({ label, checked, onChange }) => (
  <div style={{ display: 'flex', alignItems: 'center', gap: '12px', paddingTop: '24px' }}>
    <input
      type="checkbox"
      checked={Boolean(checked)}
      onChange={(e) => onChange(e.target.checked)}
      style={{ width: '20px', height: '20px', cursor: 'pointer' }}
    />
    <label style={{ fontSize: '0.875rem', color: 'var(--text-dim)', cursor: 'pointer' }}>{label}</label>
  </div>
);

const formatUptime = (seconds: number | undefined): string => {
  if (!seconds) return '-';
  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const mins = Math.floor((seconds % 3600) / 60);
  if (days > 0) return `${days}d ${hours}h ${mins}m`;
  if (hours > 0) return `${hours}h ${mins}m`;
  return `${mins}m`;
};
