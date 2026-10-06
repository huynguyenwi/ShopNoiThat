// E2E: /bao-gia estimate + submit as a signed-in customer, admin quotes it, customer accepts.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const OUT = process.argv[2] || require('path').join(__dirname, 'output');
require('fs').mkdirSync(OUT, { recursive: true });

(async () => {
  const browser = await puppeteer.launch({ executablePath: (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'), headless: true, acceptInsecureCerts: true });
  const errors = [];
  const watch = (page, who) => {
    page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`${who} console.${m.type()}: ${m.text()}`); });
    page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
  };
  const result = {};
  try {
    // Customer registers
    const ctx = await browser.createBrowserContext();
    const page = await ctx.newPage();
    watch(page, 'customer');
    await page.setViewport({ width: 1366, height: 900 });
    await page.goto(BASE + '/account/register', { waitUntil: 'networkidle2' });
    const email = 'quote-e2e-' + Date.now() + '@example.com';
    await page.type('#FullName', 'Khách Báo Giá');
    await page.type('#Email', email);
    await page.type('#PhoneNumber', '0912345678');
    await page.type('#Password', 'Khach@Hang123');
    await page.type('#ConfirmPassword', 'Khach@Hang123');
    await page.click('#AcceptTerms');
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('form button[type="submit"].btn-primary')]);

    // Estimate from free text
    await page.goto(BASE + '/bao-gia', { waitUntil: 'networkidle2' });
    await page.type('#quoteText', 'Tôi muốn bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm');
    await page.click('#quoteParseForm button[type="submit"]');
    await page.waitForSelector('#quoteResult .quote-price', { timeout: 15000 });
    result.firstPrice = await page.$eval('#quoteResult .quote-price', e => e.textContent);
    result.parsed = await page.evaluate(() => ({ kind: qKind.value, l: qLength.value, w: qWidth.value, h: qHeight.value, material: qMaterial.selectedOptions[0].text, finish: qFinish.value }));
    result.assumptions = await page.$$eval('#quoteResult .alert-warning li', l => l.map(x => x.textContent));
    result.alternatives = await page.$$eval('#quoteResult .btn-outline-secondary', b => b.map(x => x.textContent));

    // Pick the cheapest alternative wood → recalculated
    await page.click('#quoteResult .btn-outline-secondary');
    await page.waitForFunction(prev => document.querySelector('#quoteResult .quote-price') && document.querySelector('#quoteResult .quote-price').textContent !== prev, { timeout: 15000 }, result.firstPrice);
    result.cheaperPrice = await page.$eval('#quoteResult .quote-price', e => e.textContent);
    result.materialAfterSwitch = await page.$eval('#qMaterial', s => s.selectedOptions[0].text);

    // Change quantity + finish and recalc
    await page.$eval('#qQuantity', i => { i.value = '2'; });
    await page.select('#qFinish', 'TwoK');
    await page.click('#quoteSpecForm button[type="submit"]');
    await page.waitForFunction(() => document.querySelector('#quoteResult .quote-total .fw-semibold') && document.querySelector('#quoteResult .quote-total .fw-semibold').textContent.startsWith('2 sản phẩm'), { timeout: 15000 });
    result.twoPieces = await page.$eval('#quoteResult .quote-total .fw-semibold', e => e.textContent);
    await page.screenshot({ path: OUT + '/quote-page.png', fullPage: true });

    // Submit
    await page.type('#qNote', 'Cần giao trước cuối tháng');
    await page.click('#quoteSubmitForm button[type="submit"]');
    await page.waitForSelector('#quoteSuccess:not([hidden]) .alert-success', { timeout: 15000 });
    result.success = await page.$eval('#quoteSuccess h2', e => e.textContent);
    const code = result.success.split(' ').pop();

    // Admin quotes it
    const adminCtx = await browser.createBrowserContext();
    const admin = await adminCtx.newPage();
    watch(admin, 'admin');
    await admin.setViewport({ width: 1366, height: 900 });
    await admin.goto(BASE + '/account/login', { waitUntil: 'networkidle2' });
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('form button[type="submit"].btn-primary')]);
    await admin.goto(BASE + '/admin/quotes', { waitUntil: 'networkidle2' });
    const link = await admin.$x ? null : null;
    await admin.evaluate(c => [...document.querySelectorAll('a')].find(a => a.textContent === c).click(), code);
    await admin.waitForSelector('#FinalQuotedPrice');
    await admin.select('#Status', 'Quoted');
    await admin.$eval('#FinalQuotedPrice', i => { i.value = '42000000'; });
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('form[action$="/update"] button[type="submit"]')]);
    result.adminStatus = await admin.$eval('.admin-card-header .badge', e => e.textContent);
    await admin.screenshot({ path: OUT + '/quote-admin.png', fullPage: true });
    await admin.goto(BASE + '/admin/price-rules', { waitUntil: 'networkidle2' });
    result.priceRulesRows = await admin.$$eval('table tbody tr', r => r.length);
    await admin.screenshot({ path: OUT + '/price-rules.png' });

    // Customer accepts
    await page.goto(BASE + '/account/quotes/' + code, { waitUntil: 'networkidle2' });
    result.customerSeesPrice = await page.$eval('.quote-price', e => e.textContent);
    page.on('dialog', d => d.accept());
    await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('form[action$="/accept"] button')]);
    result.customerStatus = await page.$eval('h1 + .badge', e => e.textContent);
    result.notification = await page.$eval('.alert', e => e.textContent.trim()).catch(() => null);
  } catch (e) {
    result.failed = e.message;
    process.exitCode = 1;
  } finally {
    result.errors = errors;
    if (errors.length) process.exitCode = 1; // browser console / page errors fail the suite
    console.log(JSON.stringify(result, null, 2));
    await browser.close();
  }
})();
