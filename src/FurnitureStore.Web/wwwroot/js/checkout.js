/* Checkout: fill the form from a saved address, apply / remove coupons without leaving the page, prevent double orders. */
(function (document) {
    'use strict';
    const form = document.getElementById('checkoutForm');
    if (!form) return;
    const summary = document.querySelector('[data-checkout-summary]');

    form.addEventListener('change', function (event) {
        const radio = event.target.closest('[data-address-fill]');
        if (!radio) return;
        const set = function (name, value) {
            const field = form.querySelector('[name="Command.' + name + '"]');
            if (field) field.value = value || '';
        };
        set('FullName', radio.dataset.name);
        set('Phone', radio.dataset.phone);
        set('AddressLine', radio.dataset.line);
        set('Ward', radio.dataset.ward);
        set('District', radio.dataset.district);
        set('Province', radio.dataset.province);
    });

    form.addEventListener('submit', function () {
        // The button sits in the summary column (outside the form, linked with form="checkoutForm").
        const button = document.querySelector('[data-submit-once]');
        if (!button) return;
        setTimeout(function () { button.disabled = true; button.textContent = 'Đang đặt hàng...'; }, 0);
    });

    if (!summary || !window.FS) return;

    // Coupon forms post to /cart/coupon and come back to /checkout without JavaScript; with it, the API is used and
    // only the summary column is reloaded, so the address the customer is typing stays.
    async function reloadSummary() {
        const response = await fetch('/checkout/summary', { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
        if (!response.ok) {
            window.location.reload();
            return;
        }
        summary.innerHTML = await response.text();
    }

    summary.addEventListener('submit', async function (event) {
        const couponForm = event.target.closest('[data-coupon-form]');
        if (!couponForm) return;
        event.preventDefault();

        const removing = /\/remove$/.test(couponForm.getAttribute('action'));
        const codeField = couponForm.querySelector('[name="code"]');
        const button = couponForm.querySelector('button[type="submit"]');
        if (button) button.disabled = true;

        const result = removing
            ? await FS.api('/api/cart/coupon', { method: 'DELETE' })
            : await FS.api('/api/cart/coupon', { method: 'POST', body: { code: codeField ? codeField.value : '' } });

        if (!result.success) {
            if (button) button.disabled = false;
            FS.toast(result.errors && result.errors.length ? result.errors.join(' ') : result.message, 'error');
            if (codeField && codeField.type !== 'hidden') codeField.focus();
            return;
        }

        await reloadSummary();
        FS.toast(result.message, 'success');
        const focusTarget = summary.querySelector(removing ? '#couponCode' : '[data-coupon-applied] button');
        if (focusTarget) focusTarget.focus();
    });
})(document);
