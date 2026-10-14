// ============================
// PARTICLE SYSTEM
// ============================
(function () {
    const canvas = document.getElementById('particle-canvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    let particles = [];
    let mouseX = 0, mouseY = 0;
    let animId;

    function resize() {
        canvas.width = canvas.offsetWidth;
        canvas.height = canvas.offsetHeight;
    }

    class Particle {
        constructor() {
            this.reset();
        }
        reset() {
            this.x = Math.random() * canvas.width;
            this.y = Math.random() * canvas.height;
            this.size = Math.random() * 2 + 0.5;
            this.speedX = (Math.random() - 0.5) * 0.4;
            this.speedY = (Math.random() - 0.5) * 0.4;
            this.opacity = Math.random() * 0.5 + 0.1;
        }
        update() {
            this.x += this.speedX;
            this.y += this.speedY;
            if (this.x < 0 || this.x > canvas.width) this.speedX *= -1;
            if (this.y < 0 || this.y > canvas.height) this.speedY *= -1;
        }
        draw() {
            ctx.beginPath();
            ctx.arc(this.x, this.y, this.size, 0, Math.PI * 2);
            ctx.fillStyle = `rgba(232, 68, 58, ${this.opacity})`;
            ctx.fill();
        }
    }

    function init() {
        resize();
        const count = Math.min(80, Math.floor((canvas.width * canvas.height) / 15000));
        particles = [];
        for (let i = 0; i < count; i++) {
            particles.push(new Particle());
        }
    }

    function drawConnections() {
        for (let i = 0; i < particles.length; i++) {
            for (let j = i + 1; j < particles.length; j++) {
                const dx = particles[i].x - particles[j].x;
                const dy = particles[i].y - particles[j].y;
                const dist = Math.sqrt(dx * dx + dy * dy);
                if (dist < 150) {
                    ctx.beginPath();
                    ctx.moveTo(particles[i].x, particles[i].y);
                    ctx.lineTo(particles[j].x, particles[j].y);
                    ctx.strokeStyle = `rgba(232, 68, 58, ${0.06 * (1 - dist / 150)})`;
                    ctx.lineWidth = 0.5;
                    ctx.stroke();
                }
            }
            // Mouse connection
            const mdx = particles[i].x - mouseX;
            const mdy = particles[i].y - mouseY;
            const mDist = Math.sqrt(mdx * mdx + mdy * mdy);
            if (mDist < 200 && mouseX > 0) {
                ctx.beginPath();
                ctx.moveTo(particles[i].x, particles[i].y);
                ctx.lineTo(mouseX, mouseY);
                ctx.strokeStyle = `rgba(45, 127, 249, ${0.15 * (1 - mDist / 200)})`;
                ctx.lineWidth = 0.8;
                ctx.stroke();
            }
        }
    }

    function animate() {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        particles.forEach(p => { p.update(); p.draw(); });
        drawConnections();
        animId = requestAnimationFrame(animate);
    }

    window.addEventListener('resize', () => { init(); });
    canvas.addEventListener('mousemove', (e) => {
        const rect = canvas.getBoundingClientRect();
        mouseX = e.clientX - rect.left;
        mouseY = e.clientY - rect.top;
    });

    init();
    animate();
})();

// ============================
// CURSOR GLOW
// ============================
(function () {
    const glow = document.getElementById('cursor-glow');
    let showing = false;
    document.addEventListener('mousemove', (e) => {
        if (!showing) { glow.classList.add('active'); showing = true; }
        glow.style.left = e.clientX + 'px';
        glow.style.top = e.clientY + 'px';
    });
    document.addEventListener('mouseleave', () => {
        glow.classList.remove('active');
        showing = false;
    });
})();

// ============================
// HEADER SCROLL
// ============================
(function () {
    const header = document.getElementById('header');
    let ticking = false;
    window.addEventListener('scroll', () => {
        if (!ticking) {
            requestAnimationFrame(() => {
                header.classList.toggle('scrolled', window.scrollY > 60);
                ticking = false;
            });
            ticking = true;
        }
    });
})();

// ============================
// MOBILE MENU
// ============================
(function () {
    const toggle = document.getElementById('menu-toggle');
    const nav = document.getElementById('main-nav');
    toggle.addEventListener('click', () => {
        nav.classList.toggle('open');
    });
    // Close on link click
    nav.querySelectorAll('.nav-link, .nav-cta').forEach(link => {
        link.addEventListener('click', () => nav.classList.remove('open'));
    });
})();

// ============================
// SCROLL REVEAL
// ============================
(function () {
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('visible');
            }
        });
    }, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });

    document.querySelectorAll('.reveal').forEach(el => observer.observe(el));
})();

