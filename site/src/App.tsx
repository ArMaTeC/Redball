import React, { useState } from 'react'
import { LandingPage } from './LandingPage'
import { AdminDashboard } from './AdminDashboard'
import './index.css'

function App() {
  const [route, setRoute] = useState(window.location.hash || '#/')

  // Simple hash-based router
  React.useEffect(() => {
    const handleHashChange = () => setRoute(window.location.hash || '#/')
    window.addEventListener('hashchange', handleHashChange)
    return () => window.removeEventListener('hashchange', handleHashChange)
  }, [])

  return (
    <div className="app-root">
      <div className="glow-mesh"></div>
      
      {route === '#/admin' ? (
        <AdminDashboard />
      ) : (
        <LandingPage />
      )}

      {/* Floating Admin Toggle for Demo */}
      <div style={{ position: 'fixed', bottom: '20px', right: '20px', zIndex: 1000 }}>
        <a 
          href={route === '#/admin' ? '#/' : '#/admin'} 
          style={{ 
            background: 'rgba(0,0,0,0.5)', 
            backdropFilter: 'blur(10px)',
            padding: '10px 20px',
            borderRadius: '10px',
            border: '1px solid var(--border-glass)',
            color: 'white',
            textDecoration: 'none',
            fontSize: '0.75rem',
            fontWeight: '600',
            display: 'flex',
            alignItems: 'center',
            gap: '8px'
          }}
        >
          {route === '#/admin' ? 'View Public Site' : 'Admin Console'}
        </a>
      </div>
    </div>
  )
}

export default App
