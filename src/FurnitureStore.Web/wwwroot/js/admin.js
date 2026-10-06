/* Admin area helpers: confirmations, auto-submit selects, image previews with client-side checks. */
(function (window, document) {
    'use strict';

    const MAX_IMAGE_BYTES = 5 * 1024 * 1024;
    const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

    // <select data-autosubmit> submits its form on change.
    document.addEventListener('change', function (event) {
        if (event.target.matches('[data-autosubmit]') && event.target.form) {
            event.target.form.submit();
        }
    });

    /** Shows thumbnails of chosen images and rejects wrong types / oversized files before upload. */
    function previewImages(input) {
        const target = document.getElementById(input.getAttribute('data-preview-target'));
        if (!target) return;
        target.replaceChildren();

        const errors = [];
        Array.from(input.files || []).forEach(function (file) {
            if (ALLOWED_TYPES.indexOf(file.type) === -1) {
                errors.push(file.name + ': chỉ chấp nhận JPG, PNG, WEBP.');
                return;
            }
            if (file.size > MAX_IMAGE_BYTES) {
                errors.push(file.name + ': vượt quá 5 MB.');
                return;
            }
            const img = document.createElement('img');
            img.src = URL.createObjectURL(file);
            img.alt = file.name;
            img.onload = function () { URL.revokeObjectURL(img.src); };
            target.appendChild(img);
        });

        if (errors.length) {
            const message = document.createElement('div');
            message.className = 'text-danger small w-100';
            message.textContent = errors.join(' ');
            target.appendChild(message);
            input.value = '';
        }
    }

    document.addEventListener('change', function (event) {
        if (event.target.matches('[data-image-input]')) {
            previewImages(event.target);
        }
    });

    window.FSAdmin = { previewImages: previewImages };
})(window, document);

/* Color attribute form: keep the color picker and the hex text box in sync. */
(function (document) {
    'use strict';
    const picker = document.querySelector('[data-color-picker]');
    const text = document.querySelector('[data-color-text]');
    if (!picker || !text) return;
    picker.addEventListener('input', function () { text.value = picker.value.toUpperCase(); });
    text.addEventListener('input', function () { if (/^#[0-9a-fA-F]{6}$/.test(text.value)) picker.value = text.value; });
})(document);

/* Coupon form: random code, % / ₫ unit, cap only for percentages, live preview of what customers will read. */
(function (document) {
    'use strict';
    const form = document.querySelector('[data-coupon-form-admin]');
    if (!form) return;
    const field = function (name) { return form.querySelector('[name="Command.' + name + '"]'); };
    const type = form.querySelector('[data-discount-type]');
    const unit = form.querySelector('[data-discount-unit]');
    const percentOnly = form.querySelector('[data-percent-only]');
    const preview = form.querySelector('[data-coupon-preview]');
    const money = function (value) { return new Intl.NumberFormat('vi-VN').format(value) + '₫'; };
    const number = function (name) { const v = parseFloat(field(name).value); return isNaN(v) ? null : v; };

    function update() {
        const percent = type.value === 'Percentage';
        unit.textContent = percent ? '%' : '₫';
        percentOnly.hidden = !percent;

        const value = number('DiscountValue');
        if (!value || value <= 0) {
            preview.textContent = 'nhập mức giảm để xem trước';
            return;
        }
        const cap = number('MaxDiscountAmount');
        let text = percent
            ? 'Giảm ' + new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 2 }).format(value) + '%' + (cap ? ', tối đa ' + money(cap) : '')
            : 'Giảm ' + money(value);
        const min = number('MinOrderAmount');
        text += ' · ' + (min && min > 0 ? 'Đơn từ ' + money(min) : 'Mọi đơn hàng');
        const perUser = number('UsageLimitPerUser');
        if (perUser) text += ' · mỗi khách ' + perUser + ' lần';
        preview.textContent = text;
    }

    form.addEventListener('input', update);
    form.addEventListener('change', update);
    update();

    const generate = form.querySelector('[data-generate-code]');
    if (generate) {
        generate.addEventListener('click', function () {
            // No 0/O or 1/I/L: codes are often read out over the phone.
            const alphabet = 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
            const bytes = new Uint8Array(6);
            window.crypto.getRandomValues(bytes);
            let code = 'NM';
            bytes.forEach(function (b) { code += alphabet[b % alphabet.length]; });
            field('Code').value = code;
            field('Code').focus();
        });
    }
})(document);
