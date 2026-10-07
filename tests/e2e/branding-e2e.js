// Store name from /admin/store in the logo and titles, and the home page banner edited in /admin/banner (live preview, picture upload).
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const puppeteer = require('puppeteer-core');
const { PNG } = require('pngjs');
const BASE = process.env.BASE || 'https://localhost:7160';
const DB = process.env.DB || 'FurnitureStoreDb';
const EDGE = process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
const OUT = path.join(__dirname, 'output');
const FIXTURES = path.join(OUT, 'branding-fixtures');
const sql = q => execSync(`sqlcmd -S "(localdb)\\MSSQLLocalDB" -d ${DB} -E -h -1 -W -f 65001 -Q "SET NOCOUNT ON; ${q}"`, { encoding: 'utf8' }).trim();

const results = [];
const errors = [];
const check = (name, ok, detail) => { results.push(!!ok); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}${detail !== undefined ? '  -> ' + String(detail).replace(/\s+/g, ' ').slice(0, 200) : ''}`); };
const go = async (page, url) => { const r = await page.goto(BASE + url, { waitUntil: 'networkidle2' }); return r ? r.status() : 0; };
const click = (page, selector) => Promise.all([page.waitForNavigation({ waitUntil: 'networkidle2' }), page.click(selector)]);
const setValue = async (page, selector, value) => {
  await page.$eval(selector, el => { el.value = ''; });
  if (value) await page.type(selector, value); else await page.$eval(selector, el => el.dispatchEvent(new Event('input', { bubbles: true })));
};
const text = (page, selector) => page.$eval(selector, el => el.textContent.trim());

/** An opaque landscape photo stand-in (noise, so it is as heavy as a real photo). */
function writePhoto(file, width, height) {
  const png = new PNG({ width, height });
  for (let i = 0; i < width * height * 4; i += 4) {
    const n = Math.floor(Math.random() * 60);
    png.data[i] = 120 + n; png.data[i + 1] = 80 + n; png.data[i + 2] = 50 + n; png.data[i + 3] = 255;
  }
  fs.writeFileSync(file, PNG.sync.write(png));
}

(async () => {
  fs.mkdirSync(FIXTURES, { recursive: true });
  const photo = path.join(FIXTURES, 'phong-an-mua-thu.png');
  writePhoto(photo, 1800, 1200);
  // A wide logo (3:1) on a transparent background: a brown bar with a transparent border.
  const logo = path.join(FIXTURES, 'logo-shop.png');
  const logoPng = new PNG({ width: 600, height: 200 });
  for (let y = 0; y < 200; y++) {
    for (let x = 0; x < 600; x++) {
      const i = (600 * y + x) << 2;
      const inside = x > 20 && x < 580 && y > 40 && y < 160;
      logoPng.data[i] = 91; logoPng.data[i + 1] = 58; logoPng.data[i + 2] = 36; logoPng.data[i + 3] = inside ? 255 : 0;
    }
  }
  fs.writeFileSync(logo, PNG.sync.write(logoPng));

  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  try {
    const admin = await (await browser.createBrowserContext()).newPage();
    admin.on('pageerror', e => errors.push('admin: ' + e.message));
    admin.on('dialog', d => d.accept());
    await admin.setViewport({ width: 1366, height: 900 });
    await go(admin, '/account/login');
    await admin.type('#Email', 'admin@furniture.local');
    await admin.type('#Password', process.env.ADMIN_PW);
    await click(admin, 'form[action="/account/login"] button[type="submit"]');

    // ---------- Store name → logo
    await go(admin, '/admin/store');
    // Read from the form, not sqlcmd: its console output loses Vietnamese letters.
    const [originalName, originalSub] = await admin.evaluate(() => [document.getElementById('Name').value, document.getElementById('LogoSubtitle').value]);
    const previewBox = await admin.evaluate(() => ({
      icon: Math.round(document.querySelector('[data-logo-image]').getBoundingClientRect().height),
      text: Math.round(document.querySelector('[data-logo-name]').getBoundingClientRect().width)
    }));
    check('Logo preview is laid out like the header (40 px icon, readable name)', previewBox.icon === 40 && previewBox.text > 60, JSON.stringify(previewBox));
    await setValue(admin, '#Name', 'Gỗ Việt Home');
    await setValue(admin, '#LogoSubtitle', 'Home');
    check('Logo preview follows the name while typing', await text(admin, '[data-logo-name]') === 'Gỗ Việt' && await text(admin, '[data-logo-sub]') === 'Home',
      `${await text(admin, '[data-logo-name]')} / ${await text(admin, '[data-logo-sub]')}`);
    await setValue(admin, '#LogoSubtitle', '');
    check('…without a small line the logo is the whole name', await text(admin, '[data-logo-name]') === 'Gỗ Việt Home' && await admin.$eval('[data-logo-sub]', e => e.hidden));
    await setValue(admin, '#LogoSubtitle', 'Home');
    await click(admin, 'form[action="/admin/store"] button[type="submit"].btn-primary');

    const visitor = await (await browser.createBrowserContext()).newPage();
    visitor.on('pageerror', e => errors.push('visitor: ' + e.message));
    await visitor.setViewport({ width: 1366, height: 900 });
    await go(visitor, '/');
    const header = await visitor.evaluate(() => ({
      name: document.querySelector('.site-header .brand-name').textContent.trim(),
      sub: document.querySelector('.site-header .brand-sub').textContent.trim(),
      title: document.title,
      footer: document.querySelector('.site-footer').textContent.includes('Gỗ Việt Home. Bảo lưu mọi quyền.'),
      menuFits: document.querySelector('.site-header .navbar').scrollWidth <= window.innerWidth
    }));
    check('New store name in the header logo, page title and footer', header.name === 'Gỗ Việt' && header.sub === 'Home' && header.title.startsWith('Gỗ Việt Home') && header.footer, JSON.stringify(header));
    await go(admin, '/admin');
    check('…and in the admin sidebar', await text(admin, '.admin-sidebar .brand-name') === 'Gỗ Việt');

    await go(admin, '/admin/store');
    await setValue(admin, '#Name', 'Xưởng Nội Thất Gỗ Sồi Nga Đóng Theo Yêu Cầu');
    await setValue(admin, '#LogoSubtitle', '');
    await click(admin, 'form[action="/admin/store"] button[type="submit"].btn-primary');
    for (const width of [1440, 1200, 992, 375]) {
      await visitor.setViewport({ width, height: 900 });
      await go(visitor, '/');
      const fit = await visitor.evaluate(() => ({ page: document.documentElement.scrollWidth <= window.innerWidth, logo: document.querySelector('.site-header .brand').getBoundingClientRect().height }));
      check(`A long store name wraps instead of breaking the header (${width}px)`, fit.page && fit.logo < 80, JSON.stringify(fit));
    }
    await visitor.setViewport({ width: 1366, height: 900 });

    await go(admin, '/admin/store');
    await setValue(admin, '#Name', originalName);
    await setValue(admin, '#LogoSubtitle', originalSub);
    await click(admin, 'form[action="/admin/store"] button[type="submit"].btn-primary');
    await go(admin, '/admin/store');
    check('Store name restored', await admin.$eval('#Name', i => i.value) === originalName && await admin.$eval('#LogoSubtitle', i => i.value) === originalSub, originalName);

    // ---------- Logo picture
    const originalTikTok = await admin.$eval('#TikTokUrl', i => i.value);
    await (await admin.$('#storeLogo')).uploadFile(logo);
    check('Chosen logo is previewed before saving', await admin.$eval('[data-logo-image]', i => i.src.startsWith('blob:') && i.classList.contains('brand-logo-custom')));
    await admin.click('#LogoShowsName');
    check('…"logo already has the name" hides the text in the preview', await admin.$eval('[data-logo-text]', e => e.hidden));
    await admin.click('#LogoShowsName');
    await setValue(admin, '#TikTokUrl', '@nhamoc.furniture');
    await click(admin, 'form[action="/admin/store"] button[type="submit"].btn-primary');
    check('TikTok typed as @handle is saved as the full link', await admin.$eval('#TikTokUrl', i => i.value) === 'https://www.tiktok.com/@nhamoc.furniture');

    await go(visitor, '/');
    const logoInHeader = await visitor.evaluate(() => {
      const img = document.querySelector('.site-header .brand-logo');
      const r = img.getBoundingClientRect();
      const tiktok = document.querySelector('.site-footer a[aria-label="TikTok"]');
      return {
        custom: img.classList.contains('brand-logo-custom'), loaded: img.complete && img.naturalWidth > 0, height: Math.round(r.height), ratio: Math.round(r.width / r.height * 10) / 10,
        name: !!document.querySelector('.site-header .brand-name'), icon: document.querySelector('link[rel="icon"]').getAttribute('href'),
        tiktok: tiktok && tiktok.href, tiktokIcon: !!(tiktok && tiktok.querySelector('.bi-tiktok')) && getComputedStyle(tiktok.querySelector('.bi-tiktok'), '::before').content !== 'none'
      };
    });
    check('Uploaded logo in the header: 40 px high, wide shape kept, name still next to it', logoInHeader.custom && logoInHeader.loaded && logoInHeader.height === 40
      && Math.abs(logoInHeader.ratio - 3) < 0.15 && logoInHeader.name, JSON.stringify(logoInHeader));
    check('…a wide logo is not used as the tab icon', logoInHeader.icon === '/images/logo-mark.svg', logoInHeader.icon);
    check('TikTok link and icon in the footer', logoInHeader.tiktok === 'https://www.tiktok.com/@nhamoc.furniture' && logoInHeader.tiktokIcon, JSON.stringify(logoInHeader));
    await visitor.screenshot({ path: path.join(OUT, 'header-logo.png'), clip: { x: 0, y: 0, width: 1366, height: 120 } });
    const logoUrl = await visitor.$eval('.site-header .brand-logo', i => new URL(i.src).pathname);
    await go(admin, '/admin');
    check('…and on the admin sidebar', await admin.$eval('.admin-sidebar .brand-logo', i => i.classList.contains('brand-logo-custom') && i.complete && i.naturalWidth > 0));

    await go(admin, '/admin/store');
    await admin.click('#RemoveLogo');
    check('"Remove logo" previews the house icon again', await admin.$eval('[data-logo-image]', i => i.src.endsWith('/images/logo-mark.svg')));
    await setValue(admin, '#TikTokUrl', originalTikTok);
    await click(admin, 'form[action="/admin/store"] button[type="submit"].btn-primary');
    await go(visitor, '/');
    const removedStatus = await visitor.evaluate(async url => (await fetch(url, { cache: 'no-store' })).status, logoUrl);
    check('Logo removed: house icon back, picture deleted', await visitor.$eval('.site-header .brand-logo', i => i.getAttribute('src')) === '/images/logo-mark.svg' && removedStatus === 404,
      `${logoUrl} -> ${removedStatus}`);

    // ---------- Banner: live preview
    await go(admin, '/admin/banner');
    check('Banner editor opens with the current banner', await text(admin, '[data-preview="Title"]') === await admin.$eval('#Title', i => i.value));
    await setValue(admin, '#Eyebrow', 'Mới về tháng 10');
    await setValue(admin, '#Title', 'Bộ sưu tập mùa thu -');
    await setValue(admin, '#TitleHighlight', 'gỗ óc chó');
    await setValue(admin, '#Description', 'Bàn 1m4 và 4 ghế, giao trong 7 ngày.');
    await setValue(admin, '#PrimaryButtonText', 'Xem khuyến mãi');
    await setValue(admin, '#SecondaryButtonText', '');
    await setValue(admin, '#Stat1Value', '36');
    await setValue(admin, '#Stat3Value', '');
    await setValue(admin, '#Stat3Label', '');
    const preview = await admin.evaluate(() => {
      const p = document.querySelector('[data-banner-preview]');
      return {
        eyebrow: p.querySelector('[data-preview="Eyebrow"]').textContent.trim(),
        title: p.querySelector('.hero-title').textContent.replace(/\s+/g, ' ').trim(),
        primary: p.querySelector('[data-preview="PrimaryButtonText"]').textContent.trim(),
        secondaryHidden: p.querySelector('[data-preview="SecondaryButtonText"]').hidden,
        stat1: p.querySelector('[data-preview-stat="1"] .stat-value').textContent,
        stat3Hidden: p.querySelector('[data-preview-stat="3"]').hidden
      };
    });
    check('Preview follows the form while typing (texts, hidden button / statistic)',
      preview.eyebrow === 'Mới về tháng 10' && preview.title === 'Bộ sưu tập mùa thu - gỗ óc chó' && preview.primary === 'Xem khuyến mãi'
      && preview.secondaryHidden && preview.stat1 === '36' && preview.stat3Hidden, JSON.stringify(preview));

    await (await admin.$('#bannerImage')).uploadFile(photo);
    check('Chosen picture is previewed before saving', await admin.$eval('[data-preview-image]', i => i.src.startsWith('blob:')));
    await admin.screenshot({ path: path.join(OUT, 'admin-banner.png'), fullPage: true });

    // ---------- Server-side checks, then save
    await setValue(admin, '#PrimaryButtonUrl', 'javascript:alert(document.cookie)');
    await click(admin, 'form[data-banner-form] button[type="submit"]');
    const refused = await admin.evaluate(() => ({ error: (document.querySelector('[data-valmsg-for="PrimaryButtonUrl"]') || {}).textContent, title: document.getElementById('Title').value }));
    check('A javascript: link is refused with a message, typed texts are kept', /trang trong website/.test(refused.error) && refused.title === 'Bộ sưu tập mùa thu -'
      && sql('SELECT COUNT(*) FROM HomeBanners') === '0', JSON.stringify(refused));

    await setValue(admin, '#PrimaryButtonUrl', '/products?onSale=true');
    await (await admin.$('#bannerImage')).uploadFile(photo);
    await setValue(admin, '#ImageAlt', 'Phòng ăn mùa thu');
    await click(admin, 'form[data-banner-form] button[type="submit"]');
    check('Banner saved', /Đã lưu banner trang chủ/.test(await admin.$eval('main', m => m.textContent)));
    const imageUrl = sql('SELECT ImageUrl FROM HomeBanners');
    check('Picture stored resized (1400 px wide, proportions kept) with a 700 px copy',
      /-1400w\.jpg$/.test(imageUrl) && sql('SELECT CONCAT(ImageWidth, N\'x\', ImageHeight) FROM HomeBanners') === '1400x933', imageUrl);

    await go(visitor, '/');
    const hero = await visitor.evaluate(() => {
      const img = document.querySelector('.hero img');
      return {
        eyebrow: document.querySelector('.hero-eyebrow').textContent.trim(),
        title: document.querySelector('.hero-title').textContent.replace(/\s+/g, ' ').trim(),
        primary: document.querySelector('.hero .btn-primary').getAttribute('href'),
        buttons: document.querySelectorAll('.hero .btn').length,
        stats: Array.from(document.querySelectorAll('.hero-stats .stat-value')).map(s => s.textContent),
        img: img.currentSrc, loaded: img.complete && img.naturalWidth, alt: img.alt,
        ratio: Math.round(img.getBoundingClientRect().width / img.getBoundingClientRect().height * 100) / 100
      };
    });
    check('Home page shows the saved banner', hero.eyebrow === 'Mới về tháng 10' && hero.title === 'Bộ sưu tập mùa thu - gỗ óc chó'
      && hero.primary === '/products?onSale=true' && hero.buttons === 1 && JSON.stringify(hero.stats) === '["36","100%"]', JSON.stringify(hero));
    check('…with the uploaded picture, not stretched', /\/uploads\/banners\/.+w\.jpg$/.test(hero.img) && hero.loaded > 0 && hero.alt === 'Phòng ăn mùa thu'
      && Math.abs(hero.ratio - 1.5) < 0.02, JSON.stringify({ img: hero.img, loaded: hero.loaded, ratio: hero.ratio }));
    await visitor.screenshot({ path: path.join(OUT, 'home-banner.png') });

    const phone = await (await browser.createBrowserContext()).newPage();
    await phone.setViewport({ width: 375, height: 800 });
    await go(phone, '/');
    const small = await phone.evaluate(() => ({ src: document.querySelector('.hero img').currentSrc, fits: document.documentElement.scrollWidth <= window.innerWidth }));
    check('Phones load the 700 px copy, no sideways scrolling', /-700w\.jpg$/.test(small.src) && small.fits, JSON.stringify(small));
    await phone.screenshot({ path: path.join(OUT, 'home-banner-phone.png') });

    // ---------- Reset
    await go(admin, '/admin/banner');
    await click(admin, 'form[action="/admin/banner/reset"] button[type="submit"]');
    await go(visitor, '/');
    const back = await visitor.evaluate(() => ({ title: document.querySelector('.hero-title').textContent.replace(/\s+/g, ' ').trim(), img: document.querySelector('.hero img').getAttribute('src') }));
    const gone = await visitor.evaluate(async url => (await fetch(url, { cache: 'no-store' })).status, imageUrl);
    check('Reset brings back the built-in banner and deletes the picture', back.title === 'Bộ bàn ăn gỗ sồi Nga - màu óc chó' && back.img === '/images/hero-dining-room.svg'
      && gone === 404 && sql('SELECT COUNT(*) FROM HomeBanners') === '0', JSON.stringify({ ...back, gone }));
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
