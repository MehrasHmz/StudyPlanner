(() => {
    'use strict';
    const root = document.getElementById('focusExperience');
    if (!root || window.__focusController) return;
    const core = window.FocusTimer, key = 'studyplanner:focus:v1:' + document.body.dataset.userId;
    const element = id => document.getElementById(id);
    let state;
    try { state = core.restore(localStorage.getItem(key), Date.now()); } catch { state = core.create(); }
    if (root.dataset.initialTask && state.entryItemId !== root.dataset.entryItem) {
        state.task = core.cleanTask(root.dataset.initialTask);
        state.entryItemId = root.dataset.entryItem;
    }
    let interval = null, destroyed = false;
    const notice = message => { element('focusNotice').textContent = message; };
    function save() {
        try { localStorage.setItem(key, JSON.stringify(state)); }
        catch { notice('Storage is unavailable. This session cannot be restored after leaving.'); }
    }
    function render() {
        const oldStatus = state.status;
        state = core.sync(state, Date.now());
        if (oldStatus !== state.status) save();
        root.dataset.timerStatus = state.status;
        const labels = { ready: 'Ready when you are', running: 'In focus', paused: 'Paused', completed: 'Session complete' };
        if (element('focusStatus').textContent !== labels[state.status]) element('focusStatus').textContent = labels[state.status];
        element('activeTask').textContent = state.task;
        const countdown = state.mode === 'countdown';
        element('timerDisplay').textContent = core.format(core.milliseconds(state, Date.now()), countdown);
        element('timerDisplay').setAttribute('aria-label', countdown ? 'Remaining focus time' : 'Elapsed focus time');
        element('focusProgress').hidden = !countdown;
        element('focusProgress').max = state.durationMs; element('focusProgress').value = state.elapsedMs;
        element('durationControls').hidden = !countdown;
        element('durationControls').disabled = state.status === 'running' || state.status === 'paused';
        if (document.activeElement !== element('customMinutes')) element('customMinutes').value = state.durationMs / 60000;
        document.querySelectorAll('[data-duration]').forEach(button => button.setAttribute('aria-pressed', String(Number(button.dataset.duration) === state.durationMs / 60000)));
        document.querySelectorAll('[name="timerMode"]').forEach(input => { input.checked = input.value === state.mode; });
        const running = state.status === 'running';
        element('startPauseButton').querySelector('i').className = running ? 'bi bi-pause-fill' : 'bi bi-play-fill';
        element('startPauseButton').querySelector('span').textContent = running ? 'Pause' : state.status === 'paused' ? 'Resume' : state.status === 'completed' ? 'Start another' : 'Start focus';
        element('completionMessage').hidden = state.status !== 'completed';
        element('showClock').checked = state.showClock;
        element('localClock').hidden = !state.showClock;
        element('localClock').textContent = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        element('localClock').dateTime = new Date().toISOString();
    }
    function transition(next) { state = next; save(); render(); }
    const listen = (target, event, handler) => { target.addEventListener(event, handler); listeners.push([target, event, handler]); };
    const listeners = [];
    listen(element('startPauseButton'), 'click', () => transition(state.status === 'running' ? core.pause(state, Date.now()) : core.start(state, Date.now())));
    listen(element('resetButton'), 'click', () => {
        if (state.elapsedMs > 0 || state.status === 'running') { if (!window.confirm('Reset this focus timer?')) return; }
        transition(core.reset(state));
    });
    document.querySelectorAll('[name="timerMode"]').forEach(input => listen(input, 'change', () => transition(core.setMode(state, input.value, Date.now()))));
    document.querySelectorAll('[data-duration]').forEach(button => listen(button, 'click', () => transition(core.setDuration(state, Number(button.dataset.duration)))));
    listen(element('customMinutes'), 'change', () => {
        try { transition(core.setDuration(state, Number(element('customMinutes').value))); notice(''); }
        catch (error) { notice(error.message); element('customMinutes').value = state.durationMs / 60000; }
    });
    listen(element('showClock'), 'change', () => transition({ ...state, showClock: element('showClock').checked }));
    const dialog = element('taskDialog');
    listen(element('editTaskButton'), 'click', () => { element('taskTitle').value = state.task; dialog.showModal(); });
    listen(element('cancelTaskButton'), 'click', () => dialog.close());
    listen(element('studyItemSelect'), 'change', () => {
        const select = element('studyItemSelect');
        if (select.value) element('taskTitle').value = select.selectedOptions[0].textContent.trim();
    });
    listen(element('taskForm'), 'submit', event => { event.preventDefault(); transition({ ...state, task: core.cleanTask(element('taskTitle').value) }); dialog.close(); });
    const fullscreen = element('fullscreenButton');
    function fullscreenChanged() {
        const active = Boolean(document.fullscreenElement) || root.dataset.immersive === 'true';
        if (!active && element('focusNotice').textContent.startsWith('Fullscreen is unavailable.')) notice('');
        fullscreen.title = active ? 'Exit fullscreen' : 'Enter fullscreen'; fullscreen.setAttribute('aria-label', fullscreen.title);
        fullscreen.querySelector('i').className = active ? 'bi bi-fullscreen-exit' : 'bi bi-fullscreen';
    }
    listen(fullscreen, 'click', async () => {
        try {
            if (document.fullscreenElement) await document.exitFullscreen();
            else if (root.dataset.immersive === 'true') root.dataset.immersive = 'false';
            else if (root.requestFullscreen) await root.requestFullscreen();
            else root.dataset.immersive = 'true';
        } catch { root.dataset.immersive = 'true'; notice('Fullscreen is unavailable. The immersive view remains open.'); }
        fullscreenChanged();
    });
    listen(document, 'fullscreenchange', fullscreenChanged);
    listen(document, 'keydown', event => { if (event.key === 'Escape' && !document.fullscreenElement) { root.dataset.immersive = 'false'; fullscreenChanged(); } });
    listen(window, 'storage', event => { if (event.key === key) { state = core.restore(event.newValue, Date.now()); render(); } });
    listen(document, 'visibilitychange', () => { render(); save(); });
    function startUpdates() { if (interval === null && !destroyed) interval = window.setInterval(render, 250); }
    function stopUpdates() { if (interval !== null) { window.clearInterval(interval); interval = null; } }
    listen(window, 'pagehide', () => { state = core.sync(state, Date.now()); save(); stopUpdates(); });
    listen(window, 'pageshow', () => { render(); startUpdates(); });
    window.__focusController = { get activeIntervals() { return interval === null ? 0 : 1; }, destroy() { destroyed = true; stopUpdates(); listeners.forEach(([target, event, handler]) => target.removeEventListener(event, handler)); delete window.__focusController; } };
    render(); save(); startUpdates();
})();
