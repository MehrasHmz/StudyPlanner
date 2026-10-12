(() => {
    'use strict';
    const themeButton = document.getElementById('themeToggle');
    const updateTheme = () => {
        const light = document.documentElement.dataset.theme === 'light';
        const icon = document.getElementById('themeIcon');
        if (icon) icon.className = light ? 'bi bi-moon' : 'bi bi-sun';
        if (themeButton) { themeButton.title = light ? 'Switch to dark theme' : 'Switch to light theme'; themeButton.setAttribute('aria-label', themeButton.title); }
    };
    updateTheme();
    themeButton?.addEventListener('click', () => {
        const theme = document.documentElement.dataset.theme === 'dark' ? 'light' : 'dark';
        document.documentElement.dataset.theme = theme;
        document.documentElement.dataset.bsTheme = theme;
        try { localStorage.setItem('studyplanner-theme', theme); } catch { /* Theme works without storage. */ }
        updateTheme();
    });
    document.querySelectorAll('[data-password-target]').forEach(button => button.addEventListener('click', () => {
        const input = document.getElementById(button.dataset.passwordTarget);
        const show = input.type === 'password'; input.type = show ? 'text' : 'password';
        button.setAttribute('aria-pressed', String(show)); button.setAttribute('aria-label', show ? 'Hide password' : 'Show password'); button.title = show ? 'Hide password' : 'Show password'; button.querySelector('i').className = show ? 'bi bi-eye-slash' : 'bi bi-eye';
    }));
    const confirmation = document.getElementById('confirmationDialog'); let pendingForm = null;
    document.querySelectorAll('form[data-confirm]').forEach(form => form.addEventListener('submit', event => {
        if (form.dataset.confirmed === 'true') return;
        event.preventDefault();
        if (!confirmation?.showModal) { if (window.confirm(form.dataset.confirm)) { form.dataset.confirmed = 'true'; form.requestSubmit(); } return; }
        pendingForm = form; document.getElementById('confirmationMessage').textContent = form.dataset.confirm; confirmation.showModal();
    }));
    document.getElementById('confirmAction')?.addEventListener('click', () => { const form = pendingForm; confirmation.close(); if (form) { form.dataset.confirmed = 'true'; form.requestSubmit(); } });
    confirmation?.addEventListener('close', () => { pendingForm = null; });
    document.querySelectorAll('form[data-logout]').forEach(form => form.addEventListener('submit', () => { try { localStorage.removeItem('studyplanner:focus:v1:' + document.body.dataset.userId); } catch { /* Storage is optional. */ } }));
    [...document.querySelectorAll('[data-error-summary]')].find(summary => summary.offsetParent !== null)?.focus();
})();
