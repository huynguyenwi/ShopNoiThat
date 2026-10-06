using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FurnitureStore.Application.Common.Utilities;
using FurnitureStore.Application.Engagement;
using FurnitureStore.Application.Sales;
using FurnitureStore.Domain.Entities;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Application.AI;

public enum LocalTopic
{
    Greeting, Thanks, Goodbye, Identity, Help,
    OpeningHours, Address, Contact,
    Shipping, Warranty, Returns, Payment, CustomOrder,
    HowToOrder, OrderStatus, CancelOrder, Coupons, Account,
    Care, Material, Style, SizeGuide,
    FocusPrice, FocusSize, FocusColor, FocusMaterial, FocusStock, FocusCheaper
}

/// <summary>What the question is about; <see cref="Score"/> grows with the number / length of the phrases found.</summary>
/// <param name="AlsoAsked">Other policy topics asked in the same question ("giao hàng và bảo hành thế nào?").</param>
public sealed record LocalMatch(LocalTopic Topic, int Score, IReadOnlyList<LocalAssistant.MaterialInfo> Materials,
    AdviceKnowledge.StyleProfile? Style, string? Furniture, string Words, IReadOnlyList<LocalTopic>? AlsoAsked = null)
{
    /// <summary>Whether the (accent-free) question contains one of the phrases as whole words.</summary>
    public bool Mentions(params string[] phrases) => phrases.Any(p => Words.Contains(" " + p + " ", StringComparison.Ordinal));
}

/// <summary>Live data the answers are built from (loaded by <see cref="AssistantService"/> only when the topic needs it).</summary>
public sealed record LocalFacts(
    StoreInfoDto Store,
    IReadOnlyList<AIKnowledgeEntry> Knowledge,
    IReadOnlyList<Coupon> Coupons,
    IReadOnlyList<OrderListItemDto>? MyOrders,
    ProductFact? Focus,
    int? FocusWarrantyMonths,
    IReadOnlyList<ProductFact> Cheaper);

public sealed record LocalReply(string Text, IReadOnlyList<string> Suggestions);

/// <summary>
/// The assistant's built-in answers for everyday questions, used when no AI model is configured (or it is down):
/// greetings, opening hours, address, hotline, delivery, warranty, returns, payment, how to order, order status,
/// cancelling, coupons, account help, materials, styles, sizes, care, and questions about the product being viewed.
/// Runs entirely on the server with no external service. Matching is accent-insensitive on whole words ("hong" never
/// matches inside "khong"); answers only use store data (store information, running coupons, the
/// customer's own orders, the admin-editable knowledge base) plus general furniture know-how - never invented prices.
/// </summary>
public static partial class LocalAssistant
{
    /// <summary>Below this, a question that also names products is treated as a product search.</summary>
    public const int StrongScore = 2;

    private static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");

    private sealed record Trigger(string Phrase, int Weight);

    private static Trigger[] T(params string[] phrases) =>
        phrases.Select(p => p.Split('|') is [var phrase, var weight] ? new Trigger(phrase, int.Parse(weight, CultureInfo.InvariantCulture))
            : new Trigger(p, p.Split(' ').Length)).ToArray();

    // Plain (accent-free, lower-case) phrases. Weight = number of words unless given as "phrase|weight".
    private static readonly (LocalTopic Topic, Trigger[] Triggers)[] Topics =
    [
        (LocalTopic.OpeningHours, T("may gio", "gio mo cua", "mo cua", "dong cua", "gio lam viec", "lam viec luc nao", "mo den may gio",
            "chu nhat|1", "ngay le|1", "thu bay|1", "buoi toi|1")),
        (LocalTopic.Address, T("dia chi", "o dau", "showroom|2", "cua hang o", "chi nhanh", "ban do", "den xem", "xem truc tiep", "xem hang truc tiep",
            "xuong san xuat", "xuong o", "duong nao", "toi cua hang")),
        (LocalTopic.Contact, T("hotline|2", "so dien thoai", "sdt|2", "lien he", "goi dien", "zalo|2", "facebook|2", "fanpage|2", "email|2",
            "gap nhan vien", "noi chuyen voi nhan vien", "tu van vien", "nhan vien tu van")),
        (LocalTopic.Shipping, T("giao hang", "van chuyen", "ship|1", "phi ship", "phi giao", "phi van chuyen", "lap dat", "giao tan noi",
            "giao tinh", "giao ra", "freeship|2", "mien phi giao", "bao lau|1", "may ngay|1")),
        (LocalTopic.Warranty, T("bao hanh", "sua chua", "bi hong", "bi loi", "hong hoc", "bao tri")),
        (LocalTopic.Returns, T("doi tra", "tra hang", "hoan tien", "doi hang", "doi san pham", "tra lai hang", "doi mau khac")),
        (LocalTopic.Payment, T("thanh toan", "chuyen khoan", "cod|2", "tra tien", "tra gop", "dat coc", "coc truoc", "the tin dung",
            "visa|2", "momo|2", "vnpay|2", "vi dien tu", "tien mat")),
        (LocalTopic.CustomOrder, T("dat dong", "theo yeu cau", "kich thuoc rieng", "thiet ke rieng", "lam theo mau", "dong theo", "custom|2")),
        (LocalTopic.HowToOrder, T("dat hang", "mua hang", "cach mua", "dat mua", "order|1", "mua nhu the nao", "dat nhu the nao", "mua online",
            "dat online", "mua o dau")),
        (LocalTopic.OrderStatus, T("don hang", "don dat hang|3", "don cua toi", "don cua minh", "kiem tra don", "theo doi don", "tra cuu don", "trang thai don",
            "ma don", "giao chua|1", "den dau roi", "bao gio nhan", "khi nao nhan")),
        (LocalTopic.CancelOrder, T("huy don", "huy dat hang", "huy don hang|4", "khong muon mua nua", "doi y|1")),
        (LocalTopic.Coupons, T("ma giam gia|4", "ma khuyen mai|4", "voucher|3", "coupon|3", "ma uu dai|4", "giam gia", "khuyen mai", "uu dai",
            "sale|1", "co ma|1")),
        (LocalTopic.Account, T("quen mat khau|4", "doi mat khau|4", "dang ky", "tao tai khoan", "dang nhap", "tai khoan", "mat khau")),
        (LocalTopic.Care, T("bao quan", "ve sinh", "lau chui", "cham soc", "moi mot", "tray xuoc", "nam moc", "bi am", "giat vo", "lau bang gi",
            "bi bam bui")),
        (LocalTopic.Help, T("ban giup duoc gi|4", "giup duoc gi", "lam duoc gi", "ho tro gi", "co the hoi gi", "hoi duoc gi")),
        (LocalTopic.Identity, T("ban la ai|4", "ban la nguoi", "la nguoi hay", "la bot|3", "la robot|3", "la may|2", "ai dang tra loi", "ban ten gi",
            "nguoi that")),
        (LocalTopic.Greeting, T("xin chao|1", "chao|1", "hello|1", "hi|1", "alo|1", "chao shop|1", "chao ban|1")),
        (LocalTopic.Thanks, T("cam on|1", "thanks|1", "thank you|1", "ok cam on|1", "tuyet voi|1")),
        (LocalTopic.Goodbye, T("tam biet|1", "bye|1", "hen gap lai|1", "chao nhe|1"))
    ];

