const { chromium, webkit } = require('playwright');
const { execFileSync, spawnSync } = require('node:child_process');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const base = process.env.STUDYPLANNER_ADMIN_TEST_URL || 'http://127.0.0.1:5819';
const database = process.env.STUDYPLANNER_ADMIN_TEST_DATABASE;
const socket = process.env.STUDYPLANNER_ADMIN_TEST_SOCKET;
if (new URL(base).hostname !== '127.0.0.1' || !/^studyplanner_phase(?:9|10)_browser_[a-z0-9_]+$/.test(database || '') || !socket?.startsWith('/private/tmp/studyplanner-'))
    throw new Error('Use an explicitly configured isolated Phase 9 database and loopback preview.');
const dotnet = process.env.STUDYPLANNER_TEST_DOTNET || '/Users/m/.dotnet/dotnet';
const web = require('node:path').resolve('src/StudyPlanner.Web');
const connection = `Host=${socket};Port=55439;Username=studyplanner_test;Database=${database}`;
const output = process.env.STUDYPLANNER_ADMIN_TEST_OUTPUT || '/private/tmp/studyplanner-phase9-browser-evidence';
fs.mkdirSync(output, { recursive: true });
const sql = query => execFileSync('/opt/homebrew/bin/psql', ['-h', socket, '-p', '55439', '-U', 'studyplanner_test', '-d', database, '-At', '-v', 'ON_ERROR_STOP=1', '-c', query], { encoding: 'utf8' }).trim();
function provision(id, role, confirmed = true) {
    assert.match(id, /^[a-f0-9-]{36}$/);
    const args = [`${web}/bin/Release/net8.0/StudyPlanner.Web.dll`, '--manage-user-role', '--user-id', id, '--role', role, '--operator', 'browser-test-operator', '--reason', 'Synthetic browser authorization verification'];
    if (confirmed) args.push('--confirm');
    const result = spawnSync(dotnet, args, { cwd: web, encoding: 'utf8', env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Testing', ConnectionStrings__DefaultConnection: connection } });
    assert.equal(result.status, 0, result.stdout + result.stderr);
    assert.ok(!result.stdout.includes('Password=') && !result.stdout.includes('Host='));
    return result.stdout;
}
let browser;
(async () => {
    browser = process.env.STUDYPLANNER_BROWSER_ENGINE === 'webkit' ? await webkit.launch() : await chromium.launch({ channel: 'chrome', headless: true, args: ['--disable-gpu'] });
    const regular = await browser.newPage(), admin = await browser.newPage();
    const checks = [], errors = []; let screenshots = 0;
    for (const page of [regular, admin]) { page.on('pageerror', e => errors.push(e.message)); page.on('dialog', dialog => dialog.accept()); }
    const check = (value, name) => { assert.ok(value, name); checks.push(name); };
    async function capture(page, name) {
        for (const width of [1440, 1024, 768, 390, 360]) {
            await page.setViewportSize({ width, height: width < 500 ? 844 : 900 });
            for (const theme of ['light', 'dark']) {
                await page.evaluate(theme => { document.documentElement.dataset.theme = theme; document.documentElement.dataset.bsTheme = theme; }, theme);
                await page.screenshot({ path: `${output}/${name}-${width}-${theme}.png`, fullPage: true, animations: 'disabled' }); screenshots++;
                check(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), `${name} ${width} ${theme}: no overflow`);
                check(await page.locator('main input:visible, main select:visible, main textarea:visible').evaluateAll(inputs => inputs.every(input => input.labels.length || input.getAttribute('aria-label'))), `${name} ${width} ${theme}: labeled fields`);
                check(await page.locator('main a.item-action, main button.item-action, main .icon-button').evaluateAll(elements => elements.every(e => e.getAttribute('aria-label') || e.textContent.trim())), `${name} ${width} ${theme}: labeled actions`);
            }
        }
        await page.setViewportSize({ width: 1440, height: 900 });
    }
    async function registerLogin(page, name) {
        const email = `phase9-${name.toLowerCase().replaceAll(' ', '-')}-${Date.now()}@example.test`;
        await page.goto(base + '/Auth/Register'); await page.locator('#registerName').fill(name);
        await page.locator('#registerEmail').fill(email); await page.locator('#registerPassword').fill('Category-test-9!');
        await page.getByRole('button', { name: 'Create account' }).click(); await page.waitForURL('**/Auth/Login');
        await page.locator('[name="Email"]').fill(email); await page.locator('[name="Password"]').fill('Category-test-9!');
        await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(base + '/');
        return sql(`SELECT "Id" FROM "Users" WHERE "Email"='${email}'`);
    }
    const regularId = await registerLogin(regular, 'Regular user'); const adminId = await registerLogin(admin, 'Category manager');
    check(sql(`SELECT "Role" FROM "Users" WHERE "Id"='${adminId}'`) === '0', 'Registration has no automatic administrator');
    await regular.goto(base + '/Categories'); await capture(regular, 'regular-categories');
    check(await regular.locator('a[href*="/Categories/Create"]').count() === 1, 'Regular user can create personal categories');
    check(await regular.locator('a[href*="/Categories/Edit"], form[action*="/Categories/Delete"]').count() === 0, 'Regular catalog hides shared mutations');
    const categoryId = sql('SELECT "Id" FROM "Categories" WHERE "UserId" IS NULL AND NOT "IsDeleted" ORDER BY "Name" LIMIT 1');
    await regular.goto(base + '/StudyItems/Create?categoryId=' + categoryId); await capture(regular, 'regular-study-create');
    check(await regular.locator('#CategoryId').inputValue() === categoryId, 'Regular user selects shared category');
    check(await regular.getByText('New category', { exact: true }).count() === 1, 'Regular form links to personal category creation');
    await regular.locator('#Title').fill('Regular shared study'); await regular.getByRole('button', { name: 'Create Item', exact: true }).click(); await regular.waitForURL('**/StudyItems');
    const itemId = sql(`SELECT "Id" FROM "StudyItems" WHERE "UserId"='${regularId}' LIMIT 1`);
    check(Boolean(itemId), 'Regular user creates own study item');
    const denied = await regular.goto(base + '/Categories/Edit/' + categoryId); check(denied.status() === 403, 'Regular shared management GET returns 403'); await capture(regular, 'regular-forbidden');
    check(await regular.getByRole('link', { name: 'Back to categories' }).count() === 1, 'Forbidden response has useful navigation');
    check(provision(adminId, 'administrator', false).includes('Inspection only'), 'Operator preview does not grant');
    check(sql(`SELECT "Role" FROM "Users" WHERE "Id"='${adminId}'`) === '0', 'Preview leaves role unchanged');
    provision(adminId, 'administrator');
    await admin.goto(base + '/Categories'); await capture(admin, 'admin-categories');
    check(await admin.locator('a[href*="/Categories/Create"]').count() === 1, 'Existing cookie immediately gains authorized management');
    check((await admin.request.get(base + '/StudyItems/Edit/' + itemId, { maxRedirects: 0 })).status() === 404, 'Administrator cannot read foreign study item');
    await admin.goto(base + '/Categories/Create'); await capture(admin, 'admin-create');
    const name = `Governed ${Date.now()}`;
    await admin.locator('#Name').fill(name); await admin.locator('#Description').fill('Shared managed catalog');
    await admin.locator('#IsShared').check();
    await admin.getByRole('button', { name: 'Create category', exact: true }).click(); await admin.waitForURL('**/Categories');
    const managedId = sql(`SELECT "Id" FROM "Categories" WHERE "Name"='${name}'`);
    await admin.goto(base + '/Categories/Edit/' + managedId); await capture(admin, 'admin-edit');
    await admin.locator('#Name').fill(name + ' renamed'); await admin.getByRole('button', { name: 'Save category' }).click(); await admin.waitForURL('**/Categories');
    check(sql(`SELECT "Name" FROM "Categories" WHERE "Id"='${managedId}'`) === name + ' renamed', 'Administrator rename persisted');
    const tokens = async page => page.locator('input[name="__RequestVerificationToken"]').first().inputValue();
    const referenced = await admin.request.post(base + '/Categories/Delete', { form: { id: categoryId, __RequestVerificationToken: await tokens(admin) }, maxRedirects: 0 });
    check(referenced.status() === 302 && sql(`SELECT "IsDeleted" FROM "Categories" WHERE "Id"='${categoryId}'`) === 'f', 'Referenced deletion remains blocked');
    await admin.goto(base + '/Categories'); check((await admin.locator('body').textContent()).includes('referenced by study items'), 'Reference guard feedback rendered'); await capture(admin, 'admin-delete-blocked');
    const deleted = await admin.request.post(base + '/Categories/Delete', { form: { id: managedId, __RequestVerificationToken: await tokens(admin) }, maxRedirects: 0 });
    check(deleted.status() === 302 && sql(`SELECT "IsDeleted" FROM "Categories" WHERE "Id"='${managedId}'`) === 't', 'Unreferenced administrator deletion permitted');
    await admin.goto(base + '/Categories/Create'); const staleToken = await tokens(admin);
    provision(adminId, 'regular');
    const revoked = await admin.request.post(base + '/Categories/Create', { form: { Name: name + ' revoked', IsShared: 'true', Role: 'Administrator', __RequestVerificationToken: staleToken }, maxRedirects: 0 });
    check(revoked.status() === 403, 'Revocation rejects previously issued cookie and valid form token');
    check(sql(`SELECT count(*) FROM "Categories" WHERE "Name"='${name} revoked'`) === '0', 'Revoked request persists no mutation');
    await admin.goto(base + '/Categories'); await capture(admin, 'revoked-categories');
    check(await admin.locator('a[href*="/Categories/Edit"], form[action*="/Categories/Delete"]').count() === 0, 'Revoked UI removes shared management controls');
    check(await admin.locator('a[href*="/Categories/Create"]').count() === 1, 'Revoked administrator retains personal creation');
    check(sql(`SELECT count(*) FROM "RoleChangeAudits" WHERE "UserId"='${adminId}'`) === '2', 'Grant and revoke have durable audit records');
    check(sql(`SELECT "Role" FROM "Users" WHERE "Id"='${regularId}'`) === '0', 'Other user role unchanged');
    check(sql(`SELECT count(*) FROM "StudyItems" WHERE "Id"='${itemId}' AND "CategoryId"='${categoryId}' AND NOT "IsDeleted"`) === '1', 'Historical shared association preserved');
    check(errors.length === 0, 'No browser JavaScript errors');
    fs.writeFileSync(`${output}/results.json`, JSON.stringify({ browser: await browser.version(), checks, screenshots, errors }, null, 2));
    console.log(JSON.stringify({ checks: checks.length, screenshots, errors, output }));
    await browser.close(); browser = null;
})().catch(async error => { console.error(error); if (browser) await browser.close(); process.exitCode = 1; });
