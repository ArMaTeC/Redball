import React, { useState, useEffect } from 'react';
import { Shield, Download, Terminal, Activity, Zap, Palette, GitBranch, Menu, X } from 'lucide-react';
import { motion } from 'framer-motion';

interface Release {
  version: string;
  date: string;
  totalDownloads: number;
  files: Array<{
    name: string;
    size: number;
    downloads: number;
    url: string;
  }>;
}

interface Stats {
  totalDownloads: number;
  totalReleases: number;
  latestVersion: string;
  releases: Release[];
}

const formatDownloads = (num: number): string => {
  if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
  if (num >= 1000) return (num / 1000).toFixed(1) + 'k';
  return num.toString();
};

const formatDate = (dateStr: string): string => {
  return new Date(dateStr).toLocaleDateString('en-GB', {
    day: 'numeric',
    month: 'short',
    year: 'numeric'
  });
};

export const LandingPage: React.FC = () => {
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  useEffect(() => {
    fetchStats();
  }, []);

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
      setLoading(false);
    }
  };

  const latestRelease = stats?.releases?.[0];
  const setupFile = latestRelease?.files?.find(f => f.name.endsWith('-Setup.exe'));
  const zipFile = latestRelease?.files?.find(f => f.name.endsWith('.zip'));

  return (
    <div className="landing-page">
      {/* Navigation */}
      <nav style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 100,
        padding: '20px 40px',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        backdropFilter: 'blur(12px)',
        background: 'rgba(8, 12, 20, 0.8)',
        borderBottom: '1px solid rgba(148, 163, 184, 0.1)'
      }}>
        <a href="#" style={{ display: 'flex', alignItems: 'center', gap: '12px', textDecoration: 'none', color: 'white' }}>
          <div style={{
            width: '36px',
            height: '36px',
            borderRadius: '10px',
            background: 'linear-gradient(135deg, #e8443a, #ff6b5a)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center'
          }}>
            <Zap size={20} color="white" />
          </div>
          <span style={{ fontWeight: 700, fontSize: '1.25rem' }}>Redball</span>
        </a>

        {/* Desktop Nav */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '32px' }} className="desktop-nav">
          <a href="#features" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>Features</a>
          <a href="#downloads" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>Download</a>
          <a href="https://github.com/ArMaTeC/Redball" target="_blank" rel="noopener" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>GitHub</a>
          <a
            href="#downloads"
            style={{
              background: 'linear-gradient(135deg, #e8443a, #ff6b5a)',
              color: 'white',
              padding: '10px 20px',
              borderRadius: '8px',
              textDecoration: 'none',
              fontSize: '0.875rem',
              fontWeight: 600
            }}
          >
            Download
          </a>
        </div>

        {/* Mobile Menu Toggle */}
        <button
          onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
          style={{ background: 'none', border: 'none', color: 'white', cursor: 'pointer' }}
          className="mobile-menu-btn"
        >
          {mobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
        </button>
      </nav>

      {/* Mobile Menu */}
      {mobileMenuOpen && (
        <div style={{
          position: 'fixed',
          top: '72px',
          left: 0,
          right: 0,
          background: 'rgba(8, 12, 20, 0.95)',
          backdropFilter: 'blur(12px)',
          zIndex: 99,
          padding: '20px',
          display: 'flex',
          flexDirection: 'column',
          gap: '16px',
          borderBottom: '1px solid rgba(148, 163, 184, 0.1)'
        }}>
          <a href="#features" onClick={() => setMobileMenuOpen(false)} style={{ color: 'white', textDecoration: 'none', padding: '12px' }}>Features</a>
          <a href="#downloads" onClick={() => setMobileMenuOpen(false)} style={{ color: 'white', textDecoration: 'none', padding: '12px' }}>Download</a>
          <a href="https://github.com/ArMaTeC/Redball" target="_blank" rel="noopener" style={{ color: 'white', textDecoration: 'none', padding: '12px' }}>GitHub</a>
        </div>
      )}

      {/* Hero Section */}
      <section style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '120px 20px 60px',
        textAlign: 'center',
        position: 'relative'
      }}>
        <motion.div
          initial={{ opacity: 0, y: 30 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8 }}
          style={{ maxWidth: '900px' }}
        >
          {/* Badge */}
          <div style={{
            display: 'inline-flex',
            alignItems: 'center',
            gap: '8px',
            padding: '8px 16px',
            background: 'rgba(232, 68, 58, 0.1)',
            border: '1px solid rgba(232, 68, 58, 0.3)',
            borderRadius: '20px',
            marginBottom: '32px'
          }}>
            <span style={{
              width: '8px',
              height: '8px',
              background: '#10b981',
              borderRadius: '50%',
              boxShadow: '0 0 10px #10b981'
            }}></span>
            <span style={{ fontSize: '0.875rem', color: 'var(--text-primary)' }}>
              Now Available — Version {stats?.latestVersion || '2.1.x'}
            </span>
          </div>

          {/* Title */}
          <h1 style={{
            fontSize: 'clamp(2.5rem, 6vw, 4.5rem)',
            fontWeight: 800,
            lineHeight: 1.1,
            marginBottom: '24px',
            letterSpacing: '-0.02em'
          }}>
            Keep Your Windows PC{' '}
            <span style={{
              background: 'linear-gradient(135deg, #e8443a, #ff6b5a)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent'
            }}>Awake</span>
          </h1>

          <p style={{
            fontSize: 'clamp(1rem, 2vw, 1.25rem)',
            color: 'var(--text-secondary)',
            maxWidth: '600px',
            margin: '0 auto 40px',
            lineHeight: 1.7
          }}>
            Redball is a modern, lightweight keep-awake utility for Windows.
            Prevent screen lock, sleep, and idle timeout with smart monitoring and beautiful themes.
          </p>

          {/* CTA Buttons */}
          <div style={{ display: 'flex', gap: '16px', justifyContent: 'center', flexWrap: 'wrap' }}>
            {setupFile && latestRelease ? (
              <a
                href={setupFile.url}
                className="btn-primary"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '10px',
                  background: 'linear-gradient(135deg, #00d9a3, #00b386)',
                  color: 'black',
                  padding: '14px 28px',
                  borderRadius: '10px',
                  textDecoration: 'none',
                  fontWeight: 600,
                  fontSize: '0.95rem'
                }}
              >
                <Download size={20} />
                Download Free
              </a>
            ) : (
              <button disabled={loading} style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '10px',
                background: 'linear-gradient(135deg, #00d9a3, #00b386)',
                color: 'black',
                padding: '14px 28px',
                borderRadius: '10px',
                fontWeight: 600,
                fontSize: '0.95rem',
                border: 'none',
                cursor: loading ? 'wait' : 'pointer'
              }}>
                <Download size={20} />
                {loading ? 'Loading...' : 'Download Free'}
              </button>
            )}

            <a
              href="https://github.com/ArMaTeC/Redball"
              target="_blank"
              rel="noopener noreferrer"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '10px',
                background: 'rgba(255, 255, 255, 0.05)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                color: 'white',
                padding: '14px 28px',
                borderRadius: '10px',
                textDecoration: 'none',
                fontWeight: 600,
                fontSize: '0.95rem'
              }}
            >
              <GitBranch size={20} />
              View on GitHub
            </a>
          </div>

          {/* Latest Version */}
          {stats?.latestVersion && (
            <p style={{ marginTop: '20px', fontSize: '0.85rem', color: 'var(--text-muted)' }}>
              Latest: v{stats.latestVersion}
            </p>
          )}
        </motion.div>
      </section>

      {/* Stats Bar */}
      <section style={{
        background: 'linear-gradient(180deg, rgba(13, 19, 32, 0.5) 0%, rgba(13, 19, 32, 0.8) 100%)',
        borderTop: '1px solid rgba(148, 163, 184, 0.1)',
        borderBottom: '1px solid rgba(148, 163, 184, 0.1)',
        padding: '40px 20px'
      }}>
        <div style={{
          maxWidth: '1000px',
          margin: '0 auto',
          display: 'flex',
          justifyContent: 'space-around',
          flexWrap: 'wrap',
          gap: '40px'
        }}>
          <StatItem
            value={stats ? formatDownloads(stats.totalDownloads) : '—'}
            label="Downloads"
            suffix="+"
          />
          <StatItem
            value={stats ? stats.totalReleases.toString() : '—'}
            label="Releases"
          />
          <StatItem
            value={stats ? `v${stats.latestVersion}` : '—'}
            label="Current Version"
          />
          <StatItem
            value="14"
            label="Custom Themes"
          />
        </div>
      </section>

      {/* Features Section */}
      <section id="features" style={{ padding: '100px 20px', maxWidth: '1200px', margin: '0 auto' }}>
        <div style={{ textAlign: 'center', marginBottom: '60px' }}>
          <h2 style={{ fontSize: '2rem', marginBottom: '16px' }}>Why Choose Redball?</h2>
          <p style={{ color: 'var(--text-secondary)', maxWidth: '600px', margin: '0 auto' }}>
            A professional clipboard typer and keep-awake utility built for power users.
          </p>
        </div>

        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
          gap: '24px'
        }}>
          <FeatureCard
            icon={<Palette size={28} />}
            title="12 Beautiful Themes"
            description="Choose from a variety of modern dark and light themes including Midnight, Ocean, Forest, and Sunset."
            color="#ff6b5a"
          />
          <FeatureCard
            icon={<Activity size={28} />}
            title="Smart Detection"
            description="Automatically detects battery state, network activity, and idle time to intelligently manage keep-awake behaviour."
            color="#2d7ff9"
          />
          <FeatureCard
            icon={<Terminal size={28} />}
            title="TypeThing Clipboard Typer"
            description="Built-in clipboard typer that simulates human typing with variable speed for testing and automation."
            color="#00d9a3"
          />
          <FeatureCard
            icon={<Zap size={28} />}
            title="Auto-Updates"
            description="Built-in update system with delta patches for fast, efficient updates. Always stay on the latest version."
            color="#f59e0b"
          />
          <FeatureCard
            icon={<Shield size={28} />}
            title="Secure & Private"
            description="No telemetry, no data collection. Your activity stays on your machine. Optional local analytics only."
            color="#8b5cf6"
          />
          <FeatureCard
            icon={<Download size={28} />}
            title="Lightweight"
            description="Minimal resource usage. Runs quietly in the system tray without impacting performance."
            color="#06b6d4"
          />
        </div>
      </section>

      {/* Download Section */}
      <section id="downloads" style={{
        padding: '100px 20px',
        background: 'linear-gradient(180deg, transparent 0%, rgba(13, 19, 32, 0.5) 50%, transparent 100%)'
      }}>
        <div style={{ maxWidth: '800px', margin: '0 auto', textAlign: 'center' }}>
          <h2 style={{ fontSize: '2rem', marginBottom: '16px' }}>Download Redball</h2>
          <p style={{ color: 'var(--text-secondary)', marginBottom: '40px' }}>
            Free and open source. No ads, no tracking.
          </p>

          <div style={{
            display: 'grid',
            gridTemplateColumns: 'repeat(auto-fit, minmax(250px, 1fr))',
            gap: '20px',
            marginBottom: '40px'
          }}>
            {/* Installer Card */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              style={{
                background: 'linear-gradient(160deg, rgba(17, 24, 39, 0.8), rgba(13, 19, 32, 0.95))',
                border: '1px solid rgba(148, 163, 184, 0.1)',
                borderRadius: '16px',
                padding: '32px'
              }}
            >
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '8px' }}>Recommended</div>
              <h3 style={{ fontSize: '1.25rem', marginBottom: '8px' }}>Windows Installer</h3>
              <p style={{ fontSize: '0.875rem', color: 'var(--text-secondary)', marginBottom: '24px' }}>
                Recommended for most users. Full system integration with auto-updates.
              </p>
              {setupFile && latestRelease ? (
                <a
                  href={setupFile.url}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '8px',
                    background: 'linear-gradient(135deg, #00d9a3, #00b386)',
                    color: 'black',
                    padding: '12px 24px',
                    borderRadius: '8px',
                    textDecoration: 'none',
                    fontWeight: 600,
                    fontSize: '0.9rem'
                  }}
                >
                  <Download size={18} />
                  Download .exe
                </a>
              ) : (
                <button disabled style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  background: 'rgba(255, 255, 255, 0.1)',
                  color: 'var(--text-muted)',
                  padding: '12px 24px',
                  borderRadius: '8px',
                  fontWeight: 600,
                  fontSize: '0.9rem',
                  border: 'none',
                  cursor: 'not-allowed'
                }}>
                  Loading...
                </button>
              )}
            </motion.div>

            {/* ZIP Card */}
            <motion.div
              initial={{ opacity: 0, y: 20 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ delay: 0.1 }}
              style={{
                background: 'linear-gradient(160deg, rgba(17, 24, 39, 0.8), rgba(13, 19, 32, 0.95))',
                border: '1px solid rgba(148, 163, 184, 0.1)',
                borderRadius: '16px',
                padding: '32px'
              }}
            >
              <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', marginBottom: '8px' }}>Portable</div>
              <h3 style={{ fontSize: '1.25rem', marginBottom: '8px' }}>Portable ZIP</h3>
              <p style={{ fontSize: '0.875rem', color: 'var(--text-secondary)', marginBottom: '24px' }}>
                No installation required. Extract and run. Perfect for USB drives.
              </p>
              {zipFile && latestRelease ? (
                <a
                  href={zipFile.url}
                  style={{
                    display: 'inline-flex',
                    alignItems: 'center',
                    gap: '8px',
                    background: 'rgba(255, 255, 255, 0.05)',
                    border: '1px solid rgba(255, 255, 255, 0.1)',
                    color: 'white',
                    padding: '12px 24px',
                    borderRadius: '8px',
                    textDecoration: 'none',
                    fontWeight: 600,
                    fontSize: '0.9rem'
                  }}
                >
                  <Download size={18} />
                  Download .zip
                </a>
              ) : (
                <button disabled style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: '8px',
                  background: 'rgba(255, 255, 255, 0.05)',
                  color: 'var(--text-muted)',
                  padding: '12px 24px',
                  borderRadius: '8px',
                  fontWeight: 600,
                  fontSize: '0.9rem',
                  border: 'none',
                  cursor: 'not-allowed'
                }}>
                  Loading...
                </button>
              )}
            </motion.div>
          </div>
        </div>

        {/* Version History */}
        <div style={{ maxWidth: '800px', margin: '60px auto 0' }}>
          <h3 style={{ fontSize: '1.5rem', textAlign: 'center', marginBottom: '24px' }}>Recent Releases</h3>
          {stats?.releases && stats.releases.length > 0 ? (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '12px' }}>
              {stats.releases.slice(0, 6).map((release) => {
                const rSetupFile = release.files?.find(f => f.name.endsWith('.exe'));
                const downloadUrl = rSetupFile?.url || `https://github.com/ArMaTeC/Redball/releases/tag/v${release.version}`;
                return (
                  <a
                    key={release.version}
                    href={downloadUrl}
                    target={rSetupFile?.url ? undefined : '_blank'}
                    rel={rSetupFile?.url ? undefined : 'noopener noreferrer'}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      padding: '16px 20px',
                      background: 'rgba(17, 24, 39, 0.5)',
                      border: '1px solid rgba(148, 163, 184, 0.1)',
                      borderRadius: '12px',
                      textDecoration: 'none',
                      transition: 'all 0.3s ease',
                      cursor: 'pointer'
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.borderColor = 'rgba(232, 68, 58, 0.3)';
                      e.currentTarget.style.transform = 'translateY(-2px)';
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.borderColor = 'rgba(148, 163, 184, 0.1)';
                      e.currentTarget.style.transform = 'translateY(0)';
                    }}
                  >
                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px' }}>
                      <span style={{
                        fontFamily: 'JetBrains Mono, monospace',
                        fontWeight: 600,
                        color: '#e8443a'
                      }}>
                        v{release.version}
                      </span>
                      <span style={{ color: 'var(--text-muted)', fontSize: '0.85rem' }}>
                        {formatDate(release.date)}
                      </span>
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '20px' }}>
                      <span style={{ color: 'var(--text-secondary)', fontSize: '0.85rem' }}>
                        {formatDownloads(release.totalDownloads)} downloads
                      </span>
                      <span style={{ color: '#e8443a', fontSize: '0.85rem', fontWeight: 500 }}>
                        {rSetupFile?.url ? 'Download →' : 'View Release →'}
                      </span>
                    </div>
                  </a>
                );
              })}
            </div>
          ) : (
            <p style={{ textAlign: 'center', color: 'var(--text-muted)' }}>Loading release history...</p>
          )}
        </div>
      </section>

      {/* Footer */}
      <footer style={{
        padding: '60px 20px',
        textAlign: 'center',
        borderTop: '1px solid rgba(148, 163, 184, 0.1)'
      }}>
        <div style={{ display: 'flex', justifyContent: 'center', gap: '24px', marginBottom: '24px', flexWrap: 'wrap' }}>
          <a href="https://github.com/ArMaTeC/Redball" target="_blank" rel="noopener" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>GitHub</a>
          <a href="https://github.com/ArMaTeC/Redball/issues" target="_blank" rel="noopener" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>Issues</a>
          <a href="https://github.com/ArMaTeC/Redball/discussions" target="_blank" rel="noopener" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>Discussions</a>
          <a href="#/admin" style={{ color: 'var(--text-secondary)', textDecoration: 'none', fontSize: '0.9rem' }}>Admin</a>
        </div>
        <p style={{ color: 'var(--text-muted)', fontSize: '0.85rem' }}>
          © 2026 Redball. Open source under MIT License.
        </p>
      </footer>
    </div>
  );
};

