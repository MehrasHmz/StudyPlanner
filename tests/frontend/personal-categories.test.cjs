const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const crypto = require('node:crypto');
const base = process.env.STUDYPLANNER_PERSONAL_TEST_URL;
const database = process.env.STUDYPLANNER_PERSONAL_TEST_DATABASE;
if (!base || new URL(base).hostname !== '127.0.0.1' || !/^studyplanner_phase(?:9|10)_browser_[a-z0-9_]+$/.test(database || ''))
    throw new Error('Explicitly select a loopback preview backed by an isolated synthetic database.');
const output = process.env.STUDYPLANNER_PERSONAL_TEST_OUTPUT || '/private/tmp/studyplanner-personal-category-evidence';
fs.mkdirSync(output, { recursive: true });

(async () => {
    const browser = await chromium.launch({ channel: 'chrome', headless: true });
    const checks = [], errors = [];
    const check = (value, name) => { assert.ok(value, name); checks.push(name); };
    try {
        const owner = await browser.newPage(), other = await browser.newPage();
        for (const page of [owner, other]) page.on('pageerror', e => errors.push(e.message));
        async function register(page, suffix) {
            const email = `personal-${Date.now()}-${suffix}@example.test`, password = `Test!${crypto.randomBytes(12).toString('hex')}`;
            await page.goto(base + '/Auth/Register');
            await page.locator('#registerName').fill('Personal category ' + suffix);
            await page.locator('#registerEmail').fill(email); await page.locator('#registerPassword').fill(password);
            await page.getByRole('button', { name: 'Create account', exact: true }).click(); await page.waitForURL('**/Auth/Login');
            await page.locator('#loginEmail').fill(email); await page.locator('#loginPassword').fill(password);
            await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
        }
        async function create(page, name) {
            await page.goto(base + '/Categories/Create');
            check(await page.locator('#IsShared').count() === 0, 'Regular user has no shared creation option');
            await page.locator('#Name').fill(name); await page.locator('#Description').fill('Private topics');
            await page.getByRole('button', { name: 'Create category', exact: true }).click(); await page.waitForURL('**/Categories');
            const card = page.locator('.category-card').filter({ hasText: name });
            check(await card.count() === 1, 'Personal category appears');
            check(await card.getByText('Personal', { exact: true }).count() === 1, 'Personal scope is visible');
            return (await card.locator('a[href*="/Categories/Edit/"]').getAttribute('href')).split('/').pop();
        }
        const token = async page => page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
        await register(owner, 'owner'); await register(other, 'other');
        const name = 'Personal ' + Date.now(), id = await create(owner, name);
        await other.goto(base + '/Categories');
        check(!await other.getByRole('heading', { name, exact: true }).count(), 'Other account does not see private category');
        check(!(await (await other.request.get(base + '/api/categories/list')).text()).includes(id), 'API hides foreign category');
        check((await other.request.get(base + '/Categories/Edit/' + id)).status() === 404, 'Foreign edit returns 404');
        const otherId = await create(other, name);
        check(otherId !== id, 'Different users can use the same category name');
        check((await other.request.post(base + '/Categories/Delete', { form: { id, __RequestVerificationToken: await token(other) }, maxRedirects: 0 })).status() === 404, 'Forged deletion denied');
        check((await other.request.post(base + '/Categories/Create', { form: { Name: 'No CSRF' }, maxRedirects: 0 })).status() === 400, 'CSRF remains mandatory');
        await other.goto(base + '/StudyItems/Create');
        check(await other.locator(`#CategoryId option[value="${id}"]`).count() === 0, 'Foreign category absent from item selector');
        const forged = await other.request.post(base + '/StudyItems/Create', { form: {
            Title: 'Forbidden foreign selection', Description: '', Type: 'Other', Difficulty: 'Beginner', Priority: '3',
            EstimatedDurationMinutes: '30', CategoryId: id, __RequestVerificationToken: await token(other)
        }, maxRedirects: 0 });
        check(forged.status() === 200 && (await forged.text()).includes('Choose an available category'), 'Server rejects forged category selection');
        await owner.goto(base + '/StudyItems/Create?categoryId=' + id);
        check(await owner.locator('#CategoryId').inputValue() === id, 'Owned category preselected');
        const title = 'Preserved personal item ' + Date.now();
        await owner.locator('#Title').fill(title); await owner.getByRole('button', { name: 'Create Item', exact: true }).click();
        await owner.waitForURL('**/StudyItems');
        const itemRow = owner.locator('tbody tr').filter({ hasText: title });
        const itemEdit = await itemRow.locator('a[href*="/StudyItems/Edit/"]').getAttribute('href');
        await owner.goto(base + '/Categories/Edit/' + id);
        const renamed = name + ' renamed'; await owner.locator('#Name').fill(renamed);
        await owner.getByRole('button', { name: 'Save category' }).click(); await owner.waitForURL('**/Categories');
        for (const theme of ['dark', 'light']) for (const width of [1440, 1024, 768, 390, 320]) {
            await owner.setViewportSize({ width, height: 900 });
            await owner.evaluate(t => { document.documentElement.dataset.theme = t; document.documentElement.dataset.bsTheme = t; }, theme);
            await owner.waitForTimeout(200);
            check(await owner.evaluate(() => document.documentElement.scrollWidth <= innerWidth), `${theme}/${width}: no overflow`);
            check(await owner.locator('main .item-action').evaluateAll(es => es.every(e => e.getAttribute('aria-label'))), `${theme}/${width}: labeled actions`);
            await owner.screenshot({ path: `${output}/categories-${theme}-${width}.png`, fullPage: true });
        }
        await owner.setViewportSize({ width: 1440, height: 900 });
        const card = owner.locator('.category-card').filter({ hasText: renamed });
        await card.getByRole('button', { name: 'Delete ' + renamed, exact: true }).click();
        await owner.locator('#confirmAction').click(); await owner.waitForURL('**/Categories');
        check(await owner.getByRole('heading', { name: renamed, exact: true }).count() === 0, 'Referenced personal category archived');
        await owner.goto(base + '/StudyItems');
        check(await owner.locator('tbody tr').filter({ hasText: title }).count() === 1, 'Study item survives category deletion');
        await owner.goto(base + itemEdit);
        await owner.locator('#Title').fill(title + ' edited'); await owner.getByRole('button', { name: 'Save Changes' }).click();
        await owner.waitForURL('**/StudyItems');
        check(await owner.locator('tbody tr').filter({ hasText: title + ' edited' }).count() === 1, 'Item with archived category remains editable');
        const replacement = await create(owner, renamed);
        check(replacement !== id, 'Deleted category name can be reused without restoring old references');
        await other.goto(base + '/Categories');
        check(await other.getByRole('heading', { name, exact: true }).count() === 1, 'Other users category remains unchanged');
        check(errors.length === 0, 'No JavaScript errors');
        fs.writeFileSync(`${output}/results.json`, JSON.stringify({ checks, errors }, null, 2));
        console.log(JSON.stringify({ checks: checks.length, screenshots: 10, errors, output }));
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
