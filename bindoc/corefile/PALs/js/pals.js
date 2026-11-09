    document.addEventListener('DOMContentLoaded', () => {
        // --- 1. DARK MODE TOGGLE ---
        const toggle = document.getElementById('darkToggle');
        const root = document.getElementById('palRoot');
        const label = document.getElementById('modeLabel');

        const savedTheme = localStorage.getItem('theme');
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;

        if (savedTheme === 'dark' || (!savedTheme && prefersDark)) {
            root.classList.add('dark');
            toggle.checked = true;
            label.textContent = 'Dark';
        } else {
            root.classList.remove('dark');
            toggle.checked = false;
            label.textContent = 'Light';
        }

        toggle.addEventListener('change', () => {
            const isDark = toggle.checked;
            root.classList.toggle('dark', isDark);
            label.textContent = isDark ? 'Dark' : 'Light';
            localStorage.setItem('theme', isDark ? 'dark' : 'light');
        });

        // --- 2. SIDEBAR COLLAPSE ---
        const sidebarToggle = document.getElementById('sidebarToggle');
        const shell = document.querySelector('.shell');

        sidebarToggle.addEventListener('click', () => {
            shell.classList.toggle('collapsed');
        });

        // --- 3. DYNAMIC CONTENT LOADER ---
        const navItems = document.querySelectorAll('.nav-item');
        const contentContainer = document.querySelector('.content'); // Correctly targets <div class="content">

        async function loadPage(pageUrl) {
            try {
                const response = await fetch(pageUrl);
                if (!response.ok) throw new Error('Network response was not ok');
                
                const text = await response.text();
                contentContainer.innerHTML = text;
            } catch (error) {
                console.error('Failed to load page:', error);
                contentContainer.innerHTML = `<div style="padding: 20px; color: var(--text-secondary);">
                    <h3>Unable to load content</h3>
                    <p>Make sure you are running a local server environment.</p>
                </div>`;
            }
        }

        // Add event listeners to all sidebar options
        navItems.forEach(item => {
            item.addEventListener('click', () => {
                const targetPage = item.getAttribute('data-page');
                if (!targetPage) return; // Skip if no data-page is specified

                // Update visual active classes
                navItems.forEach(nav => nav.classList.remove('active'));
                item.classList.add('active');

                // Load the dynamic sub-page HTML
                loadPage(targetPage);
            });
        });

        // Load the initial home content automatically when dashboard opens
        loadPage('pages/home.html');
    });