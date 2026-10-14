const test = require('node:test');
const assert = require('node:assert/strict');
const timer = require('../../src/StudyPlanner.Web/wwwroot/js/focus-timer.js');

test('countdown starts with a timestamp and correct duration', () => {
    const state = timer.start(timer.create(), 1000);
    assert.equal(state.status, 'running'); assert.equal(state.startedAt, 1000);
    assert.equal(timer.milliseconds(state, 1000), 25 * 60000);
    assert.equal(timer.milliseconds(state, 21000), 25 * 60000 - 20000);
});
test('pause freezes remaining time even after a long absence', () => {
    const state = timer.pause(timer.start(timer.create(), 1000), 21000);
    assert.equal(state.status, 'paused'); assert.equal(state.startedAt, null);
    assert.equal(timer.milliseconds(state, 99999999), 25 * 60000 - 20000);
});
test('resume excludes paused time', () => {
    let state = timer.pause(timer.start(timer.create(), 1000), 21000);
    state = timer.start(state, 101000);
    assert.equal(timer.milliseconds(state, 111000), 25 * 60000 - 30000);
});
test('reset preserves task, duration and mode while removing timing references', () => {
    const state = timer.reset(timer.start(timer.create({ task: 'Networks', minutes: 50 }), 1000));
    assert.equal(state.task, 'Networks'); assert.equal(state.durationMs, 50 * 60000);
    assert.equal(state.elapsedMs, 0); assert.equal(state.status, 'ready'); assert.equal(state.startedAt, null);
});
test('stopwatch measures elapsed wall time', () => {
    const state = timer.start(timer.create({ mode: 'stopwatch' }), 1000);
    assert.equal(timer.milliseconds(state, 91000), 90000);
    assert.equal(timer.format(timer.milliseconds(state, 91000), false), '00:01:30');
});
test('delayed callbacks and background throttling have no interval-based drift', () => {
    const state = timer.start(timer.create({ minutes: 1 }), 1000);
    assert.equal(timer.sync(state, 71000).status, 'completed');
    assert.equal(timer.milliseconds(state, 71000), 0);
});
test('refresh restores a running timer using saved time references', () => {
    const state = timer.start(timer.create(), 1000);
    const restored = timer.restore(JSON.stringify(state), 241000);
    assert.equal(restored.status, 'running'); assert.equal(restored.elapsedMs, 240000);
    assert.equal(timer.milliseconds(restored, 241000), 21 * 60000);
});
test('countdown completes while the page is inactive', () => {
    const restored = timer.restore(JSON.stringify(timer.start(timer.create({ minutes: 1 }), 1000)), 121000);
    assert.equal(restored.status, 'completed'); assert.equal(restored.startedAt, null);
    assert.equal(timer.milliseconds(restored, 121000), 0);
});
test('mode switching preserves running time and keeps counting in both directions', () => {
    let state = timer.setMode(timer.start(timer.create(), 1000), 'stopwatch', 21000);
    assert.equal(state.status, 'running'); assert.equal(state.startedAt, 21000); assert.equal(state.elapsedMs, 20000);
    assert.equal(timer.milliseconds(state, 31000), 30000);
    state = timer.setMode(state, 'countdown', 41000);
    assert.equal(state.status, 'running'); assert.equal(state.elapsedMs, 40000);
    assert.equal(timer.milliseconds(state, 51000), 25 * 60000 - 50000);
});
test('switching paused modes preserves time without including paused wall time', () => {
    let state = timer.pause(timer.start(timer.create({ mode: 'stopwatch', task: 'Networks' }), 1000), 21000);
    state = timer.setMode(state, 'countdown', 121000);
    assert.equal(state.status, 'paused'); assert.equal(state.startedAt, null); assert.equal(state.elapsedMs, 20000);
    assert.equal(state.task, 'Networks');
    state = timer.setMode(state, 'stopwatch', 221000);
    assert.equal(timer.milliseconds(state, 321000), 20000);
});
test('a switched running timer survives serialization and refresh', () => {
    const state = timer.setMode(timer.start(timer.create(), 1000), 'stopwatch', 21000);
    const restored = timer.restore(JSON.stringify(state), 41000);
    assert.equal(restored.mode, 'stopwatch'); assert.equal(restored.status, 'running'); assert.equal(restored.elapsedMs, 40000);
});
test('switching a finished countdown to stopwatch requires explicit resume', () => {
    const state = timer.setMode(timer.start(timer.create({ minutes: 1 }), 1000), 'stopwatch', 71000);
    assert.equal(state.status, 'paused'); assert.equal(state.startedAt, null); assert.equal(state.elapsedMs, 60000);
    assert.equal(timer.milliseconds(timer.start(state, 81000), 91000), 70000);
});
test('switching an over-target stopwatch to countdown preserves elapsed time through refresh and back', () => {
    let state = timer.setMode(timer.start(timer.create({ mode: 'stopwatch', minutes: 1 }), 1000), 'countdown', 91000);
    assert.equal(state.status, 'completed'); assert.equal(state.startedAt, null); assert.equal(state.elapsedMs, 90000);
    assert.equal(timer.milliseconds(state, 101000), 0);
    state = timer.restore(JSON.stringify(state), 111000);
    assert.equal(state.elapsedMs, 90000);
    state = timer.setMode(state, 'stopwatch', 121000);
    assert.equal(state.status, 'paused'); assert.equal(timer.milliseconds(state, 131000), 90000);
});
test('same-mode changes and repeated round trips never restart the timer', () => {
    let state = timer.start(timer.create(), 1000);
    for (let i = 1; i <= 10; i++) state = timer.setMode(state, i % 2 ? 'stopwatch' : 'countdown', 1000 + i * 1000);
    state = timer.setMode(state, 'countdown', 12000);
    assert.equal(state.status, 'running'); assert.equal(state.elapsedMs, 11000);
});
test('invalid modes are rejected without mutating the current session', () => {
    const state = timer.start(timer.create(), 1000);
    assert.throws(() => timer.setMode(state, 'invalid', 21000), RangeError);
    assert.equal(state.elapsedMs, 0); assert.equal(state.startedAt, 1000);
});
test('countdown never displays negative remaining time', () => {
    const state = timer.start(timer.create({ minutes: 1 }), 1000);
    assert.equal(timer.milliseconds(state, 999999999), 0); assert.equal(timer.format(-12345), '00:00:00');
});
test('backwards clock changes do not reduce accumulated elapsed time', () => {
    let state = timer.sync(timer.start(timer.create({ mode: 'stopwatch' }), 1000), 21000);
    state = timer.sync(state, 11000);
    assert.equal(state.elapsedMs, 20000);
    assert.equal(timer.sync(state, 16000).elapsedMs, 25000);
});
test('start is idempotent while running and restart after completion begins a new session', () => {
    const initial = timer.start(timer.create({ minutes: 1 }), 1000);
    assert.equal(timer.start(initial, 2000).elapsedMs, 1000);
    const restarted = timer.start(timer.sync(initial, 71000), 80000);
    assert.equal(restarted.status, 'running'); assert.equal(restarted.elapsedMs, 0);
});
test('custom durations validate integer limits', () => {
    for (const minutes of [0, -1, 481, 1.5, NaN, Infinity]) assert.throws(() => timer.setDuration(timer.create(), minutes), RangeError);
    assert.equal(timer.setDuration(timer.create(), 480).durationMs, 480 * 60000);
    assert.equal(timer.setDuration(timer.create(), 1).durationMs, 60000);
});
test('task strings remain plain strings and bounded in length', () => {
    assert.equal(timer.cleanTask('<img src=x onerror=alert(1)>'), '<img src=x onerror=alert(1)>');
    assert.equal(timer.cleanTask('x'.repeat(1000)).length, 200);
    assert.equal(timer.cleanTask('   '), 'A little focused time');
});
for (const [label, value] of [['bad-json', '{'], ['null', 'null'], ['wrong-version', JSON.stringify({ version: 99 })],
    ['negative-time', JSON.stringify({ ...timer.create(), elapsedMs: -1 })],
    ['invalid-reference', JSON.stringify({ ...timer.create(), status: 'running', startedAt: null })]]) {
    test('corrupt persistence safely resets: ' + label, () => assert.deepEqual(timer.restore(value, 1000), timer.create()));
}
