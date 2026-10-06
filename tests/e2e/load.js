// Small load test: N requests per URL with C concurrent connections (keep-alive, brotli accepted).
const https = require('https');
const agent = new https.Agent({ keepAlive: true, maxSockets: 10, rejectUnauthorized: false });
const BASE = process.env.BASE || 'https://localhost:7160';
const urls = ['/', '/products', '/products?category=phong-khach&sort=price-asc', '/products/sofa-bang-3-cho-oslo-khung-go-soi', '/api/products?pageSize=12', '/api/products/search?q=ban', '/sitemap.xml'];
const N = 300, C = 10;

function get(path) {
  return new Promise((resolve) => {
    const started = process.hrtime.bigint();
    const req = https.get(BASE + path, { agent, headers: { 'Accept-Encoding': 'br', 'X-Forwarded-For': '10.0.0.' + Math.floor(Math.random() * 200) } }, res => {
      let bytes = 0;
      res.on('data', c => { bytes += c.length; });
      res.on('end', () => resolve({ status: res.statusCode, ms: Number(process.hrtime.bigint() - started) / 1e6, bytes }));
    });
    req.on('error', () => resolve({ status: 0, ms: 0, bytes: 0 }));
  });
}

(async () => {
  for (const url of urls) {
    await get(url); // warm-up
    const results = [];
    const started = Date.now();
    let next = 0;
    await Promise.all(Array.from({ length: C }, async () => {
      while (next < N) { next++; results.push(await get(url)); }
    }));
    const elapsed = (Date.now() - started) / 1000;
    const ms = results.map(r => r.ms).sort((a, b) => a - b);
    const p = q => ms[Math.min(ms.length - 1, Math.floor(q * ms.length))].toFixed(1);
    const errors = results.filter(r => r.status !== 200).length;
    console.log(`${url.padEnd(48)} ${(N / elapsed).toFixed(0).padStart(5)} req/s  p50 ${p(0.5).padStart(6)}ms  p95 ${p(0.95).padStart(6)}ms  ${Math.round(results[0].bytes / 1024)}KB  errors ${errors}`);
  }
  agent.destroy();
})();
