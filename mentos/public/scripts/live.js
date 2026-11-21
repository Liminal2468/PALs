(() => {
    window.PalsLive = true;

    const api = async (path, options = {}) => {
        const response = await fetch(`/api/v1${path}`, {
            credentials: 'same-origin',
            cache: 'no-store',
            ...options,
            headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }
        });
        const body = await response.json().catch(() => ({}));
        if (!response.ok) throw new Error(body?.error?.message || `Request failed (${response.status}).`);
        return body;
    };

    const escapeHtml = value => String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' }[character]));
    const safeResourceUrl = value => {
        if (typeof value !== 'string' || !URL.canParse(value)) return null;
        const url = new URL(value);
        return ['http:', 'https:'].includes(url.protocol) && !url.username && !url.password ? url.href : null;
    };
    const status = (element, message, error = false) => {
        if (!element) return;
        element.textContent = message;
        element.classList.toggle('form-status-error', error);
    };

    const trackPageView = pageUrl => {
        const featureKey = pageUrl || 'home';
        void api('/analytics/events', {
            method: 'POST',
            body: JSON.stringify({ eventType: 'page_view', featureKey, durationSeconds: 0 })
        }).catch(() => {});
    };

    async function hydrateAccount() {
        try {
            const body = await api('/me');
            const account = body.account || body;
            const name = account.display_name || account.displayName || account.email;
            document.querySelectorAll('[data-profile-name], [data-profile-display-name]').forEach(node => node.textContent = name);
            document.querySelectorAll('[data-profile-email]').forEach(node => node.textContent = account.email || '');
            const headerName = document.querySelector('.header-user .user-name');
            if (headerName) headerName.textContent = name;
            const headerRole = document.querySelector('.header-user .user-role');
            if (headerRole) headerRole.textContent = account.role || 'Student';
            const form = document.querySelector('[data-profile-form]');
            if (form) {
                if (form.elements.displayName) form.elements.displayName.value = account.display_name || account.displayName || '';
                if (form.elements.email) form.elements.email.value = account.email || '';
                if (form.elements.grade) form.elements.grade.value = account.grade || 'Grade 12';
                if (form.elements.section) form.elements.section.value = account.section || '';
                if (form.elements.bio) form.elements.bio.value = account.bio || '';
                const roles = Array.isArray(account.roles) ? account.roles : [];
                const subjects = Array.isArray(account.subjects) ? account.subjects : [];
                form.querySelectorAll('[name="roles"]').forEach(input => { input.checked = roles.includes(input.value); });
                form.querySelectorAll('[name="subjects"]').forEach(input => { input.checked = subjects.includes(input.value); });
            }
            return account;
        } catch (error) {
            if (error.message.includes('Sign in')) window.location.assign('/');
            return null;
        }
    }

    function renderTutorCards(items) {
        const list = document.querySelector('[data-tutor-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => `<article class="tutor-card" data-tutor-card data-name="${escapeHtml(item.display_name)}" data-subjects="${escapeHtml((item.subjects || '').replace(/,\s*/g, ',').toLowerCase())}" data-availability="all"><div class="t-av blue">${escapeHtml((item.display_name || '?').slice(0, 2).toUpperCase())}</div><div class="home-tutor-main"><div class="t-name">${escapeHtml(item.display_name)}</div><div class="t-meta">${escapeHtml(item.strand || 'PALs tutor')}</div><div class="t-tags"><span class="tag blue">${escapeHtml(item.subjects || 'Subjects not listed')}</span></div></div><div class="home-tutor-actions"><button class="req-btn" type="button" data-live-request="${escapeHtml(item.id)}">Request session</button></div></article>`).join('') : '<p class="empty-state">No active tutors are available yet.</p>';
        bindDirectoryFilter('tutor');
    }

    function renderTutees(items) {
        const list = document.querySelector('[data-tutee-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => `<article class="tutor-card" data-tutee-card data-name="${escapeHtml(item.display_name)}" data-subjects="${escapeHtml((item.subject || '').toLowerCase())}" data-availability="all"><div class="t-av teal">${escapeHtml((item.display_name || '?').slice(0, 2).toUpperCase())}</div><div class="home-tutor-main"><div class="t-name">${escapeHtml(item.display_name)}</div><div class="t-meta">${escapeHtml(item.subject || 'Subject not listed')}</div><p>${escapeHtml(item.requested_help || 'Looking for peer learning support.')}</p></div></article>`).join('') : '<p class="empty-state">No open learning requests are available yet.</p>';
        bindDirectoryFilter('tutee');
    }

    function bindDirectoryFilter(kind) {
        const isTutor = kind === 'tutor';
        const list = document.querySelector(isTutor ? '[data-tutor-list]' : '[data-tutee-list]');
        const search = document.querySelector(isTutor ? '[data-tutor-search]' : '[data-tutee-search]');
        const subject = document.querySelector(isTutor ? '[data-tutor-subject]' : '[data-tutee-subject]');
        const availability = document.querySelector(isTutor ? '[data-tutor-availability]' : '[data-tutee-availability]');
        const sort = document.querySelector(isTutor ? '[data-tutor-sort]' : '[data-tutee-sort]');
        const result = document.querySelector(isTutor ? '[data-tutor-results]' : '[data-tutee-results]');
        const empty = document.querySelector(isTutor ? '[data-tutor-empty]' : '[data-tutee-empty]');
        if (!list || !search || !subject || !availability || !sort || !result || !empty || list.dataset.liveFilterBound) return;
        list.dataset.liveFilterBound = 'true';
        const filter = () => {
            const cards = [...list.querySelectorAll(isTutor ? '[data-tutor-card]' : '[data-tutee-card]')];
            const query = search.value.trim().toLocaleLowerCase();
            const selectedSubject = subject.value;
            const selectedAvailability = availability.value;
            const visible = cards.filter(card => {
                const matchesQuery = !query || card.textContent.toLocaleLowerCase().includes(query);
                const matchesSubject = selectedSubject === 'all' || (card.dataset.subjects || '').split(',').includes(selectedSubject);
                const matchesAvailability = selectedAvailability === 'all' || !card.dataset.availability || card.dataset.availability === selectedAvailability;
                card.hidden = !(matchesQuery && matchesSubject && matchesAvailability);
                return !card.hidden;
            });
            visible.sort((first, second) => (first.dataset.name || '').localeCompare(second.dataset.name || ''));
            visible.forEach(card => list.append(card));
            result.textContent = `Showing ${visible.length} of ${cards.length} ${isTutor ? 'peer tutors' : 'students seeking help'}`;
            empty.hidden = visible.length > 0;
        };
        [search, subject, availability, sort].forEach(control => control.addEventListener(control === search ? 'input' : 'change', filter));
        filter();
    }

    function renderSessions(items) {
        const list = document.querySelector('[data-session-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => {
            const status = item.session_status === 'confirmed' ? 'upcoming' : item.session_status;
            const actions = status === 'upcoming'
                ? '<div class="session-card-actions"><button class="text-button" type="button" data-session-action="complete">Mark complete</button><button class="text-button danger-text" type="button" data-session-action="cancel">Cancel</button></div>'
                : status === 'requested'
                    ? '<div class="session-card-actions"><button class="text-button danger-text" type="button" data-session-action="cancel">Cancel</button></div>'
                    : '';
            return `<article class="session-card" data-session-card data-live-session data-session-id="${escapeHtml(item.id)}" data-status="${escapeHtml(status)}"><div class="session-date-block"><span>${escapeHtml(new Date(item.scheduled_start_utc).toLocaleDateString())}</span></div><div class="session-card-main"><div class="session-card-top"><span class="tag blue">${escapeHtml(item.subject)}</span><span class="session-status" data-session-status-label>${escapeHtml(item.session_status)}</span></div><h3>${escapeHtml(item.subject)}</h3><p>${escapeHtml(new Date(item.scheduled_start_utc).toLocaleString())}</p></div>${actions}</article>`;
        }).join('') : '<p class="empty-state">You have no sessions yet.</p>';
    }

    function renderHomeSessions(items) {
        const section = document.querySelector('.upcoming');
        if (!section) return;
        const rows = items.filter(item => !['completed', 'cancelled', 'no_show'].includes(item.session_status)).slice(0, 3);
        section.querySelectorAll('.session-item').forEach(item => item.remove());
        const empty = document.createElement('p');
        empty.className = 'empty-state';
        empty.textContent = rows.length ? '' : 'No upcoming sessions are scheduled.';
        if (rows.length) {
            rows.forEach(item => {
                const row = document.createElement('div');
                row.className = 'session-item';
                row.innerHTML = `<span class="sess-dot" aria-hidden="true"></span><span class="sess-info">${escapeHtml(item.subject)}</span><time class="sess-time">${escapeHtml(new Date(item.scheduled_start_utc).toLocaleString())}</time>`;
                section.append(row);
            });
        } else {
            section.append(empty);
        }
    }

    function renderFeedback(items) {
        const list = document.querySelector('[data-feedback-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => `<article class="feedback-card" data-feedback-id="${escapeHtml(item.id)}" data-category="${escapeHtml(item.category)}"><button class="feedback-vote" type="button" data-feedback-vote data-live-vote="${escapeHtml(item.id)}" aria-label="Vote for ${escapeHtml(item.title)}"><span aria-hidden="true">▲</span><strong>${escapeHtml(item.votes || 0)}</strong><span>votes</span></button><div class="feedback-card-body"><div class="feedback-card-meta"><span class="tag blue">${escapeHtml(item.category)}</span></div><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(item.description)}</p><div class="feedback-card-footer">Posted by ${escapeHtml(item.author || 'PALs user')}</div></div></article>`).join('') : '<p class="empty-state">No feedback posts yet.</p>';
    }

    function renderResources(items) {
        const resourceList = items.length ? items.map(item => {
            const url = safeResourceUrl(item.resource_url);
            const link = url
                ? `<a href="${escapeHtml(url)}">${escapeHtml(item.title)}</a>`
                : `<span>${escapeHtml(item.title)} (link unavailable)</span>`;
            return `<article class="session-item"><span class="sess-dot"></span><span class="sess-info">${link}<small>${escapeHtml(item.description || '')}</small></span></article>`;
        }).join('') : '<p class="empty-state">No resources are available yet.</p>';
        document.querySelector('.content').innerHTML = `<div class="topbar"><div><div class="topbar-title">Learning resources</div><div class="topbar-sub">Resources shared by the PALs community</div></div></div><div class="sub-content"><section class="home-section panel"><div class="section-header"><div><h1>Resources</h1><p class="topbar-sub">Browse approved learning materials.</p></div></div><form class="app-form" data-resource-form><label>Title<input name="title" required maxlength="180"></label><label>Description<textarea name="description" maxlength="2000"></textarea></label><label>Resource URL<input name="resourceUrl" type="url" required maxlength="1000"></label><button class="btn-primary" type="submit">Submit resource</button><p class="form-status" data-resource-status role="status" aria-live="polite"></p></form><div data-resource-list>${resourceList}</div></section></div>`;
    }

    async function hydratePage(pageUrl) {
        try {
            if (pageUrl === 'home') {
                const dashboard = await api('/dashboard');
                const values = { sessions: dashboard.sessions_this_month, hours: dashboard.hours_logged, tutors: dashboard.tutor_connections };
                Object.entries(values).forEach(([key, value]) => { const node = document.querySelector(`[data-home-stat="${key}"]`); if (node) node.textContent = value ?? 0; });
                const tutorGrid = document.querySelector('.home-tutor-grid');
                if (tutorGrid) {
                    tutorGrid.querySelectorAll('[data-home-tutor]').forEach(card => card.remove());
                    const empty = tutorGrid.querySelector('[data-home-empty]');
                    if (empty) {
                        empty.hidden = false;
                        empty.textContent = 'Tutor recommendations will appear after the /connect matching service is implemented.';
                    }
                }
                renderHomeSessions((await api('/sessions')).items || []);
            }
            if (pageUrl === 'tutors') renderTutorCards((await api('/tutors')).items || []);
            if (pageUrl === 'tutees') renderTutees((await api('/tutees')).items || []);
            if (pageUrl === 'sessions') renderSessions((await api('/sessions')).items || []);
            if (pageUrl === 'feedback') renderFeedback((await api('/feedback')).items || []);
            if (pageUrl === 'resources') {
                renderResources((await api('/resources')).items || []);
            }
            if (pageUrl === 'analytics') {
                const items = (await api('/analytics/summary')).items || [];
                document.querySelector('.content').innerHTML = `<div class="topbar"><div><div class="topbar-title">Learning analytics</div><div class="topbar-sub">Your consent-aware PALs activity summary</div></div></div><div class="sub-content"><section class="stats-row"><article class="stat"><div class="stat-label">Tracked features</div><div class="stat-val">${items.length}</div></article><article class="stat"><div class="stat-label">Recorded events</div><div class="stat-val">${items.reduce((total, item) => total + Number(item.events || 0), 0)}</div></article></section></div>`;
            }
            if (pageUrl === 'settings') {
                document.querySelector('.content').innerHTML = `<div class="topbar"><div><div class="topbar-title">Settings</div><div class="topbar-sub">Manage privacy and learning-data preferences</div></div></div><div class="sub-content"><section class="panel home-section"><h1>Privacy and research settings</h1><p>Optional analytics helps evaluate PALs. It is not required for ordinary platform access.</p><button class="btn-primary" type="button" data-analytics-consent>Allow learning analytics</button><p class="form-status" data-settings-status role="status" aria-live="polite"></p></section></div>`;
                document.querySelector('[data-analytics-consent]')?.addEventListener('click', async event => {
                    try { await api('/analytics/consent', { method: 'POST', body: JSON.stringify({ consentVersion: 'phase2-v1' }) }); event.target.textContent = 'Analytics enabled'; status(document.querySelector('[data-settings-status]'), 'Your consent was saved.'); }
                    catch (error) { status(document.querySelector('[data-settings-status]'), error.message, true); }
                });
            }
            trackPageView(pageUrl);
        } catch (error) {
            const target = document.querySelector('.content');
            if (target) status(target.querySelector('.form-status'), error.message, true);
            if (pageUrl === 'sessions') {
                const list = document.querySelector('[data-session-list]');
                if (list) list.innerHTML = '<p class="empty-state">Live sessions are unavailable right now.</p>';
            }
            if (pageUrl === 'tutors') {
                const list = document.querySelector('[data-tutor-list]');
                if (list) list.innerHTML = '<p class="empty-state">Live tutors are unavailable right now.</p>';
            }
            if (pageUrl === 'tutees') {
                const list = document.querySelector('[data-tutee-list]');
                if (list) list.innerHTML = '<p class="empty-state">Live tutees are unavailable right now.</p>';
            }
            console.error('Live page hydration failed:', error);
        }
    }

    document.addEventListener('submit', async event => {
        const form = event.target;
        if (form.matches('[data-profile-form]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            try {
                await api('/me', { method: 'PATCH', body: JSON.stringify({
                    displayName: data.get('displayName'),
                    grade: data.get('grade'),
                    section: data.get('section'),
                    bio: data.get('bio'),
                    roles: [...form.querySelectorAll('[name="roles"]:checked')].map(input => input.value),
                    subjects: [...form.querySelectorAll('[name="subjects"]:checked')].map(input => input.value)
                }) });
                status(form.querySelector('[data-profile-status]'), 'Profile saved.');
                await hydrateAccount();
            } catch (error) { status(form.querySelector('[data-profile-status]'), error.message, true); }
        }
        if (form.matches('#supportForm')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            try {
                await api('/support', { method: 'POST', body: JSON.stringify({ category: data.get('topic'), subject: 'PALs support request', message: data.get('message') }) });
                status(form.querySelector('#formStatus'), 'Support request submitted.');
                form.reset();
            } catch (error) { status(form.querySelector('#formStatus'), error.message, true); }
        }
        if (form.matches('[data-resource-form]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            try {
                await api('/resources', { method: 'POST', body: JSON.stringify({ title: data.get('title'), description: data.get('description'), resourceUrl: data.get('resourceUrl'), resourceType: 'link', visibility: 'community' }) });
                status(form.querySelector('[data-resource-status]'), 'Resource submitted for review.');
                form.reset();
            } catch (error) { status(form.querySelector('[data-resource-status]'), error.message, true); }
        }
        if (form.matches('[data-request-form]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            try {
                await api('/learning-requests', { method: 'POST', body: JSON.stringify({ subject: data.get('subject'), requestedHelp: data.get('note'), preferredFormat: 'either' }) });
                status(form.querySelector('[data-request-status]'), 'Learning request submitted.');
                form.reset();
            } catch (error) { status(form.querySelector('[data-request-status]'), error.message, true); }
        }
        if (form.matches('[data-feedback-form]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            try {
                await api('/feedback', { method: 'POST', body: JSON.stringify({ title: data.get('title'), category: data.get('category'), description: data.get('description') }) });
                status(form.querySelector('[data-feedback-status]'), 'Feedback posted.');
                form.reset();
                window.dispatchEvent(new CustomEvent('pals:page-loaded', { detail: { pageUrl: 'feedback' } }));
            } catch (error) { status(form.querySelector('[data-feedback-status]'), error.message, true); }
        }
    }, true);

    document.addEventListener('click', async event => {
        const sessionAction = event.target.closest('[data-live-session] [data-session-action]');
        if (sessionAction) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const card = sessionAction.closest('[data-live-session]');
            const id = card?.dataset.sessionId;
            const nextStatus = sessionAction.dataset.sessionAction === 'complete' ? 'completed' : 'cancelled';
            if (!id) return;
            try {
                await api(`/sessions/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify({ status: nextStatus }) });
                card.dataset.status = nextStatus;
                card.querySelector('[data-session-status-label]').textContent = nextStatus;
                sessionAction.closest('.session-card-actions')?.remove();
            } catch (error) {
                status(document.querySelector('[data-session-status-message]'), error.message, true);
            }
            return;
        }
        const vote = event.target.closest('[data-live-vote]');
        if (!vote) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        try { await api(`/feedback/${vote.dataset.liveVote}/votes`, { method: 'POST', body: '{}' }); vote.disabled = true; } catch (error) { console.error(error); }
    }, true);

    window.addEventListener('pals:page-loaded', event => hydratePage(event.detail.pageUrl));
    window.addEventListener('DOMContentLoaded', hydrateAccount);
})();
