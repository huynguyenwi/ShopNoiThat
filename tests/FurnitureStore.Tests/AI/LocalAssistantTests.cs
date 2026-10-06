using FurnitureStore.Application.AI;
using FurnitureStore.Application.Common.Settings;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Tests.AI;

/// <summary>The built-in answers used when no AI model is configured.</summary>
public sealed class LocalAssistantTests
{
    private static readonly StoreInfoDto Store = new("Nhà Mộc Furniture", null, null, "123 Nguyễn Văn Linh, Q.7, TP.HCM", "KCN Tân Uyên, Bình Dương",
        "1900 0000", "contact@furniture.local", "08:00 - 21:00 (Thứ 2 - Chủ nhật)", "https://facebook.com/nhamoc", null, "https://zalo.me/19000000", null);

    private static readonly ShippingSettings Shipping = new() { FreeShippingThreshold = 10_000_000, StandardFee = 300_000 };

    private static LocalFacts Facts(IReadOnlyList<AIKnowledgeEntry>? knowledge = null, IReadOnlyList<Coupon>? coupons = null,
        IReadOnlyList<OrderListItemDto>? orders = null, ProductFact? focus = null, int? warranty = null) =>
        new(Store, Shipping, knowledge ?? [], coupons ?? [], orders, focus, warranty, []);

    private static LocalReply Ask(string message, LocalFacts? facts = null, bool focus = false) =>
        LocalAssistant.Answer(LocalAssistant.Classify(message, focus) ?? throw new Xunit.Sdk.XunitException($"Not understood: {message}"), facts ?? Facts());

    // ------------------------------------------------------------------ what the question is about

    [Theory]
    [InlineData("Xin chào", LocalTopic.Greeting)]
    [InlineData("hello shop", LocalTopic.Greeting)]
    [InlineData("cảm ơn nhé", LocalTopic.Thanks)]
    [InlineData("tạm biệt", LocalTopic.Goodbye)]
    [InlineData("bạn là ai vậy", LocalTopic.Identity)]
    [InlineData("ban la nguoi hay may", LocalTopic.Identity)]
    [InlineData("bạn giúp được gì?", LocalTopic.Help)]
    [InlineData("Cửa hàng mở cửa mấy giờ?", LocalTopic.OpeningHours)]
    [InlineData("chu nhat co mo cua khong", LocalTopic.OpeningHours)]
    [InlineData("Showroom ở đâu vậy?", LocalTopic.Address)]
    [InlineData("dia chi cua hang", LocalTopic.Address)]
    [InlineData("cho mình xin số hotline", LocalTopic.Contact)]
    [InlineData("có zalo không", LocalTopic.Contact)]
    [InlineData("Phí ship bao nhiêu?", LocalTopic.Shipping)]
    [InlineData("giao hàng mất mấy ngày", LocalTopic.Shipping)]
    [InlineData("Chào shop, cho mình hỏi phí vận chuyển ra Hà Nội", LocalTopic.Shipping)] // greeting + real question
    [InlineData("bảo hành bao lâu", LocalTopic.Warranty)]
    [InlineData("ghế bị hỏng thì sao", LocalTopic.Warranty)]
    [InlineData("đổi trả thế nào", LocalTopic.Returns)]
    [InlineData("có hoàn tiền không", LocalTopic.Returns)]
    [InlineData("thanh toán bằng cách nào", LocalTopic.Payment)]
    [InlineData("có trả góp không shop", LocalTopic.Payment)]
    [InlineData("cách đặt hàng trên web", LocalTopic.HowToOrder)]
    [InlineData("đơn hàng của tôi đến đâu rồi", LocalTopic.OrderStatus)]
    [InlineData("kiem tra don hang", LocalTopic.OrderStatus)]
    [InlineData("làm sao để hủy đơn", LocalTopic.CancelOrder)]
    [InlineData("Có mã giảm giá không?", LocalTopic.Coupons)]
    [InlineData("shop co voucher khong", LocalTopic.Coupons)]
    [InlineData("quên mật khẩu thì làm sao", LocalTopic.Account)]
    [InlineData("nhận đóng theo yêu cầu không", LocalTopic.CustomOrder)]
    [InlineData("bảo quản đồ gỗ thế nào", LocalTopic.Care)]
    [InlineData("vệ sinh sofa da như thế nào", LocalTopic.Care)]
    [InlineData("Gỗ sồi và gỗ óc chó khác gì?", LocalTopic.Material)]
    [InlineData("go cao su co ben khong", LocalTopic.Material)]
    [InlineData("MDF có tốt không", LocalTopic.Material)]
    [InlineData("phong cách Bắc Âu là gì", LocalTopic.Style)]
    [InlineData("Japandi như thế nào", LocalTopic.Style)]
    [InlineData("bàn ăn 6 người cần kích thước bao nhiêu", LocalTopic.SizeGuide)]
    [InlineData("sofa dài bao nhiêu là vừa", LocalTopic.SizeGuide)]
    [InlineData("nên chọn giường 1m6 hay 1m8", LocalTopic.SizeGuide)]
    public void Classifies_EverydayQuestions(string message, LocalTopic expected)
    {
        var match = LocalAssistant.Classify(message, hasFocusProduct: false);

        Assert.NotNull(match);
        Assert.Equal(expected, match.Topic);
    }