    private static readonly LocalTopic[] SmallTalk = [LocalTopic.Greeting, LocalTopic.Thanks, LocalTopic.Goodbye];

    private static readonly LocalTopic[] PolicyTopics = [LocalTopic.Shipping, LocalTopic.Warranty, LocalTopic.Returns, LocalTopic.Payment];

    // Questions about the product being viewed (only when there is one).
    private static readonly (LocalTopic Topic, Trigger[] Triggers)[] FocusTopics =
    [
        (LocalTopic.FocusCheaper, T("re hon|3", "gia thap hon", "tiet kiem hon", "mau khac re", "binh dan hon", "re nhat|3")),
        (LocalTopic.FocusPrice, T("gia ban", "gia bao nhieu", "bao nhieu tien", "gia the nao", "gia sao", "gia tien", "muc gia", "bao nhieu|1")),
        (LocalTopic.FocusSize, T("kich thuoc", "dai bao nhieu", "rong bao nhieu", "cao bao nhieu", "to khong", "bao to", "size|1", "may cho")),
        (LocalTopic.FocusColor, T("mau gi", "mau nao", "mau khac", "mau sac", "may mau", "nhung mau")),
        (LocalTopic.FocusMaterial, T("chat lieu", "lam bang", "go gi", "vat lieu", "lam tu", "khung gi")),
        (LocalTopic.FocusStock, T("con hang", "het hang", "co san", "con khong", "con mau"))
    ];

    private static readonly string[] SizeCues =
    [
        "kich thuoc bao nhieu", "kich thuoc nao", "kich thuoc the nao", "kich thuoc chuan", "kich thuoc phu hop", "can kich thuoc",
        "chon kich thuoc", "chon size", "size nao", "bao nhieu la vua", "bao nhieu met la", "bao nhieu cm la", "dai bao nhieu la",
        "cao bao nhieu la", "rong bao nhieu la", "nen chon loai", "nen chon co", "chieu cao chuan", "co nao la vua"
    ];

    private static readonly string[] QuestionCues =
    [
        "la gi", "co tot", "tot khong", "ben khong", "co ben", "nen chon", "khac gi", "khac nhau", "so sanh", "loai nao", "uu diem",
        "nhuoc diem", "hay hon", "tot hon", "co bi", "nhu the nao", "the nao", "dac diem", "hop voi", "phu hop", "nen mua", "co nen"
    ];

    // ------------------------------------------------------------------ general furniture know-how

    public sealed record MaterialInfo(string Slug, string Name, string[] Aliases, string About, string Pros, string Cons, string Care);

