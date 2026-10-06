/* Nhà Mộc Furniture - /bao-gia: custom furniture estimate (price from the server's price calculator) and quote request.
   Requires site.js (FS) and ai-assistant.js (FSAi.renderCard). All server text is inserted with textContent. */
(function (window, document) {
    'use strict';

    if (!window.FS) return;
    const FS = window.FS;
    const $ = function (id) { return document.getElementById(id); };
    const parseForm = $('quoteParseForm');
    const specForm = $('quoteSpecForm');
    const submitForm = $('quoteSubmitForm');
    const result = $('quoteResult');
    const success = $('quoteSuccess');
    const submitError = $('quoteSubmitError');
    if (!parseForm || !specForm) return;

    let lastText = null;

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function number(id) {
        const v = Number($(id).value);
        return $(id).value !== '' && isFinite(v) ? v : null;
    }

    function specFromForm() {
        return {
            kind: $('qKind').value || null,
            lengthMm: number('qLength'),
            widthMm: number('qWidth'),
            heightMm: number('qHeight'),
            materialId: number('qMaterial'),
            finish: $('qFinish').value || null,
            colorId: number('qColor'),
            styleId: number('qStyle'),
            quantity: number('qQuantity')
        };
    }

    function fillForm(spec) {
        $('qKind').value = spec.kind || '';
        $('qLength').value = spec.lengthMm;
        $('qWidth').value = spec.widthMm;
        $('qHeight').value = spec.heightMm;
        $('qMaterial').value = spec.materialId || '';
        $('qFinish').value = spec.finish || '';
        $('qColor').value = spec.colorId || '';
        $('qStyle').value = spec.styleId || '';
        $('qQuantity').value = spec.quantity;
    }

    function errorText(response) {
        return (response.errors && response.errors.length ? response.errors.join(' ') : response.message) || 'Không tính được giá.';
    }

    function row(table, label, value, strong) {
        const tr = el('tr', strong ? 'fw-semibold' : null);
        tr.appendChild(el('th', 'fw-normal', label));
        tr.appendChild(el('td', 'text-end', value));
        table.appendChild(tr);
    }

    function render(estimate) {
        const b = estimate.breakdown;
        const s = estimate.spec;
        result.replaceChildren();

        const head = el('div', 'panel quote-total');
        head.appendChild(el('div', 'small text-muted-fs text-uppercase', 'Giá dự kiến'));
        head.appendChild(el('div', 'quote-price', FS.formatCurrency(b.unitPrice) + ' / sản phẩm'));
        if (b.quantity > 1) head.appendChild(el('div', 'fw-semibold', b.quantity + ' sản phẩm: ' + FS.formatCurrency(b.total)));
        head.appendChild(el('div', 'small mt-1', s.typeName + ' · ' + s.lengthMm + ' x ' + s.widthMm + ' x ' + s.heightMm + ' mm'
            + (s.materialName ? ' · ' + s.materialName : '') + ' · ' + s.finishName
            + (s.colorName ? ' · màu ' + s.colorName : '') + (s.styleName ? ' · ' + s.styleName : '')));
        result.appendChild(head);

        if (estimate.assumptions.length) {
            const box = el('div', 'alert alert-warning small mt-3 mb-0');
            box.appendChild(el('strong', null, 'Hệ thống tạm tính: '));
            const list = el('ul', 'mb-0 ps-3');
            estimate.assumptions.forEach(function (a) { list.appendChild(el('li', null, a)); });
            box.appendChild(list);
            result.appendChild(box);
        }

        const details = el('div', 'panel mt-3');
        details.appendChild(el('h2', 'h6', 'Chi tiết chi phí (1 sản phẩm)'));
        const table = el('table', 'table table-sm quote-breakdown mb-0');
        const body = el('tbody');
        row(body, 'Vật liệu' + (b.materialAreaM2 ? ' (' + String(b.materialAreaM2).replace('.', ',') + ' m²)' : ''), FS.formatCurrency(b.materialCost));
        row(body, 'Công gia công', FS.formatCurrency(b.laborCost));
        row(body, 'Hoàn thiện (chà nhám, lắp ráp)', FS.formatCurrency(b.finishingCost));
        row(body, 'Sơn phủ', FS.formatCurrency(b.paintCost));
        row(body, 'Phụ kiện', FS.formatCurrency(b.accessoryCost));
        row(body, 'Chi phí trực tiếp', FS.formatCurrency(b.directCost), true);
        row(body, 'Chi phí chung (' + b.overheadPercent + '%)', FS.formatCurrency(b.overheadCost));
        row(body, 'Lợi nhuận xưởng (' + b.profitPercent + '%)', FS.formatCurrency(b.profitAmount));
        row(body, 'Giá 1 sản phẩm', FS.formatCurrency(b.unitPrice), true);
        table.appendChild(body);
        details.appendChild(table);
        result.appendChild(details);

        const explanation = el('div', 'advisor-summary mt-3', estimate.explanation);
        result.appendChild(explanation);

        if (estimate.alternatives.length) {
            const alt = el('div', 'panel mt-3');
            alt.appendChild(el('h2', 'h6', 'Cùng kích thước, chất liệu khác'));
            const wrap = el('div', 'd-flex flex-wrap gap-2');
            estimate.alternatives.forEach(function (a) {
                const button = el('button', 'btn btn-sm btn-outline-secondary', a.materialName + ': ' + FS.formatCurrency(a.unitPrice));
                button.type = 'button';
                button.addEventListener('click', function () {
                    $('qMaterial').value = String(a.materialId);
                    estimate_(specFromForm());
                });
                wrap.appendChild(button);
            });
            alt.appendChild(wrap);
            result.appendChild(alt);
        }

        if (estimate.similarProducts.length && window.FSAi) {
            result.appendChild(el('h2', 'h6 mt-4 mb-0', 'Mẫu có sẵn cùng loại (giá bán lẻ để tham khảo)'));
            const grid = el('div', 'row g-3 mt-1');
            estimate.similarProducts.forEach(function (p) {
                const col = el('div', 'col-sm-6 col-xl-4');
                col.appendChild(window.FSAi.renderCard(p, false));
                grid.appendChild(col);
            });
            result.appendChild(grid);
        }

        submitForm.hidden = false;
        success.hidden = true;
    }

    async function estimate_(body) {
        result.replaceChildren(el('div', 'panel text-muted-fs', 'Đang tính giá...'));
        const response = await FS.api('/api/ai/price-estimate', { method: 'POST', body: body });
        if (!response.success) {
            result.replaceChildren(el('div', 'alert alert-warning', errorText(response)));
            submitForm.hidden = true;
            return;
        }
        fillForm(response.data.spec);
        render(response.data);
    }

    parseForm.addEventListener('submit', function (e) {
        e.preventDefault();
        lastText = $('quoteText').value.trim() || null;
        if (!lastText) {
            $('quoteText').focus();
            return;
        }
        estimate_({ text: lastText });
    });

    specForm.addEventListener('submit', function (e) {
        e.preventDefault();
        estimate_(specFromForm());
    });

    // Picking a kind pre-fills its typical size when the fields are empty.
    $('qKind').addEventListener('change', function () {
        const option = this.selectedOptions[0];
        if (!option || !option.value) return;
        [['qLength', 'length'], ['qWidth', 'width'], ['qHeight', 'height']].forEach(function (pair) {
            if (!$(pair[0]).value) $(pair[0]).value = option.getAttribute('data-' + pair[1]);
        });
    });

    submitForm.addEventListener('submit', async function (e) {
        e.preventDefault();
        submitError.classList.add('d-none');
        const button = submitForm.querySelector('button[type="submit"]');
        button.disabled = true;
        const body = Object.assign(specFromForm(), {
            text: lastText,
            name: $('qName').value.trim(),
            phone: $('qPhone').value.trim(),
            email: $('qEmail').value.trim() || null,
            note: $('qNote').value.trim() || null
        });
        const response = await FS.api('/api/quotes', { method: 'POST', body: body });
        button.disabled = false;
        if (!response.success) {
            submitError.textContent = errorText(response);
            submitError.classList.remove('d-none');
            return;
        }

        submitForm.hidden = true;
        success.replaceChildren();
        const box = el('div', 'alert alert-success mt-4');
        box.appendChild(el('h2', 'h5', 'Đã gửi yêu cầu báo giá ' + response.data.code));
        box.appendChild(el('p', 'mb-1', 'Giá dự kiến: ' + FS.formatCurrency(response.data.estimatedTotal) + '. Cửa hàng sẽ liên hệ để xác nhận giá chính thức.'));
        if (document.querySelector('meta[name="user-signed-in"][content="true"]')) {
            const link = el('a', 'alert-link', 'Theo dõi yêu cầu của bạn →');
            link.href = '/account/quotes/' + encodeURIComponent(response.data.code);
            box.appendChild(link);
        }
        success.appendChild(box);
        success.hidden = false;
        success.scrollIntoView({ behavior: 'smooth', block: 'center' });
    });

    // /bao-gia?q=... from the assistant or other pages.
    if ($('quoteText').value.trim()) parseForm.requestSubmit();
})(window, document);
