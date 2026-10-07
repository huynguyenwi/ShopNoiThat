/* Account profile: preview the chosen avatar before uploading. */
(function (document) {
    'use strict';
    const input = document.getElementById('avatar');
    if (!input) return;
    const maxBytes = Number(input.getAttribute('data-max-mb') || 50) * 1024 * 1024;
    const error = document.getElementById('avatarError');

    input.addEventListener('change', function () {
        const file = input.files && input.files[0];
        if (error) error.hidden = true;
        if (!file || !file.type.startsWith('image/')) return;
        // Said here rather than after uploading tens of megabytes: the server refuses it anyway.
        if (file.size > maxBytes) {
            input.value = '';
            if (error) {
                error.textContent = 'Ảnh "' + file.name + '" nặng ' + (file.size / 1048576).toFixed(1) + ' MB, vượt mức tối đa ' + (maxBytes / 1048576) + ' MB. Vui lòng chọn ảnh khác.';
                error.hidden = false;
            }
            return;
        }
        const preview = document.getElementById('avatarPreview');
        preview.src = URL.createObjectURL(file);
        preview.classList.remove('d-none');
        const placeholder = document.getElementById('avatarPlaceholder');
        if (placeholder) placeholder.classList.add('d-none');
    });
})(document);
