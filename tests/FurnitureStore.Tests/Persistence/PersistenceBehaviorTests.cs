using FurnitureStore.Application.Common.Exceptions;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Persistence;
using FurnitureStore.Infrastructure.Persistence.Repositories;
using FurnitureStore.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace FurnitureStore.Tests.Persistence;

public sealed class PersistenceBehaviorTests : IDisposable
{
    private readonly FixedTimeProvider _clock = FixedTimeProvider.At(2026, 9, 1);
    private readonly SqliteTestDatabase _database;

    public PersistenceBehaviorTests()
    {
        _database = new SqliteTestDatabase(_clock);
    }

    public void Dispose() => _database.Dispose();

    private async Task<int> CreateProductAsync(string sku = "TEST-01", int stock = 5)
    {
        await using var context = _database.CreateContext();
        var category = new Category { Name = "Bàn", Slug = $"ban-{sku.ToLowerInvariant()}" };
        var product = new Product
        {
            Category = category,
            Name = $"Bàn thử {sku}",
            Slug = $"ban-thu-{sku.ToLowerInvariant()}",
            Sku = sku,
            FurnitureType = FurnitureType.Table,
            Status = ProductStatus.Active,
            BasePrice = 1_000_000
        };
        product.Variants.Add(new ProductVariant { Name = "Mặc định", Sku = $"{sku}-V1", Price = 1_000_000, StockQuantity = stock, IsDefault = true });
        product.Images.Add(new ProductImage { Url = "/img/a.svg", IsPrimary = true });
        context.Products.Add(product);
        await context.SaveChangesAsync();
        return product.Id;
    }

