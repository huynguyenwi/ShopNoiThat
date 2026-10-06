/* Nhà Mộc Furniture - staff side of the chat. On every admin page: realtime badge + toast for new customer messages.
   On /admin/chat: conversation list and thread. Requires site.js (FS) and chat-core.js (FSChat). */
(function (window, document) {
    'use strict';

    if (!window.FS || !window.FSChat) return;
    const FS = window.FS;
    const C = window.FSChat;
    const badge = document.getElementById('adminChatBadge');
    const page = document.getElementById('adminChat');
    if (!badge && !page) return;

    const connection = C.createConnection();
    let refreshBadgeTimer = null;

    function refreshBadge() {
        clearTimeout(refreshBadgeTimer);
        refreshBadgeTimer = setTimeout(async function () {
            const result = await FS.api('/api/admin/chat/waiting-count');
            if (result.success && badge) {
                badge.textContent = String(result.data.count);
                badge.classList.toggle('d-none', result.data.count === 0);
            }
        }, 300);
    }

    const ui = page ? createPage() : null;

    if (connection) {
        connection.on('ReceiveMessage', function (message, conversation) {
            refreshBadge();
            if (ui) ui.onMessage(message, conversation);
            if (message.senderType === 'Customer' && (!ui || ui.selectedId() !== conversation.id || document.visibilityState !== 'visible')) {
                FS.toast('Tin nhắn mới từ ' + conversation.customerName + ': ' + message.content.substring(0, 80), 'info');
            }
        });
        connection.on('MessagesRead', function (conversationId, reader) {
            refreshBadge();
            if (ui) ui.onRead(conversationId, reader);
        });
        connection.on('Typing', function (conversationId, name, fromStaff) {
            if (ui && !fromStaff) ui.onTyping(conversationId, name);
        });
        C.keepConnected(connection, {
            onUp: function (recovered) {
                if (ui) ui.setLive('live');
                if (recovered) {
                    // Messages sent while the connection was down were not pushed: load them now.
                    refreshBadge();
                    if (ui) ui.reload();
                }
            },
            onDown: function () { if (ui) ui.setLive('offline'); }
        });
    } else if (ui) {
        ui.setLive('offline');
    }

    function hubReady() {
        return !!connection && connection.state === 'Connected';
    }

    // Without the realtime connection (server restarted, network down, WebSockets blocked) the badge and the chat page
    // still update, by polling until the connection is back.
    setInterval(function () {
        if (hubReady() || document.visibilityState !== 'visible') return;
        refreshBadge();
        if (ui) ui.poll();
    }, C.pollInterval);

    // ================================================================== /admin/chat page

    function createPage() {
        const isAdmin = page.getAttribute('data-is-admin') === 'true';
        const list = document.getElementById('chatConversationList');
        const listEmpty = document.getElementById('chatListEmpty');
        const listMore = document.getElementById('chatListMore');
        const search = document.getElementById('chatSearch');
        const empty = document.getElementById('chatEmpty');
        const inner = document.getElementById('chatThreadInner');
        const messages = document.getElementById('threadMessages');
        const older = document.getElementById('threadOlder');
        const typing = document.getElementById('threadTyping');
        const typingName = document.getElementById('threadTypingName');
        const compose = document.getElementById('threadCompose');
        const input = document.getElementById('threadInput');
        const sendButton = document.getElementById('threadSend');
        const toggleStatus = document.getElementById('threadToggleStatus');
        const live = document.getElementById('chatLive');
        const liveText = document.getElementById('chatLiveText');

        const state = {
            filter: 'all',
            search: '',
            page: 1,
            selected: Number(page.getAttribute('data-selected')) || null,
            conversation: null,
            oldestId: null,
            seen: new Set(),
            sending: false,
            typingTimer: null,
            searchTimer: null
        };

        function query(pageNumber) {
            const params = new URLSearchParams({ page: String(pageNumber), pageSize: '30' });
            if (state.search) params.set('search', state.search);
            if (state.filter === 'unread') params.set('unreadOnly', 'true');
            if (state.filter === 'open') params.set('status', 'Open');
            if (state.filter === 'closed') params.set('status', 'Closed');
            return '/api/admin/chat/conversations?' + params.toString();
        }

        function statusBadge(target, status) {
            target.textContent = status === 'Closed' ? 'Đã kết thúc' : 'Đang mở';
            target.className = 'badge ' + (status === 'Closed' ? 'text-bg-secondary' : 'text-bg-success');
        }

        function renderItem(c) {
            const item = document.createElement('button');
            item.type = 'button';
            item.className = 'admin-chat-item' + (c.id === state.selected ? ' active' : '') + (c.staffUnreadCount > 0 ? ' unread' : '');
            item.setAttribute('role', 'listitem');
            item.setAttribute('data-conversation-id', String(c.id));

            const top = document.createElement('div');
            top.className = 'd-flex align-items-center gap-2';
            const name = document.createElement('strong');
            name.className = 'text-truncate';
            name.textContent = c.customerName;
            const time = document.createElement('span');
            time.className = 'ms-auto small text-muted flex-shrink-0';
            time.textContent = C.formatTime(c.lastMessageAt);
            top.append(name, time);

            const bottom = document.createElement('div');
            bottom.className = 'd-flex align-items-center gap-2';
            const preview = document.createElement('span');
            preview.className = 'admin-chat-preview text-truncate';
            preview.textContent = c.lastMessagePreview || (c.productName ? 'Quan tâm: ' + c.productName : 'Chưa có tin nhắn');
            bottom.appendChild(preview);
            if (c.status === 'Closed') {
                const closed = document.createElement('span');
                closed.className = 'badge text-bg-light ms-auto';
                closed.textContent = 'Đã kết thúc';
                bottom.appendChild(closed);
            }
            if (c.staffUnreadCount > 0) {
                const count = document.createElement('span');
                count.className = 'badge rounded-pill text-bg-danger ms-auto';
                count.textContent = String(c.staffUnreadCount);
                bottom.appendChild(count);
            }

            item.append(top, bottom);
            return item;
        }

        /** @param {boolean} quiet background refresh: no error toast every 15 s while the server is unreachable */
        async function loadList(append, quiet) {
            const pageNumber = append ? state.page + 1 : 1;
            const result = await FS.api(query(pageNumber));
            if (!result.success) {
                if (!quiet) FS.toast(result.message || 'Không tải được danh sách.', 'error');
                return;
            }

            const data = result.data;
            state.page = data.page;
            if (!append) list.replaceChildren();
            data.items.forEach(function (c) { list.appendChild(renderItem(c)); });
            listEmpty.hidden = list.children.length > 0;
            listMore.hidden = !data.hasNext;
        }

        /** Moves / updates one conversation in the list after a realtime event (without reloading everything). */
        function upsertItem(conversation) {
            if (state.filter !== 'all' || state.search) {
                loadList(false);
                return;
            }
            const existing = list.querySelector('[data-conversation-id="' + conversation.id + '"]');
            if (existing) existing.remove();
            list.prepend(renderItem(conversation));
            listEmpty.hidden = true;
        }

        function appendMessage(message, prepend) {
            if (state.seen.has(message.id)) return;
            state.seen.add(message.id);
            const node = C.renderMessage(message, message.senderType === 'Staff');
            if (prepend) older.after(node); else messages.appendChild(node);
            if (state.oldestId === null || message.id < state.oldestId) state.oldestId = message.id;
        }

        function renderHeader(c) {
            document.getElementById('threadName').textContent = c.customerName;
            statusBadge(document.getElementById('threadStatus'), c.status);
            document.getElementById('threadGuest').hidden = !c.isGuest;
            toggleStatus.textContent = c.status === 'Closed' ? 'Mở lại' : 'Kết thúc';

            const contact = document.getElementById('threadContact');
            contact.replaceChildren();
            function link(href, text, icon) {
                const a = document.createElement('a');
                a.href = href;
                a.className = 'me-3';
                const i = document.createElement('i');
                i.className = 'bi ' + icon + ' me-1';
                i.setAttribute('aria-hidden', 'true');
                a.append(i, document.createTextNode(text));
                contact.appendChild(a);
            }
            if (c.customerPhone) link('tel:' + c.customerPhone.replace(/[^\d+]/g, ''), c.customerPhone, 'bi-telephone');
            if (c.customerEmail) link('mailto:' + c.customerEmail, c.customerEmail, 'bi-envelope');
            if (c.productSlug) link('/products/' + encodeURIComponent(c.productSlug), c.productName, 'bi-box-seam');
            if (isAdmin && c.customerId) link('/admin/customers/details/' + encodeURIComponent(c.customerId), 'Hồ sơ khách hàng', 'bi-person');
        }

        async function openThread(id) {
            const result = await FS.api('/api/admin/chat/conversations/' + id);
            if (!result.success) {
                FS.toast(result.message || 'Không mở được cuộc trò chuyện.', 'error');
                return;
            }

            const thread = result.data;
            state.selected = id;
            state.conversation = thread.conversation;
            state.seen.clear();
            state.oldestId = null;
            messages.querySelectorAll('.chat-msg').forEach(function (n) { n.remove(); });
            thread.messages.forEach(function (m) { appendMessage(m); });
            older.hidden = !thread.hasMore;
            renderHeader(thread.conversation);

            empty.hidden = true;
            inner.hidden = false;
            page.classList.add('show-thread');
            list.querySelectorAll('.admin-chat-item').forEach(function (n) {
                n.classList.toggle('active', Number(n.getAttribute('data-conversation-id')) === id);
            });
            const item = list.querySelector('[data-conversation-id="' + id + '"]');
            if (item) item.replaceWith(renderItem(thread.conversation));

            history.replaceState(null, '', '/admin/chat?id=' + id);
            C.scrollToBottom(messages, true);
            input.focus();
            refreshBadge();
        }

        /** Polling: new messages and read receipts of the open thread (opening it marks the customer's messages read). */
        async function refreshThread() {
            if (!state.selected || inner.hidden) return;
            const id = state.selected;
            const result = await FS.api('/api/admin/chat/conversations/' + id);
            if (!result.success || state.selected !== id) return;
            const before = state.seen.size;
            result.data.messages.forEach(function (m) { appendMessage(m); });
            state.conversation = result.data.conversation;
            renderHeader(result.data.conversation);
            const staffMessages = result.data.messages.filter(function (m) { return m.senderType === 'Staff'; });
            if (staffMessages.length && staffMessages[staffMessages.length - 1].isRead) C.markSeen(messages);
            if (state.seen.size > before) C.scrollToBottom(messages, false);
        }

        async function send() {
            const content = input.value.trim();
            if (!content || state.sending || !state.selected) return;
            state.sending = true;
            sendButton.disabled = true;
            try {
                let message;
                if (hubReady()) {
                    try {
                        message = await connection.invoke('SendStaffMessage', state.selected, content);
                    } catch (error) {
                        FS.toast(C.hubError(error), 'error');
                        return;
                    }
                } else {
                    const result = await FS.api('/api/admin/chat/conversations/' + state.selected + '/messages', { method: 'POST', body: { content: content } });
                    if (!result.success) {
                        FS.toast((result.errors && result.errors[0]) || result.message, 'error');
                        return;
                    }
                    message = result.data;
                }
                appendMessage(message);
                input.value = '';
                C.autoGrow(input);
                C.scrollToBottom(messages, true);
            } finally {
                state.sending = false;
                sendButton.disabled = false;
                input.focus();
            }
        }

        // ---------------------------------------------------------------- events

        list.addEventListener('click', function (e) {
            const item = e.target.closest('[data-conversation-id]');
            if (item) openThread(Number(item.getAttribute('data-conversation-id')));
        });
        listMore.addEventListener('click', function () { loadList(true); });
        document.querySelectorAll('[data-chat-filter]').forEach(function (button) {
            button.addEventListener('click', function () {
                state.filter = button.getAttribute('data-chat-filter');
                document.querySelectorAll('[data-chat-filter]').forEach(function (b) {
                    b.classList.toggle('active', b === button);
                    b.setAttribute('aria-pressed', b === button ? 'true' : 'false');
                });
                loadList(false);
            });
        });
        search.addEventListener('input', function () {
            clearTimeout(state.searchTimer);
            state.searchTimer = setTimeout(function () {
                state.search = search.value.trim();
                loadList(false);
            }, 350);
        });

        compose.addEventListener('submit', function (e) { e.preventDefault(); send(); });
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                send();
            }
        });
        const notifyTyping = C.throttle(function () {
            if (hubReady() && state.selected) connection.invoke('StaffTyping', state.selected).catch(function () { });
        }, 3000);
        input.addEventListener('input', function () { C.autoGrow(input); notifyTyping(); });

        older.addEventListener('click', async function () {
            if (!state.selected || state.oldestId === null) return;
            const before = messages.scrollHeight;
            const result = await FS.api('/api/admin/chat/conversations/' + state.selected + '/messages?before=' + state.oldestId);
            if (!result.success) return;
            for (let i = result.data.length - 1; i >= 0; i--) appendMessage(result.data[i], true);
            older.hidden = result.data.length < 30;
            messages.scrollTop = messages.scrollHeight - before;
        });

        toggleStatus.addEventListener('click', async function () {
            if (!state.conversation) return;
            const next = state.conversation.status === 'Closed' ? 'Open' : 'Closed';
            toggleStatus.disabled = true;
            const result = await FS.api('/api/admin/chat/conversations/' + state.selected + '/status', { method: 'POST', body: { status: next } });
            toggleStatus.disabled = false;
            if (!result.success) {
                FS.toast(result.message, 'error');
                return;
            }
            state.conversation = result.data;
            renderHeader(result.data);
            upsertItem(result.data);
            FS.toast(result.message, 'success');
        });

        document.getElementById('chatBack').addEventListener('click', function () {
            page.classList.remove('show-thread');
        });

        loadList(false).then(function () {
            if (state.selected) openThread(state.selected);
        });

        return {
            selectedId: function () { return state.selected; },
            reload: function () {
                loadList(false);
                if (state.selected) openThread(state.selected);
            },
            poll: function () {
                if (!state.search) loadList(false, true);
                refreshThread();
            },
            setLive: function (mode) {
                live.className = 'admin-chat-live is-' + mode;
                liveText.textContent = mode === 'live'
                    ? 'Đang nhận tin nhắn trực tiếp'
                    : 'Mất kết nối trực tiếp - tự cập nhật mỗi ' + (C.pollInterval / 1000) + ' giây';
            },
            onMessage: function (message, conversation) {
                const isOpenThread = conversation.id === state.selected && !inner.hidden;
                if (isOpenThread) {
                    typing.hidden = true;
                    appendMessage(message);
                    C.scrollToBottom(messages, message.senderType === 'Staff');
                    state.conversation = Object.assign({}, state.conversation, { status: conversation.status });
                    renderHeader(state.conversation);
                    if (message.senderType === 'Customer' && document.visibilityState === 'visible' && hubReady()) {
                        connection.invoke('MarkReadByStaff', conversation.id).catch(function () { });
                        conversation = Object.assign({}, conversation, { staffUnreadCount: 0 });
                    }
                }
                upsertItem(conversation);
                if (isOpenThread) {
                    const item = list.querySelector('[data-conversation-id="' + conversation.id + '"]');
                    if (item) item.classList.add('active');
                }
            },
            onRead: function (conversationId, reader) {
                if (reader === 'Customer' && conversationId === state.selected) C.markSeen(messages);
                if (reader === 'Staff') {
                    const item = list.querySelector('[data-conversation-id="' + conversationId + '"]');
                    if (item) {
                        item.classList.remove('unread');
                        const count = item.querySelector('.text-bg-danger');
                        if (count) count.remove();
                    }
                }
            },
            onTyping: function (conversationId, name) {
                if (conversationId !== state.selected) return;
                typingName.textContent = name + ' đang nhập...';
                typing.hidden = false;
                clearTimeout(state.typingTimer);
                state.typingTimer = setTimeout(function () { typing.hidden = true; }, 4000);
            }
        };
    }
})(window, document);
