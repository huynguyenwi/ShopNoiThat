/* Printable pages (QR labels, delivery slips): the toolbar button opens the browser's print dialog. */
(function (document) {
    'use strict';
    const button = document.querySelector('[data-print]');
    if (button) {
        button.addEventListener('click', function () { window.print(); });
    }
})(document);
