/* Account profile: a chosen avatar is previewed and uploaded right away (no second click on "Tải lên"). */
(function (window, document) {
    'use strict';
    const input = document.getElementById('avatar');
    if (!input) return;
    const form = input.form;
    const maxBytes = Number(input.getAttribute('data-max-mb') || 50) * 1024 * 1024;
    const error = document.getElementById('avatarError');
    const status = document.getElementById('avatarStatus');
    const submit = document.getElementById('avatarSubmit');
    let sending = false;

    // The button is only needed without JavaScript.
    if (submit) submit.hidden = true;

    function showError(message) {
        input.value = '';
        if (error) {
            error.textContent = message;
            error.hidden = false;
        }
    }

    function setSending(value) {
        sending = value;
        if (status) status.hidden = !value;
        form.setAttribute('aria-busy', value ? 'true' : 'false');
    }

    input.addEventListener('change', function () {
        const file = input.files && input.files[0];
        if (error) error.hidden = true;
        if (!file || sending) return;
        // Said here rather than after uploading tens of megabytes: the server refuses it anyway.
        if (file.size > maxBytes) {
            showError('Ảnh "' + file.name + '" nặng ' + (file.size / 1048576).toFixed(1) + ' MB, vượt mức tối đa ' + (maxBytes / 1048576) + ' MB. Vui lòng chọn ảnh khác.');
            return;
        }

        if (file.type.startsWith('image/')) {
            const preview = document.getElementById('avatarPreview');
            preview.src = URL.createObjectURL(file);
            preview.classList.remove('d-none');
            const placeholder = document.getElementById('avatarPlaceholder');
            if (placeholder) placeholder.classList.add('d-none');
        }

        // Other file types are sent too: the server checks the content and explains what is accepted.
        setSending(true);
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit();
        } else {
            form.submit();
        }
    });

    // Coming back with the browser's Back button restores this page as it was left, mid-upload.
    window.addEventListener('pageshow', function (event) {
        if (event.persisted) setSending(false);
    });
})(window, document);
