// Coupons end to end: admin creates a code, customers see it as an offer, apply it in the cart and at checkout
// (AJAX, typed address kept), place an order; admin sees the usage, switches the code off, cancelling releases the use.
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { chooseAddress } = require('./lib/address');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const PRODUCT = '/products/sofa-bang-3-cho-oslo-khung-go-soi';
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();
const sleep = ms => new Promise(r => setTimeout(r, ms));

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + detail : ''}`); };
const watch = (page, who) => {
  // The wrong code typed on purpose answers 400; any other console error fails the suite.
  page.on('console', m => { if (m.type() === 'error' && !/status of 400/.test(m.text())) errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
};
const go = async (page, url) => { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
const text = (page, selector) => page.$eval(selector, e => e.textContent.trim().replace(/\s+/g, ' ')).catch(() => '');

async function register(browser, who) {
  const page = await (await browser.createBrowserContext()).newPage();
  watch(page, who);
  await page.setViewport({ width: 1366, height: 900 });
  await go(page, '/account/register');
  await page.type('#FullName', 'Khách ' + who);
  await page.type('#Email', `${who}-${Date.now()}@example.com`);
  await page.type('#PhoneNumber', '0912345678');
  await page.type('#Password', 'Khach@Hang123');
  await page.type('#ConfirmPassword', 'Khach@Hang123');
  await page.click('#AcceptTerms');
  await click(page, 'form[action="/account/register"] button[type="submit"]');
  return page;
}

async function addSofa(page) {
  await go(page, PRODUCT);
  await page.click('[data-add-to-cart]');
  await page.waitForFunction(() => { const b = document.querySelector('[data-cart-count]'); return b && b.textContent.trim() === '1'; }, { timeout: 10000 });
}

async function toast(page) {
  await page.waitForFunction(() => { const c = document.getElementById('fs-toast-container'); return c && c.textContent.trim().length > 0; }, { timeout: 10000 });
  const message = await text(page, '#fs-toast-container');
  await page.evaluate(() => { const c = document.getElementById('fs-toast-container'); if (c) c.innerHTML = ''; });
  return message;
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    // ---------- Admin creates a public coupon
    const admin = await (await browser.createBrowserContext()).newPage();
    watch(admin, 'admin');
    await admin.setViewport({ width: 1366, height: 900 });
    await go(admin, '/account/login');
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await click(admin, 'form[action="/account/login"] button[type="submit"]');
    await go(admin, '/admin/coupons');
    check('Admin menu links to coupons', await admin.$('.admin-nav-link.active[href="/admin/coupons"]'));
    await click(admin, 'a[href="/admin/coupons/create"]');

    await admin.click('[data-generate-code]');
    const code = await admin.$eval('#Command_Code', i => i.value);
    check('Random code generated', /^NM[A-Z2-9]{6}$/.test(code), code);
    await admin.type('#Command_Name', 'Giảm 15% cuối tuần');
    await admin.select('#Command_DiscountType', 'FixedAmount');
    const capHidden = await admin.$eval('[data-percent-only]', e => e.hidden);
    await admin.select('#Command_DiscountType', 'Percentage');
    check('Cap field only for percentages', capHidden && !(await admin.$eval('[data-percent-only]', e => e.hidden)));
    await admin.type('#Command_DiscountValue', '15');
    await admin.type('#Command_MaxDiscountAmount', '1500000');
    await admin.type('#Command_MinOrderAmount', '3000000');
    await admin.type('#Command_UsageLimitPerUser', '1');
    const preview = await text(admin, '[data-coupon-preview]');
    check('Live preview', preview === 'Giảm 15%, tối đa 1.500.000₫ · Đơn từ 3.000.000₫ · mỗi khách 1 lần', preview);
    await click(admin, '[data-coupon-form-admin] button[type="submit"]');
    const couponId = (admin.url().match(/\/admin\/coupons\/(\d+)$/) || [])[1];
    check('Coupon created, detail page shown', couponId && (await admin.content()).includes('Đang chạy'), admin.url().replace(BASE, ''));
    check('Stored public and upper-case', sql(`SELECT CONCAT(IsPublic,'|',DiscountValue,'|',MaxDiscountAmount) FROM Coupons WHERE Code='${code}'`) === '1|15.00|1500000.00');

    // ---------- Customer: offer in the cart, one click
    const customer = await register(browser, 'coupon');
    await addSofa(customer);
    await go(customer, '/cart');
    const offer = await text(customer, `[data-coupon-offer="${code}"]`);
    check('Offer listed in the cart with the saving', offer.includes('Giảm 15%, tối đa 1.500.000₫') && offer.includes('Tiết kiệm 1.500.000₫'), offer);
    await click(customer, `[data-coupon-offer="${code}"] button[type="submit"]`);
    const applied = await text(customer, `[data-coupon-applied="${code}"]`);
    check('Applied from the offer', applied.includes(code), applied);
    check('Cart shows -1.500.000₫', (await customer.content()).includes('-1.500.000₫'));

    // ---------- Checkout: remove, wrong code, right code - all without reloading the page
    await go(customer, '/checkout');
    await customer.$eval('#Command_AddressLine', i => { i.value = ''; });
    await customer.type('#Command_AddressLine', '99 Đường Thử Mã');
    await customer.evaluate(() => { window.__notReloaded = true; });
    check('Checkout summary shows the discount', (await text(customer, '[data-summary="discount"]')) === '-1.500.000₫');

    await customer.click('[data-coupon-applied] button[type="submit"]');
    await customer.waitForSelector('[data-checkout-summary] #couponCode', { timeout: 10000 });
    const removedToast = await toast(customer);
    check('Remove via AJAX', !(await customer.$('[data-summary="discount"]')) && /Đã bỏ/.test(removedToast), removedToast);

    await customer.type('#couponCode', 'KHONGCO');
    await customer.click('[data-coupon-box] form.input-group button[type="submit"]');
    const wrongToast = await toast(customer);
    check('Unknown code -> error message', /không tồn tại/.test(wrongToast), wrongToast);

    await customer.$eval('#couponCode', i => { i.value = ''; });
    await customer.type('#couponCode', code.toLowerCase());
    await customer.click('[data-coupon-box] form.input-group button[type="submit"]');
    await customer.waitForSelector(`[data-checkout-summary] [data-coupon-applied="${code}"]`, { timeout: 10000 });
    await toast(customer);
    const total = await text(customer, '[data-summary="total"]');
    check('Typed code (lower-case) applies', (await text(customer, '[data-summary="discount"]')) === '-1.500.000₫', total);
    const kept = await customer.evaluate(() => ({ address: document.querySelector('#Command_AddressLine').value, same: window.__notReloaded === true }));
    check('Typed address kept (no page reload)', kept.same && kept.address === '99 Đường Thử Mã', JSON.stringify(kept));

    // Mobile layout of the summary with offers (own tab: switching to a mobile viewport reloads the page)
    const phone = await customer.browserContext().newPage();
    await phone.setViewport({ width: 360, height: 800, isMobile: true, hasTouch: true });
    await go(phone, '/checkout');
    const overflow = await phone.evaluate(() => { window.scrollTo(5000, window.scrollY); const x = window.scrollX; window.scrollTo(0, window.scrollY); return x; });
    check('No horizontal scroll at 360px', overflow === 0 && !!(await phone.$(`[data-coupon-applied="${code}"]`)), overflow);
    await phone.close();

    // Place the order
    await chooseAddress(customer, 'TP. Hồ Chí Minh', 'Phường Sài Gòn');
    await click(customer, 'button[form="checkoutForm"]');
    const orderCode = await text(customer, '[data-order-code]');
    const order = sql(`SELECT CONCAT(CouponCode,'|',DiscountAmount,'|',Status) FROM Orders WHERE OrderCode='${orderCode}'`);
    check('Order placed with the coupon', order === `${code}|1500000.00|Pending`, `${orderCode} ${order}`);

    // ---------- Admin: usage, delete locked, switch off
    await go(admin, `/admin/coupons/${couponId}`);
    const used = await text(admin, '[data-stat="used"]');
    check('Detail: 1 use and the order listed', used.startsWith('1') && (await admin.content()).includes(orderCode), used);
    check('Delete disabled once used', await admin.$eval('button.btn-outline-danger', b => b.disabled));
    await click(admin, `form[action="/admin/coupons/${couponId}/active"] button[type="submit"]`);
    check('Switched off', (await admin.content()).includes('Đã tắt'));

    const second = await register(browser, 'coupon2');
    await addSofa(second);
    await go(second, '/cart');
    check('Switched-off coupon no longer offered', !(await second.$(`[data-coupon-offer="${code}"]`)));
    await second.type('#couponCode', code);
    await click(second, '[data-coupon-box] form.input-group button[type="submit"]');
    const refused = await text(second, '.alert');
    check('Switched-off coupon refused when typed', /không còn hiệu lực/.test(refused), refused);

    // ---------- Admin cancels the order: the use is released
    const orderId = sql(`SELECT Id FROM Orders WHERE OrderCode='${orderCode}'`);
    await go(admin, `/admin/orders/details/${orderId}`);
    await admin.select('#status', 'Cancelled');
    await admin.type('#note', 'Khách đổi ý (kiểm thử)'); // a reason is required to cancel
    await click(admin, 'form[action*="/status"] button[type="submit"]');
    const afterCancel = sql(`SELECT CONCAT(o.Status,'|',c.UsedCount) FROM Orders o JOIN Coupons c ON c.Id=o.CouponId WHERE o.OrderCode='${orderCode}'`);
    check('Cancelling releases the use', afterCancel === 'Cancelled|0', afterCancel);
    await go(admin, `/admin/coupons?search=${code}`);
    check('Listed as switched off', (await text(admin, `[data-coupon-row="${code}"]`)).includes('Đã tắt'));
  } catch (e) {
    check('Script error', false, e.stack);
  } finally {
    const failed = results.filter(r => !r).length;
    console.log(`\n${results.length - failed}/${results.length} passed`);
    console.log(errors.length ? 'Browser errors:\n  ' + [...new Set(errors)].join('\n  ') : 'Browser errors: none');
    process.exitCode = failed || errors.length ? 1 : 0;
    await browser.close();
  }
})();
