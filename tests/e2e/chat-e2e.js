// E2E: guest chats from a product page, admin answers from /admin/chat, both in realtime (real Edge, WebSockets).
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const EDGE = (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const OUT = process.argv[2] || require('path').join(__dirname, 'output');
require('fs').mkdirSync(OUT, { recursive: true });
const sleep = ms => new Promise(r => setTimeout(r, ms));

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors', '--no-first-run'] });
  const errors = [];
  const watch = (page, who) => {
    page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`${who} console.${m.type()}: ${m.text()}`); });
    page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
    page.on('requestfailed', r => errors.push(`${who} requestfailed: ${r.url()} ${r.failure() && r.failure().errorText}`));
  };
  try {
    // ---- admin signs in and opens the chat page
    const adminCtx = await browser.createBrowserContext();
    const admin = await adminCtx.newPage();
    watch(admin, 'admin');
    await admin.setViewport({ width: 1366, height: 820 });
    await admin.goto(BASE + '/account/login', { waitUntil: 'networkidle2' });
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('form[action="/account/login"] button[type="submit"], form button[type="submit"].btn-primary')]);
    await admin.goto(BASE + '/admin/chat', { waitUntil: 'networkidle2' });
    await sleep(1500); // hub connected

    // ---- guest opens a product and asks about it
    const guestCtx = await browser.createBrowserContext();
    const guest = await guestCtx.newPage();
    watch(guest, 'guest');
    await guest.setViewport({ width: 1280, height: 800 });
    await guest.goto(BASE + '/products/sofa-bang-3-cho-oslo-khung-go-soi', { waitUntil: 'networkidle2' });
    await guest.click('[data-chat-product]');
    await guest.waitForSelector('#chatStartForm:not([hidden])', { timeout: 10000 });
    await guest.type('#chatName', 'Khách E2E');
    await guest.type('#chatPhone', '0912345678');
    await guest.click('#chatStartForm button[type="submit"]');
    await guest.waitForSelector('#chatCompose:not([hidden])', { timeout: 10000 });
    const prefilled = await guest.$eval('#chatInput', t => t.value);
    await guest.focus('#chatInput');
    await guest.keyboard.press('Enter');
    await guest.waitForFunction(() => document.querySelectorAll('#chatMessages .chat-msg.from-me').length === 1, { timeout: 10000 });
    await guest.type('#chatInput', 'Giao về Hà Nội mất mấy ngày? <b>không in đậm</b>');
    await guest.keyboard.press('Enter');
    await guest.waitForFunction(() => document.querySelectorAll('#chatMessages .chat-msg.from-me').length === 2, { timeout: 10000 });

    // ---- admin sees it live, opens it and replies
    await admin.waitForFunction(() => [...document.querySelectorAll('.admin-chat-item')].some(i => i.textContent.includes('Khách E2E')), { timeout: 10000 });
    const badge = await admin.$eval('#adminChatBadge', b => b.classList.contains('d-none') ? '0' : b.textContent.trim());
    const items = await admin.$$('.admin-chat-item');
    for (const item of items) { if ((await item.evaluate(n => n.textContent)).includes('Khách E2E')) { await item.click(); break; } }
    await admin.waitForFunction(() => document.querySelectorAll('#threadMessages .chat-msg').length >= 2, { timeout: 10000 });
    const adminThread = await admin.$$eval('#threadMessages .chat-msg .chat-bubble', b => b.map(x => x.textContent));
    const htmlInjected = await admin.$$eval('#threadMessages .chat-bubble b', b => b.length);
    const contact = await admin.$eval('#threadContact', c => c.textContent);
    await admin.type('#threadInput', 'Dạ Hà Nội giao trong 2 ngày ạ.');
    await admin.keyboard.press('Enter');

    // ---- guest receives the reply in realtime; "đã xem" appears on the guest's messages
    await guest.waitForFunction(() => [...document.querySelectorAll('#chatMessages .chat-msg.from-other .chat-bubble')].some(b => b.textContent.includes('2 ngày')), { timeout: 10000 });
    await guest.waitForFunction(() => [...document.querySelectorAll('#chatMessages .chat-seen')].some(s => s.textContent.includes('Đã xem')), { timeout: 10000 });
    await admin.waitForFunction(() => document.querySelectorAll('#threadMessages .chat-msg.from-me').length === 1, { timeout: 10000 });

    // ---- guest reloads: history is kept (cookie), launcher shows no unread
    await guest.reload({ waitUntil: 'networkidle2' });
    await guest.click('#chatLauncher');
    await guest.waitForFunction(() => document.querySelectorAll('#chatMessages .chat-msg[data-message-id]').length === 3, { timeout: 10000 });

    await guest.screenshot({ path: OUT + '/chat-guest.png' });
    await admin.screenshot({ path: OUT + '/chat-admin.png' });

    // ---- mobile layout of the widget
    const mobile = await guestCtx.newPage();
    watch(mobile, 'mobile');
    await mobile.setViewport({ width: 390, height: 780, isMobile: true, hasTouch: true });
    await mobile.goto(BASE + '/products/sofa-bang-3-cho-oslo-khung-go-soi', { waitUntil: 'networkidle2' });
    const launcherBox = await mobile.$eval('#chatLauncher', b => { const r = b.getBoundingClientRect(); return { bottom: r.bottom, top: r.top }; });
    const buyBarTop = await mobile.$eval('.mobile-buy-bar', b => b.getBoundingClientRect().top);
    await mobile.click('#chatLauncher');
    await mobile.waitForFunction(() => document.querySelectorAll('#chatMessages .chat-msg[data-message-id]').length === 3, { timeout: 10000 });
    const overflow = await mobile.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    await mobile.screenshot({ path: OUT + '/chat-mobile.png' });

    console.log(JSON.stringify({ prefilled, badge, adminThread, htmlInjected, contact, launcherAboveBuyBar: launcherBox.bottom <= buyBarTop, horizontalOverflow: overflow, errors }, null, 2));
  } catch (e) {
    console.log('E2E FAILED: ' + e.message);
    console.log(JSON.stringify({ errors }, null, 2));
    process.exitCode = 1;
  } finally {
    if (errors.length) process.exitCode = 1; // browser console / page errors fail the suite
    await browser.close();
  }
})();
