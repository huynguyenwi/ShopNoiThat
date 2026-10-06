/* Product detail: variant selection, gallery, quantity. Cart / wishlist actions live in cart.js. */
(function (window, document) {
    'use strict';

    const dataElement = document.getElementById('productData');
    if (!dataElement) return;

    const data = JSON.parse(dataElement.textContent);
    const variants = data.variants || [];
    const keyOf = { color: 'colorId', material: 'materialId', size: 'sizeId', style: 'styleId' };
    const groups = {};
    document.querySelectorAll('[data-option-group]').forEach(function (group) {
        groups[group.getAttribute('data-option-group')] = group;
    });

    const $ = function (selector) { return document.querySelector(selector); };
    const money = function (value) { return window.FS ? window.FS.formatCurrency(value) : String(value); };

    let current = variants.find(function (v) { return v.id === data.selectedVariantId; })
        || variants.find(function (v) { return v.stock > 0; })
        || variants[0];

    const selection = {};
    Object.keys(groups).forEach(function (dimension) {
        selection[dimension] = current ? current[keyOf[dimension]] : null;
    });

    function matches(variant, wanted) {
        return Object.keys(wanted).every(function (dimension) {
            return wanted[dimension] == null || variant[keyOf[dimension]] === wanted[dimension];
        });
    }

    /** Picks the best variant after the user clicks an option: exact match in stock, exact match, then relax other options. */
    function choose(dimension, value) {
        const wanted = Object.assign({}, selection);
        wanted[dimension] = value;

        let candidate = variants.find(function (v) { return matches(v, wanted) && v.stock > 0; })
            || variants.find(function (v) { return matches(v, wanted); });

        if (!candidate) {
            const withValue = variants.filter(function (v) { return v[keyOf[dimension]] === value; });
            candidate = withValue.find(function (v) { return v.stock > 0; }) || withValue[0];
        }

        if (!candidate) return;
        Object.keys(selection).forEach(function (d) { selection[d] = candidate[keyOf[d]]; });
        apply(candidate, true);
    }

    function setText(selector, text) {
        const element = $(selector);
        if (element) element.textContent = text;
    }

    function renderOptions() {
        Object.keys(groups).forEach(function (dimension) {
            const group = groups[dimension];
            group.querySelectorAll('[data-option]').forEach(function (button) {
                const value = Number(button.getAttribute('data-value'));
                const active = selection[dimension] === value;
                const wanted = Object.assign({}, selection);
                wanted[dimension] = value;
                const exists = variants.some(function (v) { return matches(v, wanted); });
                const inStock = variants.some(function (v) { return matches(v, wanted) && v.stock > 0; });

                button.classList.toggle('active', active);
                button.setAttribute('aria-pressed', active ? 'true' : 'false');
                button.classList.toggle('unavailable', !exists);
                button.classList.toggle('sold-out', exists && !inStock);
                button.title = button.getAttribute('data-label') + (!exists ? ' (không có với lựa chọn hiện tại)' : !inStock ? ' (hết hàng)' : '');

                if (active) {
                    const label = group.querySelector('[data-option-label]');
                    if (label) label.textContent = button.getAttribute('data-label');
                }
            });
        });
    }

    function renderGallery(variant) {
        const urls = [];
        (variant.images || []).concat(data.productImages || []).forEach(function (url) {
            if (urls.indexOf(url) === -1) urls.push(url);
        });
        if (urls.length === 0) return;

        const main = $('#galleryMain');
        if (main) main.src = urls[0];

        const thumbs = $('#galleryThumbs');
        if (!thumbs) return;
        thumbs.replaceChildren();
        urls.forEach(function (url, index) {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'gallery-thumb' + (index === 0 ? ' active' : '');
            button.setAttribute('data-image', url);
            button.setAttribute('role', 'listitem');
            button.setAttribute('aria-label', 'Xem ảnh ' + (index + 1));
            const img = document.createElement('img');
            img.src = url;
            img.alt = '';
            img.width = 120;
            img.height = 90;
            button.appendChild(img);
            thumbs.appendChild(button);
        });
    }

    function renderStock(variant) {
        const stock = $('#variantStock');
        const addButtons = document.querySelectorAll('[data-add-to-cart], [data-buy-now]');
        const quantity = $('#quantityInput');

        if (variant.stock <= 0) {
            stock.className = 'stock-status is-out';
            stock.textContent = 'Hết hàng - liên hệ để đặt sản xuất';
        } else if (variant.isLowStock) {
            stock.className = 'stock-status is-low';
            stock.textContent = 'Chỉ còn ' + variant.stock + ' sản phẩm';
        } else {
            stock.className = 'stock-status is-in';
            stock.textContent = 'Còn hàng';
        }

        addButtons.forEach(function (button) { button.disabled = variant.stock <= 0; });
        if (quantity) {
            quantity.max = Math.max(1, Math.min(99, variant.stock));
            if (Number(quantity.value) > Number(quantity.max)) quantity.value = quantity.max;
        }
    }

    function renderParts(variant) {
        const list = $('#variantParts');
        if (!list) return;
        list.replaceChildren();
        (variant.parts || []).forEach(function (part) {
            const li = document.createElement('li');
            li.appendChild(document.createTextNode(part.kind + (part.part ? ' (' + part.part + ')' : '') + ': '));
            const strong = document.createElement('strong');
            strong.textContent = part.name;
            li.appendChild(strong);
            list.appendChild(li);
        });
        list.classList.toggle('d-none', !variant.parts || variant.parts.length === 0);
    }

    function apply(variant, updateHistory) {
        current = variant;
        renderOptions();

        setText('#variantSku', variant.sku);
        setText('#variantPrice', money(variant.price));
        setText('[data-mobile-price]', money(variant.price));
        setText('#variantDimensions', variant.dimensionsText || '-');

        const oldPrice = $('#variantOldPrice');
        if (oldPrice) {
            oldPrice.textContent = variant.originalPrice ? money(variant.originalPrice) : '';
            oldPrice.classList.toggle('d-none', !variant.originalPrice);
        }

        document.querySelectorAll('#variantDiscount, [data-variant-badge]').forEach(function (badge) {
            badge.textContent = variant.discountPercent > 0 ? '-' + variant.discountPercent + '%' : '';
            badge.classList.toggle('d-none', !(variant.discountPercent > 0));
        });

        const hidden = $('#selectedVariantId');
        if (hidden) hidden.value = variant.id;

        renderParts(variant);
        renderStock(variant);
        renderGallery(variant);

        if (updateHistory && window.history && window.history.replaceState) {
            window.history.replaceState(null, '', window.location.pathname + '?variant=' + variant.id);
        }

        document.dispatchEvent(new CustomEvent('fs:variant-changed', { detail: variant }));
    }

    document.addEventListener('click', function (event) {
        const option = event.target.closest('[data-option]');
        if (option) {
            choose(option.getAttribute('data-option'), Number(option.getAttribute('data-value')));
            return;
        }

        const thumb = event.target.closest('.gallery-thumb');
        if (thumb) {
            const main = $('#galleryMain');
            if (main) main.src = thumb.getAttribute('data-image');
            document.querySelectorAll('.gallery-thumb').forEach(function (t) { t.classList.toggle('active', t === thumb); });
            return;
        }

        const step = event.target.closest('[data-quantity-step]');
        if (step) {
            const input = $('#quantityInput');
            const next = Number(input.value || 1) + Number(step.getAttribute('data-quantity-step'));
            input.value = Math.max(1, Math.min(Number(input.max || 99), next));
        }
    });

    const quantityInput = $('#quantityInput');
    if (quantityInput) {
        quantityInput.addEventListener('change', function () {
            const value = Math.floor(Number(quantityInput.value) || 1);
            quantityInput.value = Math.max(1, Math.min(Number(quantityInput.max || 99), value));
        });
    }

    if (current) apply(current, false);

    // Reviews: page through the list without reloading the whole product page.
    const reviewsSection = document.getElementById('reviews');
    if (reviewsSection) {
        reviewsSection.addEventListener('click', function (event) {
            const link = event.target.closest('#reviewList [data-page-link]');
            if (!link || link.closest('.disabled')) return;
            const url = new URL(link.href, window.location.origin);
            const page = url.searchParams.get('reviewPage');
            if (!page) return;
            event.preventDefault();

            fetch(window.location.pathname.replace(/\/$/, '') + '/reviews?page=' + encodeURIComponent(page), {
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'text/html' },
                credentials: 'same-origin'
            }).then(function (response) {
                if (!response.ok) throw new Error('HTTP ' + response.status);
                return response.text();
            }).then(function (html) {
                const list = document.getElementById('reviewList');
                if (!list) return;
                // Server-rendered Razor partial (HTML-encoded by the server).
                list.outerHTML = html;
                reviewsSection.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }).catch(function () {
                window.location.href = link.href;
            });
        });

        const imageInput = document.getElementById('reviewImages');
        if (imageInput) {
            imageInput.addEventListener('change', function () {
                const max = Number(imageInput.getAttribute('data-max-files') || 3);
                const tooBig = Array.prototype.some.call(imageInput.files, function (f) { return f.size > 5 * 1024 * 1024; });
                if (imageInput.files.length > max || tooBig) {
                    window.FS && window.FS.toast(tooBig ? 'Mỗi ảnh tối đa 5MB.' : 'Chỉ được chọn tối đa ' + max + ' ảnh.', 'warning');
                    imageInput.value = '';
                }
            });
        }
    }

    window.FSProduct = {
        currentVariant: function () { return current; },
        quantity: function () { return Math.max(1, Number(quantityInput ? quantityInput.value : 1) || 1); },
        productId: data.productId
    };
})(window, document);
