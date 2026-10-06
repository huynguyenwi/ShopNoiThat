using FurnitureStore.Application.Sales;

namespace FurnitureStore.Web.ViewModels.Sales;

public sealed class CheckoutViewModel
{
    public required CheckoutCommand Command { get; init; }
    public required CartDto Cart { get; init; }
    public required IReadOnlyList<CustomerAddressDto> SavedAddresses { get; init; }
    public required IReadOnlyList<CouponOfferDto> Offers { get; init; }
    public IReadOnlyList<string> Provinces => VietnamProvinces.All;
    public CheckoutSummaryModel Summary => new(Cart, Offers);
}

/// <summary>Right-hand column of /checkout; also served alone by GET /checkout/summary after a coupon change.</summary>
public sealed record CheckoutSummaryModel(CartDto Cart, IReadOnlyList<CouponOfferDto> Offers);

/// <summary>Coupon entry, applied coupon and public offers, in the cart and at checkout.</summary>
/// <param name="ReturnUrl">"/cart" or "/checkout": where the plain (no JavaScript) form posts come back to.</param>
public sealed record CouponBoxModel(CartDto Cart, IReadOnlyList<CouponOfferDto> Offers, string ReturnUrl);

public sealed class AddressPageViewModel
{
    public required IReadOnlyList<CustomerAddressDto> Addresses { get; init; }
    public required CustomerAddressCommand Command { get; init; }
    public int? EditingId { get; init; }
    public IReadOnlyList<string> Provinces => VietnamProvinces.All;
}
