// Assistant without an AI model (no AI:ApiKey): everyday questions answered from the store's own data, in the real widget.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 170) : ''}`); };
const watch = (page, who) => {
  page.on('console', m => { if (m.type() === 'error') errors.push(`${who}: ${m.text()}`); });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
};
const go = (page, url) => page.goto(BASE + url, { waitUntil: 'networkidle2' });
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);

async function openWidget(page) {
  await page.click('#aiLauncher');
  await page.waitForSelector('#aiPanel:not([hidden])');
}

async function answers(page) { return page.$$eval('#aiMessages .ai-answer', a => a.length); }

async function lastAnswer(page) {
  return page.$eval('#aiMessages .ai-answer:last-of-type', a => ({
    text: a.querySelector('.chat-bubble').textContent,
    cards: [...a.querySelectorAll('.ai-card')].map(c => ({ name: c.querySelector('.ai-card-name').textContent, price: c.querySelector('.ai-card-price strong').textContent })),
    chips: [...a.querySelectorAll('.ai-chip')].map(c => c.textContent.trim())
  }));
}

async function ask(page, question) {
  const before = await answers(page);
  await page.type('#aiInput', question);
  await page.click('#aiSend');
  await page.waitForFunction(n => document.querySelectorAll('#aiMessages .ai-answer').length > n, { timeout: 15000 }, before);
  return lastAnswer(page);
}

const price = text => Number(text.replace(/[^\d]/g, ''));

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    const page = await (await browser.createBrowserContext()).newPage();
    watch(page, 'visitor');
    await page.setViewport({ width: 1366, height: 900 });
    await go(page, '/');
    const status = await page.evaluate(async () => (await fetch('/api/ai/status')).json());
    check('Status API reports no AI model', status.success && status.data.aiEnabled === false, JSON.stringify(status.data));
    await openWidget(page);
    const header = await page.$eval('#aiPanel .chat-status', e => e.textContent.trim());
    const chips = await page.$$eval('#aiMessages .ai-welcome .ai-chip', c => c.map(x => x.textContent.trim()));
    check('No AI model: widget says it answers everyday questions', /hỏi đáp nhanh/.test(header) && chips.includes('Mã giảm giá'), `${header} | ${chips.join(', ')}`);

    // Welcome chip asking two policies at once
    await page.click('#aiMessages .ai-welcome .ai-chip[data-ai-say*="giao hàng và bảo hành"]');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length === 1, { timeout: 15000 });
    const policies = await lastAnswer(page);
    check('Chip "Giao hàng & bảo hành" answers both', /giao hàng/i.test(policies.text) && /bảo hành/i.test(policies.text), policies.text);

    const hours = await ask(page, 'Cửa hàng mở cửa mấy giờ?');
    check('Opening hours from the store settings', /mở cửa \d{2}:\d{2}/.test(hours.text) && hours.cards.length === 0, hours.text);
    const hotline = await ask(page, 'cho minh xin so hotline');       // typed without accents
    check('Hotline (question typed without accents)', /Hotline: \S+/.test(hotline.text), hotline.text);
    const coupons = await ask(page, 'Có mã giảm giá không?');
    check('Running public coupons listed', coupons.text.includes('CHAOBAN10') && coupons.text.includes('GIAM500K') && !coupons.text.includes('HETHAN'), coupons.text);
    const woods = await ask(page, 'Gỗ sồi và gỗ óc chó khác gì?');
    check('Material comparison', woods.text.includes('Gỗ sồi:') && woods.text.includes('Gỗ óc chó:') && woods.text.includes('Ưu điểm'), woods.text);
    const size = await ask(page, 'bàn ăn 6 người cần kích thước bao nhiêu?');
    check('Size guide', /6 người: 1m6/.test(size.text), size.text);
    const thanks = await ask(page, 'cảm ơn nhé');
    check('Small talk', thanks.text.startsWith('Không có gì') && thanks.cards.length === 0, thanks.text);
    const unknown = await ask(page, 'xyz qwerty abc');
    check('Unknown question: says so, points to a person', /chưa hiểu/.test(unknown.text) && unknown.chips.includes('Chat với nhân viên'), unknown.text);
    const search = await ask(page, 'Tôi cần bộ bàn ăn cho gia đình 6 người, khoảng 10 triệu');
    check('Product search still returns real products', search.cards.length > 0, search.cards.map(c => `${c.name} ${c.price}`).join('; '));

    // Product page
    const product = await (await browser.createBrowserContext()).newPage();
    watch(product, 'product');
    await product.setViewport({ width: 1366, height: 900 });
    await go(product, '/products/sofa-bang-3-cho-oslo-khung-go-soi');
    const sofaPrice = price(await product.$eval('#variantPrice', e => e.textContent));
    await product.click('[data-ai-product]');
    await product.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length >= 1, { timeout: 15000 });
    const delivery = await ask(product, 'Giao hàng mất bao lâu?');
    check('Product page: delivery question answered (not the summary again)', /giao/i.test(delivery.text) && !/thuộc nhóm/.test(delivery.text), delivery.text);
    const cheaper = await ask(product, 'Mẫu nào rẻ hơn?');
    check('Product page: cheaper sofas only', cheaper.cards.length > 0 && cheaper.cards.every(c => price(c.price) < sofaPrice), cheaper.cards.map(c => `${c.name} ${c.price}`).join('; '));
    const colors = await ask(product, 'Có màu nào khác không?');
    check('Product page: colours of this sofa', /có các màu:/.test(colors.text), colors.text);

    // Signed-in customer with an order
    const customer = await (await browser.createBrowserContext()).newPage();
    watch(customer, 'customer');
    await customer.setViewport({ width: 390, height: 844, isMobile: true, hasTouch: true });
    await go(customer, '/account/register');
    await customer.type('#FullName', 'Khách Hỏi Đơn');
    await customer.type('#Email', `local-ai-${Date.now()}@example.com`);
    await customer.type('#PhoneNumber', '0912345678');
    await customer.type('#Password', 'Khach@Hang123');
    await customer.type('#ConfirmPassword', 'Khach@Hang123');
    await customer.click('#AcceptTerms');
    await click(customer, 'form[action="/account/register"] button[type="submit"]');
    await go(customer, '/products/sofa-bang-3-cho-oslo-khung-go-soi');
    await customer.$eval('[data-purchase-form] [data-add-to-cart]', b => b.click());
    await customer.waitForFunction(() => [...document.querySelectorAll('[data-cart-count]')].some(b => b.textContent.trim() === '1'), { timeout: 10000 });
    await go(customer, '/checkout');
    await customer.type('#Command_AddressLine', '5 Đường Hỏi Trợ Lý');
    await customer.select('#Command_Province', await customer.$$eval('#Command_Province option', o => o.map(x => x.value).filter(Boolean)[0]));
    await customer.$eval('#Command_Ward', i => { i.value = 'Phường 2'; });
    await customer.click('input[name="Command.PaymentMethod"][value="COD"]');
    await click(customer, 'button[form="checkoutForm"]');
    const code = await customer.$eval('[data-order-code]', e => e.textContent.trim());
    await go(customer, '/');
    await openWidget(customer);
    const order = await ask(customer, 'đơn hàng của tôi đến đâu rồi?');
    check('Signed-in: own order status', order.text.includes(code) && order.text.includes('Chờ xác nhận'), order.text);
    const overflow = await customer.evaluate(() => { window.scrollTo(5000, window.scrollY); const x = window.scrollX; window.scrollTo(0, window.scrollY); return x; });
    check('Phone (390px): no horizontal scroll', overflow === 0, overflow);
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