// ============================
// ANIMATED COUNTERS
// ============================
function animateCounter(el, target, duration = 2000) {
    let start = 0;
    const startTime = performance.now();
    function update(now) {
        const elapsed = now - startTime;
        const progress = Math.min(elapsed / duration, 1);
        // Ease-out-expo
        const eased = progress === 1 ? 1 : 1 - Math.pow(2, -10 * progress);
        const current = Math.round(eased * target);
        el.textContent = current.toLocaleString();
        if (progress < 1) requestAnimationFrame(update);
    }
    requestAnimationFrame(update);
}

(function () {
    const counterObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting && !entry.target.dataset.animated) {
                entry.target.dataset.animated = 'true';
                const target = parseInt(entry.target.dataset.target, 10);
                if (!isNaN(target)) {
                    animateCounter(entry.target, target);
                }
            }
        });
    }, { threshold: 0.5 });

    document.querySelectorAll('.counter').forEach(el => counterObserver.observe(el));
})();

// ============================
// SOLUTION TABS
// ============================
(function () {
    const tabs = document.querySelectorAll('.solution-tab');
    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            tabs.forEach(t => t.classList.remove('active'));
            tab.classList.add('active');
            document.querySelectorAll('.solution-panel').forEach(p => p.classList.remove('active'));
            const panel = document.getElementById('panel-' + tab.dataset.panel);
            if (panel) panel.classList.add('active');
        });
    });
})();

// ============================
// COPY BUTTONS
// ============================
document.querySelectorAll('.copy-btn').forEach(btn => {
    btn.addEventListener('click', () => {
        const cmd = btn.dataset.cmd;
        navigator.clipboard.writeText(cmd).then(() => {
            btn.textContent = 'Copied!';
            btn.classList.add('copied');
            setTimeout(() => {
                btn.textContent = 'Copy';
                btn.classList.remove('copied');
            }, 2000);
        });
    });
});

// ============================
// DEMO FORM
// ============================
function handleDemoSubmit(event) {
    event.preventDefault();
    const btn = document.getElementById('form-submit-btn');
    btn.textContent = 'Sending...';
    btn.disabled = true;
    // Simulate submission
    setTimeout(() => {
        btn.textContent = '✓ Demo Requested!';
        btn.style.background = 'linear-gradient(135deg, #10b981, #34d399)';
        setTimeout(() => {
            btn.textContent = 'Request Demo';
            btn.style.background = '';
            btn.disabled = false;
            document.getElementById('demo-form').reset();
        }, 3000);
    }, 1200);
}

// ============================
// LIVE DATA SYNC FROM API
// ============================
async function syncData() {
    try {
        // Try local API first
        const stats = await fetch('/api/stats').then(r => r.ok ? r.json() : null).catch(() => null);
        const latest = await fetch('/api/releases/latest').then(r => r.ok ? r.json() : null).catch(() => null);

        if (stats) {
            // Update download counter target
            const dlCounter = document.querySelector('#stat-downloads .counter');
            if (dlCounter && !dlCounter.dataset.animated) {
                dlCounter.dataset.target = stats.totalDownloads || 0;
            } else if (dlCounter) {
                dlCounter.textContent = (stats.totalDownloads || 0).toLocaleString();
            }

            const relCounter = document.querySelector('#stat-releases-count .counter');
            if (relCounter && !relCounter.dataset.animated) {
                relCounter.dataset.target = stats.totalReleases || 0;
            } else if (relCounter) {
                relCounter.textContent = (stats.totalReleases || 0).toLocaleString();
            }

            // Update version history (last 6 versions)
            const versionList = document.getElementById('version-list');
            if (versionList && stats.releases && stats.releases.length > 0) {
                const recentReleases = stats.releases.slice(0, 6);
                versionList.innerHTML = recentReleases.map(release => {
                    const date = new Date(release.date).toLocaleDateString('en-GB', {
                        day: 'numeric',
                        month: 'short',
                        year: 'numeric'
                    });
                    const downloads = release.totalDownloads || 0;
                    const setupFile = release.files?.find(f => f.name.includes('Setup') || f.name.endsWith('.exe'));
                    const downloadUrl = setupFile ?
                        (setupFile.url || `/releases/${release.version}/${setupFile.name}`) :
                        `https://github.com/ArMaTeC/Redball/releases/download/v${release.version}/Redball-${release.version}-Setup.exe`;

                    return `
                        <a href="${downloadUrl}" class="version-item" style="background:var(--grad-card);border:1px solid var(--border-subtle);border-radius:12px;padding:16px 20px;display:flex;align-items:center;justify-content:space-between;text-decoration:none;transition:all 0.3s ease">
                            <div style="display:flex;align-items:center;gap:16px">
                                <span style="font-family:'JetBrains Mono',monospace;font-size:1.1rem;font-weight:600;color:var(--primary-light)">v${release.version}</span>
                                <span style="color:var(--text-muted);font-size:0.85rem">${date}</span>
                            </div>
                            <div style="display:flex;align-items:center;gap:20px">
                                <span style="color:var(--text-secondary);font-size:0.875rem;display:flex;align-items:center;gap:6px">
                                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width:14px;height:14px;opacity:0.6">
                                        <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
                                    </svg>
                                    ${downloads.toLocaleString()} downloads
                                </span>
                                <span style="color:var(--primary);font-size:0.875rem;font-weight:500;display:flex;align-items:center;gap:4px">
                                    Download
                                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="width:14px;height:14px">
                                        <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4M7 10l5 5 5-5M12 15V3" />
                                    </svg>
                                </span>
                            </div>
                        </a>
                    `;
                }).join('');
            }
        }

        if (latest) {
            const ver = latest.version;
            document.getElementById('hero-version').textContent = ver;
            document.getElementById('stat-latest-ver').textContent = 'v' + ver;
            document.getElementById('dl-ver-exe').textContent = 'v' + ver;
            document.getElementById('dl-ver-zip').textContent = 'v' + ver;

            // Update download URLs
            const exeFile = latest.files?.find(f => f.name.includes('Setup') || f.name.endsWith('.exe'));
            const zipFile = latest.files?.find(f => f.name.endsWith('.zip'));

            if (exeFile) {
                document.getElementById('dl-btn-exe').href = exeFile.url || `https://github.com/ArMaTeC/Redball/releases/download/v${ver}/${exeFile.name}`;
            }
            if (zipFile) {
                document.getElementById('dl-btn-zip').href = zipFile.url || `https://github.com/ArMaTeC/Redball/releases/download/v${ver}/${zipFile.name}`;
            }
        } else {
            // Fallback to GitHub API
            const ghRes = await fetch('https://api.github.com/repos/ArMaTeC/Redball/releases/latest');
            if (ghRes.ok) {
                const ghData = await ghRes.json();
                const ver = ghData.tag_name.replace('v', '');
                document.getElementById('hero-version').textContent = ver;
                document.getElementById('stat-latest-ver').textContent = 'v' + ver;
                document.getElementById('dl-ver-exe').textContent = 'v' + ver;
                document.getElementById('dl-ver-zip').textContent = 'v' + ver;

                const exe = ghData.assets.find(a => a.name.includes('Setup') || a.name.endsWith('.exe'));
                const zip = ghData.assets.find(a => a.name.endsWith('.zip'));
                if (exe) document.getElementById('dl-btn-exe').href = exe.browser_download_url;
                if (zip) document.getElementById('dl-btn-zip').href = zip.browser_download_url;
            }
        }
    } catch (err) {
        console.warn('[SYNC] Data sync warning:', err.message);
    }
}

