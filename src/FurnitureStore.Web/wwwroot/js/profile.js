/* Account profile: preview the chosen avatar before uploading. */
(function (document) {
    'use strict';
    const input = document.getElementById('avatar');
    if (!input) return;
    input.addEventListener('change', function () {
        const file = input.files && input.files[0];
        if (!file || !file.type.startsWith('image/')) return;
        const preview = document.getElementById('avatarPreview');
        preview.src = URL.createObjectURL(file);
        preview.classList.remove('d-none');
        const placeholder = document.getElementById('avatarPlaceholder');
        if (placeholder) placeholder.classList.add('d-none');
    });
})(document);
