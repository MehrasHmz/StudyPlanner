const { chromium, webkit } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { performance } = require('node:perf_hooks');
const base = process.env.STUDYPLANNER_ACCEPTANCE_URL || 'http://127.0.0.1:5821';
if (new URL(base).hostname !== '127.0.0.1') throw new Error('Use isolated loopback acceptance preview.');
const output = process.env.STUDYPLANNER_ACCEPTANCE_OUTPUT || '/private/tmp/studyplanner-phase10-acceptance';
fs.mkdirSync(output, { recursive: true });
let browser;
(async () => {
    const checks = [], timings = [], errors = [], engines = [];
    const check = (value, name) => { assert.ok(value, name); checks.push(name); };
    for (const engine of ['chrome', 'webkit']) {
        browser = engine === 'chrome' ? await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] }) : await webkit.launch();
        engines.push({ engine, version: await browser.version() });
        const page = await browser.newPage({ reducedMotion: 'reduce' }); page.on('pageerror', error => errors.push(error.message));
        for (const path of ['/', '/Auth/Login', '/Auth/Register']) {
            await page.goto(base + path);
            timings.push({ engine, path, ...await page.evaluate(() => {
                const navigation = performance.getEntriesByType('navigation')[0];
                return { domContentLoadedMs: navigation.domContentLoadedEventEnd, loadMs: navigation.loadEventEnd,
                    resourceTransferBytes: performance.getEntriesByType('resource').reduce((sum, entry) => sum + entry.transferSize, 0) };
            }) });
            for (const theme of ['dark', 'light']) {
                await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
                const ratio = await page.evaluate(() => {
                    const style = getComputedStyle(document.body);
                    const luminance = color => {
                        const rgb = color.match(/[\d.]+/g).slice(0, 3).map(Number).map(v => v / 255).map(v => v <= 0.04045 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
                        return rgb[0] * 0.2126 + rgb[1] * 0.7152 + rgb[2] * 0.0722;
                    };
                    const a = luminance(style.color), b = luminance(style.backgroundColor); return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
                });
                check(ratio >= 4.5, `${engine} ${path} ${theme}: body text contrast ${ratio.toFixed(2)}`);
                for (const width of [1440, 1024, 768, 390, 360]) {
                    await page.setViewportSize({ width, height: 900 });
                    check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${engine} ${path} ${theme} ${width}: no overflow`);
                }
            }
            // macOS WebKit uses Option+Tab for link-inclusive keyboard navigation.
            await page.keyboard.press(engine === 'webkit' ? 'Alt+Tab' : 'Tab');
            check(await page.locator(':focus').count() === 1, `${engine} ${path}: keyboard reaches control`);
            check(await page.locator(':focus').evaluate(element => {
                const style = getComputedStyle(element); return parseFloat(style.outlineWidth) > 0 || style.boxShadow !== 'none';
            }), `${engine} ${path}: visible focus indicator`);
            check(await page.evaluate(() => matchMedia('(prefers-reduced-motion: reduce)').matches && getComputedStyle(document.documentElement).scrollBehavior === 'auto'), `${engine} ${path}: reduced motion respected`);
        }
        await browser.close(); browser = null;
    }
    const requests = []; let next = 0;
    async function worker() {
        while (next < 80) {
            const index = next++; const path = ['/health/ready', '/Auth/Login', '/', '/health/live'][index % 4];
            const start = performance.now(); const response = await fetch(base + path); await response.arrayBuffer();
            assert.equal(response.status, 200); requests.push({ path, milliseconds: performance.now() - start });
        }
    }
    await Promise.all(Array.from({ length: 4 }, worker));
    const times = requests.map(r => r.milliseconds).sort((a, b) => a - b);
    check(requests.length === 80, 'Bounded 80-request, concurrency-4 local HTTP rehearsal passed');
    check(errors.length === 0, 'No JavaScript errors');
    const result = { checks, engines, errors, timings, http: { requests: 80, concurrency: 4, failures: 0, medianMs: times[39], p95Ms: times[75], maxMs: times.at(-1) } };
    fs.writeFileSync(`${output}/results.json`, JSON.stringify(result, null, 2));
    console.log(JSON.stringify({ checks: checks.length, engines, errors, http: result.http, output }));
})().catch(async error => { console.error(error); if (browser) await browser.close(); process.exitCode = 1; });
