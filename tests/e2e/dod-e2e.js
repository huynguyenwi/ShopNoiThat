// Definition-of-Done walk-through in a real browser (Edge headless):
// authorization, register / login / logout, variant selection, cart, coupons, checkout, customer orders,
// admin order workflow, stock, review after delivery, wishlist, admin category + product CRUD with variants and image.
const { execSync } = require('child_process');
const fs = require('fs');
const path = require('path');
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const EMAIL_DIR = process.env.EMAIL_DIR;
const IMAGE = path.resolve(__dirname, '../../src/FurnitureStore.Web/wwwroot/images/og-default.png');
const PRODUCT = '/products/sofa-bang-3-cho-oslo-khung-go-soi';
const stamp = Date.now().toString().slice(-6);
const sleep = ms => new Promise(r => setTimeout(r, ms));

const results = [];
const errors = [];
function check(name, ok, detail) { results.push({ name, ok: !!ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + detail : ''}`); }
function sql(query) {
  return execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${query}"`, { encoding: 'utf8' }).trim();
}
function watch(page, who) {
  page.on('console', m => { if (m.type() === 'error') errors.push(`${who} console: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
}
async function go(page, url) { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; }
async function submit(page, selector) {
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
}
async function alertText(page) { return page.$eval('.alert', a => a.textContent.trim().replace(/\s+/g, ' ')).catch(() => ''); }
async function fetchStatus(page, url, method = 'GET') {
  return page.evaluate(async (u, m) => { const r = await fetch(u, { method: m, headers: { Accept: 'application/json' } }); let body = null; try { body = await r.json(); } catch { } return { status: r.status, success: body && body.success }; }, url, method);
}
async function login(page, email, password) {
  await go(page, '/account/login');
  await page.type('#Email', email);
  await page.type('#Password', password);
  await submit(page, 'form[action="/account/login"] button[type="submit"]');
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors', '--no-first-run'] });
  try {
    // ---------- 1. Anonymous access control
    const anonCtx = await browser.createBrowserContext();
    const anon = await anonCtx.newPage(); watch(anon, 'anon');
    await go(anon, '/admin');
    check('Anonymous /admin redirects to login', anon.url().includes('/account/login') && anon.url().toLowerCase().includes('returnurl'), anon.url().replace(BASE, ''));
    const anonApi = await fetchStatus(anon, '/api/admin/chat/conversations');
    check('Anonymous admin API -> 401 JSON', anonApi.status === 401 && anonApi.success === false, JSON.stringify(anonApi));
    await go(anon, '/checkout');
    check('Anonymous /checkout requires sign-in', anon.url().includes('/account/login'));

    // ---------- 2. Register, logout, login
    const email = `dod-${stamp}@example.com`;
    const password = 'Khach@Hang123';
    const ctx = await browser.createBrowserContext();
    const page = await ctx.newPage(); watch(page, 'customer');
    await page.setViewport({ width: 1366, height: 900 });
    await go(page, '/account/register');
    await page.type('#FullName', 'Khách Kiểm Thử DoD');
    await page.type('#Email', email);
    await page.type('#PhoneNumber', '0912345678');
    await page.type('#Password', password);
    await page.type('#ConfirmPassword', password);
    await page.click('#AcceptTerms');
    await submit(page, 'form[action="/account/register"] button[type="submit"]');
    const signedIn = await page.$('form[action="/account/logout"]');
    check('Register signs the customer in', signedIn && !page.url().includes('/register'), page.url().replace(BASE, ''));
    check('New account has role USER', sql(`SELECT r.Name FROM AspNetUsers u JOIN AspNetUserRoles ur ON ur.UserId=u.Id JOIN AspNetRoles r ON r.Id=ur.RoleId WHERE u.Email='${email}'`) === 'USER');
    await page.evaluate(() => document.querySelector('form[action="/account/logout"]').submit());
    await page.waitForNavigation({ waitUntil: 'networkidle2' });
    check('Logout', !(await page.$('form[action="/account/logout"]')));
    await go(page, '/account/login');
    await page.type('#Email', email);
    await page.type('#Password', 'Sai@MatKhau1');
    await submit(page, 'form[action="/account/login"] button[type="submit"]');
    check('Wrong password is rejected', page.url().includes('/account/login') && (await page.content()).includes('không đúng'), await alertText(page) || (await page.$eval('.validation-summary-errors, .text-danger', e => e.textContent.trim()).catch(() => '')));
    await login(page, email, password);
    check('Login', !!(await page.$('form[action="/account/logout"]')));

    // ---------- 3. Customer cannot reach the back office
    const denied = await go(page, '/admin');
    check('Customer /admin -> 403 page', denied === 403 || page.url().includes('accessdenied'), `${denied} ${page.url().replace(BASE, '')}`);
    const custApi = await fetchStatus(page, '/api/admin/chat/conversations');
    check('Customer admin API -> 403 JSON', custApi.status === 403 && custApi.success === false, JSON.stringify(custApi));

    // ---------- 4. Variant selection
    await go(page, PRODUCT);
    const before = await page.evaluate(() => ({ sku: document.querySelector('#variantSku').textContent.trim(), price: document.querySelector('#variantPrice').textContent.trim(), id: document.querySelector('#selectedVariantId').value }));
    // Another color whose variant still has at least 2 in stock (earlier runs consume stock).
    const current = () => page.evaluate(() => ({ sku: document.querySelector('#variantSku').textContent.trim(), price: document.querySelector('#variantPrice').textContent.trim(), id: document.querySelector('#selectedVariantId').value, url: location.search }));
    let after = before;
    for (const swatch of await page.$$('.swatch-button[data-option="color"]')) {
      if (await swatch.evaluate(b => b.classList.contains('active'))) continue;
      await swatch.click();
      await sleep(300);
      const candidate = await current();
      if (candidate.id !== before.id && Number(sql(`SELECT StockQuantity FROM ProductVariants WHERE Id=${candidate.id}`)) >= 2) { after = candidate; break; }
    }
    check('Choosing another color switches the variant (SKU, id, URL)', after.id !== before.id && after.sku !== before.sku, `${before.sku} -> ${after.sku} ${after.price} ${after.url}`);
    const variantId = after.id;
    const stockBefore = Number(sql(`SELECT StockQuantity FROM ProductVariants WHERE Id=${variantId}`));

    // ---------- 5. Cart
    await page.$eval('#quantityInput', i => { i.value = '2'; });
    await page.click('[data-add-to-cart]');
    await page.waitForFunction(() => { const b = document.querySelector('[data-cart-count]'); return b && b.textContent.trim() === '2'; }, { timeout: 10000 });
    check('Add to cart updates the header badge', true, '2');
    await go(page, '/cart');
    const lines = await page.$$eval('input[data-autosubmit-quantity]', i => i.map(x => x.value));
    check('Cart shows the line with quantity 2', lines.length === 1 && lines[0] === '2', JSON.stringify(lines));
    await page.type('#couponCode', 'HETHAN');
    await submit(page, 'form[action="/cart/coupon"] button[type="submit"]');
    const expired = await alertText(page);
    check('Expired coupon is refused', /hết hạn|không còn hiệu lực|không hợp lệ/i.test(expired), expired);
    await page.type('#couponCode', 'chaoban10');
    await submit(page, 'form[action="/cart/coupon"] button[type="submit"]');
    const applied = await alertText(page);
    check('Coupon CHAOBAN10 applies', /áp dụng/i.test(applied) && (await page.content()).includes('CHAOBAN10'), applied);

    // ---------- 6. Checkout (COD)
    await go(page, '/checkout');
    await page.$eval('#Command_FullName', i => { i.value = ''; });
    await page.type('#Command_FullName', 'Khách Kiểm Thử DoD');
    await page.$eval('#Command_Phone', i => { i.value = ''; });
    await page.type('#Command_Phone', '0912345678');
    const province = await page.$$eval('#Command_Province option', o => o.map(x => x.value).filter(Boolean)[0]);
    await page.select('#Command_Province', province);
    await page.type('#Command_Ward', 'Phường Bến Nghé');
    await page.type('#Command_AddressLine', '12 Lê Lợi');
    await page.click('input[name="Command.PaymentMethod"][value="COD"]');
    await submit(page, 'button[form="checkoutForm"]'); // the order button sits in the summary column
    const orderCode = await page.$eval('[data-order-code]', e => e.textContent.trim()).catch(() => null);
    check('Checkout creates an order', !!orderCode && page.url().includes('/checkout/success/'), orderCode);
    const order = sql(`SELECT CONCAT(Status,'|',Subtotal,'|',DiscountAmount,'|',TotalAmount,'|',PaymentMethod) FROM Orders WHERE OrderCode='${orderCode}'`).split('|');
    check('Order stored Pending/COD with the 10% coupon (max 2.000.000đ)', order[0] === 'Pending' && order[4] === 'COD' && Number(order[2]) === Math.min(Number(order[1]) * 0.1, 2000000), order.join(' | '));
    const stockAfter = Number(sql(`SELECT StockQuantity FROM ProductVariants WHERE Id=${variantId}`));
    check('Stock reserved for the order', stockAfter === stockBefore - 2, `${stockBefore} -> ${stockAfter}`);
    check('Cart emptied after checkout', (await page.$eval('[data-cart-count]', b => b.classList.contains('d-none') || b.textContent.trim() === '0').catch(() => true)));
    const mails = EMAIL_DIR && fs.existsSync(EMAIL_DIR) ? fs.readdirSync(EMAIL_DIR).filter(f => fs.readFileSync(path.join(EMAIL_DIR, f), 'utf8').includes(orderCode)) : [];
    check('Order confirmation e-mail sent', mails.length >= 1, mails.length);

    // ---------- 7. Customer order history
    await go(page, '/account/orders');
    check('Order appears in /account/orders', (await page.content()).includes(orderCode));
    await go(page, `/account/orders/${orderCode}`);
    check('Order detail shows "Chờ xác nhận"', (await page.content()).includes('Chờ xác nhận'));
    const otherOrder = sql(`SELECT TOP 1 OrderCode FROM Orders WHERE OrderCode<>'${orderCode}'`);
    const foreign = otherOrder ? await go(page, `/account/orders/${otherOrder}`) : 404;
    check("Customer cannot open someone else's order", foreign === 404, foreign);

    // ---------- 8. Admin processes the order
    const adminCtx = await browser.createBrowserContext();
    const admin = await adminCtx.newPage(); watch(admin, 'admin');
    await admin.setViewport({ width: 1366, height: 900 });
    await login(admin, 'admin@furniture.local', process.env.ADMIN_PW);
    const dash = await go(admin, '/admin');
    check('Admin dashboard', dash === 200 && admin.url().endsWith('/admin'), dash);
    await go(admin, `/admin/orders?search=${orderCode}`);
    const detailLink = await admin.$$eval('a[href*="/admin/orders/"]', a => a.map(x => x.getAttribute('href')).find(h => /\/admin\/orders\/(details\/)?\d+/i.test(h)));
    check('Admin finds the order', !!detailLink, detailLink);
    for (const status of ['Confirmed', 'Processing', 'Shipping', 'Delivered']) {
      await go(admin, detailLink);
      await admin.select('#status', status);
      await submit(admin, 'form[action*="/status"] button[type="submit"]');
      const st = sql(`SELECT Status FROM Orders WHERE OrderCode='${orderCode}'`);
      check(`Admin sets status ${status}`, st === status, `${st} ${await alertText(admin)}`);
    }
    const payment = sql(`SELECT CONCAT(p.Status,'|',o.PaymentStatus) FROM Payments p JOIN Orders o ON o.Id=p.OrderId WHERE o.OrderCode='${orderCode}'`);
    check('COD marked paid on delivery', /Paid|Completed/i.test(payment), payment);
    await go(page, `/account/orders/${orderCode}`);
    check('Customer sees "Đã giao"', (await page.content()).includes('Đã giao'));

    // ---------- 9. Review after delivery -> moderation
    await go(page, PRODUCT + '#reviews');
    const canReview = await page.$('#reviewFormPanel form, form[action$="/reviews"]');
    check('Review form offered after delivery', !!canReview);
    if (canReview) {
      await page.click('[data-bs-target="#reviewFormPanel"]');
      await page.waitForSelector('#reviewFormPanel.show', { visible: true, timeout: 5000 });
      await page.evaluate(() => document.querySelector('#reviewStar5').click());
      await page.type('#reviewTitle', 'Sofa êm, giao nhanh');
      await page.type('#reviewComment', 'Kiểm thử DoD: sofa ngồi êm, màu đúng như hình, giao đúng hẹn.');
      await submit(page, '#reviewFormPanel button[type="submit"]');
      const review = sql(`SELECT CONCAT(r.Rating,'|',r.IsVerifiedPurchase,'|',r.IsHidden) FROM Reviews r JOIN AspNetUsers u ON u.Id=r.UserId WHERE u.Email='${email}'`);
      check('Review saved as a verified purchase and shown', review === '5|1|0' && (await page.content()).includes('Sofa êm, giao nhanh'), `${review} ${await alertText(page)}`);
    }

    // ---------- 10. Wishlist
    await go(page, PRODUCT);
    await page.click('[data-wishlist-product]');
    await page.waitForFunction(() => document.querySelector('[data-wishlist-product]').getAttribute('aria-pressed') === 'true', { timeout: 10000 });
    await go(page, '/wishlist');
    check('Wishlist', (await page.content()).includes('Sofa băng 3 chỗ Oslo'));

    // ---------- 11. Admin CRUD: category + product with 2 variants and an image
    const catName = `Kiểm thử DoD ${stamp}`;
    await go(admin, '/admin/categories/create');
    await admin.type('#Command_Name', catName);
    await submit(admin, 'form button[type="submit"].btn-primary');
    const catId = sql(`SELECT Id FROM Categories WHERE Name=N'${catName}'`);
    check('Admin creates a category', !!catId, `${catId} ${await alertText(admin)}`);

    await go(admin, '/admin/products/create');
    const productName = `Bàn trà kiểm thử ${stamp}`;
    const sku = `DOD-${stamp}`;
    await admin.type('#Command_Name', productName);
    await admin.type('#Command_Sku', sku);
    await admin.select('#Command_CategoryId', catId);
    await admin.type('#Command_ShortDescription', 'Sản phẩm tạo bởi kiểm thử DoD.');
    await admin.select('#Command_Status', 'Active');
    if ((await admin.$$('[data-variant-row]:not(template [data-variant-row])')).length === 0) await admin.click('[data-add-variant]');
    await admin.click('[data-add-variant]');
    const rows = await admin.$$('#productForm tbody[data-variant-row]');
    const colorIds = await admin.$$eval('#productForm tbody[data-variant-row] select[data-variant-color] option', o => [...new Set(o.map(x => x.value).filter(Boolean))].slice(0, 2));
    const prices = ['3500000', '3900000'];
    for (let i = 0; i < rows.length && i < 2; i++) {
      const r = rows[i];
      await r.$eval('select[data-variant-color]', (s, v) => { s.value = v; s.dispatchEvent(new Event('change', { bubbles: true })); }, colorIds[i]);
      const skuInput = await r.$('input[data-variant-sku]');
      await skuInput.click({ clickCount: 3 }); await skuInput.type(`${sku}-${i + 1}`);
      await r.$eval('input[name$=".Price"]', (e, v) => { e.value = v; }, prices[i]);
      await r.$eval('input[name$=".StockQuantity"]', e => { e.value = '5'; });
    }
    const fileInput = await admin.$('input[type="file"][name="images"]');
    await fileInput.uploadFile(IMAGE);
    await submit(admin, '#productForm button[type="submit"].btn-primary');
    const productRow = sql(`SELECT CONCAT(Id,'|',Slug,'|',Status,'|',BasePrice) FROM Products WHERE Sku='${sku}'`).split('|');
    const variantCount = productRow[0] ? sql(`SELECT COUNT(*) FROM ProductVariants WHERE ProductId=${productRow[0]}`) : '0';
    const imageCount = productRow[0] ? sql(`SELECT COUNT(*) FROM ProductImages WHERE ProductId=${productRow[0]}`) : '0';
    check('Admin creates a product with 2 variants and an image', productRow[0] && variantCount === '2' && imageCount === '1', `${productRow.join(' | ')} variants=${variantCount} images=${imageCount} ${await alertText(admin)}`);
    const slug = productRow[1];
    const publicStatus = await go(anon, `/products/${slug}`);
    const shownPrice = await anon.$eval('#variantPrice', e => e.textContent.trim()).catch(() => '');
    check('New product is live on the storefront', publicStatus === 200 && shownPrice.includes('3.500.000'), `${publicStatus} ${shownPrice}`);
    const img = await anon.$eval('#galleryMain', async i => { const r = await fetch(i.src); return { src: i.getAttribute('src'), status: r.status, type: r.headers.get('content-type'), cache: r.headers.get('cache-control') }; }).catch(e => ({ error: e.message }));
    check('Uploaded image is served', img.status === 200 && /^\/uploads\//.test(img.src) && /^image\//.test(img.type), JSON.stringify(img));

    await go(admin, `/admin/products/edit/${productRow[0]}`);
    await admin.$eval('#productForm tbody[data-variant-row] input[name$=".Price"]', e => { e.value = '3200000'; });
    await submit(admin, '#productForm button[type="submit"].btn-primary');
    await go(anon, `/products/${slug}`);
    const editedPrice = await anon.$eval('#variantPrice', e => e.textContent.trim()).catch(() => '');
    check('Price edit shows immediately (cache invalidated)', editedPrice.includes('3.200.000'), editedPrice);
    const history = sql(`SELECT COUNT(*) FROM ProductPriceHistory h JOIN ProductVariants v ON v.Id=h.ProductVariantId WHERE v.ProductId=${productRow[0]}`);
    check('Price change recorded in history', Number(history) >= 1, history);

    await go(admin, `/admin/products/edit/${productRow[0]}`);
    admin.once('dialog', d => d.accept());
    await admin.evaluate(() => { const f = [...document.querySelectorAll('form')].find(x => /\/admin\/products\/delete\/\d+$/i.test(x.getAttribute('action'))); f.removeAttribute('data-confirm'); f.submit(); });
    await admin.waitForNavigation({ waitUntil: 'networkidle2' });
    const gone = await go(anon, `/products/${slug}`);
    check('Deleted product is gone from the storefront (soft delete)', gone === 404 && sql(`SELECT IsDeleted FROM Products WHERE Id=${productRow[0]}`) === '1', gone);
    const catDelete = await admin.evaluate(async id => {
      const token = document.querySelector('input[name="__RequestVerificationToken"]').value;
      const r = await fetch('/admin/categories/delete/' + id, { method: 'POST', body: new URLSearchParams({ __RequestVerificationToken: token }), redirect: 'follow' });
      return r.status;
    }, catId);
    check('Category with a (deleted) product cannot be removed, or is removed cleanly', catDelete === 200, `${catDelete} exists=${sql(`SELECT COUNT(*) FROM Categories WHERE Id=${catId}`)}`);

    // ---------- 12. CSRF: a form POST without the anti-forgery token is rejected
    const csrf = await page.evaluate(async () => (await fetch('/cart/coupon', { method: 'POST', body: new URLSearchParams({ code: 'GIAM500K' }) })).status);
    check('POST without anti-forgery token -> 400', csrf === 400, csrf);
  } catch (e) {
    check('Script error', false, e.stack);
  } finally {
    const failed = results.filter(r => !r.ok).length;
    console.log(`\n${results.length - failed}/${results.length} passed`);
    console.log(errors.length ? 'Browser errors:\n  ' + [...new Set(errors)].join('\n  ') : 'Browser errors: none');
    process.exitCode = failed ? 1 : 0;
    await browser.close();
  }
})();
