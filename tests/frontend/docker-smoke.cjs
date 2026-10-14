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
    const sql = query => execFileSync('docker', ['compose', '-p', project, 'exec', '-T', 'postgres', 'sh', '-c',
        'exec psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -v ON_ERROR_STOP=1 -c "$1"', 'sh', query], { encoding: 'utf8' }).trim();
    const id = sql(`SELECT "Id" FROM "Users" WHERE "Email"='${email}'`);
    assert.match(id, /^[a-f0-9-]{36}$/);
    const provision = role => execFileSync('docker', ['compose', '-p', project, 'run', '--rm', '--no-deps', 'api',
        '--manage-user-role', '--user-id', id, '--role', role, '--operator', 'docker-smoke-operator', '--reason', 'Synthetic container authorization verification', '--confirm'], { stdio: 'pipe' });
    assert.equal((await page.request.get(base + '/Categories/Create')).status(), 403);
    provision('administrator');
    assert.equal((await page.request.get(base + '/Categories/Create')).status(), 200);
    provision('regular');
    assert.equal((await page.request.get(base + '/Categories/Create')).status(), 403);
    assert.equal(sql(`SELECT count(*) FROM "RoleChangeAudits" WHERE "UserId"='${id}'`), '2');
    await page.goto(base + '/Focus');
    assert.ok(await page.locator('#focusExperience').count());
    assert.equal(errors.length, 0);
    console.log(JSON.stringify({ readiness: true, registrationAndLogin: true, cookieSurvivesRecreation: true, operatorGrant: true, immediateRevocation: true, durableAudit: true, focus: true, javascriptErrors: errors.length }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