    [Fact]
    public async Task Insert_FillsAuditFields_ConcurrencyToken_AndCreatedAtOfPlainEntities()
    {
        var id = await CreateProductAsync();
        await using var context = _database.CreateContext();

        var product = await context.Products.Include(p => p.Images).SingleAsync(p => p.Id == id);

        Assert.Equal(_clock.GetUtcNow().UtcDateTime, product.CreatedAt);
        Assert.Equal("tester", product.CreatedBy);
        Assert.Null(product.UpdatedAt);
        Assert.NotEqual(Guid.Empty, product.Version);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, product.Images.Single().CreatedAt);
    }

    [Fact]
    public async Task Update_SetsUpdatedFields_AndRotatesVersion()
    {
        var id = await CreateProductAsync();
        Guid originalVersion;
        await using (var context = _database.CreateContext())
        {
            originalVersion = (await context.Products.SingleAsync(p => p.Id == id)).Version;
        }

        _clock.Advance(TimeSpan.FromHours(2));
        await using (var context = _database.CreateContext(new TestCurrentUser("admin@furniture.local")))
        {
            var product = await context.Products.SingleAsync(p => p.Id == id);
            product.Name = "Bàn đã sửa";
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        var updated = await verify.Products.SingleAsync(p => p.Id == id);
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, updated.UpdatedAt);
        Assert.Equal("admin@furniture.local", updated.UpdatedBy);
        Assert.NotEqual(originalVersion, updated.Version);
    }

    [Fact]
    public async Task DeletingProduct_IsSoftDelete_AndKeepsVariantsAndImages()
    {
        var id = await CreateProductAsync();

        await using (var context = _database.CreateContext())
        {
            var product = await context.Products.Include(p => p.Variants).Include(p => p.Images).SingleAsync(p => p.Id == id);
            context.Products.Remove(product);
            await context.SaveChangesAsync();
        }

        await using var verify = _database.CreateContext();
        Assert.False(await verify.Products.AnyAsync(p => p.Id == id));
        Assert.False(await verify.ProductVariants.AnyAsync(v => v.ProductId == id));

        var deleted = await verify.Products.IgnoreQueryFilters().SingleAsync(p => p.Id == id);
        Assert.True(deleted.IsDeleted);
        Assert.Equal("tester", deleted.DeletedBy);
        Assert.NotNull(deleted.DeletedAt);
        Assert.Equal(1, await verify.ProductVariants.IgnoreQueryFilters().CountAsync(v => v.ProductId == id));
        Assert.Equal(1, await verify.ProductImages.IgnoreQueryFilters().CountAsync(i => i.ProductId == id));
    }

    [Fact]
    public async Task SoftDeletedProduct_FreesItsSkuAndSlugForReuse()
    {
        var id = await CreateProductAsync("REUSE");
        await using (var context = _database.CreateContext())
        {
            context.Products.Remove(await context.Products.SingleAsync(p => p.Id == id));
            await context.SaveChangesAsync();
        }

        await using var again = _database.CreateContext();
        again.Products.Add(new Product
        {
            CategoryId = (await again.Categories.FirstAsync()).Id,
            Name = "Bàn thử REUSE",
            Slug = "ban-thu-reuse",
            Sku = "REUSE",
            FurnitureType = FurnitureType.Table
        });

        await again.SaveChangesAsync();
        Assert.Equal(1, await again.Products.CountAsync(p => p.Sku == "REUSE"));
    }

    [Fact]
    public async Task ConcurrentStockUpdates_SecondWriterGetsConflict()
    {
        var id = await CreateProductAsync(stock: 1);

        await using var first = _database.CreateContext();
        await using var second = _database.CreateContext();
        var variantA = await first.ProductVariants.SingleAsync(v => v.ProductId == id);
        var variantB = await second.ProductVariants.SingleAsync(v => v.ProductId == id);

        variantA.DecreaseStock(1);
        await new UnitOfWork(first, NullLogger<UnitOfWork>.Instance).SaveChangesAsync();

        variantB.DecreaseStock(1);
        await Assert.ThrowsAsync<ConflictException>(() => new UnitOfWork(second, NullLogger<UnitOfWork>.Instance).SaveChangesAsync());

        await using var verify = _database.CreateContext();
        Assert.Equal(0, (await verify.ProductVariants.SingleAsync(v => v.ProductId == id)).StockQuantity);
    }

    [Fact]
    public async Task DuplicateVariantSku_RaisesConflict()
    {
        var id = await CreateProductAsync("DUP");
        await using var context = _database.CreateContext();
        context.ProductVariants.Add(new ProductVariant { ProductId = id, Name = "Trùng", Sku = "DUP-V1", Price = 1 });

        var exception = await Assert.ThrowsAsync<ConflictException>(() => new UnitOfWork(context, NullLogger<UnitOfWork>.Instance).SaveChangesAsync());
        Assert.Contains("đã tồn tại", exception.Message);
    }

    [Fact]
    public async Task CheckConstraint_RejectsInvalidReviewRating()
    {
        var id = await CreateProductAsync("RATE");
        await using var context = _database.CreateContext();
        context.Reviews.Add(new Review { ProductId = id, UserId = "u1", ReviewerName = "A", Rating = 6, Comment = "?" });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DateTimes_AreReadBackAsUtc()
    {
        var id = await CreateProductAsync();
        await using var context = _database.CreateContext();

        var product = await context.Products.SingleAsync(p => p.Id == id);

        Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
    }

    [Fact]
    public async Task GenericRepository_SupportsBasicQueries()
    {
        await CreateProductAsync("R1");
        await CreateProductAsync("R2");
        await using var context = _database.CreateContext();
        var repository = new EfRepository<Product>(context);

        Assert.Equal(2, await repository.CountAsync());
        Assert.True(await repository.AnyAsync(p => p.Sku == "R2"));
        Assert.Single(await repository.ListAsync(p => p.Sku == "R1"));
        Assert.NotNull(await repository.GetByIdAsync((await repository.ListAsync()).First().Id));
    }

    [Fact]
    public async Task UnitOfWork_RollsBackTransaction_WhenOperationThrows()
    {
        await using var context = _database.CreateContext();
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            context.ProductColors.Add(new ProductColor { Name = "Tạm", Slug = "tam", HexCode = "#111111" });
            await unitOfWork.SaveChangesAsync(ct);
            throw new InvalidOperationException("boom");
        }));

        await using var verify = _database.CreateContext();
        Assert.False(await verify.ProductColors.AnyAsync(c => c.Slug == "tam"));
    }
}