    public static readonly IReadOnlyList<MaterialInfo> Materials =
    [
        new("go-soi-nga", "Gỗ sồi Nga", ["go soi nga", "soi nga", "russian oak"],
            "gỗ sồi tự nhiên nhập khẩu từ Nga, vân rõ và đều, màu vàng nâu nhạt; nhuộm màu óc chó lên rất đẹp.",
            "cứng chắc, chịu lực tốt, ít cong vênh khi đã sấy đạt chuẩn; giá mềm hơn óc chó và sồi Mỹ - chất liệu chính cho bàn ghế ăn gia đình.",
            "nặng; cần sơn phủ kỹ để chống ẩm ở mặt bàn ăn.",
            "lau khăn ẩm vắt kỹ rồi lau khô; dùng lót nồi, lót cốc; tránh để nước canh, nước chấm đọng lâu trên mặt bàn."),
        new("go-oc-cho", "Gỗ óc chó", ["go oc cho", "oc cho", "walnut"],
            "gỗ tự nhiên cao cấp, vân đẹp, màu nâu sô-cô-la trầm ấm.",
            "rất bền, ít cong vênh, càng dùng càng đẹp, hợp phong cách sang trọng / cổ điển.",
            "giá cao nhất trong các loại gỗ; màu tối nên phòng nhỏ dễ bị nặng.",
            "lau khăn mềm hơi ẩm rồi lau khô; 6 - 12 tháng đánh lại một lớp dầu / sáp gỗ; tránh nắng gắt chiếu trực tiếp."),
        new("go-soi", "Gỗ sồi", ["go soi", "oak"],
            "gỗ tự nhiên nhập khẩu, vân thẳng, màu sáng.",
            "cứng chắc, chịu lực tốt, ít cong vênh, giá hợp lý so với óc chó; hợp Bắc Âu, tối giản, hiện đại.",
            "nặng; gỗ sáng màu dễ lộ vết bẩn hơn gỗ tối.",
            "lau khăn ẩm vắt kỹ rồi lau khô; dùng lót cốc, tránh để nước đọng lâu trên mặt gỗ."),
        new("go-tan-bi", "Gỗ tần bì", ["go tan bi", "tan bi", "ash"],
            "gỗ tự nhiên màu sáng, vân rõ, dẻo dai.",
            "uốn cong tốt nên hay dùng cho ghế; nhẹ hơn sồi, giá mềm hơn.",
            "độ cứng thấp hơn sồi một chút, cần tránh va đập mạnh.",
            "lau khăn ẩm, tránh hóa chất tẩy mạnh."),
        new("go-cao-su", "Gỗ cao su", ["go cao su"],
            "gỗ tự nhiên trồng rừng, đã sấy và xử lý chống mối mọt, thường ghép thanh.",
            "giá tốt, thân thiện môi trường, đủ bền cho gia đình.",
            "vân không nổi bật bằng sồi / óc chó; kém chịu nước hơn gỗ cứng.",
            "lau khô ngay khi đổ nước; tránh để nơi ẩm thấp."),
        new("go-thong", "Gỗ thông", ["go thong", "pine"],
            "gỗ tự nhiên nhẹ, vân và mắt gỗ đặc trưng, mùi thơm nhẹ.",
            "nhẹ, giá mềm nhất, hợp phong cách mộc mạc / Bắc Âu.",
            "mềm nên dễ trầy, móp hơn gỗ cứng.",
            "dùng lót cốc, lót chân ghế; lau khô, tránh ẩm."),
        new("mdf-chong-am", "MDF chống ẩm", ["mdf", "go cong nghiep", "van mdf", "mdf chong am", "melamine"],
            "ván gỗ công nghiệp lõi xanh chống ẩm, phủ melamine chống trầy.",
            "giá tốt, bề mặt phẳng đẹp, nhiều màu, ít cong vênh; hợp tủ kệ, bàn làm việc.",
            "chịu nước kém hơn gỗ tự nhiên nếu bị ngâm lâu; khó sửa khi mẻ cạnh.",
            "lau khăn ẩm vắt kỹ; không để nước đọng ở mép ván."),
        new("vai-bo", "Vải bố", ["vai bo", "linen", "vai lanh"],
            "vải dệt dày pha linen, thoáng khí.",
            "mát, ít bám bụi, cảm giác tự nhiên; nhiều mẫu có vỏ tháo giặt.",
            "dễ nhăn nhẹ; vết bẩn đậm cần xử lý sớm.",
            "hút bụi 1 - 2 lần/tuần; thấm vết bẩn bằng khăn ẩm, không chà mạnh; vỏ tháo rời nên giặt khô."),
        new("vai-nhung", "Vải nhung", ["vai nhung", "sofa nhung", "ghe nhung", "velvet"],
            "vải mặt lông mịn, màu sắc sâu và sang.",
            "êm, ấm, màu đẹp; hợp phong cách cổ điển / sang trọng.",
            "dễ hằn vết ngồi, bám lông thú cưng.",
            "chải nhẹ xuôi chiều lông; hút bụi đầu mềm; tránh nắng để không bạc màu."),
        new("da-that", "Da bò thật", ["da that", "da bo", "sofa da", "ghe da", "leather"],
            "da bò thật, mặt da mềm, có vân tự nhiên.",
            "rất bền, càng dùng càng lên màu đẹp, dễ lau.",
            "giá cao; cần dưỡng định kỳ, tránh vật sắc nhọn.",
            "lau khăn ẩm vắt kỹ; dưỡng da 3 - 6 tháng/lần; tránh nắng và máy sưởi."),
        new("da-pu", "Da PU", ["da pu", "gia da", "simili", "da cong nghiep"],
            "da tổng hợp cao cấp.",
            "giá mềm, chống thấm, rất dễ vệ sinh, nhiều màu.",
            "kém thoáng hơn da thật; tuổi thọ ngắn hơn da thật.",
            "lau khăn ẩm, không dùng cồn hoặc chất tẩy mạnh."),
        new("thep-son-tinh-dien", "Thép sơn tĩnh điện", ["thep", "khung sat", "chan sat", "kim loai", "son tinh dien"],
            "thép hộp sơn tĩnh điện chống gỉ.",
            "chắc, chịu lực tốt, bền màu; hợp phong cách công nghiệp, hiện đại.",
            "lạnh, nặng; va đập mạnh có thể tróc sơn.",
            "lau khăn khô; nếu ẩm thì lau khô ngay để tránh gỉ ở vết xước."),
        new("da-marble", "Đá marble", ["da marble", "marble", "da cam thach", "mat da tu nhien"],
            "đá tự nhiên, vân mây độc bản.",
            "rất sang, mát, mỗi mặt đá là duy nhất.",
            "nặng; dễ ố với chất có axit (chanh, giấm, rượu vang) nếu không phủ chống thấm.",
            "dùng lót cốc, lau ngay khi đổ nước màu hoặc axit; phủ chống thấm định kỳ."),
        new("da-ceramic", "Đá ceramic", ["da ceramic", "ceramic", "mat da ceramic", "sintered"],
            "đá nung kết nhân tạo.",
            "chịu nhiệt, chống trầy, chống thấm, gần như không ố; rất hợp mặt bàn ăn.",
            "giá cao hơn gỗ thường; cạnh mỏng cần tránh va đập mạnh.",
            "lau khăn ẩm với nước rửa chén là đủ."),
        new("kinh-cuong-luc", "Kính cường lực", ["kinh cuong luc", "mat kinh", "ban kinh"],
            "kính tôi cứng, dày 5 - 8 mm.",
            "sáng, làm phòng thoáng hơn; an toàn hơn kính thường.",
            "lộ vân tay, cần lau thường xuyên.",
            "lau bằng nước lau kính và khăn mềm."),
        new("may-tre", "Mây tre đan", ["may tre", "ghe may", "rattan", "tre dan"],
            "mây tự nhiên đan thủ công.",
            "nhẹ, thoáng, gần gũi thiên nhiên; hợp mộc mạc, Japandi.",
            "sợi mây có thể xù khi dùng lâu; không hợp nơi quá ẩm.",
            "phủi bụi bằng cọ mềm; tránh nắng gắt và ẩm cao.")
    ];

