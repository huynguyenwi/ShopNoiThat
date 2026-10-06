using FluentValidation;
using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Application.Common.Interfaces;
using FurnitureStore.Application.Common.Validation;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.Sales;

public interface IAddressService
{
    Task<IReadOnlyList<CustomerAddressDto>> ListAsync(string userId, CancellationToken cancellationToken = default);
    Task<CustomerAddressDto> GetAsync(string userId, int id, CancellationToken cancellationToken = default);
    Task<int> CreateAsync(string userId, CustomerAddressCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(string userId, int id, CustomerAddressCommand command, CancellationToken cancellationToken = default);
    Task DeleteAsync(string userId, int id, CancellationToken cancellationToken = default);
    Task SetDefaultAsync(string userId, int id, CancellationToken cancellationToken = default);
}

public sealed class AddressService(
    IRepository<CustomerAddress> addresses,
    IValidator<CustomerAddressCommand> validator,
    IUnitOfWork unitOfWork) : IAddressService
{
    public const int MaxAddressesPerUser = 10;

    public async Task<IReadOnlyList<CustomerAddressDto>> ListAsync(string userId, CancellationToken cancellationToken = default) =>
        (await addresses.ListAsync(a => a.UserId == userId, cancellationToken))
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(ToDto).ToList();

    public async Task<CustomerAddressDto> GetAsync(string userId, int id, CancellationToken cancellationToken = default) =>
        ToDto(await GetOwnedAsync(userId, id, cancellationToken));

    public async Task<int> CreateAsync(string userId, CustomerAddressCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var existing = await addresses.ListAsync(a => a.UserId == userId, cancellationToken);
        if (existing.Count >= MaxAddressesPerUser)
        {
            throw new BusinessRuleException($"Mỗi tài khoản lưu tối đa {MaxAddressesPerUser} địa chỉ.");
        }

        var address = new CustomerAddress { UserId = userId };
        Apply(address, command);
        address.IsDefault = command.IsDefault || existing.Count == 0;
        if (address.IsDefault)
        {
            foreach (var other in existing) other.IsDefault = false;
        }

        await addresses.AddAsync(address, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return address.Id;
    }

    public async Task UpdateAsync(string userId, int id, CustomerAddressCommand command, CancellationToken cancellationToken = default)
    {
        await validator.EnsureValidAsync(command, cancellationToken);
        var address = await GetOwnedAsync(userId, id, cancellationToken);
        Apply(address, command);
        if (command.IsDefault && !address.IsDefault)
        {
            await MakeDefaultAsync(userId, address, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var address = await GetOwnedAsync(userId, id, cancellationToken);
        addresses.Remove(address);

        if (address.IsDefault)
        {
            var next = (await addresses.ListAsync(a => a.UserId == userId && a.Id != id, cancellationToken)).OrderByDescending(a => a.CreatedAt).FirstOrDefault();
            if (next is not null) next.IsDefault = true;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDefaultAsync(string userId, int id, CancellationToken cancellationToken = default)
    {
        var address = await GetOwnedAsync(userId, id, cancellationToken);
        await MakeDefaultAsync(userId, address, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task MakeDefaultAsync(string userId, CustomerAddress address, CancellationToken cancellationToken)
    {
        foreach (var other in await addresses.ListAsync(a => a.UserId == userId, cancellationToken))
        {
            other.IsDefault = other.Id == address.Id;
        }
        address.IsDefault = true;
    }

    private async Task<CustomerAddress> GetOwnedAsync(string userId, int id, CancellationToken cancellationToken)
    {
        var address = await addresses.GetByIdAsync(id, cancellationToken);
        // Another user's address is reported as not found.
        return address is null || address.UserId != userId ? throw new NotFoundException("địa chỉ", id) : address;
    }

    private static void Apply(CustomerAddress address, CustomerAddressCommand c)
    {
        address.Label = string.IsNullOrWhiteSpace(c.Label) ? null : c.Label.Trim();
        address.RecipientName = c.RecipientName.Trim();
        address.Phone = c.Phone.Trim();
        address.AddressLine = c.AddressLine.Trim();
        address.Ward = c.Ward.Trim();
        address.District = null; // no districts since 07/2025; an address saved earlier loses it when edited
        address.Province = c.Province.Trim();
    }

    private static CustomerAddressDto ToDto(CustomerAddress a) =>
        new(a.Id, a.Label, a.RecipientName, a.Phone, a.AddressLine, a.Ward, a.District, a.Province, a.IsDefault);
}

public interface IWishlistService
{
    Task<IReadOnlyList<ProductCardDto>> GetAsync(string userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetProductIdsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>Adds the product when missing, removes it otherwise. Returns true when the product is now in the wishlist.</summary>
    Task<bool> ToggleAsync(string userId, int productId, CancellationToken cancellationToken = default);

    Task RemoveAsync(string userId, int productId, CancellationToken cancellationToken = default);
}

public sealed class WishlistService(
    IWishlistRepository wishlists,
    IProductRepository products,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IWishlistService
{
    public const int MaxItems = 100;

    public async Task<IReadOnlyList<ProductCardDto>> GetAsync(string userId, CancellationToken cancellationToken = default)
    {
        var ids = await wishlists.GetProductIdsAsync(userId, cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var result = await products.SearchAsync(new ProductQuery { ProductIds = ids, PageSize = ProductQuery.MaxPageSize }.Normalize(), null,
            timeProvider.GetUtcNow().UtcDateTime - CatalogService.NewProductPeriod, cancellationToken);
        return result.Items;
    }

    public Task<IReadOnlyList<int>> GetProductIdsAsync(string userId, CancellationToken cancellationToken = default) =>
        wishlists.GetProductIdsAsync(userId, cancellationToken);

    public async Task<bool> ToggleAsync(string userId, int productId, CancellationToken cancellationToken = default)
    {
        var product = await products.GetByIdAsync(productId, cancellationToken);
        if (product is null || product.Status != ProductStatus.Active)
        {
            throw new NotFoundException("sản phẩm", productId);
        }

        var wishlist = await wishlists.GetByUserAsync(userId, cancellationToken);
        if (wishlist is null)
        {
            wishlist = new Wishlist { UserId = userId };
            await wishlists.AddAsync(wishlist, cancellationToken);
        }

        var existing = wishlist.Items.FirstOrDefault(i => i.ProductId == productId);
        bool added;
        if (existing is not null)
        {
            wishlist.Items.Remove(existing);
            added = false;
        }
        else
        {
            if (wishlist.Items.Count >= MaxItems)
            {
                throw new BusinessRuleException($"Danh sách yêu thích tối đa {MaxItems} sản phẩm.");
            }

            wishlist.Add(productId, timeProvider.GetUtcNow().UtcDateTime);
            added = true;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return added;
    }

    public async Task RemoveAsync(string userId, int productId, CancellationToken cancellationToken = default)
    {
        var wishlist = await wishlists.GetByUserAsync(userId, cancellationToken);
        var item = wishlist?.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item is not null)
        {
            wishlist!.Items.Remove(item);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
