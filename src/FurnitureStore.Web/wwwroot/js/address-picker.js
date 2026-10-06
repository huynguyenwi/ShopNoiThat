/* Nhà Mộc Furniture - province → ward / commune pickers (Views/Shared/_AddressPicker.cshtml).
   Wards come from /api/locations (data built into the site); each province is fetched once per page.
   window.FSAddress.set(provinceName, wardName) fills the pickers, e.g. from a saved address. */
(function (window, document) {
    'use strict';

    const cache = new Map();

    function wardsOf(code) {
        if (!cache.has(code)) {
            const request = fetch('/api/locations/provinces/' + encodeURIComponent(code) + '/wards', { headers: { Accept: 'application/json' } })
                .then(function (response) { return response.json(); })
                .then(function (result) {
                    if (!result || !result.success) throw new Error('wards');
                    return result.data;
                });
            request.catch(function () { cache.delete(code); }); // retry on the next change
            cache.set(code, request);
        }
        return cache.get(code);
    }

    function parts(provinceSelect) {
        const ward = document.getElementById(provinceSelect.getAttribute('data-ward-target'));
        return { province: provinceSelect, ward: ward, hint: ward && ward.parentElement.querySelector('[data-ward-hint]') };
    }

    function render(ward, wards, selected) {
        ward.replaceChildren(new Option('— Chọn phường / xã —', ''));
        let group = null;
        wards.forEach(function (w) {
            if (!group || group.label !== w.type) {
                group = document.createElement('optgroup');
                group.label = w.type;
                ward.appendChild(group);
            }
            group.appendChild(new Option(w.label, w.name)); // textContent: names are never parsed as HTML
        });
        const match = selected ? wards.find(function (w) { return w.name.toLowerCase() === selected.trim().toLowerCase(); }) : null;
        ward.value = match ? match.name : '';
        return !!match;
    }

    async function load(provinceSelect, selectedWard) {
        const p = parts(provinceSelect);
        if (!p.ward) return;
        const option = provinceSelect.selectedOptions[0];
        const code = option ? option.getAttribute('data-code') : null;
        if (p.hint) p.hint.hidden = true;

        if (!code) {
            p.ward.replaceChildren(new Option('— Chọn tỉnh / thành trước —', ''));
            p.ward.disabled = true;
            return;
        }

        p.ward.disabled = true;
        p.ward.replaceChildren(new Option('Đang tải phường / xã...', ''));
        try {
            const wards = await wardsOf(code);
            const current = provinceSelect.selectedOptions[0];
            if (!current || current.getAttribute('data-code') !== code) return; // another province was chosen meanwhile
            const found = render(p.ward, wards, selectedWard);
            if (selectedWard && !found && p.hint) {
                p.hint.textContent = '“' + selectedWard + '” không có trong danh sách phường / xã của ' + provinceSelect.value + ' (danh mục mới từ 07/2025), vui lòng chọn lại.';
                p.hint.hidden = false;
            }
        } catch (e) {
            p.ward.replaceChildren(new Option('— Không tải được, chọn lại tỉnh / thành —', ''));
            if (window.FS) window.FS.toast('Không tải được danh sách phường / xã. Vui lòng thử lại.', 'error');
        } finally {
            p.ward.disabled = false;
        }
    }

    document.addEventListener('change', function (event) {
        const select = event.target.closest('[data-province-select]');
        if (select) load(select, '');
    });

    window.FSAddress = {
        set: function (province, ward) {
            const select = document.querySelector('[data-province-select]');
            if (!select) return Promise.resolve();
            select.value = province || '';
            return load(select, ward || '');
        }
    };
})(window, document);