    // ------------------------------------------------------------------ classification

    /// <summary>The best matching topic, or null when the question is not one the built-in answers cover.</summary>
    public static LocalMatch? Classify(string message, bool hasFocusProduct)
    {
        var plain = Words(message);
        // Aliases are specific phrases: once accents are gone, single words collide ("nhung" = nhung / nhưng, "da" = da / đá).
        var materials = Materials.Where(m => m.Aliases.Any(a => Has(plain, a))).ToList();
        // "gỗ sồi Nga" also contains "gỗ sồi": plain oak only counts when it is named on its own too.
        if (materials.Any(m => m.Slug == "go-soi-nga") && !Has(plain.Replace(" soi nga ", " "), "soi"))
        {
            materials.RemoveAll(m => m.Slug == "go-soi");
        }
        var style = AdviceKnowledge.Styles.FirstOrDefault(s => Has(plain, Plain(s.Name)) || Has(plain, s.Slug.Replace('-', ' ')));
        var furniture = FurnitureWord(plain);
        var cues = QuestionCues.Count(c => Has(plain, c));

        var scores = new List<(LocalTopic Topic, int Score)>();
        foreach (var (topic, triggers) in hasFocusProduct ? FocusTopics.Concat(Topics) : Topics)
        {
            var score = triggers.Where(t => Has(plain, t.Phrase)).Sum(t => t.Weight);
            if (score > 0) scores.Add((topic, score));
        }

        // Knowledge questions: a material / style / size named together with a question ("gỗ sồi có bền không?").
        if (materials.Count > 0 && cues > 0) scores.Add((LocalTopic.Material, 2 + cues + materials.Count));
        if (style is not null && cues > 0) scores.Add((LocalTopic.Style, 2 + cues));
        // Only questions about sizes: "sofa 2m4 màu xám" is a product search, "sofa bao nhiêu mét là vừa?" is advice,
        // and so is choosing between sizes ("nên chọn giường 1m6 hay 1m8?").
        var choosingSize = (Has(plain, "nen chon") || Has(plain, "hay")) && Dimension().IsMatch(plain);
        if (furniture is not null && (SizeCues.Any(c => Has(plain, c)) || choosingSize))
        {
            scores.Add((LocalTopic.SizeGuide, 3));
        }

        // Without accents "mẫu nào" (which model) and "màu nào" (which colour) are the same words: trust the accents when typed.
        var lower = message.ToLowerInvariant();
        if (lower.Contains("mẫu", StringComparison.Ordinal) && !lower.Contains("màu", StringComparison.Ordinal))
        {
            scores.RemoveAll(s => s.Topic == LocalTopic.FocusColor);
        }

        if (scores.Count == 0) return null;

        // Small talk only when nothing else was asked ("chào shop, phí ship bao nhiêu?" is a shipping question).
        var serious = scores.Where(s => !SmallTalk.Contains(s.Topic)).ToList();
        if (serious.Count == 0 && plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 8) return null;
        var pool = serious.Count > 0 ? serious : scores;

        // Highest score wins; on a tie the general topic (declared first) beats the product-page one.
        var best = pool.OrderByDescending(s => s.Score).ThenBy(s => (int)s.Topic).First();
        if (best.Topic == LocalTopic.Material && pool.Any(s => s.Topic == LocalTopic.Care))
        {
            best = (LocalTopic.Care, best.Score); // "bảo quản gỗ óc chó thế nào?" asks for care, not a material comparison
        }

        var also = PolicyTopics.Contains(best.Topic)
            ? pool.Where(s => s.Topic != best.Topic && s.Score >= StrongScore && PolicyTopics.Contains(s.Topic)).Select(s => s.Topic).ToList()
            : [];
        return new LocalMatch(best.Topic, best.Score, materials, style, furniture, plain, also);
    }

    // ------------------------------------------------------------------ answers

    public static LocalReply Answer(LocalMatch match, LocalFacts facts)
    {
        var reply = AnswerOne(match, facts);
        if (match.AlsoAsked is not { Count: > 0 } also)
        {
            return reply;
        }

        var more = also.Select(topic => AnswerOne(match with { Topic = topic, AlsoAsked = null }, facts).Text);
        return reply with { Text = string.Join("\n\n", more.Prepend(reply.Text)) };
    }

