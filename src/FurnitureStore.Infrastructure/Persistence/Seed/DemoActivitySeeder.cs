using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Domain.Constants;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;
using FurnitureStore.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FurnitureStore.Infrastructure.Persistence.Seed;

/// <summary>
/// Demo customers, ~4 months of orders, reviews and contact messages so the dashboard charts,
/// "best sellers", ratings and admin screens have realistic data. Deterministic (fixed random seed).
/// Demo customers have no password and cannot sign in.
/// </summary>
public sealed class DemoActivitySeeder(
    ApplicationDbContext context,
    UserManager<ApplicationUser> userManager,
    TimeProvider timeProvider,
    ILogger<DemoActivitySeeder> logger)
{
    private const string MarkerEmail = "khach01@furniture.local";

    private static readonly (string Name, string Phone, string Province, string Ward)[] Customers =
    [
        ("Nguyễn Minh Anh", "0901234501", "TP. Hồ Chí Minh", "Phường Bến Thành"),
        ("Trần Quốc Bảo", "0912345602", "TP. Hà Nội", "Phường Hoàn Kiếm"),
        ("Lê Thu Hà", "0923456703", "TP. Đà Nẵng", "Phường Hải Châu"),
        ("Phạm Gia Huy", "0934567804", "TP. Hồ Chí Minh", "Phường Thảo Điền"),
        ("Võ Ngọc Lan", "0945678905", "Khánh Hòa", "Phường Nha Trang"),
        ("Đặng Hoàng Long", "0956789006", "TP. Cần Thơ", "Phường Ninh Kiều"),
        ("Bùi Khánh Linh", "0967890107", "TP. Hải Phòng", "Phường Lê Chân"),
        ("Hoàng Đức Minh", "0978901208", "Đồng Nai", "Phường Biên Hòa"),
        ("Ngô Phương Thảo", "0989012309", "Lâm Đồng", "Phường Xuân Hương - Đà Lạt"),
        ("Đỗ Thành Nam", "0390123410", "Bắc Ninh", "Phường Kinh Bắc"),
        ("Huỳnh Mỹ Duyên", "0381234511", "TP. Huế", "Phường Thuận Hóa"),
        ("Trịnh Văn Khoa", "0372345612", "Nghệ An", "Phường Trường Vinh")
    ];

    private static readonly (int Rating, string Title, string Comment)[] ReviewTemplates =
    [
        (5, "Rất hài lòng", "Gỗ đẹp, vân rõ, hoàn thiện kỹ. Đội lắp đặt đến đúng giờ và rất nhiệt tình."),
        (5, "Đáng tiền", "Sản phẩm chắc chắn, màu y như hình. Cả nhà ai cũng khen."),
        (5, "Chất lượng tốt", "Đóng gói cẩn thận, giao nhanh hơn dự kiến. Sẽ tiếp tục ủng hộ xưởng."),
        (4, "Hài lòng", "Mẫu đẹp, ngồi êm. Giao hàng hơi trễ một ngày nhưng có báo trước."),
        (4, "Tốt", "Chất liệu ổn so với giá, nhân viên tư vấn kỹ về kích thước phòng."),
        (5, "Tuyệt vời", "Đặt đúng kích thước căn hộ, lắp vừa khít. Rất đáng để mua."),
        (3, "Tạm ổn", "Sản phẩm đẹp nhưng màu thực tế đậm hơn ảnh một chút.")
    ];

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await userManager.FindByEmailAsync(MarkerEmail) is not null)
        {
            return;
        }

        var random = new Random(2026);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var customers = new List<ApplicationUser>();

        for (var i = 0; i < Customers.Length; i++)
        {
            var info = Customers[i];
            var user = new ApplicationUser
            {
                UserName = $"khach{i + 1:00}@furniture.local",
                Email = $"khach{i + 1:00}@furniture.local",
                EmailConfirmed = true,
                FullName = info.Name,
                PhoneNumber = info.Phone,
                IsActive = true,
                CreatedAt = now.AddDays(-random.Next(20, 200))
            };
            var result = await userManager.CreateAsync(user);
            if (!result.Succeeded)
            {
                logger.LogWarning("Demo customer {Email} not created: {Errors}", user.Email, string.Join("; ", result.Errors.Select(e => e.Description)));
                continue;
            }

            await userManager.AddToRoleAsync(user, AppRoles.User);
            customers.Add(user);
            context.CustomerAddresses.Add(new CustomerAddress
            {
                UserId = user.Id, Label = "Nhà riêng", RecipientName = info.Name, Phone = info.Phone,
                AddressLine = $"{random.Next(1, 300)} đường số {random.Next(1, 30)}", Ward = info.Ward, Province = info.Province, IsDefault = true
            });
        }

        if (customers.Count == 0)
        {
            return;
        }

        var variants = await context.ProductVariants.AsNoTracking()
            .Where(v => v.IsActive && v.Product.Status == ProductStatus.Active)
            .Select(v => new
            {
                v.Id, v.ProductId, ProductName = v.Product.Name, v.Name, v.Sku, v.Price,
                Image = v.Images.OrderBy(i => i.DisplayOrder).Select(i => i.Url).FirstOrDefault(),
                Color = v.Colors.Where(c => c.IsPrimary).Select(c => c.Color.Name).FirstOrDefault(),
                Material = v.Materials.Where(m => m.IsPrimary).Select(m => m.Material.Name).FirstOrDefault(),
                Size = v.Sizes.Where(s => s.IsPrimary).Select(s => s.Size.Name).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var orders = new List<Order>();
        for (var i = 0; i < 90; i++)
        {
            var customer = customers[random.Next(customers.Count)];
            var info = Customers[customers.IndexOf(customer) % Customers.Length];
            var placedAt = now.AddDays(-random.Next(0, 120)).AddHours(-random.Next(0, 12)).AddMinutes(-random.Next(0, 60));
            var ageDays = (now - placedAt).TotalDays;
            var method = random.Next(3) == 0 ? PaymentMethod.BankTransfer : PaymentMethod.COD;

            var order = new Order
            {
                OrderCode = $"DH{VietnamTime.ToLocal(placedAt):yyMMdd}-D{i:0000}",
                UserId = customer.Id,
                PaymentMethod = method,
                CustomerName = info.Name,
                CustomerPhone = info.Phone,
                CustomerEmail = customer.Email!,
                PlacedAt = placedAt,
                CreatedAt = placedAt
            };

            foreach (var line in Enumerable.Range(0, random.Next(1, 4)).Select(_ => variants[random.Next(variants.Count)]).DistinctBy(v => v.Id))
            {
                order.Items.Add(new OrderItem
                {
                    ProductId = line.ProductId, ProductVariantId = line.Id, ProductName = line.ProductName, VariantName = line.Name, Sku = line.Sku,
                    ImageUrl = line.Image, ColorName = line.Color, MaterialName = line.Material, SizeName = line.Size,
                    UnitPrice = line.Price, Quantity = random.Next(1, 3)
                });
            }

            order.RecalculateTotals();
            order.ShippingFee = order.Subtotal >= 10_000_000 ? 0 : 300_000;
            order.RecalculateTotals();
            order.Addresses.Add(new OrderAddress
            {
                RecipientName = info.Name, Phone = info.Phone, Email = customer.Email, AddressLine = $"{random.Next(1, 300)} đường số {random.Next(1, 30)}",
                Ward = info.Ward, Province = info.Province
            });
            order.StatusHistory.Add(new OrderStatusHistory { ToStatus = OrderStatus.Pending, Note = "Khách hàng đặt hàng", ChangedBy = customer.Email, ChangedAt = placedAt });
            order.Payments.Add(new Payment { Method = method, Status = PaymentStatus.Pending, Amount = order.TotalAmount, Provider = method.ToString(), CreatedAt = placedAt });
            order.PaymentStatus = method == PaymentMethod.BankTransfer ? PaymentStatus.Pending : PaymentStatus.Unpaid;

            // Walk the order through the workflow according to its age.
            var target = TargetStatus(ageDays, random);
            var at = placedAt;
            foreach (var next in PathTo(target))
            {
                at = at.AddHours(random.Next(3, 30));
                if (at > now) break;
                if (method == PaymentMethod.BankTransfer && next == OrderStatus.Confirmed)
                {
                    order.PaymentStatus = PaymentStatus.Paid;
                    order.Payments.First().Status = PaymentStatus.Paid;
                    order.Payments.First().PaidAt = at;
                }

                order.ChangeStatus(next, at, "admin@furniture.local", next == OrderStatus.Cancelled ? "Khách đổi ý" : null);
                if (next == OrderStatus.Delivered && method == PaymentMethod.COD)
                {
                    order.Payments.First().Status = PaymentStatus.Paid;
                    order.Payments.First().PaidAt = at;
                }
            }

            if (order.Status == OrderStatus.Cancelled && order.PaymentStatus != PaymentStatus.Paid)
            {
                order.PaymentStatus = PaymentStatus.Failed;
                order.Payments.First().Status = PaymentStatus.Failed;
            }
            else if (order.Status == OrderStatus.Refunded)
            {
                order.Payments.First().Status = PaymentStatus.Refunded;
            }

            orders.Add(order);
        }

        context.Orders.AddRange(orders);
        await context.SaveChangesAsync(cancellationToken);

        // Reviews from customers who received their products (one per customer and product).
        var reviewed = new HashSet<(string, int)>();
        foreach (var order in orders.Where(o => o.Status == OrderStatus.Delivered))
        {
            foreach (var item in order.Items.Where(i => i.ProductId.HasValue))
            {
                if (random.NextDouble() > 0.65 || !reviewed.Add((order.UserId, item.ProductId!.Value)))
                {
                    continue;
                }

                var template = ReviewTemplates[random.Next(ReviewTemplates.Length)];
                var createdAt = (order.DeliveredAt ?? now).AddDays(random.Next(1, 6));
                context.Reviews.Add(new Review
                {
                    ProductId = item.ProductId.Value,
                    UserId = order.UserId,
                    ReviewerName = order.CustomerName,
                    OrderItemId = item.Id,
                    Rating = template.Rating,
                    Title = template.Title,
                    Comment = template.Comment,
                    IsVerifiedPurchase = true,
                    CreatedAt = createdAt < now ? createdAt : now
                });
            }
        }

        context.ContactMessages.AddRange(
            new ContactMessage { FullName = "Phan Thị Hồng", Phone = "0911222333", Email = "hong.phan@example.com", Subject = "Đặt tủ quần áo theo kích thước",
                Message = "Tôi cần tủ quần áo 2m2 x 2m4 cho phòng ngủ master, gỗ sồi màu trắng. Xin báo giá giúp.", CreatedAt = now.AddDays(-2) },
            new ContactMessage { FullName = "Lý Công Tuấn", Phone = "0988777666", Email = "tuan.ly@example.com", Subject = "Hỏi về bảo hành",
                Message = "Sofa mua năm ngoái bị xẹp đệm, cho hỏi thủ tục bảo hành như thế nào?", Status = ContactMessageStatus.Read, CreatedAt = now.AddDays(-5) },
            new ContactMessage { FullName = "Mai Anh Thư", Phone = "0355666777", Email = "thu.mai@example.com",
                Message = "Showroom có mở cửa chủ nhật không ạ? Tôi muốn xem trực tiếp bàn ăn gỗ óc chó.", Status = ContactMessageStatus.Replied,
                RepliedAt = now.AddDays(-9), CreatedAt = now.AddDays(-10) });

        await context.SaveChangesAsync(cancellationToken);

        // Product rating summaries from the seeded reviews.
        var stats = await context.Reviews.Where(r => !r.IsHidden).GroupBy(r => r.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToListAsync(cancellationToken);
        foreach (var stat in stats)
        {
            var average = Math.Round((decimal)stat.Sum / stat.Count, 2);
            await context.Products.Where(p => p.Id == stat.ProductId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.AverageRating, average).SetProperty(p => p.ReviewCount, stat.Count), cancellationToken);
        }

        logger.LogInformation("Seeded demo activity: {Customers} customers, {Orders} orders, {Reviews} reviews",
            customers.Count, orders.Count, reviewed.Count);
    }

    private static OrderStatus TargetStatus(double ageDays, Random random)
    {
        var roll = random.NextDouble();
        if (ageDays < 2) return roll < 0.6 ? OrderStatus.Pending : OrderStatus.Confirmed;
        if (ageDays < 6) return roll < 0.3 ? OrderStatus.Confirmed : roll < 0.6 ? OrderStatus.Processing : roll < 0.9 ? OrderStatus.Shipping : OrderStatus.Cancelled;
        return roll < 0.82 ? OrderStatus.Delivered : roll < 0.95 ? OrderStatus.Cancelled : OrderStatus.Refunded;
    }

    private static IEnumerable<OrderStatus> PathTo(OrderStatus target) => target switch
    {
        OrderStatus.Pending => [],
        OrderStatus.Confirmed => [OrderStatus.Confirmed],
        OrderStatus.Processing => [OrderStatus.Confirmed, OrderStatus.Processing],
        OrderStatus.Shipping => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipping],
        OrderStatus.Delivered => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipping, OrderStatus.Delivered],
        OrderStatus.Cancelled => [OrderStatus.Confirmed, OrderStatus.Cancelled],
        OrderStatus.Refunded => [OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipping, OrderStatus.Delivered, OrderStatus.Refunded],
        _ => []
    };
}
