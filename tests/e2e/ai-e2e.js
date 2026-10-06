// E2E: AI widget on a product page, advisor page tools, panel coordination with the shop chat.
const puppeteer = require('puppeteer-core');
const BASE = process.env.BASE || 'https://localhost:7160';
const EDGE = (process.env.BROWSER_PATH || 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe');
const OUT = process.argv[2] || require('path').join(__dirname, 'output');
require('fs').mkdirSync(OUT, { recursive: true });

(async () => {
  const browser = await puppeteer.launch({ executablePath: EDGE, headless: true, acceptInsecureCerts: true, args: ['--ignore-certificate-errors'] });
  const errors = [];
  const watch = (page, who) => {
    page.on('console', m => { if (m.type() === 'error' || m.type() === 'warning') errors.push(`${who} console.${m.type()}: ${m.text()}`); });
    page.on('pageerror', e => errors.push(`${who} pageerror: ${e.message}`));
  };
  const result = {};
  try {
    const ctx = await browser.createBrowserContext();
    const page = await ctx.newPage();
    watch(page, 'guest');
    await page.setViewport({ width: 1366, height: 860 });

    // 1. Chip in the AI widget
    await page.goto(BASE + '/', { waitUntil: 'networkidle2' });
    await page.click('#aiLauncher');
    await page.waitForSelector('#aiPanel:not([hidden])');
    await page.click('#aiMessages [data-ai-say]');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length === 1, { timeout: 15000 });
    result.chipAnswer = await page.$eval('#aiMessages .ai-answer .chat-bubble', b => b.textContent.slice(0, 200));
    result.chipCards = await page.$$eval('#aiMessages .ai-answer .ai-card', c => c.map(x => x.querySelector('.ai-card-name').textContent + ' | ' + x.querySelector('.ai-card-price strong').textContent));
    await page.screenshot({ path: OUT + '/ai-widget.png' });

    // 2. Opening the shop chat closes the AI panel and hides its launcher
    await page.click('#chatLauncher');
    await page.waitForSelector('#chatPanel:not([hidden])');
    result.aiPanelHiddenWhenChatOpen = await page.$eval('#aiPanel', p => p.hidden);
    result.aiWidgetDisplayWhenChatOpen = await page.$eval('#aiWidget', w => getComputedStyle(w).display);
    await page.click('#chatClose');

    // 3. Reload: conversation restored from sessionStorage
    await page.reload({ waitUntil: 'networkidle2' });
    await page.click('#aiLauncher');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length === 1, { timeout: 10000 });
    result.restoredAfterReload = true;

    // 4. "AI tư vấn sản phẩm này" on a product page
    await page.goto(BASE + '/products/sofa-bang-3-cho-oslo-khung-go-soi', { waitUntil: 'networkidle2' });
    await page.click('[data-ai-product]');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length >= 1, { timeout: 15000 });
    result.productAdvice = await page.$eval('#aiMessages .ai-answer:last-of-type .chat-bubble', b => b.textContent.slice(0, 260));

    // 5. Typed question with follow-up
    await page.type('#aiInput', 'Tôi cần bàn ăn cho 6 người');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length >= 2, { timeout: 15000 });
    await page.type('#aiInput', 'khoảng 10 triệu thôi');
    await page.keyboard.press('Enter');
    await page.waitForFunction(() => document.querySelectorAll('#aiMessages .ai-answer').length >= 3, { timeout: 15000 });
    result.followUp = await page.$eval('#aiMessages .ai-answer:last-of-type .chat-bubble', b => b.textContent.slice(0, 260));
    result.followUpCards = await page.$$eval('#aiMessages .ai-answer:last-of-type .ai-card', c => c.map(x => x.querySelector('.ai-card-name').textContent + ' | ' + x.querySelector('.ai-card-price strong').textContent));

    // 6. Advisor page: the three tools
    const advisor = await ctx.newPage();
    watch(advisor, 'advisor');
    await advisor.setViewport({ width: 1366, height: 900 });
    await advisor.goto(BASE + '/tu-van', { waitUntil: 'networkidle2' });
    await advisor.select('#recRoom', 'Phòng khách');
    await advisor.type('#recArea', '20');
    await advisor.select('#recBudget', '30000000');
    await advisor.select('#recStyle', 'Hiện đại');
    await advisor.click('[data-advisor-form="recommend"] button[type="submit"]');
    await advisor.waitForSelector('[data-advisor-result="recommend"] .ai-card', { timeout: 15000 });
    result.roomSet = await advisor.$$eval('[data-advisor-result="recommend"] .ai-card', c => c.map(x => x.querySelector('.ai-card-category').textContent + ': ' + x.querySelector('.ai-card-name').textContent + ' | ' + x.querySelector('.ai-card-price strong').textContent));
    result.roomTotal = await advisor.$eval('[data-advisor-result="recommend"] p.fw-semibold', p => p.textContent).catch(() => null);
    await advisor.screenshot({ path: OUT + '/ai-advisor.png', fullPage: true });

    await advisor.click('button[data-bs-target="#tab-colors"]');
    await new Promise(r => setTimeout(r, 400));
    await advisor.select('#colWall', 'xám');
    await advisor.click('#colItem3');
    await advisor.click('[data-advisor-form="colors"] button[type="submit"]');
    await advisor.waitForSelector('[data-advisor-result="colors"] .color-swatch', { timeout: 15000 });
    result.colorAdvice = await advisor.$$eval('[data-advisor-result="colors"] .color-swatch strong', s => s.map(x => x.textContent));
    await advisor.screenshot({ path: OUT + '/ai-colors.png' });

    await advisor.click('button[data-bs-target="#tab-styles"]');
    await new Promise(r => setTimeout(r, 400));
    await advisor.type('#styArea', '12');
    await advisor.type('#styBudget', '10');
    await advisor.select('#styPurpose', 'làm việc tập trung');
    await advisor.click('[data-advisor-form="styles"] button[type="submit"]');
    await advisor.waitForSelector('[data-advisor-result="styles"] .style-card', { timeout: 15000 });
    result.styles = await advisor.$$eval('[data-advisor-result="styles"] .style-card h3', s => s.map(x => x.textContent));

    // 7. Mobile: both launchers visible and not overlapping the buy bar
    const mobile = await ctx.newPage();
    watch(mobile, 'mobile');
    await mobile.setViewport({ width: 390, height: 800, isMobile: true, hasTouch: true });
    await mobile.goto(BASE + '/products/sofa-bang-3-cho-oslo-khung-go-soi', { waitUntil: 'networkidle2' });
    const boxes = await mobile.evaluate(() => {
      const r = s => { const b = document.querySelector(s).getBoundingClientRect(); return { top: b.top, bottom: b.bottom }; };
      return { ai: r('#aiLauncher'), chat: r('#chatLauncher'), bar: r('.mobile-buy-bar') };
    });
    result.mobileBoxes = boxes;
    result.mobileLaunchersAboveBar = boxes.ai.bottom <= boxes.chat.top && boxes.chat.bottom <= boxes.bar.top;
    result.mobileOverflow = await mobile.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
    await mobile.click('#aiLauncher');
    await mobile.waitForSelector('#aiPanel:not([hidden])');
    await mobile.screenshot({ path: OUT + '/ai-mobile.png' });
  } catch (e) {
    result.failed = e.message;
    process.exitCode = 1;
  } finally {
    result.errors = errors;
    if (errors.length) process.exitCode = 1; // browser console / page errors fail the suite
    console.log(JSON.stringify(result, null, 2));
    await browser.close();
  }
})();