    private static LocalReply AnswerOne(LocalMatch match, LocalFacts facts)
    {
        var store = facts.Store;
        var hotline = string.IsNullOrWhiteSpace(store.Hotline) ? null : store.Hotline;
        var contactLine = hotline is null ? "chat với nhân viên" : $"gọi hotline {hotline} hoặc chat với nhân viên";

        switch (match.Topic)
        {
            case LocalTopic.Greeting:
                return new($"Chào bạn! Mình là trợ lý tự động của {store.Name}. Mình có thể tìm sản phẩm theo phòng và ngân sách, "
                    + "báo phí giao hàng, bảo hành, đổi trả, mã giảm giá, tình trạng đơn hàng... Bạn cần gì cứ hỏi nhé!",
                    ["Sofa cho phòng khách 20m²", "Phí giao hàng bao nhiêu?", "Có mã giảm giá không?"]);

            case LocalTopic.Thanks:
                return new("Không có gì ạ! Bạn cần tư vấn thêm món nào cứ nhắn mình nhé.", ["Gợi ý sản phẩm bán chạy", "Có mã giảm giá không?", "Chat với nhân viên"]);

            case LocalTopic.Goodbye:
                return new($"Cảm ơn bạn đã ghé {store.Name}. Hẹn gặp lại bạn!", ["Xem sản phẩm khuyến mãi", "Giờ mở cửa?", "Địa chỉ showroom?"]);

            case LocalTopic.Identity:
                return new($"Mình là trợ lý tự động của {store.Name} (không phải người thật). Mình trả lời các câu hỏi thường gặp và tìm sản phẩm "
                    + $"từ dữ liệu thật của cửa hàng. Cần tư vấn chi tiết hơn, bạn {contactLine} nhé.",
                    ["Chat với nhân viên", "Bạn giúp được gì?", "Giờ mở cửa?"]);

            case LocalTopic.Help:
                return new("Bạn có thể hỏi mình:\n• Tìm sản phẩm: \"bàn ăn 6 người khoảng 10 triệu\", \"sofa cho phòng 20m²\"\n"
                    + "• Chính sách: giao hàng, bảo hành, đổi trả, thanh toán\n• Mã giảm giá đang có, tình trạng đơn hàng của bạn\n"
                    + "• Kiến thức: gỗ sồi hay óc chó, cách bảo quản, chọn kích thước, phong cách nội thất\n• Giờ mở cửa, địa chỉ, hotline",
                    ["Gỗ sồi và óc chó khác gì?", "Có mã giảm giá không?", "Bàn ăn 6 người cần kích thước bao nhiêu?"]);

            case LocalTopic.OpeningHours:
                return new(string.IsNullOrWhiteSpace(store.OpeningHours)
                        ? $"Bạn vui lòng {contactLine} để biết giờ mở cửa hôm nay nhé. Website nhận đặt hàng 24/7."
                        : $"{store.Name} mở cửa {store.OpeningHours}. Website nhận đặt hàng 24/7; nhân viên xác nhận đơn trong giờ làm việc.",
                    ["Địa chỉ showroom ở đâu?", "Hotline là số nào?", "Phí giao hàng bao nhiêu?"]);

            case LocalTopic.Address:
            {
                var text = new StringBuilder($"Showroom {store.Name}: {store.Address}.");
                if (!string.IsNullOrWhiteSpace(store.WorkshopAddress)) text.Append($"\nXưởng sản xuất: {store.WorkshopAddress}.");
                if (!string.IsNullOrWhiteSpace(store.OpeningHours)) text.Append($"\nGiờ mở cửa: {store.OpeningHours}.");
                text.Append("\nBản đồ chỉ đường có ở trang Liên hệ (/contact).");
                return new(text.ToString(), ["Giờ mở cửa?", "Hotline là số nào?", "Có giao hàng tận nơi không?"]);
            }

            case LocalTopic.Contact:
            {
                var lines = new List<string>();
                if (hotline is not null) lines.Add($"• Hotline: {hotline}");
                if (!string.IsNullOrWhiteSpace(store.Email)) lines.Add($"• Email: {store.Email}");
                if (!string.IsNullOrWhiteSpace(store.ZaloUrl)) lines.Add($"• Zalo: {store.ZaloUrl}");
                if (!string.IsNullOrWhiteSpace(store.FacebookUrl)) lines.Add($"• Facebook: {store.FacebookUrl}");
                lines.Add("• Hoặc bấm \"Chat với tư vấn viên\" để nhắn trực tiếp với nhân viên.");
                return new($"Bạn liên hệ {store.Name} qua:\n{string.Join("\n", lines)}", ["Giờ mở cửa?", "Địa chỉ showroom?", "Chat với nhân viên"]);
            }

            case LocalTopic.Shipping:
            {
                var policy = Knowledge(facts, "giao hang", "van chuyen", "lap dat")
                             ?? "Phí giao hàng và lắp đặt không tính sẵn trên website: sau khi bạn gửi yêu cầu đặt hàng, cửa hàng gọi lại "
                             + "và báo phí theo địa chỉ, số món và tầng lầu. Thời gian giao cũng được báo khi xác nhận đơn.";
                if (facts.Focus is { } p)
                {
                    policy += $"\nVới {p.Name}, nhân viên báo phí giao và lắp đặt khi gọi xác nhận đơn.";
                }

                return new(policy, ["Bảo hành bao lâu?", "Thanh toán thế nào?", "Có mã giảm giá không?"]);
            }

            case LocalTopic.Warranty:
            {
                var policy = Knowledge(facts, "bao hanh")
                             ?? $"Thời gian bảo hành ghi trong mục Thông số của từng sản phẩm; bảo hành khung, kết cấu và lỗi sản xuất. Cần hỗ trợ bạn {contactLine}.";
                if (facts.Focus is { } p && facts.FocusWarrantyMonths is > 0)
                {
                    policy = $"{p.Name} được bảo hành {facts.FocusWarrantyMonths} tháng.\n" + policy;
                }

                return new(policy, ["Chính sách đổi trả?", "Cách bảo quản đồ gỗ?", "Chat với nhân viên"]);
            }

            case LocalTopic.Returns:
                return new(Knowledge(facts, "doi tra", "tra hang", "hoan tien")
                           ?? $"Sản phẩm lỗi do sản xuất hoặc giao sai mẫu được hỗ trợ đổi. Bạn {contactLine} kèm mã đơn hàng để được xử lý nhanh.",
                    ["Bảo hành bao lâu?", "Hủy đơn hàng thế nào?", "Chat với nhân viên"]);

            case LocalTopic.Payment:
            {
                var text = Knowledge(facts, "thanh toan", "chuyen khoan", "cod")
                           ?? "Website nhận yêu cầu đặt hàng, không thanh toán trực tuyến. Sau khi cửa hàng liên hệ xác nhận, bạn thanh toán "
                           + "khi nhận hàng (COD) hoặc chuyển khoản theo hướng dẫn của nhân viên.";
                // Asked about something the store does not offer online: say so (unless the admin's own text already covers it).
                if (match.Mentions("tra gop", "vi dien tu", "momo", "vnpay", "the tin dung", "visa") && !Plain(text).Contains("tra gop", StringComparison.Ordinal))
                {
                    text += "\nHiện website chưa hỗ trợ trả góp hay ví điện tử (VNPay, MoMo); cửa hàng không yêu cầu nhập thông tin thẻ ngân hàng.";
                }

                return new(text, ["Phí giao hàng bao nhiêu?", "Cách đặt hàng?", "Có mã giảm giá không?"]);
            }

            case LocalTopic.CustomOrder:
                return new(Knowledge(facts, "dat dong", "theo yeu cau")
                           ?? "Xưởng nhận đóng nội thất theo kích thước, chất liệu và màu bạn chọn. Bạn nhận báo giá dự kiến ở trang Báo giá (/bao-gia); "
                           + "giá cuối cùng do cửa hàng xác nhận.",
                    ["Báo giá bàn gỗ óc chó 1m8", "Gỗ sồi và óc chó khác gì?", "Chat với nhân viên"]);

            case LocalTopic.HowToOrder:
                return new("Đặt hàng trên website chỉ vài bước:\n1. Chọn sản phẩm, chọn màu / kích thước rồi bấm \"Thêm vào giỏ hàng\".\n"
                    + "2. Mở giỏ hàng, nhập mã giảm giá nếu có.\n3. Bấm \"Liên hệ đặt hàng\", đăng nhập (hoặc đăng ký nhanh), điền số điện thoại, "
                    + "địa chỉ nhận hàng rồi bấm \"Gửi yêu cầu\".\n"
                    + "4. Nhân viên gọi lại xác nhận mẫu, giá, phí giao và lắp đặt; bạn thanh toán khi nhận hàng hoặc chuyển khoản theo hướng dẫn.\n"
                    + "Bạn nhận email xác nhận kèm mã QR đơn hàng.",
                    ["Phí giao hàng bao nhiêu?", "Có mã giảm giá không?", "Kiểm tra đơn hàng ở đâu?"]);

            case LocalTopic.OrderStatus:
            {
                if (facts.MyOrders is null)
                {
                    return new("Bạn đăng nhập rồi vào \"Đơn hàng của tôi\" (/account/orders) để xem trạng thái đơn. Email xác nhận đơn cũng có mã QR - "
                        + $"quét là mở ngay đơn hàng. Cần hỗ trợ gấp, bạn {contactLine} kèm mã đơn nhé.",
                        ["Hủy đơn hàng thế nào?", "Phí giao hàng bao nhiêu?", "Chat với nhân viên"]);
                }

                if (facts.MyOrders.Count == 0)
                {
                    return new("Tài khoản của bạn chưa có đơn hàng nào. Bạn cần mình gợi ý sản phẩm không?", ["Gợi ý sản phẩm bán chạy", "Có mã giảm giá không?", "Cách đặt hàng?"]);
                }

                var lines = facts.MyOrders.Take(3).Select(o =>
                    $"• {o.OrderCode} ({VietnamTime.ToLocal(o.PlacedAt).ToString("dd/MM/yyyy", Vietnamese)}): {OrderStatusTransitions.DisplayName(o.Status)} - {Money(o.TotalAmount)}");
                return new($"Đơn hàng gần đây của bạn:\n{string.Join("\n", lines)}\nXem chi tiết ở \"Đơn hàng của tôi\" (/account/orders).",
                    ["Hủy đơn hàng thế nào?", "Bao lâu thì nhận được hàng?", "Chat với nhân viên"]);
            }

            case LocalTopic.CancelOrder:
                return new("Bạn tự hủy được khi đơn còn \"Chờ xác nhận\" hoặc \"Đã xác nhận\": vào \"Đơn hàng của tôi\" (/account/orders), mở đơn và bấm \"Hủy đơn\" "
                    + $"(ghi lý do). Đơn đã chuyển sang xử lý / đang giao thì bạn {contactLine} để được hỗ trợ.",
                    ["Kiểm tra đơn hàng", "Chính sách đổi trả?", "Chat với nhân viên"]);

            case LocalTopic.Coupons:
            {
                if (facts.Coupons.Count == 0)
                {
                    return new("Hiện chưa có mã giảm giá công khai. Bạn xem các sản phẩm đang giảm giá ở mục Khuyến mãi (/products?onSale=true) nhé.",
                        ["Sản phẩm đang khuyến mãi", "Phí giao hàng bao nhiêu?", "Cách đặt hàng?"]);
                }

                var lines = facts.Coupons.Take(4).Select(c =>
                    $"• {c.Code}: {CouponText.Benefit(c.DiscountType, c.DiscountValue, c.MaxDiscountAmount)} ({CouponText.Conditions(c.MinOrderAmount, c.UsageLimitPerUser, c.EndsAt)})");
                return new($"Mã giảm giá đang áp dụng:\n{string.Join("\n", lines)}\nNhập mã ở giỏ hàng hoặc trang liên hệ đặt hàng; "
                    + "sản phẩm đang giảm giá xem ở mục Khuyến mãi (/products?onSale=true).",
                    ["Cách đặt hàng?", "Phí giao hàng bao nhiêu?", "Bộ bàn ăn 6 ghế"]);
            }

            case LocalTopic.Account:
                return new("• Đăng ký: /account/register (email, số điện thoại, mật khẩu).\n• Quên mật khẩu: /account/forgotpassword - hệ thống gửi link đặt lại qua email.\n"
                    + "• Đổi mật khẩu / thông tin: vào Tài khoản, chọn \"Đổi mật khẩu\" hoặc \"Hồ sơ\".\n"
                    + "Nhập sai mật khẩu 5 lần tài khoản bị khóa tạm 15 phút để bảo vệ bạn.",
                    ["Cách đặt hàng?", "Kiểm tra đơn hàng", "Chat với nhân viên"]);

            case LocalTopic.Care:
            {
                if (match.Materials.Count > 0)
                {
                    return new(string.Join("\n", match.Materials.Take(3).Select(m => $"• {m.Name}: {m.Care}")),
                        ["Bảo hành bao lâu?", "Gỗ sồi và óc chó khác gì?", "Chat với nhân viên"]);
                }

                return new(Knowledge(facts, "bao quan", "ve sinh")
                           ?? "Lau đồ gỗ bằng khăn mềm hơi ẩm rồi lau khô; tránh hóa chất tẩy mạnh, nguồn nhiệt và nắng gắt; dùng lót cốc cho mặt bàn; "
                           + "đồ bọc vải nên hút bụi định kỳ.",
                    ["Bảo quản sofa da thế nào?", "Bảo quản mặt đá marble?", "Bảo hành bao lâu?"]);
            }

            case LocalTopic.Material:
            {
                var picked = match.Materials.Take(3).ToList();
                var text = string.Join("\n\n", picked.Select(m => $"{m.Name}: {m.About}\n• Ưu điểm: {m.Pros}\n• Lưu ý: {m.Cons}"));
                if (picked.Count >= 2)
                {
                    text += "\n\nChọn theo ngân sách và phong cách: gỗ tự nhiên cao cấp (óc chó, sồi) bền và sang hơn; gỗ cao su, thông, MDF tiết kiệm hơn.";
                }

                return new(text, [$"Sản phẩm {picked[0].Name.ToLowerInvariant()}", $"Cách bảo quản {picked[0].Name.ToLowerInvariant()}", "Chat với nhân viên"]);
            }

            case LocalTopic.Style when match.Style is { } s:
                return new($"Phong cách {s.Name}: {s.Description}\n{string.Join("\n", s.KeyPoints.Select(k => "• " + k))}\nHợp với: {string.Join(", ", s.GoodFor)}.",
                    [$"Sản phẩm phong cách {s.Name.ToLowerInvariant()}", "Tường trắng nên chọn màu gì?", "Phong cách nào hợp phòng nhỏ?"]);

            case LocalTopic.SizeGuide:
                return new(SizeGuide(match.Furniture), ["Sofa cho phòng khách 20m²", "Bàn ăn 6 người", "Nhận báo giá đặt đóng"]);

            case LocalTopic.FocusPrice when facts.Focus is { } p:
            {
                var byVariant = p.Variants.Where(v => v.Price > 0).OrderBy(v => v.Price).Take(4)
                    .Select(v => $"• {v.Name}: {Money(v.Price)}{(v.InStock ? "" : " (tạm hết)")}").ToList();
                var text = p.MaxPrice > p.Price ? $"{p.Name} có giá từ {Money(p.Price)} đến {Money(p.MaxPrice)} tùy phiên bản:" : $"{p.Name} có giá {Money(p.Price)}.";
                if (byVariant.Count > 1) text += "\n" + string.Join("\n", byVariant);
                if (p.OriginalPrice is decimal was && was > p.Price) text += $"\nĐang giảm giá so với {Money(was)}.";
                return new(text, ["Có mẫu nào rẻ hơn?", "Phí giao hàng bao nhiêu?", "Có mã giảm giá không?"]);
            }

            case LocalTopic.FocusSize when facts.Focus is { } p:
                return new(p.Sizes.Count == 0
                        ? $"{p.Name} có kích thước ghi trong mục Thông số kỹ thuật của trang sản phẩm. Cần kích thước khác, xưởng nhận đóng theo yêu cầu (/bao-gia)."
                        : $"{p.Name} có các kích thước:\n{string.Join("\n", p.Sizes.Select(s => $"• {s.Name}: {s.LengthMm} x {s.WidthMm} x {s.HeightMm} mm (dài x rộng x cao)"))}"
                          + "\nCần kích thước khác, xưởng nhận đóng theo yêu cầu (/bao-gia).",
                    ["Còn màu nào khác?", "Giá bao nhiêu?", "Phí giao hàng bao nhiêu?"]);

            case LocalTopic.FocusColor when facts.Focus is { } p:
                return new(p.Colors.Count == 0 ? $"{p.Name} hiện có một màu như trong ảnh." : $"{p.Name} có các màu: {string.Join(", ", p.Colors)}. Chọn màu ngay trên trang sản phẩm để xem ảnh và giá từng màu.",
                    ["Tường trắng nên chọn màu gì?", "Giá bao nhiêu?", "Còn hàng không?"]);

            case LocalTopic.FocusMaterial when facts.Focus is { } p:
            {
                var known = Materials.Where(m => p.MaterialSlugs.Contains(m.Slug)).Take(2).Select(m => $"• {m.Name}: {m.About} {Capitalize(m.Pros)}");
                return new($"{p.Name} làm từ: {string.Join(", ", p.Materials)}.\n{string.Join("\n", known)}",
                    ["Cách bảo quản?", "Bảo hành bao lâu?", "Có mẫu nào rẻ hơn?"]);
            }

            case LocalTopic.FocusStock when facts.Focus is { } p:
            {
                var available = p.Variants.Where(v => v.InStock).Select(v => v.Name).ToList();
                return new(!p.InStock
                        ? $"{p.Name} hiện tạm hết hàng. Bạn {contactLine} để được báo khi có hàng, hoặc xem mẫu tương tự bên dưới."
                        : available.Count > 0 && available.Count < p.Variants.Count
                            ? $"{p.Name} còn hàng các phiên bản: {string.Join(", ", available)}."
                            : $"{p.Name} hiện còn hàng. Đặt hôm nay, cửa hàng sẽ gọi xác nhận và hẹn lịch giao.",
                    ["Phí giao hàng bao nhiêu?", "Có mã giảm giá không?", "Có mẫu nào rẻ hơn?"]);
            }

            case LocalTopic.FocusCheaper when facts.Focus is { } p:
                return new(facts.Cheaper.Count == 0
                        ? $"Hiện chưa có mẫu cùng loại rẻ hơn {p.Name}. Bạn có thể xem thêm mã giảm giá hoặc đặt đóng theo ngân sách (/bao-gia)."
                        : $"Các mẫu {p.CategoryName.ToLowerInvariant()} có giá thấp hơn {Money(p.Price)}:",
                    ["Có mã giảm giá không?", "Phí giao hàng bao nhiêu?", "Chat với nhân viên"]);

            default:
                return Unknown(store);
        }
    }