    [Theory]
    [InlineData("sản phẩm này giá bao nhiêu", LocalTopic.FocusPrice)]
    [InlineData("Có màu nào khác không?", LocalTopic.FocusColor)]
    [InlineData("kích thước thế nào", LocalTopic.FocusSize)]
    [InlineData("làm bằng gỗ gì", LocalTopic.FocusMaterial)]
    [InlineData("còn hàng không", LocalTopic.FocusStock)]
    [InlineData("Mẫu nào rẻ hơn?", LocalTopic.FocusCheaper)]
    [InlineData("Giao hàng mất bao lâu?", LocalTopic.Shipping)]        // general topics still win on a product page
    [InlineData("sản phẩm này bảo hành mấy tháng", LocalTopic.Warranty)]
    public void Classifies_ProductPageQuestions(string message, LocalTopic expected)
    {
        Assert.Equal(expected, LocalAssistant.Classify(message, hasFocusProduct: true)?.Topic);
    }

    [Theory]
    [InlineData("bàn ăn 6 người khoảng 10 triệu")]       // product searches are not everyday questions
    [InlineData("sofa xám cho phòng 15m2 dưới 15 triệu")]
    [InlineData("tư vấn giúp mình sản phẩm này")]
    [InlineData("thông tin về sản phẩm")]                 // "thông" is not "gỗ thông"
    [InlineData("mẫu này đẹp nhưng hơi to")]             // "mẫu" / "nhưng" are not "màu" / "nhung"
    [InlineData("asdkjh qwe")]
    public void DoesNotGuess_OnOtherQuestions(string message)
    {
        var match = LocalAssistant.Classify(message, hasFocusProduct: false);

        Assert.True(match is null || match.Topic is not (LocalTopic.Material or LocalTopic.FocusColor), match?.Topic.ToString());
        if (message is "bàn ăn 6 người khoảng 10 triệu" or "sofa xám cho phòng 15m2 dưới 15 triệu" or "asdkjh qwe") Assert.Null(match);
    }

    // ------------------------------------------------------------------ answers built from store data

    [Fact]
    public void StoreFacts_ComeFromTheStoreSettings()
    {
        Assert.Contains("08:00 - 21:00 (Thứ 2 - Chủ nhật)", Ask("mấy giờ mở cửa").Text);
        var address = Ask("showroom ở đâu").Text;
        Assert.Contains("123 Nguyễn Văn Linh", address);
        Assert.Contains("KCN Tân Uyên", address);
        var contact = Ask("số hotline là gì").Text;
        Assert.Contains("1900 0000", contact);
        Assert.Contains("https://zalo.me/19000000", contact);
    }

    [Fact]
    public void Shipping_UsesTheShippingSettings_OrTheAdminText()
    {
        var builtIn = Ask("phí ship bao nhiêu").Text;
        Assert.Contains("10.000.000₫", builtIn);
        Assert.Contains("300.000₫", builtIn);

        AIKnowledgeEntry admin = new() { Title = "Giao hàng & lắp đặt", Keywords = "giao hàng, ship", Content = "Giao nội thành trong 48 giờ.", IsActive = true };
        Assert.Equal("Giao nội thành trong 48 giờ.", Ask("phí ship bao nhiêu", Facts(knowledge: [admin])).Text);
    }

    [Fact]
    public void SeveralPolicies_InOneQuestion_AreAllAnswered()
    {
        var reply = Ask("Chính sách giao hàng và bảo hành thế nào?").Text;

        Assert.Contains("giao hàng", reply);
        Assert.Contains("bảo hành", reply);
    }

