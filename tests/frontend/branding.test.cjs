const { chromium } = require('playwright');
const { PNG } = require('pngjs');
const assert = require('node:assert/strict');
const base = process.env.STUDYPLANNER_BRANDING_TEST_URL || 'http://localhost:5274';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(base).hostname), 'Use a local preview.');

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    let checks = 0;
    try {
        const page = await browser.newPage();
        for (const theme of ['dark', 'light']) {
            const expected = theme === 'dark' ? [180, 237, 133] : [48, 108, 54];
            await page.emulateMedia({ colorScheme: theme });
            await page.addInitScript(t => localStorage.setItem('studyplanner-theme', t), theme);
            for (const route of ['/', '/Auth/Login']) for (const width of [1440, 768, 390, 320]) {
                await page.setViewportSize({ width, height: 900 });
                await page.goto(base + route);
                const logo = page.locator('.brand-logo');
                const rect = await logo.boundingBox();
                assert.equal(rect.width, 34); assert.equal(rect.height, 34);
                assert.equal(await logo.evaluate(e => getComputedStyle(e).backgroundColor), `rgb(${expected.join(', ')})`);
                const pixels = PNG.sync.read(await logo.screenshot());
                let green = 0, purple = 0;
                for (let i = 0; i < pixels.data.length; i += 4) {
                    const [r, g, b] = pixels.data.subarray(i, i + 3);
                    if (g > r + 15 && g > b + 15) green++;
                    if (b > g + 30 && b > r + 15) purple++;
                }
                assert.ok(green > 20, `${route}/${theme}/${width}: visible green mark`);
                assert.equal(purple, 0, 'No old purple pixels');
                assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'No horizontal overflow');
                await page.screenshot({ path: `/private/tmp/studyplanner-logo-${theme}-${width}-${route === '/' ? 'home' : 'login'}.png` });
                checks++;
            }
            const favicon = await page.evaluate(async () => {
                const icon = document.querySelector('link[rel="icon"]');
                const img = new Image(); img.src = icon.href; await img.decode();
                const canvas = document.createElement('canvas'); canvas.width = canvas.height = 64;
                const ctx = canvas.getContext('2d'); ctx.drawImage(img, 0, 0, 64, 64);
                return Array.from(ctx.getImageData(0, 0, 64, 64).data);
            });
            let opaque = 0;
            for (let i = 0; i < favicon.length; i += 4) if (favicon[i + 3] > 200) {
                assert.ok(expected.every((c, j) => Math.abs(c - favicon[i + j]) <= 3), 'Green favicon pixels'); opaque++;
            }
            assert.ok(opaque > 100, 'Favicon is not blank'); checks++;
        }
        console.log(`PASS: ${checks} branding cases; green logo and favicon pixels, both themes, four widths`);
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
