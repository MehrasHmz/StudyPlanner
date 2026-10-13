const { chromium } = require('playwright');
const { execFileSync } = require('node:child_process');
const { randomUUID } = require('node:crypto');
const assert = require('node:assert/strict');
const fs = require('node:fs');

const base = process.env.STUDYPLANNER_REVIEW_TEST_URL || 'http://127.0.0.1:5816';
const database = process.env.STUDYPLANNER_REVIEW_TEST_DATABASE;
const socket = process.env.STUDYPLANNER_REVIEW_TEST_SOCKET;
if (new URL(base).hostname !== '127.0.0.1' || !/^studyplanner_phase6_browser_[a-z0-9_]+$/.test(database || '') || !socket?.startsWith('/private/tmp/studyplanner-'))
    throw new Error('Use an explicitly configured isolated Phase 6 browser database and loopback server.');
const output = process.env.STUDYPLANNER_REVIEW_TEST_OUTPUT || '/private/tmp/studyplanner-phase6-browser-evidence';
fs.mkdirSync(output, { recursive: true });
const sql = query => execFileSync('psql', ['-h', socket, '-p', '55439', '-U', 'studyplanner_test', '-d', database, '-v', 'ON_ERROR_STOP=1', '-At', '-c', query], { encoding: 'utf8' }).trim();
let browser;
(async () => {
    browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const page = await browser.newPage(); const checks = [], errors = []; let screenshots = 0;
    page.on('pageerror', error => errors.push(error.message));
    const check = (value, name) => { assert.ok(value, name); checks.push(name); };
    async function capture(name) {
        for (const width of [1440, 1024, 768, 390, 360]) {
            await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
            for (const theme of ['light', 'dark']) {
                await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
                await page.screenshot({ path: `${output}/${name}-${width}-${theme}.png`, fullPage: true, animations: 'disabled' }); screenshots++;
                check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} ${width} ${theme}: no page overflow`);
                check(await page.locator('main input:visible, main select:visible').evaluateAll(inputs => inputs.every(input => input.labels.length || input.getAttribute('aria-label'))), `${name} ${width} ${theme}: labeled controls`);
            }
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    }
    const email = `phase6-${Date.now()}@example.test`;
    await page.goto(base + '/Auth/Register');
    await page.locator('#registerName').fill('Phase 6 Browser Test'); await page.locator('#registerEmail').fill(email); await page.locator('#registerPassword').fill('Review-test-9!');
    await page.getByRole('button', { name: 'Create account' }).click(); await page.waitForURL('**/Auth/Login');
    await page.locator('[name="Email"]').fill(email); await page.locator('[name="Password"]').fill('Review-test-9!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
    const owner = sql(`SELECT "Id" FROM "Users" WHERE "Email" = '${email}'`);
    assert.match(owner, /^[a-f0-9-]{36}$/);
    const due = randomUUID(), history = randomUUID();
    const seeds = [
        [due, 'Completed review due', true, "now() - interval '1 day'", 5],
        [randomUUID(), 'Completed future review', true, "now() + interval '100 days'", 4],
        [randomUUID(), 'Completed without schedule', true, 'NULL', 3],
        [randomUUID(), 'Unfinished study', false, 'NULL', 1]
    ];
    // Controlled synthetic setup; never alter the user's original database or clock.
    for (const [id, title, completed, date, priority] of seeds)
        sql(`INSERT INTO "StudyItems" ("Id","Title","Description","Type","Difficulty","EstimatedDurationMinutes","Priority","NextReviewDate","IsCompleted","IsDeleted","CategoryId","UserId","CreatedAt") SELECT '${id}','${title}','',2,1,20,${priority},${date},${completed},false,"Id",'${owner}',now() FROM "Categories" WHERE NOT "IsDeleted" LIMIT 1`);
    sql(`INSERT INTO "ProgressRecords" ("Id","UserId","StudyItemId","CompletedAt","MinutesSpent","Notes","CreatedAt") VALUES ('${history}','${owner}','${due}',now()-interval '8 days',11,'Original history',now()-interval '8 days')`);
    const sessions = () => Number(sql(`SELECT count(*) FROM "StudySessions" WHERE "UserId"='${owner}'`));
    const progress = () => Number(sql(`SELECT count(*) FROM "ProgressRecords" WHERE "UserId"='${owner}'`));
    await page.goto(base + '/');
    check((await page.locator('.metric-value').allTextContents()).join(',') === '4,3,1,11', 'Dashboard completed, due and historical-minute counts');
    const dueRow = page.locator('.recent-item').filter({ hasText: 'Completed review due' });
    check((await dueRow.textContent()).includes('Completed') && (await dueRow.textContent()).includes('Review due'), 'Due review retains completed label');
    check(!(await page.locator('.recent-item').filter({ hasText: 'Completed future review' }).textContent()).includes('Review due'), 'Future completed review not due');
    await capture('dashboard');
    const before = sessions();
    await page.getByRole('link', { name: 'Plan session', exact: true }).click(); await page.waitForURL('**/Sessions');
    check(sessions() === before, 'Dashboard navigation creates no session');
    await page.goto(base + '/Sessions/Plan'); check(sessions() === before, 'Planning GET alias creates no session'); await capture('planning');
    await page.locator('#AvailableMinutes').evaluate(input => input.max = '1000'); await page.locator('#AvailableMinutes').fill('481');
    await page.getByRole('button', { name: 'Plan session', exact: true }).click();
    check(await page.locator('#durationError').textContent() === 'Choose between 1 and 480 minutes.', 'Server validates duration');
    check(await page.locator('#AvailableMinutes').inputValue() === '481' && sessions() === before, 'Invalid input retained without write'); await capture('planning-error');
    await page.locator('#AvailableMinutes').fill('20'); await page.getByRole('button', { name: 'Plan session', exact: true }).click();
    check(await page.locator('.session-result').count() === 1 && (await page.locator('.session-result').textContent()).includes('Completed review due'), 'Planner selects due completed review'); await capture('planned-session');
    await page.getByRole('button', { name: 'Complete session', exact: true }).click(); await page.locator('#confirmAction').click(); await page.waitForURL('**/Sessions');
    check(progress() === 2, 'Review adds one progress record while preserving history');
    check(sql(`SELECT "IsCompleted" AND "NextReviewDate" > now()+interval '6 days' AND "NextReviewDate" < now()+interval '8 days' FROM "StudyItems" WHERE "Id"='${due}'`) === 't', 'Completion preserved and next review advanced seven days');
    check(sql(`SELECT "MinutesSpent"||':'||"Notes" FROM "ProgressRecords" WHERE "Id"='${history}'`) === '11:Original history', 'Original historical record unchanged');
    await page.goto(base + '/StudyItems');
    const row = page.locator('tbody tr').filter({ hasText: 'Completed review due' });
    check((await row.textContent()).includes('Scheduled') && (await row.textContent()).includes('Completed') && !(await row.textContent()).includes('Review due'), 'Future review label after completion'); await capture('study-items');
    sql(`UPDATE "StudyItems" SET "NextReviewDate"=now()-interval '1 second' WHERE "Id"='${due}'`);
    await page.goto(base + '/Sessions/Plan'); await page.locator('#AvailableMinutes').fill('20'); await page.getByRole('button', { name: 'Plan session', exact: true }).click();
    check((await page.locator('.session-result').textContent()).includes('Completed review due'), 'Item returns for subsequent review');
    await page.getByRole('button', { name: 'Complete session', exact: true }).click(); await page.locator('#confirmAction').click(); await page.waitForURL('**/Sessions'); check(progress() === 3, 'Second review preserves both earlier records');
    await page.goto(base + `/Focus?itemId=${due}`); check(await page.locator('#focusExperience').count() === 1, 'Existing Focus navigation');
    await page.locator('#startPauseButton').click(); await page.waitForTimeout(1100); await page.reload();
    check((await page.locator('#startPauseButton').textContent()).includes('Pause'), 'Timer running state survives refresh');
    await page.locator('#startPauseButton').click(); check(progress() === 3, 'Focus timer does not record progress');
    check(errors.length === 0, 'No JavaScript errors');
    fs.writeFileSync(`${output}/results.json`, JSON.stringify({ checks, screenshots, errors, browser: await browser.version(), database }, null, 2));
    console.log(JSON.stringify({ checks: checks.length, screenshots, errors, output }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
