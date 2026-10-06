// Cart lines can be ticked (only ticked ones are ordered, the rest stay in the cart) and addresses are picked
// province → ward / commune from /api/locations (no district since 07/2025).
const path = require('path');
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { chooseAddress } = require('./lib/address');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const OUT = path.join(__dirname, 'output');
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 200) : ''}`); };
const go = async (page, url) => { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
const text = (page, selector) => page.$eval(selector, e => e.textContent.replace(/\s+/g, ' ').trim());
const money = value => Number(String(value).replace(/[^\d]/g, ''));
const overflowX = page => page.evaluate(() => { window.scrollTo(5000, window.scrollY); const x = window.scrollX; window.scrollTo(0, window.scrollY); return x; });
const checkoutButton = page => text(page, '[data-cart-checkout]');
const wardValues = page => page.$$eval('#Command_Ward option', o => o.map(x => x.value).filter(Boolean));

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    const page = await (await browser.createBrowserContext()).newPage();
    page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    page.on('pageerror', e => errors.push('pageerror: ' + e.message));
    await page.setViewport({ width: 1366, height: 900 });

    // ---------- Customer with a chair and a dining set in the cart
    const email = `chon-${Date.now()}@example.com`;
    await go(page, '/account/register');
    await page.type('#FullName', 'Khách Tích Chọn');
    await page.type('#Email', email);
    await page.type('#PhoneNumber', '0912345678');
    await page.type('#Password', 'Khach@Hang123');
    await page.type('#ConfirmPassword', 'Khach@Hang123');
    await page.click('#AcceptTerms');
    await click(page, 'form[action="/account/register"] button[type="submit"]');
    const chairId = sql("SELECT TOP 1 v.Id FROM ProductVariants v JOIN Products p ON p.Id = v.ProductId WHERE p.Sku = 'GA-CURVE' AND v.StockQuantity >= 3 ORDER BY v.Id");
    const setId = sql("SELECT TOP 1 v.Id FROM ProductVariants v JOIN Products p ON p.Id = v.ProductId WHERE p.Sku = 'BBA-ANGIA' AND v.StockQuantity >= 3 ORDER BY v.Id");
    for (const id of [chairId, setId]) {
      await page.evaluate(v => window.FS.api('/api/cart', { method: 'POST', body: { variantId: Number(v), quantity: 1 } }), id);
    }

    await go(page, '/cart');
    const lines = await page.$$eval('[data-cart-select]', b => b.map(x => ({ id: x.getAttribute('data-cart-select'), checked: x.checked })));
    check('Every line has a tick box, ticked by default', lines.length === 2 && lines.every(l => l.checked) && await page.$eval('#cartSelectAll', b => b.checked), JSON.stringify(lines));
    check('Order button counts the ticked products', (await checkoutButton(page)) === 'Liên hệ đặt hàng (2)');

    // ---------- Untick the chair: totals follow in place
    const chairLine = sql(`SELECT ci.Id FROM CartItems ci JOIN Carts c ON c.Id = ci.CartId JOIN AspNetUsers u ON u.Id = c.UserId WHERE u.Email = '${email}' AND ci.ProductVariantId = ${chairId}`);
    const setPrice = money(sql(`SELECT Price FROM ProductVariants WHERE Id = ${setId}`).split('.')[0]);
    await page.evaluate(() => { window.__samePage = true; });
    await page.click(`#select-${chairLine}`);
    await page.waitForFunction(() => document.querySelector('[data-cart-checkout]').textContent.includes('(1)'), { timeout: 10000 });
    check('Unticking updates the summary without reloading the page', await page.evaluate(() => window.__samePage === true));
    check('Subtotal covers the ticked set only', money(await text(page, '[data-summary="subtotal"]')) === setPrice, await text(page, '[data-summary="subtotal"]'));
    check('Keyboard focus stays on the box', await page.evaluate(id => document.activeElement && document.activeElement.id === 'select-' + id, chairLine));
    check('Saved: the chair line is unticked in the database', sql(`SELECT CAST(IsSelected AS int) FROM CartItems WHERE Id = ${chairLine}`) === '0');
    check('The cart keeps both products', (await page.$$('[data-cart-line]')).length === 2 && (await text(page, '[data-cart-selected-count]')).includes('1 / 2'));

    // Select all / none
    await page.click('#cartSelectAll');
    await page.waitForFunction(() => document.querySelector('[data-cart-checkout]').textContent.includes('(2)'), { timeout: 10000 });
    await page.click('#cartSelectAll');
    await page.waitForFunction(() => document.querySelector('[data-cart-checkout]').disabled, { timeout: 10000 });
    check('Nothing ticked: the order button asks to choose', (await checkoutButton(page)) === 'Chọn sản phẩm để đặt hàng');
    const toCart = await go(page, '/checkout');
    check('Contact-order page sends back to the cart when nothing is ticked', page.url().endsWith('/cart') && (await page.content()).includes('Vui lòng tích chọn sản phẩm'), `${toCart} ${page.url()}`);

    const setLine = sql(`SELECT ci.Id FROM CartItems ci JOIN Carts c ON c.Id = ci.CartId JOIN AspNetUsers u ON u.Id = c.UserId WHERE u.Email = '${email}' AND ci.ProductVariantId = ${setId}`);
    await page.click(`#select-${setLine}`);
    await page.waitForFunction(() => document.querySelector('[data-cart-checkout]').textContent.includes('(1)'), { timeout: 10000 });
    await page.screenshot({ path: path.join(OUT, 'cart-select-1366.png'), fullPage: true });

    // ---------- Contact-order page: ticked product only, province → ward
    await click(page, 'a[data-cart-checkout]');
    const summary = await text(page, '[data-checkout-summary]');
    check('Only the ticked product is ordered', summary.includes('Bộ bàn ăn gỗ sồi Nga An Gia') && !summary.includes('Ghế ăn') && summary.includes('1 sản phẩm khác vẫn ở trong'), summary.slice(0, 160));
    check('No district field', (await page.$('[name="Command.District"]')) === null && !(await page.content()).includes('Quận / Huyện'));
    check('Ward list waits for a province', await page.$eval('#Command_Ward', w => w.disabled));

    await chooseAddress(page, 'TP. Hồ Chí Minh', 'Phường Sài Gòn');
    const hcm = await page.evaluate(() => ({
      groups: [...document.querySelectorAll('#Command_Ward optgroup')].map(g => g.label),
      label: document.querySelector('#Command_Ward option[value="Phường Sài Gòn"]').textContent,
      count: [...document.querySelectorAll('#Command_Ward option')].filter(o => o.value).length
    }));
    const hcmCount = await page.evaluate(async () => (await (await fetch('/api/locations/provinces')).json()).data.find(p => p.name === 'TP. Hồ Chí Minh').wardCount);
    check('Wards of TP. Hồ Chí Minh loaded from the API, grouped Phường / Xã / Đặc khu', hcm.count === hcmCount && hcm.groups.join('|') === 'Phường|Xã|Đặc khu' && hcm.label === 'Sài Gòn', JSON.stringify(hcm));
    await chooseAddress(page, 'TP. Hà Nội', 'Phường Hoàn Kiếm');
    const hanoi = await wardValues(page);
    check('Another province replaces the list', hanoi.includes('Phường Hoàn Kiếm') && !hanoi.includes('Phường Sài Gòn'), hanoi.length);
    await chooseAddress(page, 'TP. Hồ Chí Minh', 'Phường Sài Gòn');
    await page.type('#Command_AddressLine', '9 Đồng Khởi');

    const phone = await page.browserContext().newPage();
    await phone.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
    await go(phone, '/checkout');
    check('Phone (390px): contact page has no horizontal scroll', (await overflowX(phone)) === 0);
    await go(phone, '/cart');
    check('Phone (390px): cart with tick boxes has no horizontal scroll', (await overflowX(phone)) === 0);
    await phone.screenshot({ path: path.join(OUT, 'cart-select-390.png'), fullPage: true });
    await phone.close();

    await click(page, 'button[form="checkoutForm"]');
    const code = await page.$eval('[data-order-code]', e => e.textContent.trim()).catch(() => null);
    const ordered = code ? sql(`SELECT STRING_AGG(oi.Sku, ',') FROM OrderItems oi JOIN Orders o ON o.Id = oi.OrderId WHERE o.OrderCode = '${code}'`) : '';
    const address = code ? sql(`SELECT CONCAT(a.Ward, '|', a.Province, '|', ISNULL(a.District, '-')) FROM OrderAddresses a JOIN Orders o ON o.Id = a.OrderId WHERE o.OrderCode = '${code}'`) : '';
    check('Order placed with the set only, address ward + province, no district', !!code && !ordered.includes('GA-CURVE') && ordered.startsWith('BBA-ANGIA') && address === 'Phường Sài Gòn|TP. Hồ Chí Minh|-', `${code} ${ordered} ${address}`);
    await go(page, '/cart');
    const left = await page.$$eval('[data-cart-select]', b => b.map(x => x.checked));
    check('The unticked chair is still in the cart', left.length === 1 && left[0] === false, JSON.stringify(left));

    // ---------- Saved addresses fill province + ward; an outdated ward is flagged
    await page.click('[data-cart-select]');
    await page.waitForFunction(() => document.querySelector('a[data-cart-checkout]'), { timeout: 10000 });
    const userId = sql(`SELECT Id FROM AspNetUsers WHERE Email = '${email}'`);
    sql(`INSERT INTO CustomerAddresses (UserId, Label, RecipientName, Phone, AddressLine, Ward, District, Province, IsDefault, CreatedAt) VALUES ('${userId}', N'Cũ', N'Địa Chỉ Cũ', '0912345678', N'1 Lê Lợi', N'Phường Bến Nghé', N'Quận 1', N'TP. Hồ Chí Minh', 0, SYSUTCDATETIME())`);
    await click(page, 'a[data-cart-checkout]');
    await chooseAddress(page, 'TP. Hà Nội');
    const radios = await page.$$('[data-address-fill]');
    let savedRadio = null, oldRadio = null;
    for (const r of radios) { const w = await r.evaluate(x => x.dataset.ward); if (w === 'Phường Sài Gòn') savedRadio = r; if (w === 'Phường Bến Nghé') oldRadio = r; }
    await savedRadio.click();
    await page.waitForFunction(() => document.getElementById('Command_Ward').value === 'Phường Sài Gòn', { timeout: 10000 });
    check('Saved address fills province and ward', (await page.$eval('#Command_Province', s => s.value)) === 'TP. Hồ Chí Minh');
    await oldRadio.click();
    await page.waitForFunction(() => !document.querySelector('[data-ward-hint]').hidden, { timeout: 10000 });
    check('An outdated saved ward (before 07/2025) asks to choose again', (await text(page, '[data-ward-hint]')).includes('“Phường Bến Nghé” không có trong danh sách') && (await page.$eval('#Command_Ward', w => w.value)) === '', await text(page, '[data-ward-hint]'));

    // ---------- Address book
    await go(page, '/account/addresses');
    check('Address book: no district, province → ward', (await page.$('[name="Command.District"]')) === null && !!(await page.$('#Command_Ward')));
    await page.type('#Command_RecipientName', 'Người Nhận Mới');
    await page.type('#Command_Phone', '0987654321');
    await chooseAddress(page, 'Khánh Hòa', 'Phường Nha Trang');
    await page.type('#Command_AddressLine', '5 Trần Phú');
    await click(page, 'form[action^="/account/addresses"] button[type="submit"].btn-primary');
    check('Address saved with ward of the province', sql(`SELECT CONCAT(Ward, '|', Province, '|', ISNULL(District, '-')) FROM CustomerAddresses WHERE UserId = '${userId}' AND RecipientName = N'Người Nhận Mới'`) === 'Phường Nha Trang|Khánh Hòa|-');
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
