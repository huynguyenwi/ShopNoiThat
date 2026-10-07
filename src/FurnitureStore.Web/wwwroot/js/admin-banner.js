/* /admin/banner: the preview follows the form while typing; a chosen picture is checked and previewed before upload. */
(function (document) {
    'use strict';
    const form = document.querySelector('[data-banner-form]');
    const preview = document.querySelector('[data-banner-preview]');
    if (!form || !preview) return;

    const ALLOWED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
    const field = name => form.elements.namedItem(name);
    const value = name => (field(name) && field(name).value || '').trim();

    // Text elements: [data-preview=Name]; badges / buttons put the text in a [data-preview-text] child and hide when empty.
    const HIDE_WHEN_EMPTY = ['Eyebrow', 'TitleHighlight', 'PrimaryButtonText', 'SecondaryButtonText'];
    function updateText(name) {
        const target = preview.querySelector('[data-preview="' + name + '"]');
        if (!target) return;
        const text = value(name);
        (target.querySelector('[data-preview-text]') || target).textContent = text;
        if (HIDE_WHEN_EMPTY.indexOf(name) !== -1) target.hidden = !text;
    }

    function updateStat(number) {
        const target = preview.querySelector('[data-preview-stat="' + number + '"]');
        if (!target) return;
        const statValue = value('Stat' + number + 'Value');
        const statLabel = value('Stat' + number + 'Label');
        target.querySelector('.stat-value').textContent = statValue;
        target.querySelector('.stat-label').textContent = statLabel;
        target.hidden = !statValue || !statLabel;
    }

    form.addEventListener('input', function (event) {
        const name = event.target.name || '';
        const stat = /^Stat([1-3])(Value|Label)$/.exec(name);
        if (stat) {
            updateStat(stat[1]);
        } else {
            updateText(name);
        }
    });

    // ---- picture
    const image = preview.querySelector('[data-preview-image]');
    const input = document.getElementById('bannerImage');
    const error = document.getElementById('bannerImageError');
    const remove = form.querySelector('[data-remove-image]');
    const maxBytes = Number(input && input.getAttribute('data-max-mb') || 50) * 1024 * 1024;
    let chosenUrl = null;

    function showImage() {
        if (chosenUrl) {
            image.src = chosenUrl;
        } else if (remove && remove.checked) {
            image.src = image.getAttribute('data-default-src');
        } else {
            image.src = image.getAttribute('data-saved-src');
        }
    }

    if (input) {
        input.addEventListener('change', function () {
            const file = input.files && input.files[0];
            error.hidden = true;
            if (chosenUrl) URL.revokeObjectURL(chosenUrl);
            chosenUrl = null;
            if (file) {
                let message = null;
                if (ALLOWED_TYPES.indexOf(file.type) === -1) {
                    message = 'Ảnh "' + file.name + '": chỉ chấp nhận JPG, PNG hoặc WEBP.';
                } else if (file.size > maxBytes) {
                    message = 'Ảnh "' + file.name + '" nặng ' + (file.size / 1048576).toFixed(1) + ' MB, vượt mức tối đa ' + (maxBytes / 1048576) + ' MB.';
                }

                if (message) {
                    input.value = '';
                    error.textContent = message;
                    error.hidden = false;
                } else {
                    chosenUrl = URL.createObjectURL(file);
                    if (remove) remove.checked = false;
                }
            }
            showImage();
        });
    }

    if (remove) {
        remove.addEventListener('change', function () {
            if (remove.checked && input) {
                input.value = '';
                if (chosenUrl) URL.revokeObjectURL(chosenUrl);
                chosenUrl = null;
            }
            showImage();
        });
    }
})(document);
