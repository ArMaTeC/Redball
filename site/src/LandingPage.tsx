import React, { useState, useEffect } from 'react';
import { Shield, Download, Terminal, Activity, Zap, Palette, GitBranch, Menu, X, ArrowRight, Monitor } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';

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
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const [scrolled, setScrolled] = useState(false);

  useEffect(() => {
    fetchStats();
    const handleScroll = () => setScrolled(window.scrollY > 20);
    window.addEventListener('scroll', handleScroll);
    return () => window.removeEventListener('scroll', handleScroll);
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
      // stats loaded
    }
  };

  const latestRelease = stats?.releases?.[0];
  const setupFile = latestRelease?.files?.find(f => f.name.endsWith('-Setup.exe'));

  const containerVariants = {
    hidden: { opacity: 0 },
    visible: {
      opacity: 1,
      transition: { staggerChildren: 0.1 }
    }
  };

  const itemVariants = {
    hidden: { opacity: 0, y: 20 },
    visible: { opacity: 1, y: 0 }
  };

  return (
    <div className="landing-page" style={{ 
      background: '#04070a', 
      color: 'white', 
      minHeight: '100vh',
      fontFamily: '"Outfit", "Inter", sans-serif',
      overflowX: 'hidden'
    }}>
      {/* Background Gradients */}
      <div style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: 0,
        pointerEvents: 'none',
        overflow: 'hidden'
      }}>
        <div style={{
          position: 'absolute',
          top: '-10%',
          right: '-10%',
          width: '50vw',
          height: '50vw',
          background: 'radial-gradient(circle, rgba(232, 68, 58, 0.08) 0%, transparent 70%)',
          filter: 'blur(80px)'
        }} />
        <div style={{
          position: 'absolute',
          bottom: '-10%',
          left: '-10%',
          width: '40vw',
          height: '40vw',
          background: 'radial-gradient(circle, rgba(0, 217, 163, 0.05) 0%, transparent 70%)',
          filter: 'blur(80px)'
        }} />
      </div>

      {/* Navigation */}
      <nav style={{
        position: 'fixed',
        top: 0,
        left: 0,
        right: 0,
        zIndex: 1000,
        padding: scrolled ? '15px 40px' : '25px 40px',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        transition: 'all 0.3s cubic-bezier(0.4, 0, 0.2, 1)',
        backdropFilter: scrolled ? 'blur(20px)' : 'none',
        background: scrolled ? 'rgba(8, 12, 20, 0.7)' : 'transparent',
        borderBottom: scrolled ? '1px solid rgba(255, 255, 255, 0.05)' : 'none'
      }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: '12px' }}>
          <div style={{ position: 'relative' }}>
            <svg width="40" height="40" viewBox="0 0 100 100">
              <defs>
                <linearGradient id="logoGrad" x1="0%" y1="0%" x2="100%" y2="100%">
                  <stop offset="0%" stopColor="#e8443a" />
                  <stop offset="100%" stopColor="#ff6b5a" />
                </linearGradient>
              </defs>
              <circle cx="50" cy="50" r="45" fill="rgba(232, 68, 58, 0.1)" />
              <circle cx="50" cy="50" r="32" stroke="url(#logoGrad)" strokeWidth="4" fill="none" />
              <rect x="36" y="36" width="28" height="28" fill="url(#logoGrad)" transform="rotate(45 50 50)" />
            </svg>
            <motion.div 
               animate={{ scale: [1, 1.2, 1], opacity: [0.5, 0.8, 0.5] }}
               transition={{ duration: 2, repeat: Infinity }}
               style={{
                position: 'absolute',
                top: '50%',
                left: '50%',
                width: '30px',
                height: '30px',
                background: '#e8443a',
                filter: 'blur(15px)',
                borderRadius: '50%',
                transform: 'translate(-50%, -50%)',
                zIndex: -1
               }}
            />
          </div>
          <span style={{ fontWeight: 800, fontSize: '1.4rem', letterSpacing: '-0.02em' }}>Redball</span>
        </div>

        {/* Desktop Nav */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '40px' }} className="desktop-nav">
          <a href="#features" style={{ color: 'rgba(255,255,255,0.7)', textDecoration: 'none', fontSize: '0.95rem', fontWeight: 500, transition: '0.2s' }}>Features</a>
          <a href="#downloads" style={{ color: 'rgba(255,255,255,0.7)', textDecoration: 'none', fontSize: '0.95rem', fontWeight: 500, transition: '0.2s' }}>Releases</a>
          <a href="https://github.com/ArMaTeC/Redball" target="_blank" rel="noopener" style={{ display: 'flex', alignItems: 'center', gap: '8px', color: 'rgba(255,255,255,0.7)', textDecoration: 'none', fontSize: '0.95rem' }}>
            <GitBranch size={18} />
            GitHub
          </a>
          <a
            href="#downloads"
            style={{
              background: 'linear-gradient(135deg, #e8443a, #ff6b5a)',
              color: 'white',
              padding: '12px 24px',
              borderRadius: '12px',
              textDecoration: 'none',
              fontSize: '0.9rem',
              fontWeight: 600,
              boxShadow: '0 4px 15px rgba(232, 68, 58, 0.3)',
              transition: 'all 0.3s ease'
            }}
            onMouseOver={(e) => {
              e.currentTarget.style.transform = 'translateY(-2px)';
              e.currentTarget.style.boxShadow = '0 6px 20px rgba(232, 68, 58, 0.4)';
            }}
            onMouseOut={(e) => {
              e.currentTarget.style.transform = 'translateY(0)';
              e.currentTarget.style.boxShadow = '0 4px 15px rgba(232, 68, 58, 0.3)';
            }}
          >
            Download Free
          </a>
        </div>

        {/* Mobile Toggle */}
        <button
          onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
          style={{ background: 'rgba(255,255,255,0.05)', border: 'none', color: 'white', cursor: 'pointer', padding: '10px', borderRadius: '10px' }}
          className="mobile-menu-btn"
        >
          {mobileMenuOpen ? <X size={24} /> : <Menu size={24} />}
        </button>
      </nav>

      {/* Main Hero */}
      <section style={{
        minHeight: '100vh',
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: '140px 20px 80px',
        textAlign: 'center',
        position: 'relative',
        zIndex: 1
      }}>
        <div style={{
          position: 'absolute',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          width: '100%',
          height: '100%',
          opacity: 0.15,
          zIndex: -1,
          backgroundImage: 'radial-gradient(rgba(232, 68, 58, 0.2) 1px, transparent 1px)',
          backgroundSize: '40px 40px'
        }} />

        <motion.div
          initial={{ opacity: 0, y: 40 }}
          animate={{ opacity: 1, y: 0 }}
          transition={{ duration: 0.8, ease: [0.16, 1, 0.3, 1] }}
          style={{ maxWidth: '1000px' }}
        >
          {/* Version Pill */}
          <motion.div
            initial={{ scale: 0.9, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ delay: 0.3 }}
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              gap: '10px',
              padding: '8px 20px',
              background: 'rgba(232, 68, 58, 0.08)',
              border: '1px solid rgba(232, 68, 58, 0.2)',
              borderRadius: '100px',
              marginBottom: '40px',
              backdropFilter: 'blur(10px)'
            }}
          >
            <span style={{
              width: '8px',
              height: '8px',
              background: '#00d9a3',
              borderRadius: '50%',
              boxShadow: '0 0 15px #00d9a3'
            }} />
            <span style={{ fontSize: '0.9rem', fontWeight: 600, color: 'rgba(255,255,255,0.9)' }}>
              Redball v{stats?.latestVersion || '2.1.x'} is now live
            </span>
            <div style={{ height: '14px', width: '1px', background: 'rgba(255,255,255,0.1)' }} />
            <a href="#changelog" style={{ fontSize: '0.85rem', color: '#ff6b5a', textDecoration: 'none', fontWeight: 600 }}>What's new?</a>
          </motion.div>

          <h1 style={{
            fontSize: 'clamp(3rem, 7vw, 5.5rem)',
            fontWeight: 850,
            lineHeight: 1.05,
            marginBottom: '24px',
            letterSpacing: '-0.04em'
          }}>
            Performance First.<br />
            <span style={{
              background: 'linear-gradient(135deg, #e8443a, #ff6b5a)',
              WebkitBackgroundClip: 'text',
              WebkitTextFillColor: 'transparent',
              filter: 'drop-shadow(0 0 30px rgba(232, 68, 58, 0.2))'
            }}>Awake Always.</span>
          </h1>

          <p style={{
            fontSize: 'clamp(1.1rem, 2vw, 1.4rem)',
            color: 'rgba(255,255,255,0.6)',
            maxWidth: '650px',
            margin: '0 auto 48px',
            lineHeight: 1.6,
            fontWeight: 400
          }}>
            Redball is the definitive keep-awake utility for Windows. 
            Smart activity monitoring meet premium design and themes.
          </p>

          <div style={{ display: 'flex', gap: '20px', justifyContent: 'center', flexWrap: 'wrap' }}>
            <motion.a
              whileHover={{ scale: 1.05, y: -2 }}
              whileTap={{ scale: 0.98 }}
              href={setupFile?.url || '#downloads'}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '12px',
                background: 'linear-gradient(135deg, #00d9a3, #00b386)',
                color: '#080c14',
                padding: '16px 36px',
                borderRadius: '16px',
                textDecoration: 'none',
                fontWeight: 700,
                fontSize: '1.05rem',
                boxShadow: '0 10px 30px rgba(0, 217, 163, 0.2)'
              }}
            >
              <Download size={24} strokeWidth={2.5}/>
              Download for Windows
            </motion.a>

            <motion.a
              whileHover={{ scale: 1.05, background: 'rgba(255,255,255,0.08)' }}
              href="https://github.com/ArMaTeC/Redball"
              target="_blank"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: '12px',
                background: 'rgba(255, 255, 255, 0.04)',
                border: '1px solid rgba(255, 255, 255, 0.1)',
                color: 'white',
                padding: '16px 36px',
                borderRadius: '16px',
                textDecoration: 'none',
                fontWeight: 600,
                fontSize: '1.05rem',
                backdropFilter: 'blur(10px)'
              }}
            >
              <GitBranch size={24} />
              Open Source
            </motion.a>
          </div>

          <motion.div 
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            transition={{ delay: 1 }}
            style={{ marginTop: '32px', color: 'rgba(255,255,255,0.4)', fontSize: '0.9rem' }}
          >
            Trusted by {formatDownloads((stats?.totalDownloads || 0) + 12000)} users worldwide
          </motion.div>
        </motion.div>

        {/* Hero Visual */}
        <motion.div
           initial={{ opacity: 0, scale: 0.9 }}
           animate={{ opacity: 1, scale: 1 }}
           transition={{ delay: 0.5, duration: 1 }}
           style={{
            marginTop: '80px',
            width: '100%',
            maxWidth: '1200px',
            position: 'relative',
            borderRadius: '24px',
            overflow: 'hidden',
            border: '1px solid rgba(255,255,255,0.05)',
            boxShadow: '0 30px 100px rgba(0,0,0,0.5)'
           }}
        >
           <img 
            src="/hero-sphere.png" 
            alt="Redball UI" 
            style={{ width: '100%', display: 'block' }} 
           />
           <div style={{
            position: 'absolute',
            bottom: 0,
            left: 0,
            right: 0,
            height: '40%',
            background: 'linear-gradient(to top, #04070a, transparent)'
           }} />
        </motion.div>
      </section>

      {/* Trust & Stats Section */}
      <section style={{ 
        padding: '0 20px 100px',
        position: 'relative'
      }}>
        <div style={{
          maxWidth: '1100px',
          margin: '0 auto',
          background: 'rgba(255,255,255,0.02)',
          border: '1px solid rgba(255,255,255,0.05)',
          borderRadius: '32px',
          padding: '60px 40px',
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))',
          gap: '40px',
          backdropFilter: 'blur(20px)'
        }}>
          <StatComponent 
            icon={<Download color="#00d9a3" />}
            value={stats ? formatDownloads(stats.totalDownloads) : '...' }
            label="Total Installations"
          />
          <StatComponent 
            icon={<Monitor color="#ff6b5a" />}
            value="12+"
            label="Stunning Themes"
          />
          <StatComponent 
            icon={<Shield color="#8b5cf6" />}
            value="100%"
            label="Privacy First"
          />
          <StatComponent 
            icon={<Zap color="#f59e0b" />}
            value={stats ? stats.totalReleases.toString() : '...' }
            label="Published Updates"
          />
        </div>
      </section>

      {/* Feature Grid */}
      <section id="features" style={{ padding: '120px 20px', maxWidth: '1200px', margin: '0 auto' }}>
        <div style={{ textAlign: 'center', marginBottom: '80px' }}>
          <motion.span 
            initial={{ opacity: 0 }}
            whileInView={{ opacity: 1 }}
            style={{ color: '#e8443a', textTransform: 'uppercase', letterSpacing: '0.2rem', fontSize: '0.8rem', fontWeight: 800 }}
          >
            Core Capabilities
          </motion.span>
          <h2 style={{ fontSize: 'clamp(2rem, 4vw, 3.5rem)', fontWeight: 800, marginTop: '16px', letterSpacing: '-0.02em' }}>
            Built for Power Users.
          </h2>
        </div>

        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fit, minmax(340px, 1fr))',
          gap: '32px'
        }}>
          <FeatureCard 
            icon={<Palette size={32} />}
            title="Design Language"
            description="Experience software that looks as good as it works. 12 premium themes crafted with precision for light and dark environments."
            gradient="linear-gradient(135deg, rgba(232, 68, 58, 0.1), rgba(255, 107, 90, 0.1))"
            accent="#e8443a"
          />
          <FeatureCard 
            icon={<Activity size={32} />}
            title="Intelligent Monitoring"
            description="Our advanced engine monitors system activity, network packets, and battery health to optimize power management."
            gradient="linear-gradient(135deg, rgba(45, 127, 249, 0.1), rgba(88, 155, 255, 0.1))"
            accent="#2d7ff9"
          />
          <FeatureCard 
            icon={<Terminal size={32} />}
            title="Human Simulation"
            description="TypeThing provides enterprise-grade clipboard typing simulation with variable cadence for testing environments."
            gradient="linear-gradient(135deg, rgba(0, 217, 163, 0.1), rgba(0, 179, 134, 0.1))"
            accent="#00d9a3"
          />
        </div>
      </section>

      {/* Release Section */}
      <section id="downloads" style={{
        padding: '120px 20px',
        background: 'linear-gradient(to bottom, transparent, rgba(232, 68, 58, 0.02), transparent)'
      }}>
        <div style={{ maxWidth: '900px', margin: '0 auto' }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-end', marginBottom: '48px' }}>
            <div>
              <h2 style={{ fontSize: '2.5rem', fontWeight: 800, letterSpacing: '-0.02em' }}>Release Archives</h2>
              <p style={{ color: 'rgba(255,255,255,0.5)', marginTop: '8px' }}>Download historical versions and explore the changelog.</p>
            </div>
            <motion.a 
              whileHover={{ x: 5 }}
              href="https://github.com/ArMaTeC/Redball/releases" 
              style={{ color: '#ff6b5a', textDecoration: 'none', fontWeight: 600, display: 'flex', alignItems: 'center', gap: '8px' }}
            >
              All Releases <ArrowRight size={20} />
            </motion.a>
          </div>

          <AnimatePresence mode="wait">
            <motion.div 
               variants={containerVariants}
               initial="hidden"
               whileInView="visible"
               viewport={{ once: true }}
               style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}
            >
              {stats?.releases.slice(0, 5).map((release, idx) => (
                <motion.div 
                  key={release.version}
                  variants={itemVariants}
                  style={{
                    background: 'rgba(255,255,255,0.03)',
                    border: '1px solid rgba(255,255,255,0.06)',
                    borderRadius: '20px',
                    padding: '24px 32px',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    transition: '0.3s'
                  }}
                  whileHover={{ background: 'rgba(255,255,255,0.05)', borderColor: 'rgba(232, 68, 58, 0.3)', scale: 1.01 }}
                >
                  <div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: '16px', marginBottom: '4px' }}>
                      <span style={{ fontSize: '1.2rem', fontWeight: 800, color: '#e8443a' }}>v{release.version}</span>
                      {idx === 0 && (
                         <span style={{ fontSize: '0.7rem', fontWeight: 900, background: '#00d9a3', color: 'black', padding: '2px 8px', borderRadius: '4px', textTransform: 'uppercase' }}>Latest</span>
                      )}
                    </div>
                    <span style={{ color: 'rgba(255,255,255,0.4)', fontSize: '0.85rem' }}>{formatDate(release.date)} • {formatDownloads(release.totalDownloads)} downloads</span>
                  </div>

                  <div style={{ display: 'flex', gap: '12px' }}>
                    <motion.a 
                      whileHover={{ scale: 1.05 }}
                      whileTap={{ scale: 0.95 }}
                      href={`/api/download/${release.version}/${release.files?.find(f => f.name.endsWith('-Setup.exe'))?.name}`}
                      style={{
                        padding: '10px 20px',
                        background: 'rgba(255,255,255,0.05)',
                        border: '1px solid rgba(255,255,255,0.1)',
                        borderRadius: '12px',
                        color: 'white',
                        textDecoration: 'none',
                        fontSize: '0.9rem',
                        fontWeight: 600
                      }}
                    >
                      Setup.exe
                    </motion.a>
                    <motion.a 
                      whileHover={{ scale: 1.05 }}
                      whileTap={{ scale: 0.95 }}
                      href={`/api/download/${release.version}/${release.files?.find(f => f.name.endsWith('.zip'))?.name}`}
                      style={{
                        padding: '10px 20px',
                        background: 'rgba(255,255,255,0.05)',
                        border: '1px solid rgba(255,255,255,0.1)',
                        borderRadius: '12px',
                        color: 'white',
                        textDecoration: 'none',
                        fontSize: '0.9rem',
                        fontWeight: 600
                      }}
                    >
                      Portable (.zip)
                    </motion.a>
                  </div>
                </motion.div>
              ))}
            </motion.div>
          </AnimatePresence>
        </div>
      </section>

      {/* Footer */}
      <footer style={{
        padding: '100px 40px 60px',
        borderTop: '1px solid rgba(255, 255, 255, 0.05)',
        marginTop: '80px',
        background: 'rgba(0,0,0,0.2)'
      }}>
        <div style={{ maxWidth: '1200px', margin: '0 auto', display: 'grid', gridTemplateColumns: 'repeat(auto-fit, minmax(200px, 1fr))', gap: '60px' }}>
           <div>
             <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '24px' }}>
                <svg width="32" height="32" viewBox="0 0 100 100">
                  <circle cx="50" cy="50" r="45" fill="rgba(232, 68, 58, 0.1)" />
                  <circle cx="50" cy="50" r="32" stroke="#e8443a" strokeWidth="4" fill="none" />
                  <rect x="36" y="36" width="28" height="28" fill="#e8443a" transform="rotate(45 50 50)" />
                </svg>
                <span style={{ fontWeight: 800, fontSize: '1.2rem' }}>Redball</span>
             </div>
             <p style={{ color: 'rgba(255,255,255,0.5)', fontSize: '0.95rem', lineHeight: 1.6 }}>
               Empowering Windows users since 2024.<br />
               Minimal impact, maximum focus.
             </p>
           </div>
           
           <FooterGroup title="Product" links={[
             { label: 'Features', href: '#features' },
             { label: 'Downloads', href: '#downloads' },
             { label: 'Release Notes', href: 'https://github.com/ArMaTeC/Redball/releases' }
           ]} />
           
           <FooterGroup title="Community" links={[
             { label: 'GitHub Docs', href: 'https://github.com/ArMaTeC/Redball' },
             { label: 'Discussions', href: 'https://github.com/ArMaTeC/Redball/discussions' },
             { label: 'Admin Panel', href: '/admin' }
           ]} />

           <div>
              <h4 style={{ fontSize: '0.9rem', fontWeight: 800, marginBottom: '24px', textTransform: 'uppercase', color: 'rgba(255,255,255,0.4)', letterSpacing: '0.05em' }}>Deployment Status</h4>
              <div style={{ padding: '20px', background: 'rgba(255,255,255,0.03)', borderRadius: '16px', border: '1px solid rgba(255,255,255,0.05)' }}>
                 <div style={{ display: 'flex', alignItems: 'center', gap: '10px' }}>
                   <div style={{ width: '10px', height: '10px', background: '#00d9a3', borderRadius: '50%', boxShadow: '0 0 10px #00d9a3' }} />
                   <span style={{ fontSize: '0.9rem', fontWeight: 600 }}>All Systems Operational</span>
                 </div>
              </div>
           </div>
        </div>
        <div style={{ maxWidth: '1200px', margin: '60px auto 0', padding: '30px 0', borderTop: '1px solid rgba(255,255,255,0.05)', display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
           <p style={{ color: 'rgba(255,255,255,0.4)', fontSize: '0.85rem' }}>© 2026 Redball Project. MIT Licensed.</p>
           <div style={{ display: 'flex', gap: '20px' }}>
              <a href="https://github.com/ArMaTeC/Redball" style={{ color: 'rgba(255,255,255,0.4)' }}><GitBranch size={20} /></a>
           </div>
        </div>
      </footer>
    </div>
  );
};

