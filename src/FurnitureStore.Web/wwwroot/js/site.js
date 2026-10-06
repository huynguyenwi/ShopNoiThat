/* Nhà Mộc Furniture - shared client script. Exposes window.FS helpers. */
(function (window, document, $) {
    'use strict';

    const csrfMeta = document.querySelector('meta[name="csrf-token"]');
    const csrfToken = csrfMeta ? csrfMeta.getAttribute('content') : '';
    const csrfHeader = 'X-CSRF-TOKEN';
    const safeMethods = /^(GET|HEAD|OPTIONS)$/i;

    // jQuery AJAX: attach the anti-forgery token to every state-changing same-origin request.
    if ($) {
        $.ajaxSetup({
            beforeSend: function (xhr, settings) {
                if (!safeMethods.test(settings.type) && !this.crossDomain) {
                    xhr.setRequestHeader(csrfHeader, csrfToken);
                }
            }
        });
    }

    /**
     * Calls a JSON API endpoint and always resolves to the standard envelope
     * { success, message, data, errors }.
     */
    async function api(url, options) {
        const opts = options || {};
        const method = (opts.method || 'GET').toUpperCase();
        const headers = Object.assign({
            'Accept': 'application/json',
            'X-Requested-With': 'XMLHttpRequest'
        }, opts.headers || {});

        const init = { method: method, headers: headers, credentials: 'same-origin', signal: opts.signal };

        if (opts.body !== undefined) {
            if (opts.body instanceof FormData) {
                init.body = opts.body;
            } else {
                headers['Content-Type'] = 'application/json';
                init.body = JSON.stringify(opts.body);
            }
        }

        if (!safeMethods.test(method)) {
            headers[csrfHeader] = csrfToken;
        }

        try {
            const response = await fetch(url, init);
            const contentType = response.headers.get('content-type') || '';
            if (contentType.indexOf('application/json') === -1) {
                return { success: response.ok, message: response.ok ? '' : 'Máy chủ trả về phản hồi không hợp lệ.', data: null, errors: [] };
            }
            return await response.json();
        } catch (error) {
            if (error && error.name === 'AbortError') {
                throw error;
            }
            return { success: false, message: 'Không thể kết nối máy chủ. Vui lòng kiểm tra mạng.', data: null, errors: [] };
        }
    }

    /** Shows a Bootstrap toast. Text is inserted with textContent (never innerHTML) to avoid XSS. */
    function toast(message, type) {
        const variant = type || 'success';
        let container = document.getElementById('fs-toast-container');
        if (!container) {
            container = document.createElement('div');
            container.id = 'fs-toast-container';
            container.className = 'toast-container position-fixed bottom-0 start-50 translate-middle-x p-3';
            container.style.zIndex = '1090';
            document.body.appendChild(container);
        }

        const el = document.createElement('div');
        el.className = 'toast align-items-center border-0 text-bg-' + (variant === 'error' ? 'danger' : variant === 'warning' ? 'warning' : 'dark');
        el.setAttribute('role', variant === 'error' ? 'alert' : 'status');
        el.setAttribute('aria-live', variant === 'error' ? 'assertive' : 'polite');
        el.setAttribute('aria-atomic', 'true');

        const wrap = document.createElement('div');
        wrap.className = 'd-flex';
        const body = document.createElement('div');
        body.className = 'toast-body';
        body.textContent = message;
        const close = document.createElement('button');
        close.type = 'button';
        close.className = 'btn-close btn-close-white me-2 m-auto';
        close.setAttribute('data-bs-dismiss', 'toast');
        close.setAttribute('aria-label', 'Đóng');

        wrap.appendChild(body);
        wrap.appendChild(close);
        el.appendChild(wrap);
        container.appendChild(el);

        const instance = window.bootstrap ? new window.bootstrap.Toast(el, { delay: 3500 }) : null;
        el.addEventListener('hidden.bs.toast', function () { el.remove(); });
        if (instance) {
            instance.show();
        }
    }

    /** 16900000 → "16.900.000₫" (same format as the server-side Format.Money). */
    function formatCurrency(value) {
        return new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Math.round(value || 0)) + '₫';
    }

    // ---------------------------------------------------------------- Search autocomplete
    function initSearchAutocomplete(form) {
        const input = form.querySelector('input[name="q"]');
        const box = form.querySelector('.search-suggestions');
        if (!input || !box) return;

        let timer = null;
        let controller = null;
        let activeIndex = -1;

        function hide() {
            box.hidden = true;
            box.replaceChildren();
            input.setAttribute('aria-expanded', 'false');
            activeIndex = -1;
        }

        function options() { return Array.from(box.querySelectorAll('.search-suggestion')); }

        function highlight(index) {
            const items = options();
            items.forEach(function (item, i) { item.classList.toggle('active', i === index); item.setAttribute('aria-selected', i === index ? 'true' : 'false'); });
            activeIndex = index;
        }

        function render(list, query) {
            box.replaceChildren();
            if (!list.length) {
                const empty = document.createElement('div');
                empty.className = 'search-empty';
                empty.textContent = 'Không tìm thấy sản phẩm cho "' + query + '"';
                box.appendChild(empty);
            }

            list.forEach(function (item, index) {
                const link = document.createElement('a');
                link.className = 'search-suggestion';
                link.href = item.url;
                link.id = 'search-option-' + index;
                link.setAttribute('role', 'option');

                if (item.imageUrl) {
                    const img = document.createElement('img');
                    img.src = item.imageUrl;
                    img.alt = '';
                    img.width = 56;
                    img.height = 42;
                    link.appendChild(img);
                }

                const text = document.createElement('span');
                text.className = 'search-suggestion-text';
                const name = document.createElement('span');
                name.className = 'search-suggestion-name';
                name.textContent = item.name;
                const category = document.createElement('span');
                category.className = 'search-suggestion-category';
                category.textContent = item.categoryName;
                text.appendChild(name);
                text.appendChild(category);
                link.appendChild(text);

                const price = document.createElement('span');
                price.className = 'search-suggestion-price';
                price.textContent = formatCurrency(item.price);
                link.appendChild(price);

                box.appendChild(link);
            });

            const all = document.createElement('a');
            all.className = 'search-all';
            all.href = '/products?q=' + encodeURIComponent(query);
            all.textContent = 'Xem tất cả kết quả cho "' + query + '"';
            box.appendChild(all);

            box.hidden = false;
            input.setAttribute('aria-expanded', 'true');
            activeIndex = -1;
        }

        async function fetchSuggestions(query) {
            if (controller) controller.abort();
            controller = new AbortController();
            try {
                const result = await api('/api/products/search?q=' + encodeURIComponent(query) + '&limit=6', { signal: controller.signal });
                if (input.value.trim() === query) render(result.success ? result.data : [], query);
            } catch (error) {
                if (error.name !== 'AbortError') hide();
            }
        }

        input.addEventListener('input', function () {
            clearTimeout(timer);
            const query = input.value.trim();
            if (query.length < 2) { hide(); return; }
            timer = setTimeout(function () { fetchSuggestions(query); }, 250);
        });

        input.addEventListener('keydown', function (event) {
            const items = options();
            if (box.hidden || items.length === 0) return;
            if (event.key === 'ArrowDown') {
                event.preventDefault();
                highlight((activeIndex + 1) % items.length);
            } else if (event.key === 'ArrowUp') {
                event.preventDefault();
                highlight(activeIndex <= 0 ? items.length - 1 : activeIndex - 1);
            } else if (event.key === 'Enter' && activeIndex >= 0) {
                event.preventDefault();
                window.location.href = items[activeIndex].href;
            } else if (event.key === 'Escape') {
                hide();
            }
        });

        document.addEventListener('click', function (event) {
            if (!form.contains(event.target)) hide();
        });
    }

    document.querySelectorAll('[data-search-autocomplete]').forEach(initSearchAutocomplete);

    // <form data-confirm="Message"> asks before submitting (the server still validates everything).
    let confirmedByButton = null;
    document.addEventListener('click', function (event) {
        // Buttons outside the form that target it with form="id".
        const button = event.target.closest('button[form]');
        if (!button) return;
        const form = document.getElementById(button.getAttribute('form'));
        const message = form && form.getAttribute('data-confirm');
        if (!message) return;
        if (!window.confirm(message)) {
            event.preventDefault();
        } else {
            confirmedByButton = form;
        }
    }, true);

    document.addEventListener('submit', function (event) {
        const form = event.target;
        const message = form.getAttribute && form.getAttribute('data-confirm');
        if (!message || confirmedByButton === form) {
            confirmedByButton = null;
            return;
        }
        if (!window.confirm(message)) event.preventDefault();
    }, true);

    // Show / hide password fields: <button data-toggle-password="InputId">.
    document.addEventListener('click', function (event) {
        const button = event.target.closest('[data-toggle-password]');
        if (!button) return;
        const input = document.getElementById(button.getAttribute('data-toggle-password'));
        if (!input) return;
        const show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        button.setAttribute('aria-label', show ? 'Ẩn mật khẩu' : 'Hiện mật khẩu');
        const icon = button.querySelector('i');
        if (icon) icon.className = show ? 'bi bi-eye-slash' : 'bi bi-eye';
    });

    // Header shadow once the page is scrolled.
    const header = document.querySelector('.site-header');
    if (header) {
        const onScroll = function () { header.classList.toggle('is-scrolled', window.scrollY > 8); };
        window.addEventListener('scroll', onScroll, { passive: true });
        onScroll();
    }

    window.FS = { api: api, toast: toast, formatCurrency: formatCurrency, csrfToken: csrfToken, csrfHeader: csrfHeader };
})(window, document, window.jQuery);
