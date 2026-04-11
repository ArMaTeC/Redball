import React, { useState, useEffect } from 'react';
import { Shield, Rocket, Download, Terminal, Activity } from 'lucide-react';
import { motion } from 'framer-motion';

interface Release {
  version: string;
  date: string;
  totalDownloads: number;
  files: Array<{
    name: string;
    size: number;
    downloads: number;
  }>;
}

interface Stats {
  totalDownloads: number;
  latestVersion: string;
  releases: Release[];
}

const formatDownloads = (num: number): string => {
  if (num >= 1000000) return (num / 1000000).toFixed(1) + 'M';
  if (num >= 1000) return (num / 1000).toFixed(1) + 'k';
  return num.toString();
};

export const LandingPage: React.FC = () => {
  const [stats, setStats] = useState<Stats | null>(null);
  const [loading, setLoading] = useState(true);

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

  return (
    <div className="landing-container">
      {/* Hero Section */}
      <header style={{ padding: '120px 20px 80px', textAlign: 'center', position: 'relative' }}>
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8 }}
        >
          <div className="badge" style={{
            display: 'inline-block',
            padding: '6px 16px',
            borderRadius: '20px',
            background: 'rgba(255, 51, 51, 0.1)',
            color: 'var(--primary)',
            fontSize: '0.875rem',
            fontWeight: '600',
            marginBottom: '24px',
            border: '1px solid rgba(255, 51, 51, 0.2)'
          }}>
            Redball v3.0 Now Available
          </div>
          <h1 style={{ fontSize: 'clamp(3rem, 10vw, 5rem)', lineHeight: 1.1, marginBottom: '24px' }}>
            Type Anything, <br />
            <span className="text-gradient-red">Everywhere.</span>
          </h1>
          <p style={{ maxWidth: '600px', margin: '0 auto 40px', fontSize: '1.25rem' }}>
            A professional clipboard automation engine that simulates human-like typing and keeps your systems alive.
          </p>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px', alignItems: 'center' }}>
            <div style={{ display: 'flex', gap: '16px', justifyContent: 'center', flexWrap: 'wrap' }}>
              {setupFile && latestRelease ? (
                <a
                  href={`/downloads/${latestRelease.version}/${setupFile.name}`}
                  className="btn-primary"
                  style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: '8px' }}
                >
                  <Download size={20} />
                  Download Installer
                  <span style={{ fontSize: '0.75rem', opacity: 0.8 }}>v{latestRelease.version}</span>
                </a>
              ) : (
                <button className="btn-primary" disabled={loading}>
                  <Download size={20} />
                  {loading ? 'Loading...' : 'Download for Windows'}
                </button>
              )}
              <a
                href="https://github.com/ArMaTeC/Redball/releases"
                target="_blank"
                rel="noopener noreferrer"
                className="btn-secondary"
                style={{ textDecoration: 'none', display: 'inline-flex', alignItems: 'center', gap: '8px' }}
              >
                <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M9 19c-5 1.5-5-2.5-7-3m14 6v-3.87a3.37 3.37 0 0 0-.94-2.61c3.14-.35 6.44-1.54 6.44-7A5.44 5.44 0 0 0 20 4.77 5.07 5.07 0 0 0 19.91 1S18.73.65 16 2.48a13.38 13.38 0 0 0-7 0C6.27.65 5.09 1 5.09 1A5.07 5.07 0 0 0 5 4.77a5.44 5.44 0 0 0-1.5 3.78c0 5.42 3.3 6.61 6.44 7A3.37 3.37 0 0 0 9 18.13V22" />
                </svg>
                GitHub Releases
              </a>
            </div>

            {stats && (
              <div style={{ display: 'flex', gap: '24px', fontSize: '0.875rem', color: 'var(--text-dim)' }}>
                <span>{formatDownloads(stats.totalDownloads)} total downloads</span>
                {setupFile && (
                  <span>{formatDownloads(setupFile.downloads)} installer downloads</span>
                )}
              </div>
            )}
          </div>
        </motion.div>

        {/* Feature Preview (Mockup) */}
        <motion.div
          initial={{ opacity: 0, scale: 0.9 }}
          animate={{ opacity: 1, scale: 1 }}
          transition={{ delay: 0.4, duration: 1 }}
          style={{ marginTop: '80px', position: 'relative' }}
        >
          <div className="glass-card" style={{
            maxWidth: '1000px',
            margin: '0 auto',
            height: '500px',
            overflow: 'hidden',
            boxShadow: '0 0 100px rgba(255, 51, 51, 0.1)'
          }}>
            <img
              src="/app-mockup.webp"
              alt="Redball UI Mockup"
              style={{ width: '100%', height: '100%', objectFit: 'cover', opacity: 0.8 }}
            />
          </div>
        </motion.div>
      </header>

      {/* Bento Grid Features */}
      <section style={{ padding: '100px 20px', maxWidth: '1200px', margin: '0 auto' }}>
        <h2 style={{ fontSize: '2.5rem', marginBottom: '60px', textAlign: 'center' }}>Engineered for Efficiency</h2>
        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(300px, 1fr))',
          gap: '30px'
        }}>
          <FeatureCard
            icon={<Terminal color="var(--primary)" />}
            title="TypeThing Engine"
            description="Universal character-by-character typing with human-like random delays. Works where Ctrl+V fails."
          />
          <FeatureCard
            icon={<Activity color="var(--secondary)" />}
            title="Smart Keep-Awake"
            description="Intelligent system monitoring with battery-aware, network-aware, and idle detection logic."
          />
          <FeatureCard
            icon={<Shield color="#4ade80" />}
            title="Secure & Reliable"
            description="Code-signed executables, structured logging, and local-first data storage for total privacy."
          />
          <FeatureCard
            icon={<Rocket color="#fbbf24" />}
            title="14 Custom Themes"
            description="From Cyberpunk to Midnight Blue, personalize your workspace with adaptive, high-DPI layouts."
          />
        </div>
      </section>

      {/* Footer */}
      <footer style={{ padding: '60px 20px', textAlign: 'center', borderTop: '1px solid var(--border-glass)' }}>
        <p>© 2026 ArMaTeC. All rights reserved.</p>
      </footer>
    </div>
  );
};

const FeatureCard: React.FC<{ icon: React.ReactNode, title: string, description: string }> = ({ icon, title, description }) => (
  <div className="glass-card" style={{ padding: '40px' }}>
    <div style={{ marginBottom: '20px' }}>{icon}</div>
    <h3 style={{ fontSize: '1.5rem', marginBottom: '16px' }}>{title}</h3>
    <p>{description}</p>
  </div>
);
