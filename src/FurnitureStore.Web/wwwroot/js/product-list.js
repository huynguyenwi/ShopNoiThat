/* Product list: apply filters / sort / paging with AJAX (falls back to normal form submit without JS). */
(function (window, document) {
    'use strict';

    const form = document.querySelector('[data-filter-form]');
    const results = document.getElementById('productResults');
    if (!form || !results) return;

    let timer = null;
    let controller = null;

    function buildUrl() {
        const params = new URLSearchParams();
        new FormData(form).forEach(function (value, key) {
            if (value !== null && String(value).trim() !== '') params.append(key, value);
        });
        const query = params.toString();
        return '/products' + (query ? '?' + query : '');
    }

    async function load(url, push) {
        if (controller) controller.abort();
        controller = new AbortController();
        results.classList.add('is-loading');
        try {
            const response = await fetch(url, {
                headers: { 'X-Requested-With': 'XMLHttpRequest', 'Accept': 'text/html' },
                credentials: 'same-origin',
                signal: controller.signal
            });
            if (!response.ok) throw new Error('HTTP ' + response.status);
            results.innerHTML = await response.text();
            if (push) window.history.pushState({ filters: true }, '', url);
            const top = results.getBoundingClientRect().top + window.scrollY - 120;
            if (window.scrollY > top) window.scrollTo({ top: top, behavior: 'smooth' });
        } catch (error) {
            if (error.name === 'AbortError') return;
            window.location.href = url;
        } finally {
            results.classList.remove('is-loading');
        }
    }

    function scheduleLoad() {
        clearTimeout(timer);
        timer = setTimeout(function () { load(buildUrl(), true); }, 300);
    }

    form.addEventListener('change', scheduleLoad);
    form.addEventListener('submit', function (event) {
        event.preventDefault();
        load(buildUrl(), true);
        const panel = document.getElementById('filterPanel');
        if (panel && window.bootstrap && panel.classList.contains('show')) {
            window.bootstrap.Offcanvas.getOrCreateInstance(panel).hide();
        }
    });

    form.querySelectorAll('[data-price-min], [data-price-max]').forEach(function (button) {
        button.addEventListener('click', function () {
            form.querySelector('[name=minPrice]').value = button.getAttribute('data-price-min') || '';
            form.querySelector('[name=maxPrice]').value = button.getAttribute('data-price-max') || '';
            scheduleLoad();
        });
    });

    results.addEventListener('change', function (event) {
        if (event.target.matches('[data-sort-select]')) {
            form.querySelector('[data-sort-field]').value = event.target.value;
            load(buildUrl(), true);
        }
    });

    results.addEventListener('click', function (event) {
        const pageLink = event.target.closest('[data-page-link]');
        if (pageLink && pageLink.getAttribute('href') !== '#') {
            event.preventDefault();
            load(pageLink.getAttribute('href'), true);
        }
        // Filter chips navigate normally so the sidebar form stays in sync with the URL.
    });

    window.addEventListener('popstate', function () { window.location.reload(); });
})(window, document);