    [Fact]
    public void Installments_AreSaidToBeUnavailable_OnlyWhenAsked()
    {
        Assert.Contains("chưa hỗ trợ trả góp", Ask("có trả góp không").Text);
        Assert.DoesNotContain("trả góp", Ask("thanh toán thế nào").Text);
    }

    [Fact]
    public void Coupons_ListTheRunningPublicCodes()
    {
        Coupon welcome = new() { Code = "CHAOBAN10", DiscountType = DiscountType.Percentage, DiscountValue = 10, MaxDiscountAmount = 2_000_000, MinOrderAmount = 5_000_000, UsageLimitPerUser = 1 };

        var reply = Ask("có mã giảm giá không", Facts(coupons: [welcome])).Text;
        Assert.Contains("CHAOBAN10: Giảm 10%, tối đa 2.000.000₫ (Đơn từ 5.000.000₫ · mỗi khách 1 lần)", reply);

        Assert.Contains("chưa có mã giảm giá công khai", Ask("có mã giảm giá không").Text);
    }

    [Fact]
    public void OrderStatus_ShowsTheCustomersOwnOrders_OrAsksToSignIn()
    {
        var order = new OrderListItemDto(1, "DH260930-AAAAA", new DateTime(2026, 9, 30, 2, 0, 0), OrderStatus.Shipping, PaymentStatus.Unpaid,
            PaymentMethod.COD, 18_900_000, 1, "Sofa Oslo", null);

        var mine = Ask("đơn hàng của tôi đâu", Facts(orders: [order])).Text;
        Assert.Contains("DH260930-AAAAA (30/09/2026): Đang giao hàng - 18.900.000₫", mine);

        Assert.Contains("đăng nhập", Ask("đơn hàng của tôi đâu").Text);                    // anonymous
        Assert.Contains("chưa có đơn hàng", Ask("đơn hàng của tôi đâu", Facts(orders: [])).Text);
    }

    [Fact]
    public void MaterialComparison_DescribesBothMaterials()
    {
        var reply = Ask("gỗ sồi và gỗ óc chó khác gì");

        Assert.Contains("Gỗ sồi:", reply.Text);
        Assert.Contains("Gỗ óc chó:", reply.Text);
        Assert.Contains("Ưu điểm", reply.Text);
    }

    [Fact]
    public void ProductPage_AnswersAboutThatProduct()
    {
        var sofa = new ProductFact(1, "Sofa Oslo", "sofa-oslo", "Sofa", "sofa", "Phòng khách", "Bắc Âu", FurnitureType.Sofa, null,
            16_900_000, 18_900_000, null, true, 4.8m, 12, 86, null, ["Kem be", "Xanh rêu"], ["kem", "xanh-reu"], ["Gỗ sồi", "Vải bố"], ["go-soi", "vai-bo"],
            [new ProductFactSize("3 chỗ - 2m1", 2100, 850, 800)],
            [new ProductFactVariant(1, "Kem be / 2m1", 16_900_000, true), new ProductFactVariant(2, "Xanh rêu / 2m4", 18_900_000, false)]);
        var facts = Facts(focus: sofa, warranty: 24);

        Assert.Contains("từ 16.900.000₫ đến 18.900.000₫", Ask("giá bao nhiêu", facts, focus: true).Text);
        Assert.Contains("2100 x 850 x 800 mm", Ask("kích thước thế nào", facts, focus: true).Text);
        Assert.Contains("Kem be, Xanh rêu", Ask("có màu nào khác", facts, focus: true).Text);
        Assert.Contains("còn hàng các phiên bản: Kem be / 2m1", Ask("còn hàng không", facts, focus: true).Text);
        Assert.StartsWith("Sofa Oslo được bảo hành 24 tháng.", Ask("bảo hành bao lâu", facts, focus: true).Text);
        Assert.Contains("miễn phí giao", Ask("phí ship bao nhiêu", facts, focus: true).Text); // 16.9 million is above the free-shipping threshold
    }

    [Fact]
    public void Unknown_SaysSo_AndPointsToAPerson()
    {
        var reply = LocalAssistant.Unknown(Store);

        Assert.Contains("chưa hiểu", reply.Text);
        Assert.Contains("1900 0000", reply.Text);
    }
}