    private static readonly string[] FollowUpCues =
    [
        "mau khac", "loai khac", "cai khac", "con mau", "con loai", "con cai", "xem them", "them mau", "them lua chon", "goi y them",
        "khac khong", "nua khong", "re hon", "dat hon", "lon hon", "nho hon", "dai hon", "ngan hon", "cao hon", "thap hon",
        "sang hon", "toi hon", "cai nao", "mau nao", "loai nao", "vay con", "the con"
    ];

    /// <summary>
    /// "còn mẫu khác không?", "cái nào rẻ hơn?": a message that only makes sense with the previous request, so the earlier
    /// product search continues. Anything else without a need of its own is not a reason to repeat that search.
    /// </summary>
    public static bool IsFollowUp(string message)
    {
        var plain = Words(message);
        return FollowUpCues.Any(c => Has(plain, c));
    }

    /// <summary>For questions nothing matched: say so, list what can be asked, point to a human.</summary>
    public static LocalReply Unknown(StoreInfoDto store) =>
        new("Mình chưa hiểu rõ câu hỏi này. Mình có thể tìm sản phẩm (ví dụ \"bàn ăn 6 người khoảng 10 triệu\"), trả lời về giao hàng, bảo hành, "
            + "đổi trả, thanh toán, mã giảm giá, đơn hàng, chất liệu và kích thước. "
            + (string.IsNullOrWhiteSpace(store.Hotline) ? "Hoặc bạn bấm \"Chat với tư vấn viên\" để hỏi nhân viên nhé." : $"Hoặc bạn gọi {store.Hotline} / bấm \"Chat với tư vấn viên\" để hỏi nhân viên nhé."),
            ["Bạn giúp được gì?", "Sofa cho phòng khách 20m²", "Chat với nhân viên"]);

