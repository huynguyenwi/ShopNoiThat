// Browser check of the account forms that use client-side validation.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
(async () => {
  const browser = await puppeteer.launch({ executablePath: (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe'), headless: true, acceptInsecureCerts: true });
  const page = await browser.newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  const result = {};
  const submit = async (selector) => { await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2', timeout: 15000 }), page.click(selector)]); };
  try {
    await page.setViewport({ width: 1280, height: 900 });
    const email = 'forms-' + Date.now() + '@example.com';
    await page.goto(BASE + '/account/register', { waitUntil: 'networkidle2' });
    // Client validation must block an unticked terms box with the Vietnamese message.
    await page.type('#FullName', 'Kiểm Tra Form');
    await page.type('#Email', email);
    await page.type('#PhoneNumber', '0912345678');
    await page.type('#Password', 'Khach@Hang123');
    await page.type('#ConfirmPassword', 'Khach@Hang123');
    await page.click('form[action="/account/register"] button[type="submit"]');
    await new Promise(r => setTimeout(r, 800));
    result.untickedMessage = await page.$eval('[data-valmsg-for="AcceptTerms"]', e => e.textContent.trim()).catch(() => null);
    await page.click('#AcceptTerms');
    await submit('form[action="/account/register"] button[type="submit"]');
    result.afterRegister = page.url();

    await page.goto(BASE + '/account/profile', { waitUntil: 'networkidle2' });
    await page.$eval('#FullName', i => { i.value = 'Tên Mới Đã Sửa'; });
    const gender = await page.$('#Gender');
    if (gender) await page.select('#Gender', 'Nữ');
    const dob = await page.$('#DateOfBirth');
    if (dob) await page.$eval('#DateOfBirth', i => { i.value = '1995-05-20'; });
    await submit('form[action="/account/profile"] button[type="submit"]');
    result.profileMessage = await page.$eval('.alert', e => e.textContent.trim()).catch(() => null);

    await page.goto(BASE + '/account/changepassword', { waitUntil: 'networkidle2' });
    await page.type('#CurrentPassword', 'Khach@Hang123');
    await page.type('#NewPassword', 'Khach@Hang456');
    await page.type('#ConfirmPassword', 'Khach@Hang456');
    await submit('form[action="/account/changepassword"] button[type="submit"]');
    result.passwordMessage = await page.$eval('.alert', e => e.textContent.trim()).catch(() => null);

    const ctx = await browser.createBrowserContext();
    const anon = await ctx.newPage();
    anon.on('pageerror', e => errors.push(e.message));
    await anon.goto(BASE + '/account/forgotpassword', { waitUntil: 'networkidle2' });
    await anon.type('#Email', email);
    await Promise.all([anon.waitForNavigation({ waitUntil: 'networkidle2', timeout: 15000 }), anon.click('form[action="/account/forgotpassword"] button[type="submit"]')]);
    result.forgotUrl = anon.url();
  } catch (e) {
    result.failed = e.message;
    process.exitCode = 1;
  } finally {
    result.errors = errors;
    if (errors.length) process.exitCode = 1; // browser console / page errors fail the suite
    console.log(JSON.stringify(result, null, 1));
    await browser.close();
  }
})();
