/* Cart & wishlist interactions: add to cart, buy now, heart buttons, header badge. */
(function (window, document) {
    'use strict';

    const FS = window.FS;
    if (!FS) return;

    const signedIn = (document.querySelector('meta[name="user-signed-in"]') || {}).content === 'true';

    function setCartCount(count) {
        document.querySelectorAll('[data-cart-count]').forEach(function (badge) {
            badge.textContent = count;
            badge.classList.toggle('d-none', !count);
        });
    }

    function goToLogin() {
        window.location.href = '/account/login?returnUrl=' + encodeURIComponent(window.location.pathname + window.location.search);
    }

    function selectedPurchase() {
        const product = window.FSProduct;
        const variant = product ? product.currentVariant() : null;
        return variant ? { variantId: variant.id, quantity: product.quantity() } : null;
    }

    async function addToCart(buyNow, button) {
        const purchase = selectedPurchase();
        if (!purchase) {
            FS.toast('Vui lòng chọn phiên bản sản phẩm.', 'warning');
            return;
        }

        const original = button.innerHTML;
        button.disabled = true;
        button.textContent = buyNow ? 'Đang chuyển...' : 'Đang thêm...';
        // "Liên hệ đặt hàng" orders this product alone: the rest of the cart stays for later.
        const result = await FS.api('/api/cart', { method: 'POST', body: Object.assign({ buyNow: buyNow }, purchase) });
        button.disabled = false;
        button.innerHTML = original;

        if (!result.success) {
            FS.toast(result.errors && result.errors.length ? result.errors.join(' ') : result.message, 'error');
            return;
        }

        const count = result.data.items.reduce(function (sum, item) { return sum + item.quantity; }, 0);
        setCartCount(count);
        if (buyNow) {
            window.location.href = signedIn ? '/checkout' : '/account/login?returnUrl=%2Fcheckout';
        } else {
            FS.toast('Đã thêm vào giỏ hàng (' + count + ' sản phẩm).', 'success');
        }
    }

    async function toggleWishlist(button) {
        if (!signedIn) {
            goToLogin();
            return;
        }

        const productId = button.getAttribute('data-wishlist-product');
        button.disabled = true;
        const result = await FS.api('/api/wishlist/' + encodeURIComponent(productId) + '/toggle', { method: 'POST' });
        button.disabled = false;

        if (!result.success) {
            FS.toast(result.message, 'error');
            return;
        }

        markWishlist(productId, result.data.inWishlist);
        FS.toast(result.message, 'success');
    }

    function markWishlist(productId, active) {
        document.querySelectorAll('[data-wishlist-product="' + productId + '"]').forEach(function (button) {
            button.classList.toggle('active', active);
            button.setAttribute('aria-pressed', active ? 'true' : 'false');
            const icon = button.querySelector('i');
            if (icon) icon.className = 'bi ' + (active ? 'bi-heart-fill' : 'bi-heart') + (icon.className.indexOf('me-1') >= 0 ? ' me-1' : '');
        });
    }

    document.addEventListener('click', function (event) {
        const add = event.target.closest('[data-add-to-cart]');
        if (add) {
            event.preventDefault();
            addToCart(false, add);
            return;
        }

        const buy = event.target.closest('[data-buy-now]');
        if (buy) {
            event.preventDefault();
            addToCart(true, buy);
            return;
        }

        const heart = event.target.closest('[data-wishlist-product]');
        if (heart) {
            event.preventDefault();
            toggleWishlist(heart);
        }
    });

    // Cart page: typing a new quantity submits that line's form.
    document.addEventListener('change', function (event) {
        if (event.target.matches('[data-autosubmit-quantity]') && event.target.form) {
            event.target.form.submit();
        }
    });

    // Cart page: ticking lines (only ticked lines are ordered). Saved through the API, then the cart body is reloaded in
    // place so the totals follow without the page jumping. Without JavaScript the same forms post normally.
    const cartContent = document.querySelector('[data-cart-content]');
    let selecting = false;

    async function select(url, selected, box) {
        if (selecting) return;
        selecting = true;
        cartContent.setAttribute('aria-busy', 'true');
        const result = await FS.api(url, { method: 'PUT', body: { selected: selected } });
        if (!result.success) {
            box.checked = !selected;
            FS.toast((result.errors && result.errors[0]) || result.message, 'error');
        } else {
            try {
                const response = await fetch('/cart/content', { credentials: 'same-origin', headers: { 'X-Requested-With': 'XMLHttpRequest' } });
                if (response.ok) {
                    const focusId = box.id;
                    cartContent.innerHTML = await response.text();
                    // A message of the previous request ("Vui lòng tích chọn...") no longer applies.
                    const status = document.querySelector('[data-cart-status]');
                    if (status) status.replaceChildren();
                    const again = document.getElementById(focusId);
                    if (again) again.focus(); // keyboard users stay where they were
                } else {
                    window.location.reload();
                }
            } catch (e) {
                window.location.reload();
            }
        }
        cartContent.removeAttribute('aria-busy');
        selecting = false;
    }

    if (cartContent) {
        cartContent.addEventListener('change', function (event) {
            const line = event.target.closest('[data-cart-select]');
            if (line) {
                select('/api/cart/items/' + encodeURIComponent(line.getAttribute('data-cart-select')) + '/selected', line.checked, line);
                return;
            }
            const all = event.target.closest('[data-cart-select-all]');
            if (all) select('/api/cart/selected', all.checked, all);
        });
    }

    // Highlight hearts of products already in the wishlist.
    if (signedIn && document.querySelector('[data-wishlist-product]')) {
        FS.api('/api/wishlist/ids').then(function (result) {
            if (result.success) {
                result.data.forEach(function (id) { markWishlist(String(id), true); });
            }
        });
    }

    window.FSCart = { setCount: setCartCount };
})(window, document);
