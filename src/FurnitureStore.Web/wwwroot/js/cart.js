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
        const result = await FS.api('/api/cart', { method: 'POST', body: purchase });
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