const StatItem: React.FC<{ value: string; label: string; suffix?: string }> = ({ value, label, suffix }) => (
  <div style={{ textAlign: 'center' }}>
    <div style={{
      fontSize: 'clamp(2rem, 4vw, 3rem)',
      fontWeight: 700,
      background: 'linear-gradient(135deg, #00d9a3, #00b386)',
      WebkitBackgroundClip: 'text',
      WebkitTextFillColor: 'transparent'
    }}>
      {value}{suffix}
    </div>
    <div style={{ fontSize: '0.75rem', color: 'var(--text-muted)', textTransform: 'uppercase', letterSpacing: '1px', marginTop: '4px' }}>
      {label}
    </div>
  </div>
);

const FeatureCard: React.FC<{ icon: React.ReactNode; title: string; description: string; color: string }> = ({
  icon,
  title,
  description,
  color
}) => (
  <motion.div
    initial={{ opacity: 0, y: 20 }}
    whileInView={{ opacity: 1, y: 0 }}
    viewport={{ once: true }}
    style={{
      background: 'linear-gradient(160deg, rgba(17, 24, 39, 0.6), rgba(13, 19, 32, 0.8))',
      border: '1px solid rgba(148, 163, 184, 0.1)',
      borderRadius: '16px',
      padding: '32px',
      transition: 'all 0.3s ease'
    }}
    whileHover={{ y: -4, borderColor: 'rgba(148, 163, 184, 0.2)' }}
  >
    <div style={{
      width: '48px',
      height: '48px',
      borderRadius: '12px',
      background: `${color}20`,
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      color: color,
      marginBottom: '20px'
    }}>
      {icon}
    </div>
    <h3 style={{ fontSize: '1.25rem', marginBottom: '12px', fontWeight: 600 }}>{title}</h3>
    <p style={{ fontSize: '0.9rem', color: 'var(--text-secondary)', lineHeight: 1.6 }}>{description}</p>
  </motion.div>
);