    // ------------------------------------------------------------------ helpers

    private static string SizeGuide(string? furniture) => furniture switch
    {
        "sofa" => "Chọn sofa theo phòng: sofa nên dài khoảng 2/3 bức tường đặt nó.\n• Phòng dưới 15m²: sofa 2 chỗ 1m6 - 1m8\n• 15 - 25m²: sofa 3 chỗ 2m - 2m4\n"
                  + "• Trên 25m²: sofa góc chữ L 2m6 trở lên\nChừa lối đi ít nhất 60 - 80 cm và khoảng 40 - 45 cm giữa sofa với bàn trà.",
        "ban an" => "Kích thước bàn ăn theo số người (mỗi người cần khoảng 60 cm chiều ngang):\n• 2 - 4 người: 80 x 80 cm đến 1m2 x 80 cm\n• 6 người: 1m6 - 1m8 x 90 cm\n"
                    + "• 8 người: 2m - 2m2 x 1m\nChiều cao bàn chuẩn 75 cm; chừa khoảng 80 - 90 cm sau lưng ghế để kéo ghế thoải mái.",
        "giuong" => "Kích thước giường phổ biến (dài 2m):\n• 1m2: một người\n• 1m4 - 1m6: hai người, phòng nhỏ\n• 1m8 - 2m: hai người rộng rãi hoặc có con nhỏ ngủ cùng\n"
                    + "Chừa ít nhất 60 cm lối đi hai bên giường.",
        "ban lam viec" => "Bàn làm việc: cao 72 - 76 cm, sâu từ 60 cm; rộng 1m - 1m2 cho laptop, 1m4 trở lên nếu dùng 2 màn hình. "
                          + "Ghế nên có mặt ngồi cao 42 - 50 cm, chỉnh được; bàn nâng hạ giúp đổi tư thế ngồi / đứng.",
        "ke tivi" => "Kệ tivi nên dài hơn tivi khoảng 30 - 50 cm và cao 40 - 55 cm để mắt ngang giữa màn hình khi ngồi. "
                     + "Khoảng cách ngồi xem nên gấp 1,5 - 2,5 lần đường chéo màn hình.",
        "tu quan ao" => "Tủ quần áo thường sâu 55 - 60 cm (đủ treo móc áo), cao 2m - 2m4; mỗi cánh rộng 40 - 50 cm. Cửa lùa hợp phòng hẹp vì không cần chỗ mở cánh.",
        "ghe" => "Ghế ăn có mặt ngồi cao khoảng 45 cm (cách mặt bàn 75 cm khoảng 30 cm); ghế làm việc nên chỉnh độ cao 42 - 50 cm và có tựa lưng đỡ thắt lưng.",
        _ => "Bạn cho mình biết món đồ (sofa, bàn ăn, giường, bàn làm việc, kệ tivi, tủ...) và diện tích phòng hoặc số người dùng, "
             + "mình sẽ gợi ý kích thước phù hợp. Cần kích thước riêng, xưởng nhận đóng theo yêu cầu (/bao-gia)."
    };

