const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// This workflow creates synthetic records in the isolated local preview database.
const base = process.env.STUDYPLANNER_FORMS_TEST_URL || 'http://127.0.0.1:5815';
if (new URL(base).hostname !== '127.0.0.1') throw new Error('Use a loopback synthetic preview.');
const output = process.env.STUDYPLANNER_FORMS_TEST_OUTPUT || '/private/tmp/studyplanner-forms-20261011';
fs.mkdirSync(output, { recursive: true });
let browser;
(async () => {
    browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const page = await browser.newPage();
    const errors = [], results = [];
    page.on('pageerror', error => errors.push(error.message));
    const check = (value, name) => { assert.ok(value, name); results.push(name); };
    async function capture(name) {
        for (const width of [1440, 1024, 768, 390, 360]) {
            await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
            for (const theme of ['dark', 'light']) {
                await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
                await page.screenshot({ path: `${output}/${name}-${width}-${theme}.png`, fullPage: true, animations: 'disabled' });
                check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} ${width} ${theme}: no overflow`);
                check(await page.locator('main input:visible, main select:visible, main textarea:visible').evaluateAll(controls => controls.every(control => control.labels.length || control.getAttribute('aria-label'))), `${name} ${width} ${theme}: labeled fields`);
            }
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    }
    await page.goto(base + '/Auth/Register');
    const stamp = Date.now(), email = `study-forms-${stamp}@example.test`;
    await page.locator('#registerName').fill('Study Forms Review');
    await page.locator('#registerEmail').fill(email);
    await page.locator('#registerPassword').fill('Forms-review-9!');
    await page.getByRole('button', { name: 'Create account' }).click(); await page.waitForURL('**/Auth/Login');
    await page.locator('[name="Email"]').fill(email); await page.locator('[name="Password"]').fill('Forms-review-9!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
    await page.goto(base + '/Categories/Create'); await capture('category-create');
    check(await page.locator('#Description').getAttribute('data-val-required') === null, 'Optional category notes have no required metadata');
    const name = `Networks ${stamp}`;
    await page.locator('#Name').fill(name);
    await page.locator('#Description').fill('Routing and transport protocols\nPractical lab exercises');
    check((await page.locator('[data-count-for="Description"]').textContent()).endsWith('/ 500'), 'Category character counter');
    await page.getByRole('button', { name: 'Create category', exact: true }).click(); await page.waitForURL('**/Categories');
    const card = page.locator('.category-card').filter({ hasText: name });
    const editUrl = await card.locator('a[href*="Edit"]').getAttribute('href');
    const createUrl = await card.locator('a[href*="StudyItems/Create"]').getAttribute('href');
    await capture('categories');
    await page.goto(base + editUrl); await capture('category-edit');
    await page.locator('#Name').fill(name + ' edited');
    await page.locator('#Description').evaluate(element => element.removeAttribute('maxlength'));
    await page.locator('#Description').fill('x'.repeat(501));
    await page.getByRole('button', { name: 'Save category' }).click();
    check(await page.locator('.validation-summary-errors').isVisible(), 'Category server validation visible');
    check(await page.locator('#Name').inputValue() === name + ' edited', 'Category name preserved on error');
    check(await page.locator('[data-valmsg-for="Description"]').textContent() !== '', 'Inline category error');
    await page.locator('#Description').fill('');
    await page.getByRole('button', { name: 'Save category' }).click(); await page.waitForURL('**/Categories');
    check(await page.locator('.category-card').filter({ hasText: name + ' edited' }).count() === 1, 'Blank optional category description saved');
    await page.goto(base + createUrl); await capture('item-create');
    check(await page.locator('#Description').getAttribute('data-val-required') === null, 'Optional study notes have no required metadata');
    check(await page.locator('#CategoryId').inputValue() === new URL(base + createUrl).searchParams.get('categoryId'), 'Category entry preselected');
    check(await page.locator('#Type').inputValue() === 'Other', 'General study default');
    await page.locator('#Title').fill('Understand transport protocols');
    await page.locator('#Type').selectOption('Programming');
    await page.locator('#Difficulty').selectOption('Intermediate');
    await page.locator('#Priority').selectOption('4');
    await page.locator('[data-minutes="60"]').click();
    check(await page.locator('#EstimatedDurationMinutes').inputValue() === '60', 'Duration preset writes real field');
    check(await page.locator('[data-minutes="60"]').getAttribute('aria-pressed') === 'true', 'Duration preset state');
    await page.locator('[data-review-days="7"]').click();
    const review = await page.locator('#NextReviewDate').inputValue();
    check(/^\d{4}-\d{2}-\d{2}$/.test(review), 'Review preset writes date');
    await page.getByRole('button', { name: 'Create Item' }).click(); await page.waitForURL('**/StudyItems');
    const row = page.locator('tbody tr').filter({ hasText: 'Understand transport protocols' });
    await row.locator('a[href*="Edit"]').click(); await page.waitForURL('**/StudyItems/Edit/**'); await capture('item-edit');
    check(await page.locator('#EstimatedDurationMinutes').inputValue() === '60' && await page.locator('#Priority').inputValue() === '4' && await page.locator('#Type').inputValue() === 'Programming' && await page.locator('#NextReviewDate').inputValue() === review, 'Study settings persist after save');
    await page.locator('#EstimatedDurationMinutes').evaluate(element => { element.min = '-1'; });
    await page.locator('#EstimatedDurationMinutes').fill('0');
    await page.getByRole('button', { name: 'Save Changes' }).click();
    check(await page.locator('.validation-summary-errors').isVisible(), 'Study edit server validation visible');
    check(await page.locator('#Type').inputValue() === 'Programming' && await page.locator('#Priority').inputValue() === '4', 'Study choices preserved on error');
    await page.locator('[data-minutes="30"]').click();
    await page.locator('[data-review-days=""]').click();
    await page.getByRole('button', { name: 'Save Changes' }).click(); await page.waitForURL('**/StudyItems');
    check((await row.textContent()).includes('30 min'), 'Edited estimate persists');
    check(errors.length === 0, 'No JavaScript errors');
    fs.writeFileSync(`${output}/results.json`, JSON.stringify({ checks: results.length, results, errors, screenshots: 50, browser: await browser.version() }, null, 2));
    console.log(JSON.stringify({ checks: results.length, screenshots: 50, errors, output }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
