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
    const initials = name => String(name || '').trim().split(/\s+/).slice(0, 2).map(part => part[0] || '').join('').toUpperCase();
    let currentAccount = null;
    let activeAnalyticsPage = null;
    let activeAnalyticsPageStartedAt = 0;

    async function trackPageView(pageUrl) {
        try {
            const consent = await api('/analytics/consent');
            if (!consent.consented) {
                activeAnalyticsPage = null;
                activeAnalyticsPageStartedAt = 0;
                return;
            }
            const now = Date.now();
            if (activeAnalyticsPage && activeAnalyticsPageStartedAt) {
                const durationSeconds = Math.min(86400, Math.floor((now - activeAnalyticsPageStartedAt) / 1000));
                if (durationSeconds > 0) {
                    await api('/analytics/events', {
                        method: 'POST',
                        body: JSON.stringify({
                            eventType: 'feature_view',
                            featureKey: activeAnalyticsPage,
                            durationSeconds
                        })
                    });
                }
            }
            await api('/analytics/events', {
                method: 'POST',
                body: JSON.stringify({ eventType: 'page_view', featureKey: pageUrl || 'home', durationSeconds: 0 })
            });
            activeAnalyticsPage = pageUrl || 'home';
            activeAnalyticsPageStartedAt = now;
        } catch (error) {
            console.error('Unable to record the PALs page view:', error);
        }
    };

    async function hydrateAccount() {
        try {
            const body = await api('/me');
            const account = body.account || body;
            currentAccount = account;
            const name = account.display_name || account.displayName || account.email || 'Profile unavailable';
            const gradeSection = [account.grade, account.section].filter(value => typeof value === 'string' && value.trim()).join(' · ');
            const role = typeof account.role === 'string' && account.role
                ? account.role.charAt(0).toUpperCase() + account.role.slice(1)
                : '';
            document.querySelectorAll('[data-profile-name], [data-profile-display-name]').forEach(node => node.textContent = name);
            document.querySelectorAll('[data-profile-email]').forEach(node => node.textContent = account.email || '');
            document.querySelectorAll('[data-profile-avatar], .header-user .avatar').forEach(node => node.textContent = initials(name));
            document.querySelectorAll('[data-profile-summary]').forEach(node => node.textContent = gradeSection || role);
            const headerName = document.querySelector('.header-user .user-name');
            if (headerName) headerName.textContent = name;
            const headerRole = document.querySelector('.header-user .user-role');
            if (headerRole) headerRole.textContent = gradeSection || role;
            document.querySelectorAll('[data-profile-role]').forEach(node => node.textContent = role ? `${role} profile` : 'PALs profile');
            const interests = document.querySelector('[data-profile-interest-list]');
            if (interests) {
                const subjects = Array.isArray(account.subjects) ? account.subjects : [];
                interests.replaceChildren(...(subjects.length
                    ? subjects.map(subject => {
                        const tag = document.createElement('span');
                        tag.className = 'tag blue';
                        tag.textContent = subject;
                        return tag;
                    })
                    : [Object.assign(document.createElement('span'), { textContent: 'No interests selected yet.' })]));
            }
            const form = document.querySelector('[data-profile-form]');
            if (form) {
                if (form.elements.displayName) form.elements.displayName.value = account.display_name || account.displayName || '';
                if (form.elements.email) form.elements.email.value = account.email || '';
                if (form.elements.grade) form.elements.grade.value = account.grade || '';
                if (form.elements.section) form.elements.section.value = account.section || '';
                if (form.elements.bio) form.elements.bio.value = account.bio || '';
                const roles = Array.isArray(account.roles) ? account.roles : [];
                const subjects = Array.isArray(account.subjects) ? account.subjects : [];
                form.querySelectorAll('[name="roles"]').forEach(input => { input.checked = roles.includes(input.value); });
                const subjectGroup = form.querySelector('.profile-subjects');
                if (subjectGroup) {
                    const knownSubjects = new Set([...subjectGroup.querySelectorAll('[name="subjects"]')].map(input => input.value));
                    subjects.filter(subject => !knownSubjects.has(subject)).forEach(subject => {
                        const label = document.createElement('label');
                        const input = document.createElement('input');
                        input.type = 'checkbox';
                        input.name = 'subjects';
                        input.value = subject;
                        input.checked = true;
                        label.append(input, document.createTextNode(` ${subject}`));
                        subjectGroup.append(label);
                    });
                }
                form.querySelectorAll('[name="subjects"]').forEach(input => { input.checked = subjects.includes(input.value); });
            }
            document.querySelectorAll('[data-open-learning-request]').forEach(button => {
                button.hidden = role.toLowerCase() === 'tutor';
            });
            document.querySelectorAll('.topbar [data-navigate="tutors"]').forEach(button => {
                if (role.toLowerCase() === 'tutor') {
                    button.dataset.navigate = 'tutees';
                    button.textContent = 'Find learners';
                }
            });
            return account;
        } catch (error) {
            if (error.message.includes('Sign in')) {
                window.location.assign('/');
                return null;
            }
            console.error('Unable to hydrate the PALs profile:', error);
            currentAccount = null;
            document.querySelectorAll('[data-profile-name], [data-profile-display-name], .header-user .user-name').forEach(node => node.textContent = 'Profile unavailable');
            document.querySelectorAll('[data-profile-role], .header-user .user-role, [data-profile-summary]').forEach(node => node.textContent = '');
            document.querySelectorAll('[data-profile-avatar], .header-user .avatar').forEach(node => node.textContent = '?');
            const interests = document.querySelector('[data-profile-interest-list]');
            if (interests) interests.textContent = 'Profile interests are unavailable.';
            return null;
        }
    }

    function subjectCategories(subject) {
        const value = String(subject || '').toLowerCase();
        if (/program|computer|mobile app|coding/.test(value)) return 'programming';
        if (/math|calculus|statistic|algebra/.test(value)) return 'math';
        if (/science|physics|chemistry|biology/.test(value)) return 'science';
        if (/research|communication|culture|humanit/.test(value)) return 'research';
        return 'all';
    }

    function recommendationCard(item, kind, home = false) {
        const name = item.candidateName || 'PALs learner';
        const role = item.candidateRole;
        const isTutorCandidate = role === 'tutor';
        const score = Math.round(Number(item.score) || 0);
        const category = subjectCategories(item.subject);
        const className = kind === 'tutor' ? 'data-tutor-card' : 'data-tutee-card';
        const homeAttribute = home ? 'data-home-tutor' : '';
        const callToAction = isTutorCandidate ? 'Request session' : 'Offer peer support';
        const profile = [item.grade ? `Grade ${item.grade}` : '', item.strand || ''].filter(Boolean).join(' · ');
        const details = role === 'tutee' && item.requestedHelp
            ? `<p>${escapeHtml(item.requestedHelp)}</p>`
            : '';
        return `<article class="tutor-card ${home ? 'home-tutor-card' : 'tutor-directory-card'}" ${className} ${homeAttribute} data-name="${escapeHtml(name)}" data-subjects="${escapeHtml(category)}" data-match="${score}">
            <div class="t-av ${isTutorCandidate ? 'blue' : 'teal'}">${escapeHtml(initials(name) || '?')}</div>
            <div class="${home ? 'home-tutor-main' : 'tutor-profile-main'}">
                <div class="t-name">${escapeHtml(name)}</div>
                <div class="t-meta">${escapeHtml(profile || (isTutorCandidate ? 'PALs tutor' : 'PALs learner'))}</div>
                ${details}
                <div class="t-tags"><span class="tag blue">${escapeHtml(item.subject || 'Subject not listed')}</span></div>
                <p class="topbar-sub" title="${escapeHtml(item.explanation || '')}">${escapeHtml(item.explanation || '')}</p>
            </div>
            <div class="${home ? 'home-tutor-actions' : 'tutor-card-actions'}">
                <span class="mbadge ${score >= 80 ? 'high' : 'med'}">${score}% match</span>
                <button class="req-btn" type="button" data-live-connect
                    data-request-id="${escapeHtml(item.requestId)}"
                    data-candidate-user-id="${escapeHtml(item.candidateUserId)}"
                    data-candidate-name="${escapeHtml(name)}"
                    data-candidate-role="${escapeHtml(role)}"
                    data-subject="${escapeHtml(item.subject)}">${callToAction}</button>
            </div>
        </article>`;
    }

    function renderHomeRecommendations(items) {
        const list = document.querySelector('.home-tutor-grid');
        if (!list) return;
        const isTutor = currentAccount?.role === 'tutor';
        const title = document.querySelector('#matchesTitle');
        if (title) title.textContent = isTutor ? 'Recommended learners' : 'Recommended peer tutors';
        const description = list.closest('.home-section')?.querySelector('.topbar-sub');
        if (description) description.textContent = isTutor
            ? 'Open learning requests matched to your tutoring subjects.'
            : 'Ranked using your open learning requests.';
        const browse = list.closest('.home-section')?.querySelector('[data-navigate="tutors"]');
        if (browse && isTutor) {
            browse.dataset.navigate = 'tutees';
            browse.textContent = 'Browse requests';
        }
        document.querySelectorAll('.topbar [data-navigate="tutors"]').forEach(button => {
            if (isTutor) {
                button.dataset.navigate = 'tutees';
                button.textContent = 'Find learners';
            }
        });
        list.innerHTML = items.length
            ? items.slice(0, 4).map(item => recommendationCard(item, 'tutor', true)).join('')
            : '<p class="empty-state" data-home-empty>No matching recommendations yet. Add an open learning request or update your tutor subjects.</p>';
    }

    function renderTutorCards(items) {
        const list = document.querySelector('[data-tutor-list]');
        if (!list) return;
        if (currentAccount?.role === 'tutor') {
            const title = document.querySelector('.topbar-title');
            const intro = document.querySelector('.tutor-intro h1');
            const description = document.querySelector('.tutor-intro .page-description');
            if (title) title.textContent = 'Find a student seeking help';
            if (intro) intro.textContent = 'Find a learner to support';
            if (description) description.textContent = 'These learning requests match your active tutor subjects and proficiency.';
        }
        const empty = document.querySelector('[data-tutor-empty]');
        const count = document.querySelector('[data-tutor-count]');
        if (count) count.textContent = String(items.length);
        document.querySelectorAll('[data-open-learning-request]').forEach(button => {
            button.hidden = currentAccount?.role === 'tutor';
        });
        list.innerHTML = items.map(item => recommendationCard(item, 'tutor')).join('');
        if (empty) {
            empty.textContent = items.length ? 'No tutors match those filters.' : 'No ranked tutors match your open learning requests yet. Post a request to find a tutor.';
            empty.hidden = items.length > 0;
        }
        bindDirectoryFilter('tutor');
    }

    function renderTutees(items) {
        const list = document.querySelector('[data-tutee-list]');
        if (!list) return;
        const empty = document.querySelector('[data-tutee-empty]');
        const count = document.querySelector('[data-tutee-count]');
        if (count) count.textContent = String(items.length);
        list.innerHTML = items.map(item => recommendationCard(item, 'tutee')).join('');
        if (empty) {
            empty.textContent = items.length ? 'No requests match those filters.' : 'No open learning requests currently match your tutor profile.';
            empty.hidden = items.length > 0;
        }
        bindDirectoryFilter('tutee');
    }

    function bindDirectoryFilter(kind) {
        const isTutor = kind === 'tutor';
        const list = document.querySelector(isTutor ? '[data-tutor-list]' : '[data-tutee-list]');
        const search = document.querySelector(isTutor ? '[data-tutor-search]' : '[data-tutee-search]');
        const subject = document.querySelector(isTutor ? '[data-tutor-subject]' : '[data-tutee-subject]');
        const sort = document.querySelector(isTutor ? '[data-tutor-sort]' : '[data-tutee-sort]');
        const result = document.querySelector(isTutor ? '[data-tutor-results]' : '[data-tutee-results]');
        const empty = document.querySelector(isTutor ? '[data-tutor-empty]' : '[data-tutee-empty]');
        if (!list || !search || !subject || !sort || !result || !empty || list.dataset.liveFilterBound) return;
        list.dataset.liveFilterBound = 'true';
        const filter = () => {
            const cards = [...list.querySelectorAll(isTutor ? '[data-tutor-card]' : '[data-tutee-card]')];
            const query = search.value.trim().toLocaleLowerCase();
            const selectedSubject = subject.value;
            const visible = cards.filter(card => {
                const matchesQuery = !query || card.textContent.toLocaleLowerCase().includes(query);
                const matchesSubject = selectedSubject === 'all' || (card.dataset.subjects || '').split(',').includes(selectedSubject);
                card.hidden = !(matchesQuery && matchesSubject);
                return !card.hidden;
            });
            if (sort.value === 'name') visible.sort((first, second) => (first.dataset.name || '').localeCompare(second.dataset.name || ''));
            else visible.sort((first, second) => Number(second.dataset.match) - Number(first.dataset.match));
            visible.forEach(card => list.append(card));
            result.textContent = `Showing ${visible.length} of ${cards.length} ${isTutor ? 'peer tutors' : 'students seeking help'}`;
            empty.hidden = visible.length > 0;
        };
        [search, subject, sort].forEach(control => control.addEventListener(control === search ? 'input' : 'change', filter));
        document.querySelector(isTutor ? '[data-tutor-reset]' : '[data-tutee-reset]')?.addEventListener('click', () => {
            search.value = '';
            subject.value = 'all';
            sort.value = 'match';
            filter();
            search.focus();
        });
        filter();
    }

    function renderSessions(items) {
        const list = document.querySelector('[data-session-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => {
            const rawStatus = item.session_status;
            const status = rawStatus === 'confirmed' ? 'upcoming' : rawStatus === 'no_show' ? 'cancelled' : rawStatus;
            const date = new Date(item.scheduled_start_utc);
            const peerPrefix = item.peer_role === 'tutor' ? 'Peer tutor' : 'Learner';
            const peer = item.peer_name ? `<p>${peerPrefix}: ${escapeHtml(item.peer_name)}</p>` : '';
            const actions = status === 'upcoming'
                ? '<div class="session-card-actions"><button class="text-button" type="button" data-session-action="complete">Mark complete</button><button class="text-button danger-text" type="button" data-session-action="cancel">Cancel</button></div>'
                : status === 'requested'
                    ? item.current_participation_status === 'invited'
                        ? '<div class="session-card-actions"><button class="text-button" type="button" data-session-action="confirm">Confirm session</button><button class="text-button danger-text" type="button" data-session-action="decline">Decline</button></div>'
                        : '<div class="session-card-actions"><button class="text-button danger-text" type="button" data-session-action="cancel">Withdraw request</button></div>'
                    : '';
            const statusLabel = rawStatus === 'requested' ? 'Awaiting response' : rawStatus === 'confirmed' ? 'Confirmed' : rawStatus === 'no_show' ? 'No-show' : rawStatus;
            const monthDay = Number.isNaN(date.getTime()) ? 'Date TBD' : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
            const time = Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
            return `<article class="session-card" data-session-card data-live-session data-session-id="${escapeHtml(item.id)}" data-status="${escapeHtml(status)}"><div class="session-date-block"><span>${escapeHtml(date.toLocaleDateString(undefined, { weekday: 'short' }))}</span><strong>${escapeHtml(time)}</strong><small>${escapeHtml(monthDay)}</small></div><div class="session-card-main"><div class="session-card-top"><span class="tag blue">${escapeHtml(item.subject)}</span><span class="session-status" data-session-status-label>${escapeHtml(statusLabel)}</span></div><h3>${escapeHtml(item.subject)}</h3>${peer}<p>${escapeHtml(date.toLocaleString())}</p></div>${actions}</article>`;
        }).join('') : '';
        list.insertAdjacentHTML('beforeend', '<p class="empty-state" data-session-empty hidden>No sessions in this view yet. Find a peer match to get started.</p>');
        document.querySelector('[data-session-filter]')?.dispatchEvent(new Event('change'));
    }

    function renderHomeSessions(items) {
        const section = document.querySelector('.upcoming');
        if (!section) return;
        const rows = items
            .filter(item => !['completed', 'cancelled', 'no_show'].includes(item.session_status))
            .sort((first, second) => new Date(first.scheduled_start_utc) - new Date(second.scheduled_start_utc))
            .slice(0, 3);
        section.querySelectorAll('.session-item, .empty-state').forEach(item => item.remove());
        const empty = document.createElement('p');
        empty.className = 'empty-state';
        empty.dataset.homeSessionEmpty = '';
        empty.hidden = rows.length > 0;
        empty.textContent = rows.length ? '' : 'No upcoming sessions are scheduled.';
        if (rows.length) {
            rows.forEach(item => {
                const row = document.createElement('div');
                row.className = 'session-item';
                const peer = item.peer_name ? ` with ${item.peer_name}` : '';
                row.innerHTML = `<span class="sess-dot" aria-hidden="true"></span><span class="sess-info">${escapeHtml(item.subject)}${escapeHtml(peer)}</span><time class="sess-time">${escapeHtml(new Date(item.scheduled_start_utc).toLocaleString())}</time>`;
                section.append(row);
            });
        }
        section.append(empty);
    }

    function renderFeedback(items) {
        const list = document.querySelector('[data-feedback-list]');
        if (!list) return;
        list.innerHTML = items.length ? items.map(item => {
            const voted = item.voted_by_current_user === true || item.voted_by_current_user === 1;
            return `<article class="feedback-card" data-feedback-id="${escapeHtml(item.id)}" data-category="${escapeHtml(item.category)}"><button class="feedback-vote" type="button" data-feedback-vote data-live-vote="${escapeHtml(item.id)}" aria-pressed="${voted}" ${voted ? 'disabled' : ''} aria-label="Vote for ${escapeHtml(item.title)}"><span aria-hidden="true">▲</span><strong data-vote-count>${escapeHtml(item.votes || 0)}</strong><span>votes</span></button><div class="feedback-card-body"><div class="feedback-card-meta"><span class="tag blue">${escapeHtml(item.category)}</span></div><h3>${escapeHtml(item.title)}</h3><p>${escapeHtml(item.description)}</p><div class="feedback-card-footer">Posted by ${escapeHtml(item.author || 'PALs user')}</div></div></article>`;
        }).join('') : '';
        list.insertAdjacentHTML('beforeend', '<p class="empty-state" data-feedback-empty hidden>No feedback posts yet.</p>');
        const count = document.querySelector('[data-feedback-count]');
        if (count) count.textContent = String(items.length);
        const filter = document.querySelector('[data-feedback-filter]');
        if (filter && !filter.dataset.liveFilterBound) {
            filter.dataset.liveFilterBound = 'true';
            filter.addEventListener('change', () => {
                const visible = [...list.querySelectorAll('.feedback-card')].filter(card => {
                    const matches = filter.value === 'all' || card.dataset.category === filter.value;
                    card.hidden = !matches;
                    return matches;
                });
                const empty = list.querySelector('[data-feedback-empty]');
                if (empty) {
                    empty.textContent = items.length ? 'No feedback posts in this category.' : 'No feedback posts yet.';
                    empty.hidden = visible.length > 0;
                }
            });
        }
        document.querySelector('[data-feedback-filter]')?.dispatchEvent(new Event('change'));
    }

    async function renderAnalytics() {
        const [consent, summary] = await Promise.all([
            api('/analytics/consent'),
            api('/analytics/summary')
        ]);
        const consented = consent.consented === true;
        const items = consented ? summary.items || [] : [];
        const button = document.querySelector('[data-analytics-consent-button]');
        if (button) {
            button.disabled = false;
            button.dataset.consented = String(consented);
            button.textContent = consented ? 'Withdraw consent' : 'Allow analytics';
        }
        status(
            document.querySelector('[data-analytics-consent-status]'),
            consented
                ? 'Analytics is enabled. You can withdraw consent at any time.'
                : 'Analytics is off. No activity is recorded until you opt in.'
        );
        const totalEvents = items.reduce((total, item) => total + Number(item.events || 0), 0);
        const totalSeconds = items.reduce((total, item) => total + Number(item.duration_seconds || 0), 0);
        const values = {
            '[data-analytics-feature-count]': items.length,
            '[data-analytics-event-count]': totalEvents,
            '[data-analytics-duration]': `${Math.floor(totalSeconds / 60)} min`
        };
        Object.entries(values).forEach(([selector, value]) => {
            const node = document.querySelector(selector);
            if (node) node.textContent = String(value);
        });
        const host = document.querySelector('[data-analytics-summary]');
        if (!host) return;
        host.innerHTML = !consented
            ? '<p class="empty-state">Enable optional analytics consent to view your activity summary.</p>'
            : items.length
                ? `<div class="table-wrap"><table class="analytics-table"><thead><tr><th scope="col">Feature</th><th scope="col">Event</th><th scope="col">Count</th><th scope="col">Time</th></tr></thead><tbody>${items.map(item => `<tr><td>${escapeHtml(item.feature_key)}</td><td>${escapeHtml(item.event_type)}</td><td>${escapeHtml(item.events)}</td><td>${escapeHtml(item.duration_seconds || 0)} sec</td></tr>`).join('')}</tbody></table></div>`
                : '<p class="empty-state">No consented activity has been recorded yet.</p>';
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
            await hydrateAccount();
            if (pageUrl === 'home') {
                const [dashboard, matches, sessions] = await Promise.all([
                    api('/dashboard'),
                    api('/connect/recommendations'),
                    api('/sessions')
                ]);
                const values = {
                    sessions: dashboard.sessions_this_month,
                    hours: Number(dashboard.hours_logged || 0).toFixed(1),
                    tutors: dashboard.tutor_connections,
                    requests: dashboard.open_requests
                };
                Object.entries(values).forEach(([key, value]) => { const node = document.querySelector(`[data-home-stat="${key}"]`); if (node) node.textContent = value ?? 0; });
                renderHomeRecommendations(matches.items || []);
                renderHomeSessions(sessions.items || []);
            }
            if (pageUrl === 'tutors') renderTutorCards((await api('/connect/recommendations')).items || []);
            if (pageUrl === 'tutees') renderTutees((await api('/connect/recommendations')).items || []);
            if (pageUrl === 'sessions') renderSessions((await api('/sessions')).items || []);
            if (pageUrl === 'feedback') renderFeedback((await api('/feedback')).items || []);
            if (pageUrl === 'resources') {
                renderResources((await api('/resources')).items || []);
            }
            if (pageUrl === 'analytics') await renderAnalytics();
            if (pageUrl === 'settings') {
                document.querySelector('.content').innerHTML = `<div class="topbar"><div><div class="topbar-title">Settings</div><div class="topbar-sub">Manage privacy and learning-data preferences</div></div></div><div class="sub-content"><section class="panel home-section"><h1>Privacy and research settings</h1><p>Optional analytics helps evaluate PALs. It is not required for ordinary platform access.</p><button class="btn-primary" type="button" data-analytics-consent>Allow learning analytics</button><p class="form-status" data-settings-status role="status" aria-live="polite"></p></section></div>`;
                document.querySelector('[data-analytics-consent]')?.addEventListener('click', async event => {
                    try { await api('/analytics/consent', { method: 'POST', body: JSON.stringify({ consentVersion: 'phase2-v1' }) }); event.target.textContent = 'Analytics enabled'; status(document.querySelector('[data-settings-status]'), 'Your consent was saved.'); }
                    catch (error) { status(document.querySelector('[data-settings-status]'), error.message, true); }
                });
            }
        } catch (error) {
            const target = document.querySelector('.content');
            if (target) status(target.querySelector('.form-status'), error.message, true);
            if (pageUrl === 'home') status(document.querySelector('[data-home-empty]'), 'Dashboard recommendations are unavailable right now.', true);
            if (pageUrl === 'analytics') status(document.querySelector('[data-analytics-consent-status]'), error.message, true);
            if (pageUrl === 'feedback') status(document.querySelector('[data-feedback-empty]'), 'Community feedback is unavailable right now.', true);
            if (pageUrl === 'sessions') {
                const list = document.querySelector('[data-session-list]');
                if (list) list.innerHTML = '<p class="empty-state">Live sessions are unavailable right now.</p>';
            }
            if (pageUrl === 'tutors') {
                const list = document.querySelector('[data-tutor-list]');
                if (list) list.innerHTML = '';
                status(document.querySelector('[data-tutor-empty]'), 'Live recommendations are unavailable right now.', true);
            }
            if (pageUrl === 'tutees') {
                const list = document.querySelector('[data-tutee-list]');
                if (list) list.innerHTML = '';
                status(document.querySelector('[data-tutee-empty]'), 'Live recommendations are unavailable right now.', true);
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
                const start = new Date(data.get('scheduledStart'));
                const end = new Date(data.get('scheduledEnd'));
                if (!Number.isFinite(start.getTime()) || !Number.isFinite(end.getTime()) || end <= start) {
                    status(form.querySelector('[data-request-status]'), 'Choose a session end time after the start.', true);
                    return;
                }
                await api('/connect', {
                    method: 'POST',
                    body: JSON.stringify({
                        requestId: Number(data.get('requestId')),
                        candidateUserId: Number(data.get('candidateUserId')),
                        scheduledStartUtc: start.toISOString(),
                        scheduledEndUtc: end.toISOString()
                    })
                });
                status(form.querySelector('[data-request-status]'), 'Session request sent. The other student must confirm it.');
                form.querySelector('[data-close-dialog]')?.click();
                const pageUrl = document.querySelector('.nav-item.active')?.dataset.page || 'home';
                await hydratePage(pageUrl);
            } catch (error) { status(form.querySelector('[data-request-status]'), error.message, true); }
        }
        if (form.matches('[data-learning-request-form]')) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const data = new FormData(form);
            const proficiencyValue = data.get('currentProficiency');
            try {
                await api('/learning-requests', {
                    method: 'POST',
                    body: JSON.stringify({
                        subject: data.get('subject'),
                        requestedHelp: data.get('requestedHelp'),
                        currentProficiency: proficiencyValue ? Number(proficiencyValue) : null,
                        preferredFormat: 'one_to_one'
                    })
                });
                status(form.querySelector('[data-learning-request-status]'), 'Learning request posted. Matching recommendations are now being refreshed.');
                form.querySelector('[data-close-dialog]')?.click();
                form.reset();
                const pageUrl = document.querySelector('.nav-item.active')?.dataset.page || 'tutors';
                await hydratePage(pageUrl);
            } catch (error) { status(form.querySelector('[data-learning-request-status]'), error.message, true); }
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
        const connectButton = event.target.closest('[data-live-connect]');
        if (connectButton) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const dialog = document.querySelector('[data-request-dialog]');
            const form = dialog?.querySelector('[data-request-form]');
            if (!dialog || !form) return;
            form.reset();
            form.querySelector('[data-request-id]').value = connectButton.dataset.requestId || '';
            form.querySelector('[data-candidate-user-id]').value = connectButton.dataset.candidateUserId || '';
            form.querySelector('[data-request-subject]').value = connectButton.dataset.subject || '';
            form.querySelector('[data-request-tutor-label]').textContent = connectButton.dataset.candidateName || '';
            const isTutorCandidate = connectButton.dataset.candidateRole === 'tutor';
            const title = dialog.querySelector('h2');
            if (title) title.textContent = isTutorCandidate ? 'Request a tutoring session' : 'Offer peer support';
            const submit = form.querySelector('button[type="submit"]');
            if (submit) submit.textContent = isTutorCandidate ? 'Send session request' : 'Send offer';
            status(form.querySelector('[data-request-status]'), '');
            dialog.showModal();
            return;
        }

        const openLearningRequest = event.target.closest('[data-open-learning-request]');
        if (openLearningRequest) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const dialog = document.querySelector('[data-learning-request-dialog]');
            if (dialog && !dialog.open) dialog.showModal();
            return;
        }

        const consentButton = event.target.closest('[data-analytics-consent-button]');
        if (consentButton) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const enabled = consentButton.dataset.consented !== 'true';
            consentButton.disabled = true;
            try {
                await api('/analytics/consent', {
                    method: 'POST',
                    body: JSON.stringify({ consented: enabled, consentVersion: 'phase2-v1' })
                });
                await renderAnalytics();
                if (enabled) await trackPageView('analytics');
            } catch (error) {
                consentButton.disabled = false;
                status(document.querySelector('[data-analytics-consent-status]'), error.message, true);
            }
            return;
        }

        const sessionAction = event.target.closest('[data-live-session] [data-session-action]');
        if (sessionAction) {
            event.preventDefault();
            event.stopImmediatePropagation();
            const card = sessionAction.closest('[data-live-session]');
            const id = card?.dataset.sessionId;
            const action = sessionAction.dataset.sessionAction;
            const nextStatus = action === 'complete' ? 'completed' : action === 'confirm' ? 'confirmed' : 'cancelled';
            if (!id) return;
            sessionAction.disabled = true;
            try {
                await api(`/sessions/${encodeURIComponent(id)}`, { method: 'PATCH', body: JSON.stringify({ status: nextStatus }) });
                const items = (await api('/sessions')).items || [];
                renderSessions(items);
                renderHomeSessions(items);
            } catch (error) {
                const message = document.querySelector('[data-session-status-message]');
                status(message, error.message, true);
                if (message) message.hidden = false;
                sessionAction.disabled = false;
            }
            return;
        }
        const vote = event.target.closest('[data-live-vote]');
        if (!vote) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        vote.disabled = true;
        try {
            await api(`/feedback/${vote.dataset.liveVote}/votes`, { method: 'POST', body: '{}' });
            vote.setAttribute('aria-pressed', 'true');
            const count = vote.querySelector('[data-vote-count]');
            if (count) count.textContent = String(Number(count.textContent || 0) + 1);
        } catch (error) {
            vote.disabled = false;
            status(document.querySelector('[data-feedback-status]'), error.message, true);
        }
    }, true);

    window.addEventListener('pals:page-loaded', event => {
        void hydratePage(event.detail.pageUrl);
        void trackPageView(event.detail.pageUrl);
    });
})();