    private static string? FurnitureWord(string plain)
    {
        if (Has(plain, "sofa") || Has(plain, "ghe sofa")) return "sofa";
        if (Has(plain, "ban an")) return "ban an";
        if (Has(plain, "giuong") || Has(plain, "nem")) return "giuong";
        if (Has(plain, "ban lam viec") || Has(plain, "ban hoc") || Has(plain, "ban may tinh")) return "ban lam viec";
        if (Has(plain, "ke tivi") || Has(plain, "ke tv") || Has(plain, "tivi")) return "ke tivi";
        if (Has(plain, "tu quan ao") || Has(plain, "tu ao")) return "tu quan ao";
        if (Has(plain, "ghe")) return "ghe";
        return null;
    }

    /// <summary>The admin-editable knowledge base entry for the topic (its keywords matched the question), if any.</summary>
    private static string? Knowledge(LocalFacts facts, params string[] topicWords) =>
        facts.Knowledge.FirstOrDefault(k => topicWords.Any(w => Plain($"{k.Title} {k.Keywords}").Contains(w, StringComparison.Ordinal)))?.Content;

    private static string Money(decimal value) => value.ToString("#,0", Vietnamese) + "₫";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Plain(string text) => ShoppingIntentParser.Plain(text);

    /// <summary>Accent-free text with single spaces between words and a space at both ends, for whole-word matching.</summary>
    private static string Words(string text) => " " + Regex.Replace(Plain(text), @"[^\p{L}\d]+", " ").Trim() + " ";

    private static bool Has(string words, string phrase) => words.Contains(" " + phrase + " ", StringComparison.Ordinal);

    /// <summary>A size in the question: "1m6", "2m", "180cm", "1.8 m".</summary>
    [GeneratedRegex(@" \d+([.,]\d+)?\s?(m\d*|cm|met) ")]
    private static partial Regex Dimension();
}
