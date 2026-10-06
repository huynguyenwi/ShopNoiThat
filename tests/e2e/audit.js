// Site audit in a real browser: CSP violations / console errors, horizontal overflow on phones, axe accessibility, load timing.
const fs = require('fs');
const puppeteer = require('puppeteer-core');
const OUT = process.argv[2] || require('path').join(__dirname, 'output');
fs.mkdirSync(OUT, { recursive: true });
const BASE = process.env.BASE || 'https://localhost:7160';
const axeSource = fs.readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');

const publicPages = ['/', '/products', '/products?category=phong-khach', '/products?category=bo-ban-an&size=set-6-ghe-160', '/products/bo-ban-an-go-soi-nga-an-gia', '/products/sofa-bang-3-cho-oslo-khung-go-soi', '/cart', '/contact', '/tu-van', '/bao-gia',
  '/account/login', '/account/register', '/account/forgotpassword', '/this-page-does-not-exist'];
const customerPages = ['/account/profile', '/account/orders', '/account/addresses', '/wishlist', '/account/quotes', '/account/ai-history', '/checkout'];
const adminPages = ['/admin', '/admin/orders', '/admin/products', '/admin/products/create', '/admin/categories', '/admin/customers', '/admin/reviews',
  '/admin/chat', '/admin/quotes', '/admin/price-rules', '/admin/coupons', '/admin/coupons/create', '/admin/coupons/1', '/admin/coupons/1/edit', '/admin/products/qrlabels?id=1', '/admin/orders/print/1', '/admin/orders/details/1', '/admin/ai', '/admin/ai-knowledge', '/admin/store', '/admin/audit-logs'];

async function login(browser, email, password) {
  const ctx = await browser.createBrowserContext();
  const page = await ctx.newPage();
  await page.goto(BASE + '/account/login', { waitUntil: 'networkidle2' });
  await page.type('#Email', email);
  await page.type('#Password', password);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click('form[action="/account/login"] button[type="submit"]')]);
  await page.close();
  return ctx;
}

async function audit(ctx, url, report, options) {
  const page = await ctx.newPage();
  const issues = [];
  page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') issues.push(m.type() + ': ' + m.text()); });
  page.on('pageerror', e => issues.push('pageerror: ' + e.message));
  page.on('requestfailed', r => { const f = r.failure(); if (f && f.errorText !== 'net::ERR_ABORTED') issues.push('failed: ' + r.url() + ' ' + f.errorText); });

  // Desktop load + timing + accessibility
  await page.setViewport({ width: 1366, height: 900 });
  const started = Date.now();
  const response = await page.goto(BASE + url, { waitUntil: 'networkidle2' });
  const loadMs = Date.now() - started;
  const timing = await page.evaluate(() => { const n = performance.getEntriesByType('navigation')[0]; return { ttfb: Math.round(n.responseStart - n.requestStart), dom: Math.round(n.domContentLoadedEventEnd - n.startTime), transfer: n.transferSize }; });
  // axe runs in a separate page that bypasses CSP (injecting it inline would itself violate the policy)
  const axePage = await ctx.newPage();
  await axePage.setBypassCSP(true);
  await axePage.setViewport({ width: 1366, height: 900 });
  await axePage.goto(BASE + url, { waitUntil: 'networkidle2' });
  await axePage.addScriptTag({ content: axeSource });
  const axe = await axePage.evaluate(async () => {
    const r = await window.axe.run(document, { runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] }, resultTypes: ['violations'] });
    return r.violations.filter(v => v.impact === 'serious' || v.impact === 'critical').map(v => ({ id: v.id, impact: v.impact, nodes: v.nodes.length, sample: v.nodes[0] && v.nodes[0].target.join(' ') }));
  });
  await axePage.close();

  // Phone widths: no horizontal scroll
  const overflow = [];
  for (const width of [360, 390]) {
    await page.setViewport({ width, height: 800, isMobile: true, hasTouch: true });
    await page.reload({ waitUntil: 'networkidle2' });
    const wide = await page.evaluate(() => {
      const w = document.documentElement.clientWidth;
      window.scrollTo(5000, window.scrollY);
      const scrolled = window.scrollX;
      window.scrollTo(0, window.scrollY);
      if (scrolled === 0) return null; // the page cannot be scrolled sideways
      const culprits = [...document.querySelectorAll('body *')].filter(e => { const r = e.getBoundingClientRect(); return r.right > w + 1 && r.width > 0 && getComputedStyle(e).position !== 'fixed'; })
        .slice(0, 3).map(e => e.tagName.toLowerCase() + (e.id ? '#' + e.id : '') + (e.className && typeof e.className === 'string' ? '.' + e.className.split(' ').slice(0, 2).join('.') : ''));
      return { scrollWidth: document.documentElement.scrollWidth, culprits };
    });
    if (wide) overflow.push({ width, ...wide });
  }

  report.push({ url, status: response ? response.status() : null, loadMs, ...timing, issues: [...new Set(issues)].slice(0, 6), axe, overflow });
  await page.close();
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'), headless: true, acceptInsecureCerts: true });
  const report = [];
  try {
    const anon = await browser.createBrowserContext();
    for (const url of publicPages) await audit(anon, url, report);

    // Customer: register a throw-away account (cleaned up afterwards)
    const custCtx = await browser.createBrowserContext();
    const reg = await custCtx.newPage();
    await reg.goto(BASE + '/account/register', { waitUntil: 'networkidle2' });
    await reg.type('#FullName', 'Kiểm Tra Audit');
    await reg.type('#Email', 'audit-' + Date.now() + '@example.com');
    await reg.type('#PhoneNumber', '0912345678');
    await reg.type('#Password', 'Khach@Hang123');
    await reg.type('#ConfirmPassword', 'Khach@Hang123');
    await reg.click('#AcceptTerms');
    await Promise.all([reg.waitForNavigation({ waitUntil: 'networkidle2' }), reg.click('form[action="/account/register"] button[type="submit"]')]);
    await reg.close();
    for (const url of customerPages) await audit(custCtx, url, report);

    const adminCtx = await login(browser, 'admin@furniture.local', process.env.ADMIN_PW);
    for (const url of adminPages) await audit(adminCtx, url, report);
  } catch (e) {
    report.push({ failed: e.message });
    process.exitCode = 1;
  } finally {
    fs.writeFileSync(OUT + '/audit.json', JSON.stringify(report, null, 1));
    for (const r of report) {
      const flags = [];
      if (r.issues && r.issues.length) flags.push('ISSUES ' + r.issues.join(' || '));
      if (r.axe && r.axe.length) flags.push('AXE ' + r.axe.map(a => `${a.id}(${a.nodes}) ${a.sample}`).join('; '));
      if (r.overflow && r.overflow.length) flags.push('OVERFLOW ' + JSON.stringify(r.overflow));
      console.log(`${r.status} ${String(r.loadMs).padStart(5)}ms ttfb=${r.ttfb} ${String(Math.round((r.transfer || 0) / 1024)).padStart(4)}KB ${r.url}${flags.length ? '\n     ' + flags.join('\n     ') : ''}`);
      if (r.failed) console.log('FAILED ' + r.failed);
      // The 404 page reporting its own 404 status is expected; anything else fails the audit.
      const expected404 = r.status === 404 && (r.issues || []).every(i => i.includes('status of 404'));
      if ((r.axe && r.axe.length) || (r.overflow && r.overflow.length) || (r.issues && r.issues.length && !expected404)) process.exitCode = 1;
    }
    await browser.close();
  }
})();
