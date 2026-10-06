using FurnitureStore.Application.Catalog;
using FurnitureStore.Application.Catalog.Admin;
using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FurnitureStore.Tests.Catalog;

public sealed class CategoryAndAttributeAdminTests : IAsyncLifetime
{
    private ServiceTestHost _host = null!;

    public async Task InitializeAsync() => _host = await ServiceTestHost.CreateAsync();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private Task<T> Categories<T>(Func<ICategoryAdminService, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<ICategoryAdminService>()));

    private Task<T> Attributes<T>(Func<IAttributeAdminService, Task<T>> action) =>
        _host.RunAsync(sp => action(sp.GetRequiredService<IAttributeAdminService>()));

    [Fact]
    public async Task Category_CreateChild_AppearsInPublicTree()
    {
        var parent = (await Categories(s => s.ListAsync())).Single(c => c.Slug == "phong-khach");

        var id = await Categories(s => s.CreateAsync(new CategoryUpsertCommand { Name = "Đèn trang trí", ParentId = parent.Id, IsActive = true }));

        var created = await Categories(s => s.GetAsync(id));
        Assert.Equal("den-trang-tri", created.Slug);
        var tree = await _host.RunAsync(sp => sp.GetRequiredService<ICatalogService>().GetCategoryTreeAsync());
        Assert.Contains(tree.Single(c => c.Slug == "phong-khach").Children, c => c.Slug == "den-trang-tri");
    }

    [Fact]
    public async Task Category_OnlyTwoLevels_AndNoSelfParent()
    {
        var child = (await Categories(s => s.ListAsync())).Single(c => c.Slug == "sofa");
        var root = (await Categories(s => s.ListAsync())).Single(c => c.Slug == "phong-khach");

        await Assert.ThrowsAsync<AppValidationException>(() =>
            Categories(s => s.CreateAsync(new CategoryUpsertCommand { Name = "Cấp 3", ParentId = child.Id })));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            Categories(async s => { await s.UpdateAsync(root.Id, new CategoryUpsertCommand { Name = root.Name, ParentId = root.Id }); return 0; }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            Categories(s => s.CreateAsync(new CategoryUpsertCommand { Name = "Trùng", Slug = "sofa" })));
    }

    [Fact]
    public async Task Category_CannotDeleteWhenItHasProductsOrChildren()
    {
        var list = await Categories(s => s.ListAsync());
        var withProducts = list.Single(c => c.Slug == "sofa");
        var withChildren = list.Single(c => c.Slug == "phong-khach");

        await Assert.ThrowsAsync<BusinessRuleException>(() => Categories(async s => { await s.DeleteAsync(withProducts.Id); return 0; }));
        await Assert.ThrowsAsync<BusinessRuleException>(() => Categories(async s => { await s.DeleteAsync(withChildren.Id); return 0; }));

        var emptyId = await Categories(s => s.CreateAsync(new CategoryUpsertCommand { Name = "Tạm thời", ParentId = withChildren.Id }));
        await Categories(async s => { await s.DeleteAsync(emptyId); return 0; });
        Assert.DoesNotContain(await Categories(s => s.ListAsync()), c => c.Id == emptyId);
    }

    [Fact]
    public async Task Attributes_CrudForEveryKind()
    {
        var colorId = await Attributes(s => s.CreateAsync(AttributeKind.Color, new AttributeUpsertCommand { Name = "Xanh ngọc", HexCode = "#2a9d8f" }));
        var materialId = await Attributes(s => s.CreateAsync(AttributeKind.Material, new AttributeUpsertCommand { Name = "Gỗ xoan đào", MaterialGroup = MaterialGroup.NaturalWood }));
        var sizeId = await Attributes(s => s.CreateAsync(AttributeKind.Size, new AttributeUpsertCommand { Name = "2m2 x 1m", LengthMm = 2200, WidthMm = 1000, HeightMm = 750, FurnitureType = FurnitureType.Table }));
        var styleId = await Attributes(s => s.CreateAsync(AttributeKind.Style, new AttributeUpsertCommand { Name = "Wabi-sabi", Code = "WabiSabi" }));

        Assert.Equal("#2A9D8F", (await Attributes(s => s.GetAsync(AttributeKind.Color, colorId))).HexCode);
        Assert.Equal("go-xoan-dao", (await Attributes(s => s.GetAsync(AttributeKind.Material, materialId))).Slug);

        await Attributes(async s => { await s.UpdateAsync(AttributeKind.Size, sizeId, new AttributeUpsertCommand { Name = "2m2 x 1m", LengthMm = 2200, WidthMm = 1000, HeightMm = 760 }); return 0; });
        Assert.Equal(760, (await Attributes(s => s.GetAsync(AttributeKind.Size, sizeId))).HeightMm);

        await Attributes(async s => { await s.DeleteAsync(AttributeKind.Style, styleId); return 0; });
        await Assert.ThrowsAsync<NotFoundException>(() => Attributes(s => s.GetAsync(AttributeKind.Style, styleId)));
    }

    [Theory]
    [InlineData(AttributeKind.Color, "Đỏ", null)]         // hex missing
    [InlineData(AttributeKind.Color, "Đỏ", "red")]        // hex invalid
    [InlineData(AttributeKind.Style, "Kiểu mới", null)]   // code missing
    [InlineData(AttributeKind.Size, "Thiếu kích thước", null)]
    public async Task Attributes_ValidateKindSpecificFields(AttributeKind kind, string name, string? value)
    {
        var command = new AttributeUpsertCommand { Name = name, HexCode = value, Code = value };

        await Assert.ThrowsAsync<AppValidationException>(() => Attributes(s => s.CreateAsync(kind, command)));
    }

    [Fact]
    public async Task Attributes_InUse_CannotBeDeleted_AndDuplicatesRejected()
    {
        var colors = await Attributes(s => s.ListAsync(AttributeKind.Color));
        var walnut = colors.Single(c => c.Slug == "nau-oc-cho");

        Assert.True(walnut.UsageCount > 0);
        await Assert.ThrowsAsync<BusinessRuleException>(() => Attributes(async s => { await s.DeleteAsync(AttributeKind.Color, walnut.Id); return 0; }));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            Attributes(s => s.CreateAsync(AttributeKind.Color, new AttributeUpsertCommand { Name = "Nâu óc chó", HexCode = "#000000" })));
    }
}
