/* /admin/store: live preview of the logo (picture, name, small line) while the form is filled in. */
(function (document) {
    'use strict';
    const nameInput = document.querySelector('[data-logo-source="name"]');
    const subInput = document.querySelector('[data-logo-source="subtitle"]');
    const nameOut = document.querySelector('[data-logo-name]');
    const subOut = document.querySelector('[data-logo-sub]');
    const textOut = document.querySelector('[data-logo-text]');
    const image = document.querySelector('[data-logo-image]');
    const fileInput = document.getElementById('storeLogo');
    const fileError = document.getElementById('storeLogoError');
    const showsName = document.querySelector('[data-logo-shows-name]');
    const remove = document.querySelector('[data-remove-logo]');
    if (!nameInput || !subInput || !nameOut || !subOut) return;

    const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
    const maxBytes = Number(fileInput && fileInput.getAttribute('data-max-mb') || 50) * 1024 * 1024;
    let chosenUrl = null;

    // Same rule as StoreInfoDto.BrandNameOf: a name ending with the small line drops it ("Nhà Mộc Furniture" → "Nhà Mộc").
    function brandName(name, subtitle) {
        name = name.trim();
        if (!subtitle || !name.toLowerCase().endsWith(' ' + subtitle.toLowerCase())) return name;
        const shortName = name.slice(0, name.length - subtitle.length - 1).trim();
        return shortName || name;
    }

    function hasPicture() {
        if (chosenUrl) return true;
        return !!image && image.getAttribute('data-saved-custom') === 'true' && !(remove && remove.checked);
    }

    function update() {
        const subtitle = subInput.value.trim();
        nameOut.textContent = brandName(nameInput.value, subtitle) || 'Tên cửa hàng';
        subOut.textContent = subtitle;
        subOut.hidden = !subtitle;
        if (image) {
            const custom = hasPicture();
            image.src = chosenUrl || (custom ? image.getAttribute('data-saved-src') : image.getAttribute('data-default-src'));
            image.classList.toggle('brand-logo-custom', custom);
            // The house icon never contains the name.
            if (textOut) textOut.hidden = custom && !!showsName && showsName.checked;
        }
    }

    if (fileInput) {
        fileInput.addEventListener('change', function () {
            const file = fileInput.files && fileInput.files[0];
            fileError.hidden = true;
            if (chosenUrl) URL.revokeObjectURL(chosenUrl);
            chosenUrl = null;
            if (file) {
                let message = null;
                if (ALLOWED_TYPES.indexOf(file.type) === -1) {
                    message = 'Ảnh "' + file.name + '": chỉ chấp nhận PNG, JPG hoặc WEBP.';
                } else if (file.size > maxBytes) {
                    message = 'Ảnh "' + file.name + '" nặng ' + (file.size / 1048576).toFixed(1) + ' MB, vượt mức tối đa ' + (maxBytes / 1048576) + ' MB.';
                }

                if (message) {
                    fileInput.value = '';
                    fileError.textContent = message;
                    fileError.hidden = false;
                } else {
                    chosenUrl = URL.createObjectURL(file);
                    if (remove) remove.checked = false;
                }
            }
            update();
        });
    }

    if (remove) {
        remove.addEventListener('change', function () {
            if (remove.checked && fileInput) {
                fileInput.value = '';
                if (chosenUrl) URL.revokeObjectURL(chosenUrl);
                chosenUrl = null;
            }
            update();
        });
    }

    nameInput.addEventListener('input', update);
    subInput.addEventListener('input', update);
    if (showsName) showsName.addEventListener('change', update);
    update();
})(document);
