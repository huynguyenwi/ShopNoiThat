// Every edit form must save when submitted unchanged from a real browser (empty optional inputs included),
// and a value the model binder rejects must bring the form back with the field marked, saving nothing.
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { chooseAddress } = require('./lib/address');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const check = (name, ok, detail) => { results.push(ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail ? '  -> ' + detail : ''}`); };

async function state(page) {
  return page.evaluate(() => ({
    url: location.pathname + location.search,
    success: [...document.querySelectorAll('.alert-success, [role="status"].alert')].map(a => a.textContent.trim().replace(/\s+/g, ' ')).join(' '),
    errors: [...document.querySelectorAll('.validation-summary-errors li, .field-validation-error, .alert-danger')].map(e => e.textContent.trim()).filter(Boolean).join(' | '),
    marked: [...document.querySelectorAll('.input-validation-error')].map(e => e.name)
  }));
}

// Submits the form that owns the page's primary submit button, exactly as a user click would.
async function resubmit(page, url, buttonSelector) {
  await page.goto(BASE + url, { waitUntil: 'networkidle2' });
  if (page.url().includes('/account/login')) return { url: 'login', errors: 'not signed in', marked: [] };
  const button = await page.$(buttonSelector);
  if (!button) return { url, errors: 'submit button not found: ' + buttonSelector, marked: [] };
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), button.click()]);
  return state(page);
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  const consoleErrors = [];
  try {
    const admin = await (await browser.createBrowserContext()).newPage();
    admin.on('pageerror', e => consoleErrors.push(e.message));
    await admin.setViewport({ width: 1366, height: 900 });
    await admin.goto(BASE + '/account/login', { waitUntil: 'networkidle2' });
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('form[action="/account/login"] button[type="submit"]')]);

    const productId = sql('SELECT TOP 1 Id FROM Products WHERE IsDeleted=0 ORDER BY Id');
    const ruleId = sql('SELECT TOP 1 Id FROM PriceRules ORDER BY Id');
    const knowledgeId = sql('SELECT TOP 1 Id FROM AIKnowledgeEntries ORDER BY Id');
    const pages = [
      [`/admin/products/edit/${productId}`, '#productForm button[type="submit"].btn-primary'],
      ['/admin/categories/edit/1', 'main form button[type="submit"].btn-primary'],
      ['/admin/attributes/colors/1/edit', 'main form button[type="submit"].btn-primary'],
      ['/admin/attributes/materials/1/edit', 'main form button[type="submit"].btn-primary'],
      ['/admin/attributes/sizes/1/edit', 'main form button[type="submit"].btn-primary'],
      ['/admin/attributes/styles/1/edit', 'main form button[type="submit"].btn-primary'],
      [`/admin/price-rules/${ruleId}/edit`, 'main form button[type="submit"].btn-primary'],
      [`/admin/ai-knowledge/${knowledgeId}/edit`, 'main form button[type="submit"].btn-primary'],
      ['/admin/store', 'main form button[type="submit"].btn-primary'],
      ['/admin/coupons/1/edit', '[data-coupon-form-admin] button[type="submit"]']
    ];
    for (const [url, button] of pages) {
      const r = await resubmit(admin, url, button);
      check(`Unchanged re-submit saves: ${url}`, !r.errors && r.marked.length === 0, r.errors || r.marked.join(',') || r.success.slice(0, 60));
    }

    // A rejected value comes back marked; nothing is saved.
    const before = sql(`SELECT CONCAT(LengthMm,'|',WarrantyMonths) FROM Products WHERE Id=${productId}`);
    await admin.goto(BASE + `/admin/products/edit/${productId}`, { waitUntil: 'networkidle2' });
    await admin.$eval('#Command_WarrantyMonths', i => { i.type = 'text'; i.value = 'mười hai'; });
    await admin.$eval('#productForm', f => f.setAttribute('novalidate', 'novalidate'));
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('#productForm button[type="submit"].btn-primary')]);
    const bad = await state(admin);
    const after = sql(`SELECT CONCAT(LengthMm,'|',WarrantyMonths) FROM Products WHERE Id=${productId}`);
    check('Rejected number: form returns with the field marked, nothing saved', bad.marked.includes('Command.WarrantyMonths') && /không hợp lệ/.test(bad.errors) && before === after, `${bad.marked} | ${bad.errors.slice(0, 90)} | ${before} -> ${after}`);
    const border = await admin.$eval('#Command_WarrantyMonths', i => getComputedStyle(i).borderColor);
    check('Marked field has the red invalid border', /rgb\(2(0[0-9]|1[0-9]|2[0-9]), 5[0-9], 69\)|rgb\(220, 53, 69\)/.test(border), border);

    // Customer forms
    const customer = await (await browser.createBrowserContext()).newPage();
    customer.on('pageerror', e => consoleErrors.push(e.message));
    const email = `forms-${Date.now()}@example.com`;
    await customer.goto(BASE + '/account/register', { waitUntil: 'networkidle2' });
    await customer.type('#FullName', 'Khách Kiểm Form');
    await customer.type('#Email', email);
    await customer.type('#PhoneNumber', '0912345678');
    await customer.type('#Password', 'Khach@Hang123');
    await customer.type('#ConfirmPassword', 'Khach@Hang123');
    await customer.click('#AcceptTerms');
    await Promise.all([customer.waitForNavigation({ waitUntil: 'networkidle2' }), customer.click('form[action="/account/register"] button[type="submit"]')]);
    const profile = await resubmit(customer, '/account/profile', 'form[action="/account/profile"] button[type="submit"]');
    check('Unchanged re-submit saves: /account/profile', !profile.errors, profile.errors || profile.success.slice(0, 60));

    await customer.goto(BASE + '/account/addresses', { waitUntil: 'networkidle2' });
    const addressForm = await customer.$('form[action="/account/addresses"]');
    if (addressForm) {
      await customer.evaluate(() => { const b = document.querySelector('[data-bs-target="#addressForm"], [data-address-new]'); if (b) b.click(); });
      const fill = (sel, v) => customer.$eval(sel, (i, val) => { i.value = val; }, v);
      await fill('#Command_RecipientName', 'Khách Kiểm Form');
      await fill('#Command_Phone', '0912345678');
      await chooseAddress(customer);
      await fill('#Command_AddressLine', '1 Nguyễn Huệ');
      await Promise.all([customer.waitForNavigation({ waitUntil: 'networkidle2' }), customer.$eval('form[action="/account/addresses"]', f => f.requestSubmit())]);
      const saved = await state(customer);
      check('New address saves', !saved.errors && sql(`SELECT COUNT(*) FROM CustomerAddresses a JOIN AspNetUsers u ON u.Id=a.UserId WHERE u.Email='${email}'`) === '1', saved.errors || saved.success.slice(0, 60));
    } else {
      check('Address form present', false);
    }
  } catch (e) {
    check('Script error', false, e.stack);
  } finally {
    const failed = results.filter(r => !r).length;
    console.log(`\n${results.length - failed}/${results.length} passed; page errors: ${consoleErrors.length ? consoleErrors.join(' | ') : 'none'}`);
    process.exitCode = failed ? 1 : 0;
    await browser.close();
  }
})();
