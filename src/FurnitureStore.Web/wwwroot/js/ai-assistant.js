/* Nhà Mộc Furniture - AI assistant widget. Requires site.js (FS) and chat-core.js (FSChat). Exposes window.FSAi.
   Everything coming from the server is inserted with textContent (never innerHTML). */
(function (window, document) {
    'use strict';

    if (!window.FS) return;
    const FS = window.FS;
    const storageKey = 'fs.ai.conversation';

    function storage(action, value) {
        try {
            if (action === 'get') return window.sessionStorage.getItem(storageKey);
            if (action === 'set') window.sessionStorage.setItem(storageKey, value);
            if (action === 'clear') window.sessionStorage.removeItem(storageKey);
        } catch (e) { /* storage disabled: the conversation simply is not restored */ }
        return null;
    }

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    /** Product card built from a ProductRecommendationDto (price from the database). */
    function renderCard(product, compact) {
        const card = el('a', 'ai-card' + (compact ? ' ai-card-compact' : ''));
        card.href = product.url;

        const media = el('div', 'ai-card-media');
        if (product.imageUrl) {
            const img = el('img');
            img.src = product.imageUrl;
            img.alt = '';
            img.loading = 'lazy';
            img.width = 160;
            img.height = 120;
            media.appendChild(img);
        }
        if (!product.inStock) media.appendChild(el('span', 'badge text-bg-secondary ai-card-badge', 'Tạm hết'));
        card.appendChild(media);

        const body = el('div', 'ai-card-body');
        body.appendChild(el('div', 'ai-card-category', product.categoryName));
        body.appendChild(el('div', 'ai-card-name', product.name));
        const price = el('div', 'ai-card-price');
        price.appendChild(el('strong', null, FS.formatCurrency(product.price)));
        if (product.originalPrice) price.appendChild(el('s', 'ms-1 small text-muted', FS.formatCurrency(product.originalPrice)));
        body.appendChild(price);
        if (product.reason) body.appendChild(el('div', 'ai-card-reason', product.reason));
        if (!compact && product.variantSummary) body.appendChild(el('div', 'ai-card-variants', product.variantSummary));
        card.appendChild(body);
        return card;
    }

    window.FSAi = { renderCard: renderCard };

    // ================================================================== floating widget

    const root = document.getElementById('aiWidget');
    if (!root) return;

    const launcher = document.getElementById('aiLauncher');
    const panel = document.getElementById('aiPanel');
    const list = document.getElementById('aiMessages');
    const thinking = document.getElementById('aiThinking');
    const compose = document.getElementById('aiCompose');
    const input = document.getElementById('aiInput');
    const sendButton = document.getElementById('aiSend');
    const grow = window.FSChat ? window.FSChat.autoGrow : function () { };

    const state = { conversationId: Number(storage('get')) || null, productId: null, busy: false, restored: false };

    function isOpen() { return !panel.hidden; }

    function scrollDown() { list.scrollTop = list.scrollHeight; }

    function addUser(text) {
        const row = el('div', 'chat-msg from-me');
        row.appendChild(el('div', 'chat-bubble', text));
        list.appendChild(row);
        scrollDown();
    }

    function addAssistant(text, products, suggestions, isError) {
        list.querySelectorAll('.ai-suggestions').forEach(function (s) { s.remove(); }); // only the latest chips stay
        const row = el('div', 'chat-msg from-other ai-answer' + (isError ? ' is-error' : ''));
        row.appendChild(el('div', 'chat-bubble', text));
        if (products && products.length) {
            const cards = el('div', 'ai-cards');
            products.forEach(function (p) { cards.appendChild(renderCard(p, true)); });
            row.appendChild(cards);
        }
        if (suggestions && suggestions.length) {
            const chips = el('div', 'ai-suggestions');
            suggestions.forEach(function (s) {
                const chip = el('button', 'ai-chip', s);
                chip.type = 'button';
                chip.setAttribute('data-ai-say', s);
                chips.appendChild(chip);
            });
            row.appendChild(chips);
        }
        list.appendChild(row);
        scrollDown();
    }

    function resetMessages() {
        list.querySelectorAll('.chat-msg:not(.ai-welcome)').forEach(function (n) { n.remove(); });
    }

    async function restore() {
        state.restored = true;
        if (!state.conversationId) return;
        const result = await FS.api('/api/ai/conversations/' + state.conversationId);
        if (!result.success) {
            state.conversationId = null;
            storage('clear');
            return;
        }
        resetMessages();
        (result.data.messages || []).forEach(function (m) {
            if (m.role === 'User') addUser(m.content);
            else if (m.role === 'Assistant') addAssistant(m.content, m.products, [], m.isError);
        });
    }

    async function send(text) {
        const message = (text || '').trim();
        if (!message || state.busy) return;
        state.busy = true;
        sendButton.disabled = true;
        addUser(message);
        input.value = '';
        grow(input);
        thinking.hidden = false;
        scrollDown();

        try {
            const result = await FS.api('/api/ai/chat', {
                method: 'POST',
                body: { conversationId: state.conversationId, message: message, productId: state.productId }
            });
            if (!result.success) {
                addAssistant((result.errors && result.errors.length ? result.errors.join(' ') : result.message) || 'Không gửi được câu hỏi.', [], [], true);
                return;
            }
            state.conversationId = result.data.conversationId;
            storage('set', String(state.conversationId));
            addAssistant(result.data.reply, result.data.products, result.data.suggestions, false);
        } finally {
            thinking.hidden = true;
            state.busy = false;
            sendButton.disabled = false;
            input.focus();
        }
    }

    function open() {
        document.dispatchEvent(new CustomEvent('fs:panel-open', { detail: 'ai' }));
        document.body.setAttribute('data-open-panel', 'ai');
        panel.hidden = false;
        launcher.setAttribute('aria-expanded', 'true');
        root.classList.add('is-open');
        const ready = state.restored ? Promise.resolve() : restore();
        ready.then(function () { scrollDown(); input.focus(); });
        return ready;
    }

    function close(keepFocus) {
        panel.hidden = true;
        launcher.setAttribute('aria-expanded', 'false');
        root.classList.remove('is-open');
        if (document.body.getAttribute('data-open-panel') === 'ai') document.body.removeAttribute('data-open-panel');
        if (!keepFocus) launcher.focus();
    }

    function newConversation() {
        state.conversationId = null;
        state.productId = null;
        storage('clear');
        resetMessages();
    }

    document.addEventListener('fs:panel-open', function (e) { if (e.detail !== 'ai' && isOpen()) close(true); });
    launcher.addEventListener('click', function () { if (isOpen()) close(); else open(); });
    document.getElementById('aiClose').addEventListener('click', function () { close(); });
    document.getElementById('aiNew').addEventListener('click', function () { newConversation(); input.focus(); });
    panel.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });

    compose.addEventListener('submit', function (e) { e.preventDefault(); send(input.value); });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            send(input.value);
        }
    });
    input.addEventListener('input', function () { grow(input); });

    document.addEventListener('click', function (e) {
        const chip = e.target.closest('[data-ai-say]');
        if (chip && root.contains(chip)) {
            send(chip.getAttribute('data-ai-say'));
            return;
        }

        // "AI tư vấn sản phẩm này" on product pages: a new conversation about that product.
        const productButton = e.target.closest('[data-ai-product]');
        if (productButton) {
            e.preventDefault();
            const productId = Number(productButton.getAttribute('data-ai-product')) || null;
            if (state.productId !== productId) newConversation();
            state.restored = true;
            state.productId = productId;
            open().then(function () { send('Tư vấn giúp mình về sản phẩm này'); });
            return;
        }

        const opener = e.target.closest('[data-ai-open]');
        if (opener) {
            e.preventDefault();
            open().then(function () {
                const text = opener.getAttribute('data-ai-open');
                if (text) send(text);
            });
        }
    });
})(window, document);
