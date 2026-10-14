(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    else root.FocusTimer = api;
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';
    const MINUTE = 60000, MAX_ELAPSED = 365 * 24 * 60 * MINUTE;
    const validDuration = minutes => Number.isInteger(minutes) && minutes >= 1 && minutes <= 480;
    const cleanTask = task => typeof task === 'string' ? task.trim().slice(0, 200) || 'A little focused time' : 'A little focused time';
    function create(options = {}) {
        return { version: 1, mode: options.mode === 'stopwatch' ? 'stopwatch' : 'countdown', task: cleanTask(options.task),
            durationMs: (validDuration(options.minutes) ? options.minutes : 25) * MINUTE, elapsedMs: 0,
            startedAt: null, status: 'ready', showClock: true, entryItemId: null };
    }
    function sync(state, now) {
        const next = { ...state };
        if (next.status === 'running') {
            // Wall time, not interval callbacks, is the elapsed-time source; backwards jumps never reduce accumulated time.
            next.elapsedMs = Math.min(MAX_ELAPSED, next.elapsedMs + Math.max(0, now - next.startedAt));
            next.startedAt = now;
            if (next.mode === 'countdown' && next.elapsedMs >= next.durationMs) {
                next.elapsedMs = next.durationMs; next.status = 'completed'; next.startedAt = null;
            }
        }
        return next;
    }
    function start(state, now) {
        const next = state.status === 'completed' ? reset(state) : sync(state, now);
        if (next.status === 'running') return next;
        return { ...next, status: 'running', startedAt: now };
    }
    function pause(state, now) {
        const next = sync(state, now);
        return next.status === 'running' ? { ...next, status: 'paused', startedAt: null } : next;
    }
    function reset(state) { return { ...state, elapsedMs: 0, startedAt: null, status: 'ready' }; }
    function setMode(state, mode, now = Date.now()) {
        if (!['countdown', 'stopwatch'].includes(mode)) throw new RangeError('Invalid timer mode.');
        const next = { ...sync(state, now), mode };
        if (mode === 'countdown' && next.elapsedMs >= next.durationMs) {
            next.status = 'completed'; next.startedAt = null;
        } else if (mode === 'stopwatch' && next.status === 'completed') {
            // Switching views must not silently resume a finished session.
            next.status = 'paused'; next.startedAt = null;
        }
        return next;
    }
    function setDuration(state, minutes) {
        if (!validDuration(minutes)) throw new RangeError('Choose 1 to 480 whole minutes.');
        return reset({ ...state, durationMs: minutes * MINUTE });
    }
    function restore(serialized, now) {
        try {
            const value = JSON.parse(serialized);
            if (!value || value.version !== 1 || !['countdown', 'stopwatch'].includes(value.mode) ||
                !['ready', 'running', 'paused', 'completed'].includes(value.status) ||
                !validDuration(value.durationMs / MINUTE) || typeof value.task !== 'string' ||
                !Number.isFinite(value.elapsedMs) || value.elapsedMs < 0 || value.elapsedMs > MAX_ELAPSED ||
                (value.status === 'running' && (!Number.isFinite(value.startedAt) || value.startedAt < 0)) ||
                (value.mode === 'stopwatch' && value.status === 'completed')) return create();
            const state = { ...create(), mode: value.mode, task: cleanTask(value.task), durationMs: value.durationMs,
                elapsedMs: value.elapsedMs, status: value.status, startedAt: value.status === 'running' ? value.startedAt : null,
                showClock: value.showClock !== false, entryItemId: typeof value.entryItemId === 'string' ? value.entryItemId.slice(0, 36) : null };
            if (state.status === 'completed') state.elapsedMs = Math.max(state.elapsedMs, state.durationMs);
            return sync(state, now);
        } catch { return create(); }
    }
    function milliseconds(state, now) {
        const current = sync(state, now);
        return current.mode === 'countdown' ? Math.max(0, current.durationMs - current.elapsedMs) : current.elapsedMs;
    }
    function format(milliseconds, countdown = true) {
        const seconds = Math.max(0, countdown ? Math.ceil(milliseconds / 1000) : Math.floor(milliseconds / 1000));
        return [Math.floor(seconds / 3600), Math.floor(seconds / 60) % 60, seconds % 60]
            .map(value => String(value).padStart(2, '0')).join(':');
    }
    return { create, sync, start, pause, reset, setMode, setDuration, restore, milliseconds, format, cleanTask };
});
