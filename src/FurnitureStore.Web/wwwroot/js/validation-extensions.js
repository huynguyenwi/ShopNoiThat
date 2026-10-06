/* Extra client-side validation rules for jQuery Validate (loaded after jquery.validate.unobtrusive). */
(function ($) {
    'use strict';
    if (!$ || !$.validator || !$.validator.unobtrusive) return;
    // [MustBeTrue] checkboxes, e.g. "Tôi đồng ý với điều khoản": valid only when ticked.
    $.validator.addMethod('mustbetrue', function (value, element) { return element.checked; });
    $.validator.unobtrusive.adapters.addBool('mustbetrue');
})(window.jQuery);
