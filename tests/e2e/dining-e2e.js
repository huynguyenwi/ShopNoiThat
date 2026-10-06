// Dining-set store end to end: the home page and menu lead with the Russian-oak dining sets (table 1m2 + 4 chairs /
// 1m6 + 6 chairs, walnut color); the cart and the "Liên hệ đặt hàng" page never charge delivery (the store quotes it when
// it calls back); staff record the quoted fee on the order and the total, delivery slip, customer page and e-mail follow.
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EMAIL_DIR = process.env.EMAIL_DIR;
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const OUT = path.join(__dirname, 'output');
const SET = '/products/bo-ban-an-go-soi-nga-an-gia';
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 200) : ''}`); };
const watch = (page, who) => {
  page.on('console', m => { if (m.type() === 'error') errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
};
const go = async (page, url) => { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
const text = (page, selector) => page.$eval(selector, e => e.textContent.replace(/\s+/g, ' ').trim());
const money = value => Number(String(value).replace(/[^\d]/g, ''));
const vnd = value => Math.round(value).toLocaleString('vi-VN').replace(/,/g, '.') + '₫';
const overflowX = page => page.evaluate(() => { window.scrollTo(5000, window.scrollY); const x = window.scrollX; window.scrollTo(0, window.scrollY); return x; });

async function newPage(browser, who, width = 1366) {
  const page = await (await browser.createBrowserContext()).newPage();
  watch(page, who);
  await page.setViewport({ width, height: 900 });
  return page;
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

async function ask(page, question) {
  const before = await page.$$eval('#aiMessages .ai-answer', a => a.length);
  await page.type('#aiInput', question);
  await page.click('#aiSend');
  await page.waitForFunction(n => document.querySelectorAll('#aiMessages .ai-answer').length > n, { timeout: 15000 }, before);
  return page.$eval('#aiMessages .ai-answer:last-of-type .chat-bubble', b => b.textContent);
}

/** Rows of the visible top-level menu items: one row means the menu does not wrap. */
const menuRows = page => page.$$eval('#mainNav > .navbar-nav > .nav-item', items =>
  [...new Set(items.filter(i => i.offsetParent !== null).map(i => Math.round(i.getBoundingClientRect().top)))]);

(async () => {
  fs.mkdirSync(OUT, { recursive: true });
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    // ---------- Home page and menu
    const visitor = await newPage(browser, 'visitor');
    await go(visitor, '/');
    await visitor.screenshot({ path: path.join(OUT, 'dining-home-1366.png') });
    check('Hero: Russian-oak dining sets in walnut', (await text(visitor, 'h1')).includes('Bộ bàn ăn gỗ sồi Nga') && (await text(visitor, '.hero')).includes('Bàn 1m2 + 4 ghế'), await text(visitor, 'h1'));
    check('Hero illustration: dining room', await visitor.$eval('.hero-illustration', i => i.src.includes('hero-dining-room.svg') && i.complete && i.naturalWidth > 0));
    const focus = await visitor.$eval('[data-focus-section]', s => ({
      top: s.getBoundingClientRect().top + window.scrollY,
      roomsTop: document.getElementById('rooms-title').getBoundingClientRect().top + window.scrollY,
      cards: [...s.querySelectorAll('.product-card')].map(c => c.textContent.replace(/\s+/g, ' ')),
      sizes: [...s.querySelectorAll('.focus-size strong')].map(x => x.textContent.trim())
    }));
    check('Dining sets are the first section, before the rooms', focus.top < focus.roomsTop, `${focus.top} < ${focus.roomsTop}`);
    check('Dining-set section lists the sets', focus.cards.length >= 6 && focus.cards.filter(c => c.includes('gỗ sồi Nga')).length >= 6, focus.cards.length);
    check('Quick links by size, 4 and 6 chairs first', focus.sizes[0] === 'Bàn 1m2 + 4 ghế' && focus.sizes[1] === 'Bàn 1m6 + 6 ghế', focus.sizes.join(' | '));
    check('Main menu: "Bộ bàn ăn"', await visitor.$eval('#mainNav a.nav-link[href="/products?category=bo-ban-an"]', a => a.textContent.trim() === 'Bộ bàn ăn' && a.offsetParent !== null));
    check('Menu on one row at 1366px', (await menuRows(visitor)).length === 1, JSON.stringify(await menuRows(visitor)));
    await visitor.setViewport({ width: 1100, height: 900 });
    await go(visitor, '/');
    check('Menu on one row at 1100px', (await menuRows(visitor)).length === 1, JSON.stringify(await menuRows(visitor)));
    await visitor.screenshot({ path: path.join(OUT, 'dining-home-1100.png') });
    await visitor.setViewport({ width: 1366, height: 900 });
    await go(visitor, '/');

    await click(visitor, '[data-focus-section] .focus-size[href$="size=set-6-ghe-160"]');
    const sixChair = await visitor.$$eval('.product-card', c => c.map(x => x.textContent.replace(/\s+/g, ' ')));
    check('6-chair link lists the Russian-oak sets only', visitor.url().includes('size=set-6-ghe-160') && sixChair.length === 6 && sixChair.every(c => c.includes('gỗ sồi Nga')), `${sixChair.length} cards`);

    const phone = await newPage(browser, 'phone', 390);
    await go(phone, '/');
    check('Phone (390px): home has no horizontal scroll', (await overflowX(phone)) === 0);
    await phone.screenshot({ path: path.join(OUT, 'dining-home-390.png'), fullPage: false });

    // ---------- Product page: sizes, material, color, price per size
    await go(visitor, SET);
    const sizes = await visitor.$$eval('[data-option="size"]', b => b.map(x => x.dataset.label));
    check('Set sizes: table 1m2 + 4 chairs, table 1m6 + 6 chairs', sizes.join('|') === 'Bàn 1m2 + 4 ghế|Bàn 1m6 + 6 ghế', sizes.join(' | '));
    const page = await visitor.content();
    check('Russian oak, walnut color shown', page.includes('Gỗ sồi Nga') && page.includes('Nâu óc chó'));
    const fourPrice = money(await text(visitor, '#variantPrice'));
    await visitor.click('[data-option="size"][data-label="Bàn 1m6 + 6 ghế"]');
    await visitor.waitForFunction(p => Number(document.getElementById('variantPrice').textContent.replace(/[^\d]/g, '')) !== p, { timeout: 5000 }, fourPrice);
    const sixPrice = money(await text(visitor, '#variantPrice'));
    check('6-chair set costs more than 4-chair', sixPrice > fourPrice, `${vnd(fourPrice)} -> ${vnd(sixPrice)}`);
    check('Buy button reads "Liên hệ đặt hàng"', (await text(visitor, '[data-buy-now]')) === 'Liên hệ đặt hàng');
    check('Delivery line: fee quoted on contact', (await text(visitor, '.service-list')).includes('phí báo khi liên hệ'));

    // ---------- Customer: cart, contact-order page, success
    const customer = await newPage(browser, 'customer');
    const email = await register(customer, 'dining');
    await go(customer, SET);
    await customer.click('[data-option="size"][data-label="Bàn 1m6 + 6 ghế"]');
    await customer.waitForFunction(() => document.querySelector('[data-option="size"][data-label="Bàn 1m6 + 6 ghế"]').classList.contains('active'));
    await click(customer, '[data-buy-now]');
    check('"Liên hệ đặt hàng" on the product page opens the contact-order page', customer.url().endsWith('/checkout'), customer.url());

    await go(customer, '/cart');
    const cart = await text(customer, '.order-summary');
    check('Cart: delivery quoted on contact, total = goods', cart.includes('Cửa hàng báo khi liên hệ') && cart.includes('Tổng tiền hàng') && !/miễn phí/i.test(cart), cart);
    check('Cart button: "Liên hệ đặt hàng"', (await text(customer, '.order-summary a.btn-primary')) === 'Liên hệ đặt hàng');

    await go(customer, '/checkout');
    await customer.screenshot({ path: path.join(OUT, 'dining-contact-1366.png'), fullPage: true });
    check('Contact page title', (await text(customer, 'h1')) === 'Liên hệ đặt hàng');
    check('No payment step', (await customer.$$('input[name="Command.PaymentMethod"]')).length === 0);
    check('Next steps explained (call back, delivery fee, pay on delivery)', (await customer.$$eval('.contact-steps li', l => l.length)) === 3);
    const subtotal = money(await text(customer, '[data-summary="subtotal"]'));
    const total = money(await text(customer, '[data-summary="total"]'));
    check('Summary: shipping quoted on contact, total = subtotal', (await text(customer, '[data-summary="shipping"]')) === 'Cửa hàng báo khi liên hệ' && total === subtotal && subtotal === sixPrice, `${vnd(subtotal)} / ${vnd(total)}`);
    const phoneView = await customer.browserContext().newPage();
    watch(phoneView, 'customer-phone');
    await phoneView.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
    await go(phoneView, '/checkout');
    check('Phone (390px): contact page has no horizontal scroll', (await overflowX(phoneView)) === 0);
    await phoneView.screenshot({ path: path.join(OUT, 'dining-contact-390.png'), fullPage: true });
    await phoneView.close();

    await customer.type('#Command_AddressLine', '25 Đường Bàn Ăn');
    await customer.select('#Command_Province', await customer.$$eval('#Command_Province option', o => o.map(x => x.value).filter(Boolean)[0]));
    await customer.$eval('#Command_Ward', i => { i.value = 'Phường 3'; });
    await customer.type('#Command_Note', 'Gọi sau 18h, tầng 3 có thang máy');
    await click(customer, 'button[form="checkoutForm"]');
    const code = await customer.$eval('[data-order-code]', e => e.textContent.trim()).catch(() => null);
    const success = await text(customer, 'main');
    check('Success: request sent, the store will call back', !!code && (await text(customer, 'h1')) === 'Đã gửi yêu cầu đặt hàng!' && success.includes('0912345678') && success.includes('chưa gồm phí giao hàng'), code);
    const [status, method, fee, sub, discount, orderTotal, orderId] = sql(`SELECT CONCAT(Status,'|',PaymentMethod,'|',ShippingFee,'|',Subtotal,'|',DiscountAmount,'|',TotalAmount,'|',Id) FROM Orders WHERE OrderCode='${code}'`).split('|');
    check('Order: pending, pay on delivery, no shipping charged', status === 'Pending' && method === 'COD' && Number(fee) === 0 && Number(orderTotal) === Number(sub) - Number(discount), `${status} ${method} fee=${fee} total=${orderTotal}`);
    const item = sql(`SELECT CONCAT(MaterialName,'|',ColorName,'|',SizeName) FROM OrderItems WHERE OrderId=${orderId}`);
    check('Order line: Gỗ sồi Nga / Nâu óc chó / Bàn 1m6 + 6 ghế', item === 'Gỗ sồi Nga|Nâu óc chó|Bàn 1m6 + 6 ghế', item);
    if (EMAIL_DIR) {
      const mail = fs.readdirSync(EMAIL_DIR).map(f => fs.readFileSync(path.join(EMAIL_DIR, f), 'utf8')).find(m => m.includes(code) && m.includes('Đã nhận yêu cầu đặt hàng'));
      check('Confirmation e-mail: fee quoted on contact', !!mail && mail.includes('Cửa hàng báo khi liên hệ') && mail.includes('Tổng tiền hàng'));
    }
    check('Admin notified to call back', sql(`SELECT TOP 1 Message FROM Notifications WHERE Link='/admin/orders/details/${orderId}'`).includes('Gọi lại'));

    // ---------- Admin records the fee quoted by phone
    const admin = await newPage(browser, 'admin');
    await go(admin, '/account/login');
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await click(admin, 'form[action="/account/login"] button[type="submit"]');
    await go(admin, `/admin/orders/details/${orderId}`);
    check('Admin: fee not quoted yet', (await text(admin, '[data-shipping-fee]')) === 'Cửa hàng báo khi liên hệ');
    await go(admin, `/admin/orders/print/${orderId}`);
    check('Slip warns the COD amount lacks delivery', (await text(admin, '.slip-cod')).includes('Chưa gồm phí giao hàng'));

    await go(admin, `/admin/orders/details/${orderId}`);
    await admin.$eval('#shippingFee', i => { i.value = ''; });
    await admin.type('#shippingFee', '450000');
    await click(admin, '[data-shipping-fee-form] button[type="submit"]');
    const expected = Number(orderTotal) + 450000;
    check('Fee saved, total updated', (await text(admin, '[data-shipping-fee]')) === '450.000₫' && money(await text(admin, '[data-order-total]')) === expected, await text(admin, '[data-order-total]'));
    await admin.screenshot({ path: path.join(OUT, 'dining-admin-fee.png'), fullPage: true });
    await go(admin, `/admin/orders/print/${orderId}`);
    const slip = await text(admin, '.slip-cod');
    check('Slip collects goods + delivery', slip.includes(`Thu hộ (COD): ${vnd(expected)}`) && !slip.includes('Chưa gồm'), slip);
    await go(customer, `/account/orders/${code}`);
    check('Customer sees the quoted fee and new total', (await text(customer, '.summary-lines')).includes('450.000₫') && (await text(customer, '.summary-lines')).includes(vnd(expected)));
    if (EMAIL_DIR) {
      const mail = fs.readdirSync(EMAIL_DIR).map(f => fs.readFileSync(path.join(EMAIL_DIR, f), 'utf8')).find(m => m.includes(code) && m.includes('đã báo phí giao hàng'));
      check('Customer e-mailed the fee', !!mail && mail.includes('450.000₫'));
    }

    // Wrong amount (browser checks bypassed): refused by the server
    await go(admin, `/admin/orders/details/${orderId}`);
    await admin.$eval('[data-shipping-fee-form]', f => { f.noValidate = true; f.querySelector('#shippingFee').value = '-5'; });
    await click(admin, '[data-shipping-fee-form] button[type="submit"]');
    check('Negative fee refused with a message', /phải từ 0/.test(await admin.content()) && Number(sql(`SELECT ShippingFee FROM Orders WHERE Id=${orderId}`)) === 450000);

    for (const s of ['Confirmed', 'Processing', 'Shipping', 'Delivered']) {
      await admin.select('#status', s);
      await click(admin, 'form[action$="/status/' + orderId + '"] button[type="submit"]');
    }
    check('Delivered: the fee can no longer be changed', (await admin.$('[data-shipping-fee-form]')) === null && (await text(admin, '[data-shipping-fee]')) === '450.000₫');

    // ---------- Assistant without an AI model
    await go(visitor, '/');
    await visitor.click('#aiLauncher');
    await visitor.waitForSelector('#aiPanel:not([hidden])');
    const chips = await visitor.$$eval('#aiMessages .ai-welcome .ai-chip', c => c.map(x => x.textContent.trim()));
    check('Widget suggests the Russian-oak question', chips.includes('Gỗ sồi Nga có bền?'), chips.join(' | '));
    const shipping = await ask(visitor, 'phí ship bao nhiêu');
    check('Assistant: delivery fee quoted when the store calls', /báo phí/.test(shipping) && !/10\.000\.000/.test(shipping), shipping);
    const oak = await ask(visitor, 'gỗ sồi Nga có bền không?');
    check('Assistant explains gỗ sồi Nga', oak.includes('Gỗ sồi Nga') && /Ưu điểm/.test(oak), oak);
    const howTo = await ask(visitor, 'cách đặt hàng trên web');
    check('Assistant: how to order = contact request', howTo.includes('Liên hệ đặt hàng') && howTo.includes('Gửi yêu cầu'), howTo);
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
