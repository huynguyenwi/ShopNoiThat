/* Nhà Mộc Furniture - /tu-van: AI product suggestions, color matching and style selection.
   Requires site.js (FS) and ai-assistant.js (FSAi.renderCard). All server text is inserted with textContent. */
(function (window, document) {
    'use strict';

    if (!window.FS || !window.FSAi) return;
    const FS = window.FS;

    const endpoints = {
        recommend: '/api/ai/recommend',
        colors: '/api/ai/color-recommend',
        styles: '/api/ai/style-recommend'
    };

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function numberOrNull(value) {
        const n = Number(String(value || '').replace(',', '.'));
        return value !== '' && isFinite(n) && n > 0 ? n : null;
    }

    function readForm(kind, form) {
        const data = new FormData(form);
        const text = function (name) { const v = (data.get(name) || '').toString().trim(); return v || null; };
        if (kind === 'recommend') {
            return {
                roomType: text('roomType'), roomAreaM2: numberOrNull(data.get('roomAreaM2')), people: numberOrNull(data.get('people')),
                budget: numberOrNull(data.get('budget')), style: text('style'), colors: text('colors'), materials: text('materials'), needs: text('needs')
            };
        }
        if (kind === 'colors') {
            return {
                wallColor: text('wallColor'), floorColor: text('floorColor'), style: text('style'),
                furniture: data.getAll('furniture').join(', ') || null, note: text('note')
            };
        }
        const millions = numberOrNull(data.get('budgetMillions'));
        return {
            roomType: text('roomType'), roomAreaM2: numberOrNull(data.get('roomAreaM2')), budget: millions ? millions * 1000000 : null,
            colors: text('colors'), purpose: text('purpose'), preferences: text('preferences')
        };
    }

    function productGrid(products) {
        const row = el('div', 'row g-3 mt-1');
        products.forEach(function (p) {
            const col = el('div', 'col-sm-6 col-xl-4');
            col.appendChild(window.FSAi.renderCard(p, false));
            row.appendChild(col);
        });
        return row;
    }

    function renderRecommend(target, data) {
        target.appendChild(el('div', 'advisor-summary', data.summary));
        if (data.totalPrice) {
            const total = el('p', 'mt-3 mb-0 fw-semibold');
            total.textContent = 'Tổng giá bộ gợi ý: ' + FS.formatCurrency(data.totalPrice);
            target.appendChild(total);
        }
        if (data.items.length) target.appendChild(productGrid(data.items));
    }

    function renderColors(target, data) {
        target.appendChild(el('div', 'advisor-summary', data.summary));
        const list = el('div', 'mt-3 d-grid gap-3');
        data.advice.forEach(function (group) {
            const box = el('div', 'panel py-3');
            box.appendChild(el('h3', 'h6 mb-2', group.furniture));
            const swatches = el('div', 'd-flex flex-wrap gap-2');
            group.colors.forEach(function (c) {
                const swatch = el('span', 'color-swatch');
                swatch.title = c.reason;
                const dot = el('span', 'dot');
                dot.style.backgroundColor = /^#[0-9a-fA-F]{3,8}$/.test(c.hex) ? c.hex : '#ccc';
                swatch.appendChild(dot);
                const label = el('span');
                label.appendChild(el('strong', null, c.name));
                label.appendChild(el('span', 'd-block small text-muted-fs', c.reason));
                swatch.appendChild(label);
                swatches.appendChild(swatch);
            });
            box.appendChild(swatches);
            list.appendChild(box);
        });
        target.appendChild(list);
        if (data.products.length) {
            target.appendChild(el('h3', 'h6 mt-4 mb-0', 'Sản phẩm đang bán có màu phù hợp'));
            target.appendChild(productGrid(data.products));
        }
    }

    function renderStyles(target, data) {
        target.appendChild(el('div', 'advisor-summary', data.summary));
        const row = el('div', 'row g-3 mt-1');
        data.styles.forEach(function (s, index) {
            const col = el('div', 'col-md-4');
            const card = el('div', 'style-card' + (index === 0 ? ' is-best' : ''));
            if (index === 0) card.appendChild(el('span', 'badge text-bg-success mb-2', 'Phù hợp nhất'));
            card.appendChild(el('h3', 'h5', s.name));
            card.appendChild(el('p', 'small text-muted-fs', s.description));
            card.appendChild(el('p', 'small mb-2', s.reason));
            const points = el('ul', 'small ps-3 mb-2');
            s.keyPoints.forEach(function (k) { points.appendChild(el('li', null, k)); });
            card.appendChild(points);
            const link = el('a', 'small', 'Xem sản phẩm phong cách này →');
            link.href = '/products?style=' + encodeURIComponent(s.slug);
            card.appendChild(link);
            col.appendChild(card);
            row.appendChild(col);
        });
        target.appendChild(row);
        if (data.products.length) {
            target.appendChild(el('h3', 'h6 mt-4 mb-0', 'Gợi ý sản phẩm theo phong cách ' + (data.styles[0] ? data.styles[0].name : '')));
            target.appendChild(productGrid(data.products));
        }
    }

    const renderers = { recommend: renderRecommend, colors: renderColors, styles: renderStyles };

    document.querySelectorAll('[data-advisor-form]').forEach(function (form) {
        const kind = form.getAttribute('data-advisor-form');
        const target = document.querySelector('[data-advisor-result="' + kind + '"]');
        const button = form.querySelector('button[type="submit"]');

        form.addEventListener('submit', async function (e) {
            e.preventDefault();
            button.disabled = true;
            target.replaceChildren(el('div', 'text-muted-fs py-4', 'Trợ lý đang phân tích và tìm sản phẩm phù hợp...'));
            try {
                const result = await FS.api(endpoints[kind], { method: 'POST', body: readForm(kind, form) });
                target.replaceChildren();
                if (!result.success) {
                    target.appendChild(el('div', 'alert alert-warning', (result.errors && result.errors.length ? result.errors.join(' ') : result.message) || 'Không nhận được gợi ý.'));
                    return;
                }
                renderers[kind](target, result.data);
                if (window.innerWidth < 992) target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            } finally {
                button.disabled = false;
            }
        });
    });
})(window, document);
