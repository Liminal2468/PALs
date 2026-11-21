document.addEventListener('DOMContentLoaded', () => {
    const root = document.getElementById('palRoot');
    const toggle = document.getElementById('darkToggle');
    const label = document.getElementById('modeLabel');
    const sidebarToggle = document.getElementById('sidebarToggle');
    const shell = document.querySelector('.shell');
    const content = document.querySelector('.content');
    const navItems = [...document.querySelectorAll('.nav-item')];

    const keys = {
        profile: 'pals:profile',
        feedback: 'pals:feedback',
        votes: 'pals:feedbackVotes',
        requests: 'pals:sessionRequests',
        sessionStatuses: 'pals:sessionStatuses',
        tutorFavorites: 'pals:tutorFavorites'
    };

    function readStored(key, fallback) {
        const value = localStorage.getItem(key);
        if (value === null) return fallback;
        try {
            return JSON.parse(value);
        } catch (error) {
            console.error(`Unable to read saved PALs data for ${key}:`, error);
            throw new Error('Saved demo data could not be read. Clear the related browser storage entry and try again.');
        }
    }

    function saveStored(key, value) {
        localStorage.setItem(key, JSON.stringify(value));
    }

    function emit(name, detail) {
        window.dispatchEvent(new CustomEvent(name, { detail }));
    }

    function setFormStatus(element, message, isError = false) {
        if (!element) return;
        element.textContent = message;
        element.classList.toggle('form-status-error', isError);
    }

    function updateHeader(profile) {
        const name = profile?.displayName || 'J. Mike';
        const section = profile?.section || 'Grade 12 - ITM302';
        const headerName = document.querySelector('.header-user .user-name');
        const headerRole = document.querySelector('.header-user .user-role');
        const avatar = document.querySelector('.header-user .avatar');
        if (headerName) headerName.textContent = name;
        if (headerRole) headerRole.textContent = `${profile?.grade || 'Grade 12'} - ${section}`;
        if (avatar) avatar.textContent = initials(name);
        document.querySelectorAll('[data-profile-name]').forEach(node => {
            node.textContent = name;
        });
    }

    function initials(name) {
        return name.trim().split(/\s+/).slice(0, 2).map(part => part[0] || '').join('').toUpperCase() || 'JM';
    }

    function setActivePage(pageUrl) {
        navItems.forEach(item => {
            const active = item.getAttribute('data-page') === pageUrl;
            item.classList.toggle('active', active);
            if (active) item.setAttribute('aria-current', 'page');
            else item.removeAttribute('aria-current');
        });
    }

    async function loadPage(pageName) {
        try {
            const response = await fetch(`/pals/partial/${encodeURIComponent(pageName)}`, {
                credentials: 'same-origin',
                cache: 'no-store'
            });
            if (!response.ok) throw new Error(`Request failed with status ${response.status}.`);
            content.innerHTML = await response.text();
            setActivePage(pageName);
            initializeLoadedPage();
            window.dispatchEvent(new CustomEvent('pals:page-loaded', { detail: { pageUrl: pageName } }));
        } catch (error) {
            console.error('Failed to load page:', error);
            content.innerHTML = `<div style="padding:20px;color:var(--text-secondary)">
                <h3>Unable to load content</h3>
                <p>Make sure you are running PALs from a local web server, then try again.</p>
            </div>`;
        }
    }

    function initializeLoadedPage() {
        initializeHome();
        initializeTutors();
        initializeTutees();
        initializeSessions();
        initializeHelp();
        initializeFeedback();
        initializeProfile();
    }

    function initializeTutors() {
        const cards = [...content.querySelectorAll('[data-tutor-card]')];
        if (!cards.length) return;
        if (window.PalsLive) return;
        const search = content.querySelector('[data-tutor-search]');
        const subject = content.querySelector('[data-tutor-subject]');
        const availability = content.querySelector('[data-tutor-availability]');
        const sort = content.querySelector('[data-tutor-sort]');
        const resultCount = content.querySelector('[data-tutor-results]');
        const empty = content.querySelector('[data-tutor-empty]');

        try {
            const favorites = readStored(keys.tutorFavorites, []);
            if (!Array.isArray(favorites) || favorites.some(id => typeof id !== 'string')) {
                throw new Error('Saved tutor favorites have an invalid format.');
            }
            content.querySelectorAll('[data-tutor-favorite]').forEach(button => {
                const favorite = favorites.includes(button.dataset.tutorFavorite);
                button.setAttribute('aria-pressed', String(favorite));
                button.textContent = favorite ? '★' : '☆';
            });
        } catch (error) {
            console.error('Unable to load saved tutor favorites:', error);
            setFormStatus(content.querySelector('[data-tutor-favorite-status]'), error.message, true);
        }

        const filterTutors = () => {
            const query = search.value.trim().toLocaleLowerCase();
            const selectedSubject = subject.value;
            const selectedAvailability = availability.value;
            const visibleCards = cards.filter(card => {
                const matchesQuery = !query || card.textContent.toLocaleLowerCase().includes(query);
                const matchesSubject = selectedSubject === 'all' || card.dataset.subjects.split(',').includes(selectedSubject);
                const matchesAvailability = selectedAvailability === 'all' || card.dataset.availability === selectedAvailability;
                card.hidden = !(matchesQuery && matchesSubject && matchesAvailability);
                return !card.hidden;
            });
            const mode = sort.value;
            visibleCards.sort((first, second) => {
                if (mode === 'rating') return Number(second.dataset.rating) - Number(first.dataset.rating);
                if (mode === 'name') return first.dataset.name.localeCompare(second.dataset.name);
                return Number(second.dataset.match) - Number(first.dataset.match);
            }).forEach(card => content.querySelector('[data-tutor-list]').append(card));
            resultCount.textContent = `Showing ${visibleCards.length} of ${cards.length} peer tutors`;
            empty.hidden = visibleCards.length > 0;
        };

        [search, subject, availability, sort].forEach(control => {
            control.addEventListener(control === search ? 'input' : 'change', filterTutors);
        });
        content.querySelector('[data-tutor-reset]')?.addEventListener('click', () => {
            search.value = '';
            subject.value = 'all';
            availability.value = 'all';
            sort.value = 'match';
            filterTutors();
            search.focus();
        });
        filterTutors();
    }

    function initializeTutees() {
        const cards = [...content.querySelectorAll('[data-tutee-card]')];
        if (!cards.length) return;
        if (window.PalsLive) return;
        const search = content.querySelector('[data-tutee-search]');
        const subject = content.querySelector('[data-tutee-subject]');
        const availability = content.querySelector('[data-tutee-availability]');
        const sort = content.querySelector('[data-tutee-sort]');
        const resultCount = content.querySelector('[data-tutee-results]');
        const empty = content.querySelector('[data-tutee-empty]');
        const list = content.querySelector('[data-tutee-list]');

        const filterTutees = () => {
            const query = search.value.trim().toLocaleLowerCase();
            const selectedSubject = subject.value;
            const selectedAvailability = availability.value;
            const visibleCards = cards.filter(card => {
                const matchesQuery = !query || card.textContent.toLocaleLowerCase().includes(query);
                const matchesSubject = selectedSubject === 'all' || card.dataset.subjects.split(',').includes(selectedSubject);
                const matchesAvailability = selectedAvailability === 'all' || card.dataset.availability === selectedAvailability;
                card.hidden = !(matchesQuery && matchesSubject && matchesAvailability);
                return !card.hidden;
            });
            if (sort.value === 'name') visibleCards.sort((first, second) => first.dataset.name.localeCompare(second.dataset.name));
            else if (sort.value === 'subject') visibleCards.sort((first, second) => first.dataset.subjects.localeCompare(second.dataset.subjects));
            else visibleCards.sort((first, second) => Number(first.dataset.recent) - Number(second.dataset.recent));
            visibleCards.forEach(card => list.append(card));
            resultCount.textContent = `Showing ${visibleCards.length} of ${cards.length} students seeking help`;
            empty.hidden = visibleCards.length > 0;
        };

        [search, subject, availability, sort].forEach(control => {
            control.addEventListener(control === search ? 'input' : 'change', filterTutees);
        });
        content.querySelector('[data-tutee-reset]')?.addEventListener('click', () => {
            search.value = '';
            subject.value = 'all';
            availability.value = 'all';
            sort.value = 'recent';
            filterTutees();
            search.focus();
        });
        filterTutees();
    }

    function initializeSessions() {
        const list = content.querySelector('[data-session-list]');
        if (!list) return;

        content.querySelector('[data-session-filter]')?.addEventListener('change', updateSessionList);
        updateSessionList();
    }

    function createSessionRequestCard(request) {
        const card = document.createElement('article');
        card.className = 'session-card';
        card.dataset.sessionCard = '';
        card.dataset.sessionId = request.id;
        card.dataset.status = request.status === 'cancelled' ? 'cancelled' : 'requested';
        card.dataset.sessionRequest = '';

        const dateBlock = document.createElement('div');
        dateBlock.className = 'session-date-block';
        const date = new Date(request.preferredTime);
        const day = document.createElement('span');
        day.textContent = Number.isNaN(date.getTime()) ? 'Date' : date.toLocaleDateString(undefined, { weekday: 'short' });
        const time = document.createElement('strong');
        time.textContent = Number.isNaN(date.getTime()) ? 'TBD' : date.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
        const monthDay = document.createElement('small');
        monthDay.textContent = Number.isNaN(date.getTime()) ? 'Requested' : date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
        dateBlock.append(day, time, monthDay);

        const main = document.createElement('div');
        main.className = 'session-card-main';
        const top = document.createElement('div');
        top.className = 'session-card-top';
        const subject = document.createElement('span');
        subject.className = 'tag blue';
        subject.textContent = request.subject;
        const status = document.createElement('span');
        status.className = 'session-status';
        status.dataset.sessionStatusLabel = '';
        status.textContent = card.dataset.status === 'cancelled' ? 'Cancelled' : 'Pending response';
        const title = document.createElement('h3');
        const peer = document.createElement('p');
        peer.textContent = request.role === 'tutee' ? `Student seeking support: ${request.tutor}` : `Peer tutor: ${request.tutor}`;
        const note = document.createElement('p');
        note.className = 'session-card-note';
        note.textContent = request.note ? `Your note: ${request.note}` :
            request.role === 'tutee' ? 'Your offer is saved in this browser only.' : 'Your request is saved in this browser only.';
        const titleText = request.role === 'tutee'
            ? `${request.subject} peer support offer`
            : `${request.subject} tutoring request`;
        top.append(subject, status);
        title.textContent = titleText;
        main.append(top, title, peer, note);

        const actions = document.createElement('div');
        actions.className = 'session-card-actions';
        const cancel = document.createElement('button');
        cancel.className = 'text-button danger-text';
        cancel.type = 'button';
        cancel.dataset.sessionAction = 'cancel';
        cancel.textContent = request.role === 'tutee' ? 'Withdraw offer' : 'Cancel request';
        actions.append(cancel);
        card.append(dateBlock, main, actions);
        return card;
    }

    function setSessionCardStatus(card, status) {
        card.dataset.status = status;
        const label = card.querySelector('[data-session-status-label]');
        if (label) {
            label.textContent = status === 'upcoming' ? 'Upcoming' :
                status === 'completed' ? 'Completed' : status === 'cancelled' ? 'Cancelled' : 'Pending response';
        }
        const actions = card.querySelector('.session-card-actions');
        if (actions) {
            actions.hidden = status === 'completed' || status === 'cancelled';
        }
    }

    function updateSessionList() {
        const list = content.querySelector('[data-session-list]');
        if (!list) return;
        const cards = [...list.querySelectorAll('[data-session-card]')];
        const filter = content.querySelector('[data-session-filter]')?.value || 'all';
        const counts = { requested: 0, upcoming: 0, completed: 0, cancelled: 0 };
        let visible = 0;
        cards.forEach(card => {
            const status = card.dataset.status;
            if (counts[status] !== undefined) counts[status]++;
            const matches = filter === 'all' || status === filter;
            card.hidden = !matches;
            if (matches) visible++;
        });
        Object.entries(counts).forEach(([status, count]) => {
            const target = content.querySelector(`[data-session-stat="${status}"]`);
            if (target) target.textContent = String(count);
        });
        const empty = content.querySelector('[data-session-empty]');
        if (empty) empty.hidden = visible > 0;
    }

    function updateSessionStatus(card, status) {
        const id = card.dataset.sessionId;
        if (!id) return;
        const feedback = content.querySelector('[data-session-status-message]');
        if (feedback) {
            feedback.textContent = 'This session is not connected to a live API record.';
            feedback.hidden = false;
        }
    }

    async function initializeHelp() {
        const faqList = content.querySelector('[data-help-faq-list]');
        const supportHost = content.querySelector('[data-help-support]');
        if (!faqList || !supportHost) return;

        try {
            const [faqResponse, supportResponse] = await Promise.all([
                fetch('/faqs'),
                fetch('/contactsupport')
            ]);
            if (!faqResponse.ok) throw new Error(`FAQ content request failed with status ${faqResponse.status}.`);
            if (!supportResponse.ok) throw new Error(`Support content request failed with status ${supportResponse.status}.`);

            const [faqMarkup, supportMarkup] = await Promise.all([faqResponse.text(), supportResponse.text()]);
            const faqDocument = new DOMParser().parseFromString(faqMarkup, 'text/html');
            const supportDocument = new DOMParser().parseFromString(supportMarkup, 'text/html');
            const categories = [...faqDocument.querySelectorAll('.faq-category')];
            const supportSections = ['.support-info', '.contact-card']
                .map(selector => supportDocument.querySelector(selector))
                .filter(Boolean);
            if (!categories.length || supportSections.length !== 2) {
                throw new Error('FAQ or support content is missing required page sections.');
            }

            faqList.replaceChildren(...categories.map(category => document.importNode(category, true)));
            supportHost.replaceChildren(...supportSections.map(section => document.importNode(section, true)));

            const noResults = document.createElement('p');
            noResults.className = 'empty-state help-no-results';
            noResults.textContent = 'No questions match your search. Try another keyword.';
            noResults.hidden = true;
            faqList.append(noResults);
            initializeHelpSearch(faqList, noResults);
        } catch (error) {
            console.error('Unable to load Help page content:', error);
            faqList.innerHTML = '<p class="help-error" role="alert">FAQ content could not be loaded. Please reload the Help page and try again.</p>';
            supportHost.innerHTML = '<p class="help-error" role="alert">Support information could not be loaded. Please reload the Help page and try again.</p>';
        }
    }

    function initializeHelpSearch(faqList, noResults) {
        const search = content.querySelector('[data-help-faq-search]');
        const count = content.querySelector('[data-help-faq-count]');
        const items = [...faqList.querySelectorAll('.faq-item')];
        const categories = [...faqList.querySelectorAll('.faq-category')];
        const filter = () => {
            const query = search.value.trim().toLocaleLowerCase();
            let visible = 0;
            categories.forEach(category => {
                const questions = [...category.querySelectorAll('.faq-item')];
                let categoryVisible = 0;
                questions.forEach(item => {
                    const matches = item.textContent.toLocaleLowerCase().includes(query);
                    item.hidden = !matches;
                    if (!matches) item.open = false;
                    if (matches) {
                        categoryVisible++;
                        visible++;
                    }
                });
                category.hidden = categoryVisible === 0;
            });
            count.textContent = query
                ? `${visible} ${visible === 1 ? 'question' : 'questions'} found`
                : `${items.length} questions across ${categories.length} topics`;
            noResults.hidden = visible > 0;
        };
        search.addEventListener('input', filter);
        filter();
    }

    function initializeHome() {
        const filters = content.querySelectorAll('[data-home-filter]');
        if (!filters.length) return;
        filters.forEach(button => button.addEventListener('click', () => {
            const subject = button.dataset.homeFilter;
            filters.forEach(filter => {
                const active = filter === button;
                filter.classList.toggle('active', active);
                filter.setAttribute('aria-pressed', String(active));
            });
            let visible = 0;
            content.querySelectorAll('[data-home-tutor]').forEach(card => {
                const matches = subject === 'all' || card.dataset.subjects.split(',').includes(subject);
                card.hidden = !matches;
                if (matches) visible++;
            });
            const empty = content.querySelector('[data-home-empty]');
            if (empty) empty.hidden = visible !== 0;
        }));
    }

    function initializeFeedback() {
        const list = content.querySelector('[data-feedback-list]');
        if (!list) return;
        const status = content.querySelector('[data-feedback-status]');

        try {
            const posts = readStored(keys.feedback, []);
            if (!Array.isArray(posts)) throw new Error('Saved feedback data has an invalid format.');
            posts.slice().reverse().forEach(post => list.prepend(createFeedbackCard(post)));

            const votes = readStored(keys.votes, []);
            if (!Array.isArray(votes)) throw new Error('Saved feedback votes have an invalid format.');
            votes.forEach(id => {
                const card = [...list.querySelectorAll('[data-feedback-id]')].find(item => item.dataset.feedbackId === id);
                const button = card?.querySelector('[data-feedback-vote]');
                if (!button) return;
                button.setAttribute('aria-pressed', 'true');
                const count = button.querySelector('[data-vote-count]');
                if (count) count.textContent = String(Number(count.textContent) + 1);
            });
            updateFeedbackCount();
        } catch (error) {
            console.error('Unable to initialize the feedback forum:', error);
            setFormStatus(status, error.message, true);
        }

        const filter = content.querySelector('[data-feedback-filter]');
        filter?.addEventListener('change', () => {
            const category = filter.value;
            let visible = 0;
            list.querySelectorAll('.feedback-card').forEach(card => {
                const matches = category === 'all' || card.dataset.category === category;
                card.hidden = !matches;
                if (matches) visible++;
            });
            const empty = list.querySelector('[data-feedback-empty]');
            if (empty) empty.hidden = visible !== 0;
        });
    }

    function createFeedbackCard(post) {
        const card = document.createElement('article');
        card.className = 'feedback-card';
        card.dataset.feedbackId = post.id;
        card.dataset.category = post.category;

        const vote = document.createElement('button');
        vote.className = 'feedback-vote';
        vote.type = 'button';
        vote.dataset.feedbackVote = '';
        vote.setAttribute('aria-label', `Vote for ${post.title}`);
        vote.setAttribute('aria-pressed', 'false');
        vote.innerHTML = '<span aria-hidden="true">▲</span><strong data-vote-count>0</strong><span>votes</span>';

        const body = document.createElement('div');
        body.className = 'feedback-card-body';
        const meta = document.createElement('div');
        meta.className = 'feedback-card-meta';
        const category = document.createElement('span');
        category.className = 'tag blue';
        category.textContent = post.category;
        const sample = document.createElement('span');
        sample.textContent = 'Community post';
        meta.append(category, sample);

        const title = document.createElement('h3');
        title.textContent = post.title;
        const description = document.createElement('p');
        description.textContent = post.description;
        const footer = document.createElement('div');
        footer.className = 'feedback-card-footer';
        footer.textContent = `Posted by ${post.author || 'You'} · Saved in this browser`;
        body.append(meta, title, description, footer);
        card.append(vote, body);
        return card;
    }

    function updateFeedbackCount() {
        const count = content.querySelector('[data-feedback-count]');
        const list = content.querySelector('[data-feedback-list]');
        if (count && list) count.textContent = String(list.querySelectorAll('.feedback-card').length);
    }

    function initializeProfile() {
        const form = content.querySelector('[data-profile-form]');
        if (!form) return;
        const status = content.querySelector('[data-profile-status]');
        const defaultProfile = {
            displayName: form.elements.displayName.value,
            email: form.elements.email.value,
            grade: form.elements.grade.value,
            section: form.elements.section.value,
            bio: form.elements.bio.value,
            roles: [...form.querySelectorAll('[name="roles"]:checked')].map(input => input.value),
            subjects: [...form.querySelectorAll('[name="subjects"]:checked')].map(input => input.value)
        };
        let profile = defaultProfile;
        applyProfile(form, profile);

        form.addEventListener('submit', event => {
            event.preventDefault();
            const updated = {
                displayName: form.elements.displayName.value.trim(),
                email: defaultProfile.email,
                grade: form.elements.grade.value,
                section: form.elements.section.value.trim(),
                bio: form.elements.bio.value.trim(),
                roles: [...form.querySelectorAll('[name="roles"]:checked')].map(input => input.value),
                subjects: [...form.querySelectorAll('[name="subjects"]:checked')].map(input => input.value)
            };
            if (!updated.displayName) {
                setFormStatus(status, 'Enter a display name before saving.', true);
                return;
            }
            setFormStatus(status, 'Profile saving requires the live PALs API.');
        });

        content.querySelector('[data-reset-profile]')?.addEventListener('click', () => {
            setFormStatus(status, 'Live profiles cannot be reset from browser storage.');
        });
    }

    function applyProfile(form, profile) {
        form.elements.displayName.value = profile.displayName || '';
        form.elements.grade.value = profile.grade || 'Grade 12';
        form.elements.section.value = profile.section || '';
        form.elements.bio.value = profile.bio || '';
        ['roles', 'subjects'].forEach(group => {
            const selected = Array.isArray(profile[group]) ? profile[group] : [];
            form.querySelectorAll(`[name="${group}"]`).forEach(input => {
                input.checked = selected.includes(input.value);
            });
        });

        const name = profile.displayName || 'J. Mike';
        const gradeSection = `${profile.grade || 'Grade 12'} · ${profile.section || 'Section not set'}`;
        content.querySelector('[data-profile-display-name]').textContent = name;
        content.querySelector('[data-profile-summary]').textContent = gradeSection;
        content.querySelector('[data-profile-avatar]').textContent = initials(name);
        content.querySelector('[data-profile-email]').textContent = profile.email || '';
        const interests = content.querySelector('[data-profile-interest-list]');
        interests.replaceChildren();
        (profile.subjects || []).forEach(subject => {
            const tag = document.createElement('span');
            tag.className = 'tag blue';
            tag.textContent = subject;
            interests.append(tag);
        });
        if (!profile.subjects?.length) interests.textContent = 'No interests selected yet.';
        updateHeader(profile);
    }

    function normalizeProfile(value, fallback) {
        if (!value || typeof value !== 'object' || Array.isArray(value)) {
            throw new Error('Saved profile data has an invalid format.');
        }
        const validRoles = ['tutee', 'tutor'];
        const validSubjects = ['Programming', 'Mathematics', 'Science', 'Research', 'Self improvement'];
        if (typeof value.displayName !== 'string' ||
            (value.grade !== 'Grade 11' && value.grade !== 'Grade 12') ||
            typeof value.section !== 'string' ||
            typeof value.bio !== 'string' ||
            !Array.isArray(value.roles) || value.roles.some(role => !validRoles.includes(role)) ||
            !Array.isArray(value.subjects) || value.subjects.some(subject => !validSubjects.includes(subject))) {
            throw new Error('Saved profile data has an invalid format.');
        }
        return {
            ...fallback,
            ...value,
            email: fallback.email,
            roles: [...new Set(value.roles)],
            subjects: [...new Set(value.subjects)]
        };
    }

    function openRequestDialog(peerName, role = 'tutor') {
        const dialog = content.querySelector('[data-request-dialog]');
        if (!dialog) return;
        const isTutee = role === 'tutee';
        dialog.dataset.requestRole = role;
        dialog.querySelector('[data-request-tutor-label]').textContent = isTutee
            ? `Offering peer support to ${peerName}`
            : `Requesting a session with ${peerName}`;
        dialog.querySelector('[data-request-tutor-id]').value = peerName;
        const status = dialog.querySelector('[data-request-status]');
        setFormStatus(status, '');
        dialog.showModal();
    }

    document.addEventListener('click', event => {
        const nav = event.target.closest('[data-navigate], .nav-item[data-page]');
        if (nav) {
            if (nav.closest('.sidebar') && window.matchMedia('(max-width: 768px)').matches) {
                shell?.classList.add('collapsed');
            }
            loadPage(nav.dataset.navigate || nav.dataset.page);
            return;
        }

        const requestButton = event.target.closest('[data-request-tutor], [data-request-tutee]');
        if (requestButton) {
            const role = requestButton.hasAttribute('data-request-tutee') ? 'tutee' : 'tutor';
            openRequestDialog(requestButton.dataset.requestTutor || requestButton.dataset.requestTutee, role);
            return;
        }

        const tutorDetails = event.target.closest('[data-tutor-details]');
        if (tutorDetails) {
            const details = tutorDetails.closest('[data-tutor-card]')?.querySelector('[data-tutor-extra]');
            if (details) {
                details.hidden = !details.hidden;
                tutorDetails.textContent = details.hidden ? 'About tutor' : 'Hide details';
            }
            return;
        }

        const favoriteButton = event.target.closest('[data-tutor-favorite]');
        if (favoriteButton) {
            toggleTutorFavorite(favoriteButton);
            return;
        }

        const sessionAction = event.target.closest('[data-session-action]');
        if (sessionAction) {
            const card = sessionAction.closest('[data-session-card]');
            if (card && sessionAction.dataset.sessionAction === 'cancel') updateSessionStatus(card, 'cancelled');
            else if (card && sessionAction.dataset.sessionAction === 'complete') updateSessionStatus(card, 'completed');
            return;
        }

        const closeDialog = event.target.closest('[data-close-dialog]');
        if (closeDialog) {
            closeDialog.closest('dialog')?.close();
            return;
        }

        const toggleFeedback = event.target.closest('[data-toggle-feedback-form]');
        if (toggleFeedback) {
            const composer = content.querySelector('[data-feedback-composer]');
            if (composer) {
                composer.hidden = !composer.hidden;
                if (!composer.hidden) composer.querySelector('input[name="title"]')?.focus();
            }
            return;
        }

        const voteButton = event.target.closest('[data-feedback-vote]');
        if (voteButton && content.contains(voteButton)) {
            toggleVote(voteButton);
        }
    });

    function toggleTutorFavorite(button) {
        const tutorId = button.dataset.tutorFavorite;
        if (!tutorId) return;
        try {
            const favorites = readStored(keys.tutorFavorites, []);
            if (!Array.isArray(favorites) || favorites.some(id => typeof id !== 'string')) {
                throw new Error('Saved tutor favorites have an invalid format.');
            }
            const selected = favorites.includes(tutorId);
            const updated = selected ? favorites.filter(id => id !== tutorId) : [...favorites, tutorId];
            saveStored(keys.tutorFavorites, updated);
            button.setAttribute('aria-pressed', String(!selected));
            button.textContent = selected ? '☆' : '★';
            button.setAttribute('aria-label', `${selected ? 'Save' : 'Remove'} ${button.closest('[data-tutor-card]').dataset.name} ${selected ? 'to' : 'from'} favorites`);
        } catch (error) {
            console.error('Unable to update tutor favorites:', error);
            const status = content.querySelector('[data-tutor-favorite-status]');
            if (status) {
                status.textContent = error.message || 'Favorite could not be saved in browser storage.';
                status.hidden = false;
            }
        }
    }

    function toggleVote(button) {
        const card = button.closest('[data-feedback-id]');
        const id = card?.dataset.feedbackId;
        const count = button.querySelector('[data-vote-count]');
        if (!id || !count) return;
        try {
            const votes = readStored(keys.votes, []);
            if (!Array.isArray(votes)) throw new Error('Saved feedback votes have an invalid format.');
            const selected = votes.includes(id);
            const nextVotes = selected ? votes.filter(vote => vote !== id) : [...votes, id];
            saveStored(keys.votes, nextVotes);
            button.setAttribute('aria-pressed', String(!selected));
            count.textContent = String(Math.max(0, Number(count.textContent) + (selected ? -1 : 1)));
        } catch (error) {
            console.error('Unable to update feedback vote:', error);
            const status = content.querySelector('[data-feedback-status]');
            setFormStatus(status, error.message || 'Vote could not be saved in browser storage.', true);
        }
    }

    content.addEventListener('submit', event => {
        const supportForm = event.target.closest('[data-help-support] #supportForm');
        if (supportForm) {
            event.preventDefault();
            const email = supportForm.elements.email;
            const status = supportForm.querySelector('#formStatus');
            if (!email.validity.valid || !email.value.trim().toLowerCase().endsWith('@cubao.sti.edu.ph')) {
                email.setCustomValidity('Use a valid @cubao.sti.edu.ph school email.');
                email.reportValidity();
                return;
            }
            email.setCustomValidity('');
            status.textContent = 'The live support API is unavailable, so your message has not been sent.';
            return;
        }

        const requestForm = event.target.closest('[data-request-form]');
        if (requestForm) {
            event.preventDefault();
            const formData = new FormData(requestForm);
            setFormStatus(requestForm.querySelector('[data-request-status]'), 'The live learning-request API is unavailable.', true);
            return;
        }

        const feedbackForm = event.target.closest('[data-feedback-form]');
        if (feedbackForm) {
            event.preventDefault();
            const formData = new FormData(feedbackForm);
            setFormStatus(content.querySelector('[data-feedback-status]'), 'The live feedback API is unavailable.', true);
        }
    });

    content.addEventListener('input', event => {
        if (event.target.matches('[data-help-support] #supportEmail')) {
            event.target.setCustomValidity('');
        }
    });

    if (toggle && root && label) {
        const savedTheme = localStorage.getItem('theme');
        const prefersDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
        const isDark = savedTheme === 'dark' || (!savedTheme && prefersDark);
        root.classList.toggle('dark', isDark);
        toggle.checked = isDark;
        label.textContent = isDark ? 'Dark' : 'Light';
        toggle.addEventListener('change', () => {
            const dark = toggle.checked;
            root.classList.toggle('dark', dark);
            label.textContent = dark ? 'Dark' : 'Light';
            localStorage.setItem('theme', dark ? 'dark' : 'light');
        });
    }

    if (window.matchMedia('(max-width: 768px)').matches) shell?.classList.add('collapsed');
    sidebarToggle?.addEventListener('click', () => shell?.classList.toggle('collapsed'));

    updateHeader({ displayName: 'J. Mike', grade: 'Grade 12', section: 'Section not set' });
    loadPage('home');
});
