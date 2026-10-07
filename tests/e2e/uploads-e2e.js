// Picture uploads of any photo size (resized by the server to their frame) and admin notifications opened one by one.
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { PNG } = require('pngjs');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const OUT = path.join(__dirname, 'output');
const FIXTURES = path.join(OUT, 'upload-fixtures');
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 200) : ''}`); };
const go = async (page, url) => { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);

/** A PNG photo stand-in: noise makes it as heavy as a real phone photo. */
function writePng(file, width, height, noisy) {
  const png = new PNG({ width, height });
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = (width * y + x) << 2;
      const top = y < height / 2;
      const n = noisy ? Math.floor(Math.random() * 120) - 60 : 0;
      png.data[i] = Math.max(0, Math.min(255, (top ? 92 : 230) + n));
      png.data[i + 1] = Math.max(0, Math.min(255, (top ? 64 : 216) + n));
      png.data[i + 2] = Math.max(0, Math.min(255, (top ? 51 : 191) + n));
      png.data[i + 3] = 255;
    }
  }
  fs.writeFileSync(file, PNG.sync.write(png));
  return fs.statSync(file).size;
}

(async () => {
  fs.mkdirSync(FIXTURES, { recursive: true });
  const photo = path.join(FIXTURES, 'anh-chup-dien-thoai.png');
  const photoBytes = writePng(photo, 3200, 2400, true);
  const portrait = path.join(FIXTURES, 'anh-doc.png');
  writePng(portrait, 1500, 2000, false);
  const tooBig = path.join(FIXTURES, 'qua-lon.jpg');
  fs.writeFileSync(tooBig, Buffer.concat([Buffer.from([0xFF, 0xD8, 0xFF, 0xE0]), Buffer.alloc(53 * 1024 * 1024)]));

  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    // ---------- Avatar from a big phone photo
    const customer = await (await browser.createBrowserContext()).newPage();
    customer.on('pageerror', e => errors.push('customer: ' + e.message));
    await customer.setViewport({ width: 1366, height: 900 });
    await go(customer, '/account/register');
    await customer.type('#FullName', 'Khách Ảnh');
    await customer.type('#Email', `anh-${Date.now()}@example.com`);
    await customer.type('#PhoneNumber', '0912345678');
    await customer.type('#Password', 'Khach@Hang123');
    await customer.type('#ConfirmPassword', 'Khach@Hang123');
    await customer.click('#AcceptTerms');
    await click(customer, 'form[action="/account/register"] button[type="submit"]');
    await go(customer, '/account/profile');
    await (await customer.$('#avatar')).uploadFile(photo);
    check('Avatar preview shows the chosen photo before uploading', await customer.$eval('#avatarPreview', i => !i.classList.contains('d-none') && i.src.startsWith('blob:')));
    await click(customer, 'form[action="/account/avatar"] button[type="submit"]');
    const avatar = await customer.evaluate(() => {
      const i = document.getElementById('avatarPreview');
      const h = document.querySelector('.user-menu-avatar');
      return { message: (document.querySelector('.alert') || {}).textContent, src: i.getAttribute('src'), loaded: i.complete && i.naturalWidth, header: h && h.getAttribute('src') };
    });
    check(`Avatar from a ${(photoBytes / 1048576).toFixed(1)} MB photo is accepted and shown (profile + header)`,
      /Đã cập nhật ảnh đại diện/.test(avatar.message) && /-256w\.jpg$/.test(avatar.src) && avatar.loaded === 256 && avatar.header === avatar.src, JSON.stringify(avatar));

    await (await customer.$('#avatar')).uploadFile(tooBig);
    const tooBigState = await customer.evaluate(() => ({ error: document.getElementById('avatarError'), files: document.getElementById('avatar').files.length }))
      .then(() => customer.$eval('#avatarError', e => ({ visible: !e.hidden, text: e.textContent })));
    const cleared = await customer.$eval('#avatar', i => i.files.length === 0);
    check('A file over 50 MB is refused right away, before uploading it', tooBigState.visible && /vượt mức tối đa 50 MB/.test(tooBigState.text) && cleared, tooBigState.text);

    // ---------- Admin: product photos resized to their frames
    const admin = await (await browser.createBrowserContext()).newPage();
    admin.on('pageerror', e => errors.push('admin: ' + e.message));
    await admin.setViewport({ width: 1366, height: 900 });
    await go(admin, '/account/login');
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await click(admin, 'form[action="/account/login"] button[type="submit"]');
    const productId = sql("SELECT Id FROM Products WHERE Sku = 'BBA-MOCNHIEN'");
    const slug = sql(`SELECT Slug FROM Products WHERE Id = ${productId}`);
    const before = Number(sql(`SELECT COUNT(*) FROM ProductImages WHERE ProductId = ${productId}`));
    await go(admin, `/admin/products/edit/${productId}`);
    await (await admin.$('input[name="images"][data-image-input]')).uploadFile(photo, portrait);
    const previews = await admin.$$eval('#productImagePreview img', i => i.length);
    check('Admin sees both photos before saving (no 5 MB refusal)', previews === 2, previews);
    await click(admin, '#productForm button[type="submit"].btn-primary');
    const urls = sql(`SELECT Url FROM ProductImages WHERE ProductId = ${productId} AND Url LIKE '/uploads/%' ORDER BY Id`).split(/\r?\n/).filter(Boolean);
    check('Two pictures stored, resized for the gallery', Number(sql(`SELECT COUNT(*) FROM ProductImages WHERE ProductId = ${productId}`)) === before + 2
      && urls.length >= 2 && /-1200w\.jpg$/.test(urls[urls.length - 2]) && /-900w\.jpg$/.test(urls[urls.length - 1]), urls.slice(-2).join(' | '));

    const sizes = await admin.evaluate(async list => {
      const load = src => new Promise(resolve => { const i = new Image(); i.onload = () => resolve([i.naturalWidth, i.naturalHeight]); i.onerror = () => resolve(null); i.src = src; });
      const out = [];
      for (const u of list) out.push([u, await load(u), await load(u.replace(/-\d+w\.jpg$/, '-480w.jpg'))]);
      return out;
    }, urls.slice(-2));
    check('Landscape photo: 1200 x 900 + 480 x 360 copy (proportions kept)', JSON.stringify(sizes[0][1]) === '[1200,900]' && JSON.stringify(sizes[0][2]) === '[480,360]', JSON.stringify(sizes[0]));
    check('Portrait photo: 900 x 1200 + 480 x 640 copy, never stretched', JSON.stringify(sizes[1][1]) === '[900,1200]' && JSON.stringify(sizes[1][2]) === '[480,640]', JSON.stringify(sizes[1]));

    // Gallery shows a portrait photo whole; cards load the small copy
    await go(customer, `/products/${slug}`);
    const thumb = await customer.$$eval('.gallery-thumb img', i => i.map(x => x.getAttribute('src')));
    check('Gallery thumbnails use the 480 px copies', thumb.some(s => /-480w\.jpg$/.test(s)), thumb.slice(-2).join(' | '));
    check('Main photo fits the frame without cropping (object-fit: contain)', await customer.$eval('#galleryMain', i => getComputedStyle(i).objectFit === 'contain'));
    sql(`UPDATE ProductImages SET IsPrimary = CASE WHEN Url = '${urls[urls.length - 2]}' THEN 1 ELSE 0 END WHERE ProductId = ${productId}`);
    sql(`UPDATE Products SET UpdatedAt = SYSUTCDATETIME() WHERE Id = ${productId}`);
    // A fresh visitor: browsers reuse a larger copy they already have in cache.
    const visitor = await (await browser.createBrowserContext()).newPage();
    await visitor.setViewport({ width: 1366, height: 900 });
    await go(visitor, '/products?category=bo-ban-an');
    const card = await visitor.$$eval('.product-card img', imgs => imgs.map(i => ({ src: i.currentSrc, set: i.getAttribute('srcset') })).find(x => x.set));
    check('Product cards pick the 480 px copy (srcset)', !!card && /-480w\.jpg$/.test(card.src), card && card.src);
    const retina = await (await browser.createBrowserContext()).newPage();
    await retina.setViewport({ width: 1366, height: 900, deviceScaleFactor: 3 });
    await go(retina, '/products?category=bo-ban-an');
    const sharp = await retina.$$eval('.product-card img', imgs => imgs.map(i => ({ src: i.currentSrc, set: i.getAttribute('srcset') })).find(x => x.set));
    check('…and the 1200 px one on high-DPI screens (stays sharp)', !!sharp && /-1200w\.jpg$/.test(sharp.src), sharp && sharp.src);

    // ---------- Notifications: open one, the bell goes down, unread ones stand out
    sql("INSERT INTO Notifications (RecipientRole, Type, Title, Message, Link, IsRead, CreatedAt) VALUES ('ADMIN', 'NewContactMessage', N'Liên hệ mới từ E2E', N'Khách hỏi giá bộ bàn ăn', '/admin/contacts', 0, SYSUTCDATETIME())");
    await go(admin, '/admin/notifications');
    const bell = async () => Number((await admin.$eval('a[href="/admin/notifications"][aria-label]', a => a.getAttribute('aria-label'))).match(/\d+/)[0]);
    const unreadBefore = await bell();
    const look = await admin.evaluate(() => {
      const unread = document.querySelector('.notification-item.is-unread');
      const read = document.querySelector('.notification-item.is-read');
      return { unread: unread && getComputedStyle(unread).backgroundColor, read: read && getComputedStyle(read).backgroundColor, badge: !!document.querySelector('.is-unread .notification-new'), id: unread && unread.dataset.notification };
    });
    check('Unread notifications are highlighted (bright background + "Mới")', look.unread === 'rgb(255, 243, 214)' && look.badge && look.read !== look.unread, JSON.stringify(look));
    const link = sql(`SELECT ISNULL(Link, '') FROM Notifications WHERE Id = ${look.id}`);
    await click(admin, `.notification-item[data-notification="${look.id}"]`);
    check('Opening a notification shows its page', new URL(admin.url()).pathname + new URL(admin.url()).search === (link || '/admin/notifications'), admin.url());
    check('…and the bell count goes down by one', (await bell()) === unreadBefore - 1, `${unreadBefore} -> ${await bell()}`);
    await go(admin, '/admin/notifications');
    check('…and it is shown as read', await admin.$eval(`.notification-item[data-notification="${look.id}"]`, b => b.classList.contains('is-read')));
    await admin.screenshot({ path: path.join(OUT, 'notifications.png') });
  } catch (e) {
    check('Script error', false, e.stack);
  } finally {
    const failed = results.filter(r => !r).length;
    console.log(`\n${results.length - failed}/${results.length} passed`);
    console.log(errors.length ? 'Browser errors:\n  ' + [...new Set(errors)].join('\n  ') : 'Browser errors: none');
    process.exitCode = failed || errors.length ? 1 : 0;
    await browser.close();
    fs.rmSync(FIXTURES, { recursive: true, force: true });
  }
})();
