/* /admin/store: live preview of the logo text while the name / small line are typed. */
(function (document) {
    'use strict';
    const nameInput = document.querySelector('[data-logo-source="name"]');
    const subInput = document.querySelector('[data-logo-source="subtitle"]');
    const nameOut = document.querySelector('[data-logo-name]');
    const subOut = document.querySelector('[data-logo-sub]');
    if (!nameInput || !subInput || !nameOut || !subOut) return;

    // Same rule as StoreInfoDto.BrandNameOf: a name ending with the small line drops it ("Nhà Mộc Furniture" → "Nhà Mộc").
    function brandName(name, subtitle) {
        name = name.trim();
        if (!subtitle || !name.toLowerCase().endsWith(' ' + subtitle.toLowerCase())) return name;
        const shortName = name.slice(0, name.length - subtitle.length - 1).trim();
        return shortName || name;
    }

    function update() {
        const subtitle = subInput.value.trim();
        nameOut.textContent = brandName(nameInput.value, subtitle) || 'Tên cửa hàng';
        subOut.textContent = subtitle;
        subOut.hidden = !subtitle;
    }

    nameInput.addEventListener('input', update);
    subInput.addEventListener('input', update);
    update();
})(document);
