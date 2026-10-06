/* Nhà Mộc Furniture - shared chat helpers (customer widget + admin chat). Exposes window.FSChat. */
(function (window, document) {
    'use strict';

    const timeFormat = new Intl.DateTimeFormat('vi-VN', { hour: '2-digit', minute: '2-digit', timeZone: 'Asia/Ho_Chi_Minh' });
    const dateFormat = new Intl.DateTimeFormat('vi-VN', { day: '2-digit', month: '2-digit', year: 'numeric', timeZone: 'Asia/Ho_Chi_Minh' });

    /** Server dates are UTC; older serializers may omit the "Z". */
    function toDate(value) {
        if (!value) return new Date();
        const text = String(value);
        return new Date(/[zZ]|[+-]\d\d:\d\d$/.test(text) ? text : text + 'Z');
    }

    function formatTime(value) {
        const date = toDate(value);
        const today = dateFormat.format(new Date());
        const day = dateFormat.format(date);
        return day === today ? timeFormat.format(date) : timeFormat.format(date) + ' ' + day;
    }

    /** SignalR wraps HubException messages: "... HubException: <message>". Only that part is meant for users. */
    function hubError(error) {
        const text = error && error.message ? error.message : '';
        const marker = 'HubException: ';
        const index = text.indexOf(marker);
        return index >= 0 ? text.substring(index + marker.length) : 'Không gửi được tin nhắn. Vui lòng thử lại.';
    }

    /**
     * Builds a message element. Content is inserted with textContent (never innerHTML): chat text is untrusted.
     * @param {object} message ChatMessageDto
     * @param {boolean} mine true when the current viewer wrote it
     */
    function renderMessage(message, mine) {
        const row = document.createElement('div');
        const kind = message.senderType === 'System' ? 'system' : (mine ? 'from-me' : 'from-other');
        row.className = 'chat-msg ' + kind;
        row.setAttribute('data-message-id', String(message.id));

        const bubble = document.createElement('div');
        bubble.className = 'chat-bubble';
        bubble.textContent = message.content;
        row.appendChild(bubble);

        const meta = document.createElement('div');
        meta.className = 'chat-meta';
        const parts = [];
        if (!mine && message.senderType !== 'System') parts.push(message.senderName);
        parts.push(formatTime(message.sentAt));
        meta.textContent = parts.join(' · ');
        if (mine) {
            const seen = document.createElement('span');
            seen.className = 'chat-seen';
            seen.textContent = message.isRead ? ' · Đã xem' : '';
            meta.appendChild(seen);
        }
        row.appendChild(meta);
        return row;
    }

    function markSeen(container) {
        container.querySelectorAll('.chat-msg.from-me .chat-seen').forEach(function (el) { el.textContent = ' · Đã xem'; });
    }

    function scrollToBottom(container, force) {
        const nearBottom = container.scrollHeight - container.scrollTop - container.clientHeight < 120;
        if (force || nearBottom) container.scrollTop = container.scrollHeight;
    }

    /** Creates (but does not start) a hub connection with automatic reconnect. */
    function createConnection() {
        if (!window.signalR) return null;
        return new window.signalR.HubConnectionBuilder()
            .withUrl('/hubs/chat')
            .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
            .configureLogging(window.signalR.LogLevel.Warning)
            .build();
    }

    /** Grows a textarea with its content (up to its CSS max-height). */
    function autoGrow(textarea) {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, 140) + 'px';
    }

    /** Calls fn at most once per `ms`. */
    function throttle(fn, ms) {
        let last = 0;
        return function () {
            const now = Date.now();
            if (now - last >= ms) {
                last = now;
                fn.apply(null, arguments);
            }
        };
    }

    window.FSChat = {
        toDate: toDate,
        formatTime: formatTime,
        hubError: hubError,
        renderMessage: renderMessage,
        markSeen: markSeen,
        scrollToBottom: scrollToBottom,
        createConnection: createConnection,
        autoGrow: autoGrow,
        throttle: throttle
    };
})(window, document);
