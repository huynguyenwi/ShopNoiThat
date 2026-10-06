// QR codes end to end: the codes shown on the pages are decoded like a phone camera would (screenshot -> jsQR), then the
// decoded address is opened: product page (also after the product was renamed), order page for its customer / admins
// only, sign-in first when anonymous; print pages and the confirmation e-mail carry scannable codes too.
const fs = require('fs');
const path = require('path');
const https = require('https');
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { chooseAddress } = require('./lib/address');
const jsQR = require('jsqr');
const { PNG } = require('pngjs');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EMAIL_DIR = process.env.EMAIL_DIR;
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const PRODUCT = '/products/sofa-bang-3-cho-oslo-khung-go-soi';
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + detail : ''}`); };
const watch = (page, who) => {
  // The stranger opening someone else's order answers 404 on purpose.
  page.on('console', m => { if (m.type() === 'error' && !/status of 404/.test(m.text())) errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
};
const go = async (page, url) => { const r = await page.goto(url.startsWith('http') ? url : BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
const path_ = url => url.replace(BASE, '');

function decode(pngBuffer) {
  const png = PNG.sync.read(Buffer.from(pngBuffer)); // puppeteer returns a Uint8Array
  const result = jsQR(new Uint8ClampedArray(png.data), png.width, png.height);
  return result ? result.data : null;
}

/** Decodes the QR code as displayed: screenshot of the <img> element, like a camera pointed at the screen. */
async function scanElement(page, selector) {
  const element = await page.waitForSelector(selector, { visible: true, timeout: 10000 });
  await element.evaluate(e => e.scrollIntoView({ block: 'center' })); // lazy-loaded images load when on screen
  await page.waitForFunction(s => { const i = document.querySelector(s); return i && i.complete && i.naturalWidth > 0; }, { timeout: 10000 }, selector);
  return decode(await element.screenshot({ type: 'png' }));
}

function download(url) {
  return new Promise((resolve, reject) => {
    https.get(url, { rejectUnauthorized: false }, res => {
      const chunks = [];
      res.on('data', c => chunks.push(c));
      res.on('end', () => resolve({ status: res.statusCode, type: res.headers['content-type'], body: Buffer.concat(chunks) }));
    }).on('error', reject);
  });
}

async function newPage(browser, who, width = 1366) {
  const page = await (await browser.createBrowserContext()).newPage();
  watch(page, who);
  await page.setViewport({ width, height: 900 });
  return page;
}

async function login(page, email, password) {
  await page.type('#Email', email);
  await page.type('#Password', password);
  await click(page, 'form[action="/account/login"] button[type="submit"]');
}

async function register(page, who) {
  const email = `${who}-${Date.now()}@example.com`;
  await go(page, '/account/register');
  await page.type('#FullName', 'Khách ' + who);
  await page.type('#Email', email);
  await page.type('#PhoneNumber', '0912345678');
  await page.type('#Password', 'Khach@Hang123');
  await page.type('#ConfirmPassword', 'Khach@Hang123');
  await page.click('#AcceptTerms');
  await click(page, 'form[action="/account/register"] button[type="submit"]');
  return email;
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    const productId = sql(`SELECT Id FROM Products WHERE Slug='${PRODUCT.split('/').pop()}'`);

    // ---------- Product: code in the product page modal
    const visitor = await newPage(browser, 'visitor');
    await go(visitor, PRODUCT);
    await visitor.click('[data-bs-target="#productQrModal"]');
    await visitor.waitForSelector('#productQrModal.show', { visible: true });
    const productTarget = await scanElement(visitor, '#productQrModal img.qr-image');
    check('Product page QR scans to the stable address', productTarget === `${BASE}/q/p/${productId}`, productTarget);

    const phone = await newPage(browser, 'phone', 390);
    await go(phone, productTarget);
    check('Scanning opens the product page', path_(phone.url()) === PRODUCT && (await phone.$eval('h1', h => h.textContent)).includes('Oslo'), path_(phone.url()));

    const png = await download(`${BASE}/qr/products/${productId}.png?download=true`);
    check('PNG download scans the same', png.status === 200 && png.type === 'image/png' && decode(png.body) === productTarget);

    // ---------- Admin: card, labels, renaming keeps the code working
    const admin = await newPage(browser, 'admin');
    await go(admin, '/account/login');
    await login(admin, 'admin@furniture.local', process.env.ADMIN_PW);
    await go(admin, `/admin/products/edit/${productId}`);
    const cardTarget = await scanElement(admin, '[data-qr-card] img.qr-image');
    check('Admin product card QR', cardTarget === productTarget && (await admin.$eval('[data-qr-target]', e => e.textContent)) === productTarget);

    await go(admin, `/admin/products/qrlabels?id=${productId}`);
    check('Single label printable', (await admin.$$('[data-qr-label]')).length === 1 && (await scanElement(admin, '[data-qr-label] img')) === productTarget);
    const categoryId = sql(`SELECT CategoryId FROM Products WHERE Id=${productId}`);
    await go(admin, `/admin/products/qrlabels?categoryId=${categoryId}`);
    const labels = await admin.$$eval('[data-qr-label]', l => l.map(x => ({ id: x.getAttribute('data-qr-label'), ok: x.querySelector('img').naturalWidth > 0 })));
    const labelScans = [];
    for (const l of labels) labelScans.push(await scanElement(admin, `[data-qr-label="${l.id}"] img`) === `${BASE}/q/p/${l.id}`);
    check('Category label sheet: every label scans to its product', labels.length > 1 && labelScans.every(Boolean), `${labels.length} labels`);

    const originalSlug = PRODUCT.split('/').pop();
    await go(admin, `/admin/products/edit/${productId}`);
    await admin.$eval('#Command_Slug', i => { i.value = ''; });
    await admin.type('#Command_Slug', 'sofa-oslo-doi-ten-qr');
    await click(admin, '#productForm button[type="submit"].btn-primary');
    await go(phone, productTarget);
    check('Printed code still works after renaming the URL', path_(phone.url()) === '/products/sofa-oslo-doi-ten-qr', path_(phone.url()));
    await go(admin, `/admin/products/edit/${productId}`);
    await admin.$eval('#Command_Slug', (i, v) => { i.value = v; }, originalSlug);
    await click(admin, '#productForm button[type="submit"].btn-primary');
    check('Slug restored', sql(`SELECT Slug FROM Products WHERE Id=${productId}`) === originalSlug);

    // ---------- Order: place one, scan its code
    const customer = await newPage(browser, 'customer');
    const email = await register(customer, 'qr');
    await go(customer, PRODUCT);
    await customer.click('[data-add-to-cart]');
    await customer.waitForFunction(() => { const b = document.querySelector('[data-cart-count]'); return b && b.textContent.trim() === '1'; }, { timeout: 10000 });
    await go(customer, '/checkout');
    await customer.type('#Command_AddressLine', '12 Đường Quét Mã');
    await chooseAddress(customer);
    await click(customer, 'button[form="checkoutForm"]');
    const code = await customer.$eval('[data-order-code]', e => e.textContent.trim());
    const orderId = sql(`SELECT Id FROM Orders WHERE OrderCode='${code}'`);
    const orderTarget = await scanElement(customer, '[data-qr-card] img.qr-image');
    check('Success page QR scans to the order address', orderTarget === `${BASE}/q/o/${code}`, orderTarget);
    await go(customer, `/account/orders/${code}`);
    check('Order page QR', (await scanElement(customer, '[data-qr-card] img.qr-image')) === orderTarget);

    // Customer's own phone: not signed in -> sign in -> the order
    const ownPhone = await newPage(browser, 'own-phone', 390);
    await go(ownPhone, orderTarget);
    check('Anonymous scan asks to sign in', path_(ownPhone.url()).startsWith('/account/login?ReturnUrl=%2Fq%2Fo%2F'), path_(ownPhone.url()));
    await login(ownPhone, email, 'Khach@Hang123');
    check('After sign-in: the order page', path_(ownPhone.url()) === `/account/orders/${code}` && (await ownPhone.content()).includes(code), path_(ownPhone.url()));
    const overflow = await ownPhone.evaluate(() => { window.scrollTo(5000, window.scrollY); const x = window.scrollX; window.scrollTo(0, window.scrollY); return x; });
    check('Order page fits a 390px phone', overflow === 0, overflow);

    // Someone else scanning it
    const stranger = await newPage(browser, 'stranger');
    await register(stranger, 'qr-stranger');
    const strangerStatus = await go(stranger, orderTarget);
    check('Another customer gets "not found"', strangerStatus === 404 && !(await stranger.content()).includes('12 Đường Quét Mã'), strangerStatus);

    // Staff scanning the delivery slip
    await go(admin, orderTarget);
    check('Admin scan opens the back-office order', path_(admin.url()) === `/admin/orders/details/${orderId}`, path_(admin.url()));
    check('Admin order card QR', (await scanElement(admin, '[data-qr-card] img.qr-image')) === orderTarget);
    await go(admin, `/admin/orders/print/${orderId}`);
    const slipScan = await scanElement(admin, '.slip-qr img');
    check('Delivery slip QR scans, COD amount shown', slipScan === orderTarget && /Thu hộ \(COD\)/.test(await admin.$eval('.slip-cod', e => e.textContent)), slipScan);

    // Confirmation e-mail
    if (EMAIL_DIR) {
      const mail = fs.readdirSync(EMAIL_DIR).map(f => fs.readFileSync(path.join(EMAIL_DIR, f), 'utf8')).find(m => m.includes(code) && m.includes('/qr/orders/'));
      const src = mail && (mail.match(/src="([^"]*\/qr\/orders\/[^"]*)"/) || [])[1];
      const mailImage = src ? await download(src.replace(/&amp;/g, '&')) : null;
      check('E-mail QR image loads and scans', mailImage && mailImage.status === 200 && decode(mailImage.body) === orderTarget, src);
    }
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
