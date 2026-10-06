using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Catalog;

public sealed class ProductAdminServiceTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Admin<T>(Func<IProductAdminService, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<IProductAdminService>()));

    private Task<T> Db<T>(Func<ApplicationDbContext, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<ApplicationDbContext>()));

    private async Task<ProductUpsertCommand> NewCommandAsync(string sku = "BA-TEST-01")
    {
        var lookups = await Admin(s => s.GetLookupsAsync());
        var category = lookups.Categories.First(c => c.Name == "Bàn ăn");
        var walnut = lookups.Materials.First(m => m.Slug == "go-oc-cho");
        var brown = lookups.Colors.First(c => c.Slug == "nau-oc-cho");
        var black = lookups.Colors.First(c => c.Slug == "den");
        var size160 = lookups.Sizes.First(s => s.Slug == "ban-an-160");
        var size180 = lookups.Sizes.First(s => s.Slug == "ban-an-180");

        return new ProductUpsertCommand
        {
            Name = "Bàn ăn thử nghiệm Óc Chó",
            Sku = sku,
            CategoryId = category.Id,
            FurnitureType = FurnitureType.Table,
            Status = ProductStatus.Active,
            ShortDescription = "Bàn ăn dùng cho kiểm thử.",
            Variants =
            [
                new() { Sku = $"{sku}-NOC-160", ColorId = brown.Id, MaterialId = walnut.Id, SizeId = size160.Id, Price = 15_000_000, StockQuantity = 0 },
                new() { Sku = $"{sku}-NOC-180", ColorId = brown.Id, MaterialId = walnut.Id, SizeId = size180.Id, Price = 18_000_000, OriginalPrice = 20_000_000, StockQuantity = 4 },
                new() { Sku = $"{sku}-D-180", ColorId = black.Id, MaterialId = walnut.Id, SizeId = size180.Id, Price = 18_500_000, StockQuantity = 2,
                    SecondaryColorId = brown.Id, SecondaryColorPart = "Mặt bàn" }
            ]
        };
    }

    [Fact]
    public async Task Create_PersistsProductVariantsAndDerivedFields()
    {
        var command = await NewCommandAsync();

        var saved = await Admin(s => s.CreateAsync(command));

        Assert.Equal("ban-an-thu-nghiem-oc-cho", saved.Slug);
        Assert.Equal(3, saved.VariantIds.Count);

        var detail = await Admin(s => s.GetDetailAsync(saved.ProductId));
        Assert.Equal(6, detail.StockQuantity);
        Assert.Equal(15_000_000, detail.Price);
        Assert.Equal("Nâu óc chó / 1m6 (6 người)", detail.Variants[0].Name);
        // Default variant falls back to the first one in stock.
        Assert.Equal($"{command.Sku}-NOC-180", detail.Variants.Single(v => v.IsDefault).Sku);
        Assert.Contains(detail.Variants[2].Parts, p => p.Part == "Mặt bàn");

        var searchText = await Db(db => db.Products.Where(p => p.Id == saved.ProductId).Select(p => p.SearchText).SingleAsync());
        Assert.Contains("ban an thu nghiem oc cho", searchText);
        Assert.Contains("den", searchText);

        var publicSearch = await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetProductsAsync(new ProductQuery { Search = "thu nghiem" }));
        Assert.Single(publicSearch.Items);
    }

    [Fact]
    public async Task Create_WithoutVariants_CreatesDefaultVariantFromProductPrice()
    {
        var command = await NewCommandAsync("SIMPLE-1");
        command.Variants.Clear();
        command.BasePrice = 5_000_000;
        command.DiscountPrice = 4_500_000;
        command.StockQuantity = 7;

        var saved = await Admin(s => s.CreateAsync(command));
        var detail = await Admin(s => s.GetDetailAsync(saved.ProductId));

        var variant = Assert.Single(detail.Variants);
        Assert.Equal("SIMPLE-1", variant.Sku);
        Assert.Equal(4_500_000, variant.Price);
        Assert.Equal(5_000_000, variant.OriginalPrice);
        Assert.Equal(10, variant.DiscountPercent);
        Assert.Equal(7, detail.StockQuantity);
    }

    [Fact]
    public async Task Create_RejectsInvalidData_WithFieldErrors()
    {
        var command = await NewCommandAsync();
        command.Name = "";
        command.Sku = "bad sku!";
        command.Variants[0].Price = 0;
        command.Variants[1].OriginalPrice = 1;
        command.Variants[2].Sku = command.Variants[0].Sku;

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.CreateAsync(command)));

        Assert.True(ex.FieldErrors.ContainsKey("Name"));
        Assert.True(ex.FieldErrors.ContainsKey("Sku"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Biến thể 1:") && e.Contains("Giá"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Biến thể 2:") && e.Contains("Giá cũ"));
        Assert.Contains(ex.Errors, e => e.Contains("trùng SKU"));
    }

    [Fact]
    public async Task Create_RejectsSkuAlreadyUsedByAnotherProduct()
    {
        var command = await NewCommandAsync("SF-OSLO");

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.CreateAsync(command)));

        Assert.Contains("SF-OSLO", ex.FieldErrors["Sku"][0]);
    }

    [Fact]
    public async Task Create_RejectsVariantSkuUsedElsewhere_AndUnknownReferences()
    {
        var command = await NewCommandAsync();
        command.Variants[0].Sku = "SF-OSLO-K-210";
        var usedSku = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.CreateAsync(command)));
        Assert.Contains("SF-OSLO-K-210", usedSku.Errors[0]);

        var badRef = await NewCommandAsync("BA-TEST-02");
        badRef.CategoryId = 999_999;
        badRef.Variants[1].ColorId = 999_999;
        var refs = await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.CreateAsync(badRef)));
        Assert.True(refs.FieldErrors.ContainsKey("CategoryId"));
        Assert.Contains(refs.Errors, e => e.Contains("Biến thể 2") && e.Contains("màu"));
    }

    [Fact]
    public async Task Create_WithDuplicateName_GetsUniqueSlug()
    {
        var first = await Admin(async s => s.CreateAsync(await NewCommandAsync("DUP-1")));
        var secondCommand = await NewCommandAsync("DUP-2");

        var second = await Admin(s => s.CreateAsync(secondCommand));

        Assert.Equal((await first).Slug + "-2", second.Slug);
    }

    [Fact]
    public async Task Update_ChangesPrices_RecordsHistory_AddsAndRemovesVariants()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));
        var edit = await Admin(s => s.GetForEditAsync(saved.ProductId));
        var command = edit.Command;

        command.Variants[1].Price = 17_000_000;                   // price change → history
        command.Variants.RemoveAt(2);                               // removed
        command.Variants.Add(new VariantUpsertModel { Sku = "BA-TEST-01-NEW", ColorId = command.Variants[0].ColorId, MaterialId = command.Variants[0].MaterialId, Price = 9_900_000, StockQuantity = 3, IsDefault = true });
        foreach (var v in command.Variants.Take(2)) v.IsDefault = false;

        var updated = await Admin(s => s.UpdateAsync(saved.ProductId, command));
        var detail = await Admin(s => s.GetDetailAsync(saved.ProductId));

        Assert.Equal(3, updated.VariantIds.Count);
        Assert.Equal(3, detail.Variants.Count);
        Assert.DoesNotContain(detail.Variants, v => v.Sku == "BA-TEST-01-D-180");
        Assert.Equal("BA-TEST-01-NEW", detail.Variants.Single(v => v.IsDefault).Sku);
        Assert.Equal(9_900_000, detail.Price);

        var history = await Db(db => db.ProductPriceHistory.Where(h => h.ProductId == saved.ProductId).ToListAsync());
        var change = Assert.Single(history);
        Assert.Equal(18_000_000, change.OldPrice);
        Assert.Equal(17_000_000, change.NewPrice);
        Assert.Equal("admin@furniture.local", change.ChangedBy);
    }

    [Fact]
    public async Task Update_WithStaleVersion_IsRejected()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));
        var staleForm = (await Admin(s => s.GetForEditAsync(saved.ProductId))).Command;
        var freshForm = (await Admin(s => s.GetForEditAsync(saved.ProductId))).Command;

        freshForm.Name = "Admin A đã sửa";
        await Admin(s => s.UpdateAsync(saved.ProductId, freshForm));

        staleForm.Name = "Admin B ghi đè";
        await Assert.ThrowsAsync<ConflictException>(() => Admin(s => s.UpdateAsync(saved.ProductId, staleForm)));
        Assert.Equal("Admin A đã sửa", (await Admin(s => s.GetDetailAsync(saved.ProductId))).Name);
    }

    [Fact]
    public async Task Update_RequiresAtLeastOneVariant_AndOwnVariantIds()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));
        var command = (await Admin(s => s.GetForEditAsync(saved.ProductId))).Command;

        var noVariants = new ProductUpsertCommand { Name = command.Name, Sku = command.Sku, CategoryId = command.CategoryId, Version = command.Version };
        await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.UpdateAsync(saved.ProductId, noVariants)));

        var otherProductVariantId = await Db(db => db.ProductVariants.Where(v => v.ProductId != saved.ProductId).Select(v => v.Id).FirstAsync());
        command.Variants[0].Id = otherProductVariantId;
        await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.UpdateAsync(saved.ProductId, command)));
    }

    [Fact]
    public async Task Delete_IsSoftDelete_AndHidesFromCatalog()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));

        await Admin(async s => { await s.DeleteAsync(saved.ProductId); return 0; });

        Assert.True(await Db(db => db.Products.IgnoreQueryFilters().AnyAsync(p => p.Id == saved.ProductId && p.IsDeleted)));
        Assert.Null(await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetProductBySlugAsync(saved.Slug)));
        await Assert.ThrowsAsync<NotFoundException>(() => Admin(s => s.GetForEditAsync(saved.ProductId)));
    }

    [Fact]
    public async Task SetStatus_ControlsPublicVisibility()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));

        await Admin(async s => { await s.SetStatusAsync(saved.ProductId, ProductStatus.Inactive); return 0; });
        Assert.Null(await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetProductBySlugAsync(saved.Slug)));

        await Admin(async s => { await s.SetStatusAsync(saved.ProductId, ProductStatus.Active); return 0; });
        Assert.NotNull(await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetProductBySlugAsync(saved.Slug)));
    }

    [Fact]
    public async Task Images_AddSetPrimaryAndDelete()
    {
        var command = await NewCommandAsync();
        var saved = await Admin(s => s.CreateAsync(command));

        var added = await Admin(s => s.AddImagesAsync(saved.ProductId,
        [
            new ImageUpload(InMemoryFileStorage.TinyPng(), "a.png"),
            new ImageUpload(InMemoryFileStorage.TinyPng(), "b.png"),
            new ImageUpload(InMemoryFileStorage.TinyPng(), "v.png", saved.VariantIds[1])
        ]));

        Assert.Equal(3, added.Count);
        Assert.True(added[0].IsPrimary);
        Assert.False(added[1].IsPrimary);
        Assert.Equal(saved.VariantIds[1], added[2].VariantId);
        Assert.Equal(3, _host.Files.Files.Count);

        await Admin(async s => { await s.SetPrimaryImageAsync(saved.ProductId, added[1].Id); return 0; });
        await Admin(async s => { await s.DeleteImageAsync(saved.ProductId, added[0].Id); return 0; });

        var images = (await Admin(s => s.GetForEditAsync(saved.ProductId))).Images;
        Assert.Equal(2, images.Count);
        Assert.Single(images, i => i.IsPrimary && i.Id == added[1].Id);
        Assert.Equal(2, _host.Files.Files.Count);
    }

    [Fact]
    public async Task Images_RejectFakeFiles_AndVariantsOfOtherProducts()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));
        var foreignVariant = await Db(db => db.ProductVariants.Where(v => v.ProductId != saved.ProductId).Select(v => v.Id).FirstAsync());

        await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.AddImagesAsync(saved.ProductId,
            [new ImageUpload(new MemoryStream("<?php echo 1; ?>"u8.ToArray()), "shell.png")])));
        await Assert.ThrowsAsync<AppValidationException>(() => Admin(s => s.AddImagesAsync(saved.ProductId,
            [new ImageUpload(InMemoryFileStorage.TinyPng(), "a.png", foreignVariant)])));

        Assert.Empty(_host.Files.Files);
    }

    [Fact]
    public async Task AdminChanges_AreAudited()
    {
        var saved = await Admin(async s => await s.CreateAsync(await NewCommandAsync()));
        await Admin(async s => { await s.DeleteAsync(saved.ProductId); return 0; });

        var logs = await Db(db => db.AuditLogs.Where(a => a.EntityName == "Product" && a.EntityId == saved.ProductId.ToString()).ToListAsync());

        Assert.Contains(logs, l => l.Action == AuditAction.Create && l.UserName == "admin@furniture.local");
        Assert.Contains(logs, l => l.Action == AuditAction.Delete);
    }

    [Fact]
    public async Task AdminList_FiltersBySearchStatusAndStock()
    {
        var all = await Admin(s => s.ListAsync(new AdminProductQuery { PageSize = 100 }));
        var searched = await Admin(s => s.ListAsync(new AdminProductQuery { Search = "sf-oslo-k" }));
        var outOfStock = await Admin(s => s.ListAsync(new AdminProductQuery { Stock = StockFilter.OutOfStock, PageSize = 100 }));

        Assert.Equal(37, all.TotalCount);
        Assert.Equal("SF-OSLO", Assert.Single(searched.Items).Sku);
        Assert.NotEmpty(outOfStock.Items);
    }
}
