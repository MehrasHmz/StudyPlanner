const { chromium } = require('playwright');
const { execFileSync } = require('node:child_process');
const assert = require('node:assert/strict');

// Only run against an explicitly named disposable Compose project.
const project = process.env.STUDYPLANNER_DOCKER_TEST_PROJECT;
if (!project || !/^studyplanner-(verify|ci)-[a-z0-9-]+$/.test(project)) throw new Error('Set a disposable verification Compose project.');
const base = process.env.STUDYPLANNER_DOCKER_TEST_URL || 'http://127.0.0.1:8080';
if (new URL(base).hostname !== '127.0.0.1') throw new Error('Docker smoke tests require loopback.');

let browser;
(async () => {
    browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    const ready = await page.request.get(base + '/health/ready'); assert.equal(ready.status(), 200);
    await page.goto(base + '/Auth/Register');
    const email = `docker-${Date.now()}@example.test`;
    await page.locator('#registerName').fill('Docker Persistence Test');
    await page.locator('#registerEmail').fill(email);
    await page.locator('#registerPassword').fill('Docker-test-9!');
    await page.getByRole('button', { name: 'Create account' }).click(); await page.waitForURL('**/Auth/Login');
    await page.locator('[name="Email"]').fill(email); await page.locator('[name="Password"]').fill('Docker-test-9!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
    assert.ok(await page.locator('.nav-user').count());
    execFileSync('docker', ['compose', '-p', project, 'up', '-d', '--no-deps', '--force-recreate', 'api'], { stdio: 'pipe' });
    let healthy = false;
    for (let attempt = 0; attempt < 45; attempt++) {
        try { healthy = (await page.request.get(base + '/health/ready')).status() === 200; } catch { /* Container replacement temporarily closes the port. */ }
        if (healthy) break;
        await new Promise(resolve => setTimeout(resolve, 1000));
    }
    assert.ok(healthy, 'Readiness recovers after recreation');
    await page.goto(base + '/');
    assert.ok((await page.locator('.nav-user').textContent()).includes('Docker Persistence Test'), 'Authentication cookie survives recreation');
    await page.goto(base + '/Focus');
    assert.ok(await page.locator('#focusExperience').count());
    assert.equal(errors.length, 0);
    console.log(JSON.stringify({ readiness: true, registrationAndLogin: true, cookieSurvivesRecreation: true, focus: true, javascriptErrors: errors.length }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
