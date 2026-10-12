(() => {
    let theme = 'dark';
    try { const saved = localStorage.getItem('studyplanner-theme'); if (saved === 'light' || saved === 'dark') theme = saved; } catch { /* Storage is optional. */ }
    document.documentElement.dataset.theme = theme;
    document.documentElement.dataset.bsTheme = theme;
})();
