using FluentValidation;
using FurnitureStore.Application.Common.Validation;

namespace FurnitureStore.Application.Sales;

public sealed class CheckoutCommandValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().WithMessage("Vui lòng nhập họ tên người nhận.").MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Vui lòng nhập số điện thoại.")
            .Matches(ValidationPatterns.VietnamesePhone).WithMessage(ValidationPatterns.VietnamesePhoneMessage);
        RuleFor(x => x.Email).NotEmpty().WithMessage("Vui lòng nhập email.").EmailAddress().WithMessage("Email không hợp lệ.").MaximumLength(256);
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Vui lòng nhập số nhà, tên đường.").MaximumLength(300);
        RuleFor(x => x.Ward).NotEmpty().WithMessage("Vui lòng nhập phường / xã.").MaximumLength(100);
        RuleFor(x => x.District).MaximumLength(100);
        RuleFor(x => x.Province).Must(VietnamProvinces.IsValid).WithMessage("Vui lòng chọn tỉnh / thành phố.");
        RuleFor(x => x.Note).MaximumLength(1000).WithMessage("Ghi chú tối đa 1.000 ký tự.");
        RuleFor(x => x.PaymentMethod).IsInEnum().WithMessage("Phương thức thanh toán không hợp lệ.");
    }
}

public sealed class CustomerAddressCommandValidator : AbstractValidator<CustomerAddressCommand>
{
    public CustomerAddressCommandValidator()
    {
        RuleFor(x => x.Label).MaximumLength(50);
        RuleFor(x => x.RecipientName).NotEmpty().WithMessage("Vui lòng nhập tên người nhận.").MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().WithMessage("Vui lòng nhập số điện thoại.")
            .Matches(ValidationPatterns.VietnamesePhone).WithMessage(ValidationPatterns.VietnamesePhoneMessage);
        RuleFor(x => x.AddressLine).NotEmpty().WithMessage("Vui lòng nhập số nhà, tên đường.").MaximumLength(300);
        RuleFor(x => x.Ward).NotEmpty().WithMessage("Vui lòng nhập phường / xã.").MaximumLength(100);
        RuleFor(x => x.District).MaximumLength(100);
        RuleFor(x => x.Province).Must(VietnamProvinces.IsValid).WithMessage("Vui lòng chọn tỉnh / thành phố.");
    }
}
