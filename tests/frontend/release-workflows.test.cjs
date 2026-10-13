const { chromium } = require('playwright');
const { execFileSync } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const assert = require('node:assert/strict');
const fs = require('node:fs');

const base = process.env.STUDYPLANNER_RELEASE_TEST_URL || 'http://127.0.0.1:5818';
const database = process.env.STUDYPLANNER_RELEASE_TEST_DATABASE;
const socket = process.env.STUDYPLANNER_RELEASE_TEST_SOCKET;
if (new URL(base).hostname !== '127.0.0.1' || !/^studyplanner_phase8_browser_[a-z0-9_]+$/.test(database || '') || !socket?.startsWith('/private/tmp/studyplanner-'))
    throw new Error('Configure an isolated Phase 8 browser database and loopback server.');
const output = process.env.STUDYPLANNER_RELEASE_TEST_OUTPUT || '/private/tmp/studyplanner-phase8-browser-evidence';
fs.mkdirSync(output, { recursive: true });
const sql = query => execFileSync('psql', ['-h', socket, '-p', '55439', '-U', 'studyplanner_test', '-d', database, '-v', 'ON_ERROR_STOP=1', '-At', '-c', query], { encoding: 'utf8' }).trim();
let browser;
(async () => {
    browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const page = await browser.newPage(); const checks = [], errors = []; let screenshots = 0;
    page.on('pageerror', error => errors.push(error.message));
    const check = (value, name) => { assert.ok(value, name); checks.push(name); };
    async function capture(name) {
        for (const width of [1440, 768, 390, 360]) {
            await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
            for (const theme of ['light', 'dark']) {
                await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
                await page.screenshot({ path: `${output}/${name}-${width}-${theme}.png`, fullPage: true, animations: 'disabled' }); screenshots++;
                check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} ${width} ${theme}: no page overflow`);
                check(await page.locator('main input:visible, main select:visible, main textarea:visible').evaluateAll(inputs => inputs.every(input => input.labels.length || input.getAttribute('aria-label'))), `${name} ${width} ${theme}: labeled controls`);
                check(await page.locator('img').evaluateAll(imgs => imgs.every(img => img.complete && img.naturalWidth > 0)), `${name} ${width} ${theme}: images rendered`);
            }
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    }
    await page.goto(base + '/'); await capture('landing');
    const email = `release-${Date.now()}@example.test`, password = 'Release-test-9!';
    await page.goto(base + '/Auth/Register'); await capture('registration');
    await page.locator('#registerName').fill('Release Workflow Test'); await page.locator('#registerEmail').fill(email); await page.locator('#registerPassword').fill(password);
    await page.getByRole('button', { name: 'Create account' }).click(); await page.waitForURL('**/Auth/Login'); await capture('login');
    await page.locator('[name="Email"]').fill(email); await page.locator('[name="Password"]').fill(password);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
    const owner = sql(`SELECT "Id" FROM "Users" WHERE "Email"='${email}'`); assert.match(owner, /^[a-f0-9-]{36}$/);
    const ids = [randomUUID(), randomUUID()];
    for (let i = 0; i < 2; i++) sql(`INSERT INTO "StudyItems" ("Id","Title","Description","Type","Difficulty","EstimatedDurationMinutes","Priority","IsCompleted","IsDeleted","CategoryId","UserId","CreatedAt") SELECT '${ids[i]}','Release study ${i}','',2,1,20,${5-i},false,false,"Id",'${owner}',now() FROM "Categories" WHERE NOT "IsDeleted" LIMIT 1`);
    const progress = () => Number(sql(`SELECT count(*) FROM "ProgressRecords" WHERE "UserId"='${owner}'`));
    const sessions = () => Number(sql(`SELECT count(*) FROM "StudySessions" WHERE "UserId"='${owner}'`));
    await page.goto(base + '/'); await capture('dashboard');
    await page.getByRole('link', { name: 'Study history', exact: true }).click(); await capture('history-empty');
    check((await page.locator('main').textContent()).includes('No sessions on this page.'), 'Empty history');
    await page.goto(base + '/Sessions'); check(sessions() === 0, 'Planning GET writes nothing'); await page.locator('#AvailableMinutes').fill('45');
    await page.getByRole('button', { name: 'Plan session', exact: true }).click();
    check(await page.locator('.session-result').count() === 2, 'Two planned activities');
    check(await page.locator('#minutes-0').inputValue() === '' && await page.locator('#minutes-1').inputValue() === '', 'No planned estimate prefilled as actual');
    const session = await page.locator('[name="SessionId"]').inputValue(); assert.match(session, /^[a-f0-9-]{36}$/);
    await capture('completion');
    await page.locator('#include-0').focus(); await page.keyboard.press('Space'); check(await page.locator('#minutes-0').isDisabled(), 'Keyboard omission disables first row');
    await page.locator('#minutes-1').fill('7'); await page.locator('#score-1').evaluate(input => input.max = '2'); await page.locator('#score-1').fill('1.2');
    await page.locator('#notes-1').fill('Private <b>study notes</b>\nSecond line');
    await page.getByRole('button', { name: 'Complete session', exact: true }).click(); await page.locator('#confirmAction').click();
    await page.locator('[data-valmsg-for="Results[1].Score"]').filter({ hasText: 'Score must be between 0 and 1.' }).waitFor();
    check(await page.locator('#minutes-1').inputValue() === '7' && await page.locator('#score-1').inputValue() === '1.2' && (await page.locator('#notes-1').inputValue()).includes('Private <b>'), 'Validation retains submitted data');
    check(progress() === 0 && await page.locator('#minutes-0').isDisabled(), 'Invalid submission writes nothing and retains omitted row'); await capture('completion-error');
    await page.locator('#score-1').fill('0.75'); await page.getByRole('button', { name: 'Complete session', exact: true }).click(); await page.locator('#confirmAction').click(); await page.waitForURL('**/Sessions');
    check(progress() === 1, 'Only submitted subset recorded');
    check(sql(`SELECT "IsCompleted" FROM "StudyItems" WHERE "Id"='${ids[0]}'`) === 'f', 'Omitted item remains unfinished');
    check(sql(`SELECT "MinutesSpent"||':'||"ResultScore" FROM "ProgressRecords" WHERE "UserId"='${owner}'`) === '7:0.75', 'Actual time and score persisted');
    await page.goto(base + `/Sessions/Details/${session}`); await capture('details');
    check((await page.locator('main').textContent()).includes('40 min') && (await page.locator('main').textContent()).includes('7 min'), 'Planned and recorded totals distinct');
    check((await page.locator('.history-notes').textContent()).includes('Private <b>study notes</b>') && await page.locator('.history-notes b').count() === 0, 'Private notes safely encoded');
    await page.goto(base + '/Sessions/History'); await capture('history'); check((await page.locator('main').textContent()).includes('7 min'), 'History records actual total');
    for (let i = 0; i < 12; i++) sql(`INSERT INTO "StudySessions" ("Id","UserId","PlannedDate","AvailableMinutes","Status","TotalPlannedItems","TotalPlannedMinutes","CreatedAt") VALUES ('${randomUUID()}','${owner}',now()+interval '${i+1} minutes',45,0,0,0,now())`);
    await page.goto(base + '/Sessions/History'); check(await page.locator('tbody tr').count() === 10, 'History page bounded to ten');
    await page.getByRole('link', { name: 'Next history page' }).click(); check(await page.locator('tbody tr').count() === 3, 'Second history page');
    await page.goto(base + '/Categories'); check(await page.locator('.category-card').count() > 0, 'Category navigation');
    await page.goto(base + '/StudyItems'); check(!(await page.locator('main').textContent()).includes('Private <b>study notes'), 'Notes not exposed in study-item/category views');
    await page.goto(base + `/Focus?itemId=${ids[1]}`); await page.locator('#startPauseButton').click(); await page.waitForTimeout(1100); await page.reload();
    check((await page.locator('#startPauseButton').textContent()).includes('Pause'), 'Focus persistence'); await page.locator('#startPauseButton').click();
    check(progress() === 1, 'Focus independent from backend progress'); await capture('focus');
    await page.goto(base + '/'); check((await page.locator('.metric-value').allTextContents()).at(-1) === '7', 'Dashboard uses stored minutes, not planned duration');
    await page.locator('.nav-user').click(); await page.getByRole('button', { name: 'Sign out', exact: true }).click();
    await page.goto(base + '/Sessions/History'); check(page.url().includes('/Auth/Login'), 'Logout protects history');
    check(errors.length === 0, 'No JavaScript errors');
    fs.writeFileSync(`${output}/results.json`, JSON.stringify({ checks, screenshots, errors, browser: await browser.version(), database }, null, 2));
    console.log(JSON.stringify({ checks: checks.length, screenshots, errors, output }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