syncData();
setInterval(syncData, 300000);

// ============================
// SMOOTH SCROLL FOR NAV
// ============================
document.querySelectorAll('a[href^="#"]').forEach(anchor => {
    anchor.addEventListener('click', function (e) {
        const target = document.querySelector(this.getAttribute('href'));
        if (target) {
            e.preventDefault();
            const offset = parseInt(getComputedStyle(document.documentElement).getPropertyValue('--header-h')) || 72;
            window.scrollTo({
                top: target.offsetTop - offset,
                behavior: 'smooth'
            });
        }
    });
});

// ============================
// BACK TO TOP BUTTON
// ============================
(function () {
    const btn = document.getElementById('back-to-top');
    let scrollTick = false;
    window.addEventListener('scroll', () => {
        if (!scrollTick) {
            requestAnimationFrame(() => {
                btn.classList.toggle('visible', window.scrollY > 600);
                scrollTick = false;
            });
            scrollTick = true;
        }
    });
    btn.addEventListener('click', () => {
        window.scrollTo({ top: 0, behavior: 'smooth' });
    });
})();

// ============================
// ACTIVE NAV HIGHLIGHTING
// ============================
(function () {
    const sections = document.querySelectorAll('section[id]');
    const navLinks = document.querySelectorAll('.nav-link[href^="#"]');
    const observer = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                const id = entry.target.getAttribute('id');
                navLinks.forEach(link => {
                    link.classList.toggle('active', link.getAttribute('href') === '#' + id);
                });
            }
        });
    }, { threshold: 0.2, rootMargin: '-80px 0px -50% 0px' });
    sections.forEach(s => observer.observe(s));
})();

// ============================
// PARALLAX HERO
// ============================
(function () {
    const heroBg = document.querySelector('.hero-bg');
    const heroContent = document.querySelector('.hero-content');
    let rafPending = false;
    window.addEventListener('scroll', () => {
        if (!rafPending) {
            requestAnimationFrame(() => {
                const scrollY = window.scrollY;
                const heroH = window.innerHeight;
                if (scrollY < heroH) {
                    const ratio = scrollY / heroH;
                    heroBg.style.transform = `translateY(${scrollY * 0.35}px)`;
                    heroContent.style.transform = `translateY(${scrollY * 0.15}px)`;
                    heroContent.style.opacity = 1 - ratio * 0.8;
                }
                rafPending = false;
            });
            rafPending = true;
        }
    });
})();
