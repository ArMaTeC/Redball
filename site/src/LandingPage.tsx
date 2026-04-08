import React from 'react';
import { Shield, Rocket, Download, Terminal, ChevronRight, Activity } from 'lucide-react';
import { motion } from 'framer-motion';

export const LandingPage: React.FC = () => {
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
          <div style={{ display: 'flex', gap: '16px', justifyContent: 'center' }}>
            <button className="btn-primary">
              <Download size={20} />
              Download for Windows
            </button>
            <button className="btn-secondary">
              See How It Works
              <ChevronRight size={20} />
            </button>
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
