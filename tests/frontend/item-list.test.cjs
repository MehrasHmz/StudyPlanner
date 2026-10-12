const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');

// Run only against the isolated local preview; this workflow creates synthetic records.
const base = 'http://127.0.0.1:5815';
const output = '/private/tmp/studyplanner-item-layout-20261011';
fs.mkdirSync(output, { recursive: true });
let browser;
(async () => {
    browser = await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const page = await browser.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto(base + '/Auth/Register');
    const email = `item-layout-${Date.now()}@example.test`;
    await page.locator('#registerName').fill('Item Layout Review');
    await page.locator('#registerEmail').fill(email);
    await page.locator('#registerPassword').fill('Layout-review-9!');
    await page.getByRole('button', { name: 'Create account' }).click();
    await page.waitForURL('**/Auth/Login');
    await page.locator('[name="Email"]').fill(email);
    await page.locator('[name="Password"]').fill('Layout-review-9!');
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.waitForURL(base + '/');
    for (const [type, title] of [['Vocabulary', 'Review chapter vocabulary'], ['Algorithm', 'Practice graph traversal'], ['Programming', 'Build the project exercises'], ['Other', 'Read and summarize a long chapter with notes']]) {
        await page.goto(base + '/StudyItems/Create');
        await page.locator('#Title').fill(title);
        await page.locator('#Description').fill('Isolated layout test');
        await page.locator('#Type').selectOption(type);
        const category = await page.locator('#CategoryId option').nth(1).getAttribute('value');
        await page.locator('#CategoryId').selectOption(category);
        await page.getByRole('button', { name: 'Create Item' }).click();
        await page.waitForURL('**/StudyItems');
    }
    let checks = 0;
    const check = (value, message) => { assert.ok(value, message); checks++; };
    for (const width of [1440, 1024, 768, 390, 360]) {
        await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
        for (const theme of ['dark', 'light']) {
            await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
            await page.screenshot({ path: `${output}/items-${width}-${theme}.png`, fullPage: true, animations: 'disabled' });
            check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), 'No page overflow');
            check(await page.locator('.item-actions').evaluateAll(groups => groups.every(group => {
                const controls = [...group.querySelectorAll('.item-action')].map(element => element.getBoundingClientRect());
                return controls.length === 3 && controls.every(rect => rect.width === 44 && rect.height === 44 && rect.top === controls[0].top);
            })), 'Action icons share stable dimensions and alignment');
            check(await page.locator('.item-action').evaluateAll(controls => controls.every(element => element.getAttribute('aria-label') && element.dataset.tooltip)), 'Actions have names and tooltips');
            check(await page.locator('.item-kind-icon i').evaluateAll(icons => icons.length === 4 && icons.every(icon => !['none', 'normal', '""'].includes(getComputedStyle(icon, '::before').content))), 'All item-type icons render');
        }
    }
    await page.setViewportSize({ width: 1440, height: 900 });
    const row = page.locator('tbody tr').filter({ hasText: 'Review chapter vocabulary' });
    await row.locator('.item-status-toggle').click();
    await page.waitForURL('**/StudyItems');
    check(await row.locator('.item-status-toggle').getAttribute('aria-pressed') === 'true', 'Completion toggle still submits successfully');
    await row.getByRole('button', { name: 'Delete Review chapter vocabulary' }).click();
    check(await page.locator('#confirmationDialog').isVisible(), 'Delete still requires confirmation');
    await page.locator('#confirmationDialog').getByRole('button', { name: 'Cancel' }).click();
    check(await row.count() === 1, 'Cancel preserves the item');
    await row.getByRole('link', { name: 'Edit Review chapter vocabulary' }).click();
    await page.waitForURL('**/StudyItems/Edit/**');
    check(await page.locator('#Title').inputValue() === 'Review chapter vocabulary', 'Edit action opens the correct item');
    check(errors.length === 0, 'No JavaScript errors');
    console.log(JSON.stringify({ checks, screenshots: 10, errors, output }));
})().catch(error => { console.error(error); process.exitCode = 1; }).finally(async () => { await browser?.close(); });
