/* Nhà Mộc Furniture - customer chat widget (SignalR with REST fallback). Requires site.js (FS) and chat-core.js (FSChat). */
(function (window, document) {
    'use strict';

    const root = document.getElementById('chatWidget');
    if (!root || !window.FS || !window.FSChat) return;

    const FS = window.FS;
    const C = window.FSChat;
    const el = function (id) { return document.getElementById(id); };
    const launcher = el('chatLauncher');
    const panel = el('chatPanel');
    const loading = el('chatLoading');
    const startForm = el('chatStartForm');
    const startError = el('chatStartError');
    const list = el('chatMessages');
    const olderButton = el('chatOlder');
    const typing = el('chatTyping');
    const typingName = el('chatTypingName');
    const compose = el('chatCompose');
    const input = el('chatInput');
    const sendButton = el('chatSend');
    const unreadBadge = el('chatUnread');
    const statusText = el('chatStatus');

    const state = {
        loaded: false,
        conversationId: null,
        oldestId: null,
        productId: null,
        unread: 0,
        sending: false,
        connection: null,
        seen: new Set(),
        typingTimer: null
    };

    // Keep the launcher above the product page's sticky "buy" bar on phones (its height depends on the text).
    const buyBar = document.querySelector('.mobile-buy-bar');
    if (buyBar) {
        document.body.classList.add('has-buy-bar');
        const placeAboveBuyBar = function () { document.documentElement.style.setProperty('--buy-bar-height', buyBar.offsetHeight + 'px'); };
        placeAboveBuyBar();
        window.addEventListener('resize', placeAboveBuyBar);
    }

    function isOpen() { return !panel.hidden; }

    function setUnread(count) {
        state.unread = Math.max(0, count);
        unreadBadge.textContent = state.unread > 9 ? '9+' : String(state.unread);
        unreadBadge.classList.toggle('d-none', state.unread === 0);
    }

    function show(section) {
        loading.hidden = section !== 'loading';
        startForm.hidden = section !== 'start';
        list.hidden = section !== 'chat';
        compose.hidden = section !== 'chat';
        if (section !== 'chat') typing.hidden = true;
    }

    function append(message, prepend) {
        if (state.seen.has(message.id)) return;
        state.seen.add(message.id);
        const node = C.renderMessage(message, message.senderType === 'Customer');
        if (prepend) {
            olderButton.after(node);
        } else {
            list.appendChild(node);
        }
        if (state.oldestId === null || message.id < state.oldestId) state.oldestId = message.id;
    }

    function resetMessages() {
        list.querySelectorAll('.chat-msg[data-message-id]').forEach(function (n) { n.remove(); });
        state.seen.clear();
        state.oldestId = null;
    }

    async function load() {
        show('loading');
        const result = await FS.api('/api/chat');
        if (!result.success) {
            show('chat');
            FS.toast(result.message || 'Không tải được cuộc trò chuyện.', 'error');
            return;
        }

        const data = result.data;
        state.loaded = true;
        state.conversationId = data.conversation ? data.conversation.id : null;
        if (data.requiresContactInfo) {
            show('start');
            el('chatName').focus();
            return;
        }

        resetMessages();
        (data.messages || []).forEach(function (m) { append(m); });
        olderButton.hidden = !data.hasMore;
        show('chat');
        setUnread(0);
        C.scrollToBottom(list, true);
        input.focus();
        await connect();
    }

    async function connect() {
        if (state.connection) return;
        const connection = C.createConnection();
        if (!connection) return; // SignalR script missing: REST still works
        state.connection = connection;

        connection.on('ReceiveMessage', function (message, conversation) {
            state.conversationId = conversation.id;
            if (message.senderType === 'Customer') {
                append(message); // sent from another tab
            } else {
                typing.hidden = true;
                if (isOpen() && state.loaded && document.visibilityState === 'visible') {
                    append(message);
                    connection.invoke('MarkRead').catch(function () { });
                } else {
                    if (state.loaded) append(message);
                    setUnread(state.unread + 1);
                }
            }
            C.scrollToBottom(list, message.senderType === 'Customer');
        });

        connection.on('MessagesRead', function (conversationId, reader) {
            if (reader === 'Staff') C.markSeen(list);
        });

        connection.on('Typing', function (conversationId, name, fromStaff) {
            if (!fromStaff || !isOpen()) return;
            typingName.textContent = name + ' đang nhập...';
            typing.hidden = false;
            clearTimeout(state.typingTimer);
            state.typingTimer = setTimeout(function () { typing.hidden = true; }, 4000);
        });

        connection.onreconnecting(function () { statusText.textContent = 'Đang kết nối lại...'; });
        connection.onreconnected(async function () {
            statusText.textContent = 'Thường trả lời trong vài phút';
            if (state.loaded && isOpen()) await refresh(); // pick up messages missed while offline
        });
        connection.onclose(function () {
            statusText.textContent = 'Mất kết nối realtime - tin nhắn vẫn được gửi';
            state.connection = null;
        });

        try {
            await connection.start();
            statusText.textContent = 'Thường trả lời trong vài phút';
        } catch (e) {
            state.connection = null;
            statusText.textContent = 'Không kết nối realtime được - tin nhắn vẫn được gửi';
        }
    }

    async function refresh() {
        const result = await FS.api('/api/chat');
        if (result.success && result.data) {
            (result.data.messages || []).forEach(function (m) { append(m); });
            C.scrollToBottom(list, false);
        }
    }

    async function reconnect() {
        if (state.connection) {
            const old = state.connection;
            state.connection = null;
            try { await old.stop(); } catch (e) { /* ignore */ }
        }
        await connect();
    }

    async function send() {
        const content = input.value.trim();
        if (!content || state.sending) return;
        state.sending = true;
        sendButton.disabled = true;

        try {
            let message = null;
            if (state.connection && state.connection.state === 'Connected') {
                try {
                    message = await state.connection.invoke('SendMessage', content, state.productId);
                } catch (error) {
                    FS.toast(C.hubError(error), 'error');
                    return;
                }
            } else {
                const result = await FS.api('/api/chat/messages', { method: 'POST', body: { content: content, productId: state.productId } });
                if (!result.success) {
                    FS.toast((result.errors && result.errors[0]) || result.message, 'error');
                    return;
                }
                message = result.data;
            }

            append(message);
            state.productId = null;
            input.value = '';
            C.autoGrow(input);
            C.scrollToBottom(list, true);
            if (!state.connection) await connect(); // signed-in customer's first message created the conversation
        } finally {
            state.sending = false;
            sendButton.disabled = false;
            input.focus();
        }
    }

    function open() {
        document.dispatchEvent(new CustomEvent('fs:panel-open', { detail: 'chat' }));
        document.body.setAttribute('data-open-panel', 'chat');
        panel.hidden = false;
        launcher.setAttribute('aria-expanded', 'true');
        root.classList.add('is-open');
        if (!state.loaded) {
            load();
        } else {
            if (state.unread > 0 && state.connection) state.connection.invoke('MarkRead').catch(function () { });
            setUnread(0);
            C.scrollToBottom(list, true);
            if (!compose.hidden) input.focus();
        }
    }

    function close(keepFocus) {
        panel.hidden = true;
        launcher.setAttribute('aria-expanded', 'false');
        root.classList.remove('is-open');
        if (document.body.getAttribute('data-open-panel') === 'chat') document.body.removeAttribute('data-open-panel');
        if (!keepFocus) launcher.focus();
    }

    // Only one floating panel at a time (shop chat / AI assistant).
    document.addEventListener('fs:panel-open', function (e) { if (e.detail !== 'chat' && isOpen()) close(true); });

    // ------------------------------------------------------------------ events

    launcher.addEventListener('click', function () { if (isOpen()) close(); else open(); });
    el('chatClose').addEventListener('click', function () { close(); });
    panel.addEventListener('keydown', function (e) { if (e.key === 'Escape') close(); });

    startForm.addEventListener('submit', async function (e) {
        e.preventDefault();
        startError.classList.add('d-none');
        const body = {
            name: el('chatName').value.trim(),
            phone: el('chatPhone').value.trim() || null,
            email: el('chatEmail').value.trim() || null,
            productId: state.productId
        };
        const button = startForm.querySelector('button[type="submit"]');
        button.disabled = true;
        const result = await FS.api('/api/chat/start', { method: 'POST', body: body });
        button.disabled = false;
        if (!result.success) {
            startError.textContent = (result.errors && result.errors.length ? result.errors.join(' ') : result.message) || 'Vui lòng kiểm tra lại thông tin.';
            startError.classList.remove('d-none');
            return;
        }

        state.conversationId = result.data.id;
        root.setAttribute('data-has-chat', 'true');
        show('chat');
        input.focus();
        await reconnect(); // the new chat cookie must be sent with the hub connection
    });

    compose.addEventListener('submit', function (e) { e.preventDefault(); send(); });
    input.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            send();
        }
    });
    const notifyTyping = C.throttle(function () {
        if (state.connection && state.connection.state === 'Connected' && state.conversationId) {
            state.connection.invoke('Typing').catch(function () { });
        }
    }, 3000);
    input.addEventListener('input', function () { C.autoGrow(input); notifyTyping(); });

    olderButton.addEventListener('click', async function () {
        if (state.oldestId === null) return;
        const before = list.scrollHeight;
        const result = await FS.api('/api/chat/messages?before=' + encodeURIComponent(state.oldestId));
        if (!result.success) return;
        const older = result.data || [];
        for (let i = older.length - 1; i >= 0; i--) append(older[i], true);
        olderButton.hidden = older.length < 30;
        list.scrollTop = list.scrollHeight - before;
    });

    document.addEventListener('visibilitychange', function () {
        if (document.visibilityState === 'visible' && isOpen() && state.unread > 0 && state.connection) {
            state.connection.invoke('MarkRead').catch(function () { });
            setUnread(0);
        }
    });

    // "Hỏi về sản phẩm này" buttons on product pages.
    document.addEventListener('click', function (e) {
        const trigger = e.target.closest('[data-chat-product]');
        if (!trigger) return;
        e.preventDefault();
        state.productId = Number(trigger.getAttribute('data-chat-product')) || null;
        const name = trigger.getAttribute('data-chat-product-name');
        open();
        if (name && !input.value) {
            input.value = 'Tôi muốn được tư vấn về sản phẩm "' + name + '".';
            C.autoGrow(input);
        }
    });

    // Returning visitors: show unread replies on the launcher and receive new ones in realtime.
    if (root.getAttribute('data-has-chat') === 'true') {
        FS.api('/api/chat/unread').then(function (result) {
            if (result.success && result.data && result.data.hasConversation) {
                setUnread(result.data.count);
                connect();
            }
        });
    }

    if (new URLSearchParams(window.location.search).get('chat') === 'open') open();
})(window, document);