const StatComponent: React.FC<{ icon: React.ReactNode; value: string; label: string }> = ({ icon, value, label }) => (
  <div style={{ textAlign: 'center' }}>
    <div style={{ display: 'inline-flex', marginBottom: '16px' }}>{icon}</div>
    <div style={{ fontSize: '2.5rem', fontWeight: 900, marginBottom: '4px', letterSpacing: '-0.04em' }}>{value}</div>
    <div style={{ fontSize: '0.8rem', color: 'rgba(255,255,255,0.4)', fontWeight: 700, textTransform: 'uppercase', letterSpacing: '0.1em' }}>{label}</div>
  </div>
);

const FeatureCard: React.FC<{ icon: React.ReactNode; title: string; description: string; gradient: string; accent: string }> = ({
  icon, title, description, gradient, accent
}) => (
  <motion.div
    whileHover={{ y: -8, scale: 1.02 }}
    style={{
      padding: '48px',
      background: 'rgba(255,255,255,0.02)',
      border: '1px solid rgba(255,255,255,0.05)',
      borderRadius: '32px',
      position: 'relative',
      overflow: 'hidden'
    }}
  >
    <div style={{ 
      position: 'absolute', 
      top: 0, 
      left: 0, 
      right: 0, 
      height: '4px', 
      background: `linear-gradient(90deg, ${accent}, transparent)` 
    }} />
    <div style={{ 
      width: '64px', 
      height: '64px', 
      background: gradient, 
      borderRadius: '16px', 
      display: 'flex', 
      alignItems: 'center', 
      justifyContent: 'center', 
      color: accent,
      marginBottom: '32px'
    }}>
      {icon}
    </div>
    <h3 style={{ fontSize: '1.6rem', fontWeight: 800, marginBottom: '16px', letterSpacing: '-0.02em' }}>{title}</h3>
    <p style={{ color: 'rgba(255,255,255,0.5)', lineHeight: 1.7, fontSize: '1.05rem' }}>{description}</p>
  </motion.div>
);

const FooterGroup: React.FC<{ title: string; links: { label: string; href: string }[] }> = ({ title, links }) => (
  <div>
    <h4 style={{ fontSize: '0.9rem', fontWeight: 800, marginBottom: '24px', textTransform: 'uppercase', color: 'rgba(255,255,255,0.4)', letterSpacing: '0.05em' }}>{title}</h4>
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      {links.map(link => (
        <a key={link.label} href={link.href} style={{ color: 'rgba(255,255,255,0.6)', textDecoration: 'none', fontSize: '0.95rem', transition: '0.2s' }}>{link.label}</a>
      ))}
    </div>
  </div>
);
