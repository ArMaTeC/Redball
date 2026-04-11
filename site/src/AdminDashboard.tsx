import React, { useState, useEffect } from 'react';
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

  const fetchLogs = async () => {
    try {
      const res = await fetch('/api/admin/logs');
      if (res.ok) {
        const text = await res.text();
        setBuildLogs(text.split('\n').filter(Boolean).slice(-50)); // Last 50 lines
      }
    } catch (e) { console.error('Failed to fetch logs', e); }
  };

  useEffect(() => {
    fetchStats();
    if (activeTab === 'build') {
      const interval = setInterval(fetchLogs, 2000);
      return () => clearInterval(interval);
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
    setBuildLogs(prev => [...prev, '[BUILD] Starting Redball build pipeline...']);
    try {
      await fetch('/api/admin/build', { method: 'POST' });
    } catch {
      setBuildLogs(prev => [...prev, '[ERROR] Failed to trigger build server']);
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
