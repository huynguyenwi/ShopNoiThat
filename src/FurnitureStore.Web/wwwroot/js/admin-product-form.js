/* Admin product form: dynamic variant rows, slug generation, variant SKU suggestions. */
(function (window, document) {
    'use strict';

    const form = document.querySelector('[data-product-form]');
    if (!form) return;

    const table = form.querySelector('.variant-table');
    const template = document.getElementById('variantTemplate');
    let counter = 0;

    /** Vietnamese-aware slug: "Bàn ăn gỗ óc chó" → "ban-an-go-oc-cho" (same rules as the server). */
    function slugify(text) {
        return (text || '')
            .toLowerCase()
            .replace(/đ/g, 'd')
            .normalize('NFD').replace(/[̀-ͯ]/g, '')
            .replace(/[^a-z0-9]+/g, '-')
            .replace(/^-+|-+$/g, '')
            .substring(0, 200);
    }

    // ---------------------------------------------------------------- slug
    const slugSource = form.querySelector('[data-slug-source]');
    const slugTarget = form.querySelector('[data-slug-target]');
    if (slugSource && slugTarget) {
        let touched = slugTarget.value.trim() !== '';
        slugTarget.addEventListener('input', function () { touched = slugTarget.value.trim() !== ''; });
        slugSource.addEventListener('input', function () {
            if (!touched) slugTarget.placeholder = slugify(slugSource.value) || 'tu-dong-tao-tu-ten';
        });
    }

    // ---------------------------------------------------------------- variant rows
    function addVariant() {
        const key = 'n' + (++counter) + Date.now().toString(36);
        const html = template.innerHTML.replace(/__key__/g, key);
        const wrapper = document.createElement('table');
        wrapper.innerHTML = html;
        const row = wrapper.querySelector('tbody');
        table.appendChild(row);

        if (!form.querySelector('input[name="defaultVariantKey"]:checked')) {
            row.querySelector('input[name="defaultVariantKey"]').checked = true;
        }
        suggestSku(row);
        row.querySelector('[data-variant-sku]').focus();
    }

    function variantRows() { return Array.from(table.querySelectorAll('[data-variant-row]')); }

    function removeVariant(row) {
        if (variantRows().length <= 1) {
            window.alert('Sản phẩm phải có ít nhất một biến thể.');
            return;
        }
        if (!window.confirm('Xóa biến thể này? Thay đổi chỉ được áp dụng khi bấm Lưu.')) return;

        const wasDefault = row.querySelector('input[name="defaultVariantKey"]').checked;
        row.remove();
        if (wasDefault) {
            const first = table.querySelector('input[name="defaultVariantKey"]');
            if (first) first.checked = true;
        }
    }

    /** Suggests "<PRODUCT-SKU>-<COLOR>-<SIZE>" for empty variant SKUs. */
    function suggestSku(row) {
        const skuInput = row.querySelector('[data-variant-sku]');
        if (!skuInput || (skuInput.value && !skuInput.dataset.suggested)) return;

        const productSku = (form.querySelector('[data-product-sku]').value || '').trim().toUpperCase();
        if (!productSku) return;

        const parts = [productSku];
        const color = row.querySelector('[data-variant-color]');
        const size = row.querySelector('[data-variant-size]');
        const colorCode = color && color.selectedOptions[0] ? color.selectedOptions[0].getAttribute('data-code') : null;
        const sizeCode = size && size.selectedOptions[0] ? size.selectedOptions[0].getAttribute('data-code') : null;
        if (colorCode) parts.push(colorCode.split('-').map(function (p) { return p.charAt(0); }).join('').toUpperCase());
        if (sizeCode) parts.push(sizeCode.split('-').pop().toUpperCase());

        skuInput.value = parts.join('-');
        skuInput.dataset.suggested = 'true';
    }

    form.addEventListener('click', function (event) {
        if (event.target.closest('[data-add-variant]')) {
            addVariant();
            return;
        }

        const remove = event.target.closest('[data-remove-variant]');
        if (remove) {
            removeVariant(remove.closest('[data-variant-row]'));
            return;
        }

        const toggle = event.target.closest('[data-toggle-variant-details]');
        if (toggle) {
            const details = toggle.closest('[data-variant-row]').querySelector('.variant-details');
            details.hidden = !details.hidden;
        }
    });

    form.addEventListener('change', function (event) {
        if (event.target.matches('[data-variant-color], [data-variant-size]')) {
            suggestSku(event.target.closest('[data-variant-row]'));
        }
    });

    form.addEventListener('input', function (event) {
        if (event.target.matches('[data-variant-sku]')) {
            delete event.target.dataset.suggested;
        }
    });

    // Show the details row of variants that already have secondary parts, so admins notice them.
    variantRows().forEach(function (row) {
        const details = row.querySelector('.variant-details');
        const hasExtras = Array.from(details.querySelectorAll('select, input:not([type=file])'))
            .some(function (field) { return field.value && field.value !== ''; });
        if (hasExtras) details.hidden = false;
    });
})(window, document);
