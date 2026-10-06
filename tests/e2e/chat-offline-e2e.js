// Chat without the realtime connection: the hub (/hubs/chat) is blocked for the admin, then for a guest. Messages must
// still show up (polling), the admin page must say the connection is down, and realtime must come back on its own.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 200) : ''}`); };
const go = (page, url) => page.goto(BASE + url, { waitUntil: 'networkidle2' });
const sleep = ms => new Promise(r => setTimeout(r, ms));
const POLL_WAIT = 25000; // polling runs every 15 s

/** A page whose hub requests can be blocked, as when WebSockets / the server connection are unavailable. */
async function newPage(browser, who) {
  const page = await (await browser.createBrowserContext()).newPage();
  page.hubBlocked = false;
  await page.setRequestInterception(true);
  page.on('request', r => (page.hubBlocked && r.url().includes('/hubs/chat') ? r.abort('connectionrefused') : r.continue()));
  page.on('console', m => {
    // Refused hub requests are expected here; anything else is a real error.
    if (m.type() === 'error' && !/hubs\/chat|ERR_CONNECTION_REFUSED|Failed to start the connection|Failed to complete negotiation|Connection disconnected/.test(m.text())) errors.push(`${who}: ${m.text()}`);
  });
  page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
  await page.setViewport({ width: 1366, height: 820 });
  return page;
}

const adminItems = page => page.$$eval('.admin-chat-item', i => i.map(x => x.textContent.replace(/\s+/g, ' ')));
const threadTexts = page => page.$$eval('#threadMessages .chat-msg .chat-bubble', b => b.map(x => x.textContent));
const guestTexts = page => page.$$eval('#chatMessages .chat-msg[data-message-id] .chat-bubble', b => b.map(x => x.textContent));
const waitFor = (page, fn, arg, timeout = POLL_WAIT) => page.waitForFunction(fn, { timeout, polling: 500 }, arg).then(() => true, () => false);

async function sendAsGuest(page, text) {
  await page.type('#chatInput', text);
  await page.keyboard.press('Enter');
  await waitFor(page, t => [...document.querySelectorAll('#chatMessages .chat-msg.from-me .chat-bubble')].some(b => b.textContent === t), text, 10000);
}

async function startGuestChat(page, name) {
  await go(page, '/');
  await page.click('#chatLauncher');
  await page.waitForSelector('#chatStartForm:not([hidden])', { timeout: 10000 });
  await page.type('#chatName', name);
  await page.type('#chatPhone', '0912345678');
  await page.click('#chatStartForm button[type="submit"]');
  await page.waitForSelector('#chatCompose:not([hidden])', { timeout: 10000 });
}

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    const stamp = Date.now().toString().slice(-5);
    const guestName = `Khách Mất Mạng ${stamp}`;

    // ---------- Admin without realtime
    const admin = await newPage(browser, 'admin');
    await go(admin, '/account/login');
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await Promise.all([admin.waitForNavigation({ waitUntil: 'networkidle2' }), admin.click('form[action="/account/login"] button[type="submit"]')]);
    admin.hubBlocked = true;
    await go(admin, '/admin/chat');
    const offline = await waitFor(admin, () => document.getElementById('chatLive').classList.contains('is-offline'), null, 10000);
    check('Admin page says realtime is down', offline, await admin.$eval('#chatLive', e => e.textContent));

    const guest = await newPage(browser, 'guest');
    await startGuestChat(guest, guestName);
    await sendAsGuest(guest, 'Tin 1: admin đang mất kết nối');
    check('Without realtime, the new conversation still appears in the admin list (polling)',
      await waitFor(admin, n => [...document.querySelectorAll('.admin-chat-item')].some(i => i.textContent.includes(n)), guestName), (await adminItems(admin)).slice(0, 2).join(' | '));
    check('…and the chat badge counts it', await waitFor(admin, () => document.getElementById('adminChatBadge').textContent.trim() !== '0', null, 5000));

    const items = await admin.$$('.admin-chat-item');
    for (const item of items) { if ((await item.evaluate(n => n.textContent)).includes(guestName)) { await item.click(); break; } }
    await admin.waitForFunction(() => document.querySelectorAll('#threadMessages .chat-msg').length >= 1, { timeout: 10000 });
    await sendAsGuest(guest, 'Tin 2: khi admin đang mở cuộc trò chuyện');
    check('Open thread receives new customer messages (polling)',
      await waitFor(admin, t => [...document.querySelectorAll('#threadMessages .chat-bubble')].some(b => b.textContent === t), 'Tin 2: khi admin đang mở cuộc trò chuyện'), (await threadTexts(admin)).join(' | '));

    await admin.type('#threadInput', 'Admin trả lời khi mất kết nối');
    await admin.keyboard.press('Enter');
    check('Admin can still reply (sent over HTTP), guest gets it in realtime',
      await waitFor(guest, t => [...document.querySelectorAll('#chatMessages .chat-msg.from-other .chat-bubble')].some(b => b.textContent === t), 'Admin trả lời khi mất kết nối', 10000), (await guestTexts(guest)).join(' | '));

    // ---------- Realtime comes back by itself
    admin.hubBlocked = false;
    check('Realtime reconnects without reloading the page', await waitFor(admin, () => document.getElementById('chatLive').classList.contains('is-live'), null, 40000), await admin.$eval('#chatLive', e => e.textContent));
    await sendAsGuest(guest, 'Tin 3: sau khi có lại kết nối');
    check('…and messages are instant again', await waitFor(admin, t => [...document.querySelectorAll('#threadMessages .chat-bubble')].some(b => b.textContent === t), 'Tin 3: sau khi có lại kết nối', 5000));

    // ---------- Guest without realtime
    const guest2 = await newPage(browser, 'guest2');
    guest2.hubBlocked = true;
    const guest2Name = `Khách Không Realtime ${stamp}`;
    await startGuestChat(guest2, guest2Name);
    await sendAsGuest(guest2, 'Tin từ khách không có realtime');
    check('Guest without realtime: message saved and shown to the admin', await waitFor(admin, n => [...document.querySelectorAll('.admin-chat-item')].some(i => i.textContent.includes(n)), guest2Name, 10000));
    check('Guest widget says it is reconnecting', /kết nối lại/.test(await guest2.$eval('#chatStatus', e => e.textContent)), await guest2.$eval('#chatStatus', e => e.textContent));
    const items2 = await admin.$$('.admin-chat-item');
    for (const item of items2) { if ((await item.evaluate(n => n.textContent)).includes(guest2Name)) { await item.click(); break; } }
    await waitFor(admin, n => document.getElementById('threadName').textContent === n, guest2Name, 10000);
    await admin.type('#threadInput', 'Cửa hàng trả lời khách không có realtime');
    await admin.keyboard.press('Enter');
    check('Guest without realtime still receives the reply (polling)',
      await waitFor(guest2, t => [...document.querySelectorAll('#chatMessages .chat-msg.from-other .chat-bubble')].some(b => b.textContent === t), 'Cửa hàng trả lời khách không có realtime'), (await guestTexts(guest2)).join(' | '));
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
