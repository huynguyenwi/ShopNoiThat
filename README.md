# Nhà Mộc Furniture — Website bán đồ nội thất

Website thương mại điện tử bán nội thất, **chủ lực là bộ bàn ghế ăn gỗ sồi Nga màu óc chó** (bàn 1m2 + 4 ghế, bàn 1m6 + 6 ghế),
vẫn bán các nhóm khác (sofa, giường, tủ, kệ...). Khách gửi **yêu cầu đặt hàng**, cửa hàng gọi lại xác nhận và báo phí giao hàng
(xem [mục 21](#21-bàn-ghế-ăn--liên-hệ-đặt-hàng)). Xây dựng bằng **ASP.NET Core 8 MVC**,
**SQL Server**, **Entity Framework Core 8**, **ASP.NET Core Identity**, **Bootstrap 5.3**, có kiến trúc sẵn sàng tích hợp AI
(OpenAI hoặc API tương thích).

> Đã hoàn thành cả 11 phase (xem [Tiến độ](#12-tiến-độ)). Bản Release đã được kiểm tra trên database tạo từ file SQL,
> chạy ở môi trường Production: 705 test tự động + 15 bộ kiểm thử trình duyệt (xem [mục 17](#17-kiểm-thử)).

---

## 1. Yêu cầu hệ thống

| Thành phần | Phiên bản | Ghi chú |
|---|---|---|
| .NET SDK | **8.0.x** (đã kiểm tra với 8.0.417) | `global.json` ghim SDK 8; máy có SDK 9/10 vẫn dùng SDK 8 để build |
| SQL Server | LocalDB 2019+ / Express / Developer / SQL Server 2019+ | Mặc định dùng **LocalDB** đi kèm Visual Studio |
| dotnet-ef | 8.0.31 | Khai báo trong `.config/dotnet-tools.json`, cài bằng `dotnet tool restore` |
| Trình duyệt | Chrome / Edge / Firefox bản mới | |

Kiểm tra nhanh:

```powershell
dotnet --list-sdks           # phải có 8.0.x
sqllocaldb info              # phải có MSSQLLocalDB
```

## 2. Cấu trúc solution

```
FurnitureStore.sln
├── src/
│   ├── FurnitureStore.Domain          # Entities, Enums, Constants, domain rules (không phụ thuộc gì)
│   ├── FurnitureStore.Application     # DTOs, Interfaces, Services, Validators, Settings, business logic
│   ├── FurnitureStore.Infrastructure  # EF Core DbContext, Configurations, Migrations, Repositories,
│   │                                  # Identity, Seed, (AI / Payment / File storage ở các phase sau)
│   └── FurnitureStore.Web             # MVC Controllers, Views, ViewModels, Areas/Admin, API, wwwroot
└── tests/
    └── FurnitureStore.Tests           # Unit tests + Integration tests (SQLite in-memory & SQL Server LocalDB)
```

Hướng phụ thuộc: `Web → Infrastructure → Application → Domain`. Domain không tham chiếu Identity:
entity chỉ lưu `UserId`, khóa ngoại tới `AspNetUsers` được cấu hình ở tầng Infrastructure.

Các quyết định kỹ thuật chính:

- **Repository + Unit of Work**: `IRepository<T>`/`IUnitOfWork` khai báo ở Application, cài đặt bằng EF Core ở Infrastructure.
  Unit of Work chuyển lỗi xung đột dữ liệu (concurrency, trùng SKU/slug/email) thành `ConflictException` (HTTP 409).
- **Audit tự động**: `AuditableEntityInterceptor` điền `CreatedAt/By`, `UpdatedAt/By`, chuyển xóa `Product` thành
  **soft delete**, và sinh lại token `Version` (optimistic concurrency) cho `Product`, `ProductVariant`, `Order`, `Coupon`, `QuoteRequest`.
- **Giá và tồn kho nằm ở variant (SKU)**. `Product.BasePrice/DiscountPrice/StockQuantity` là giá trị "giá từ"/tổng tồn
  được đồng bộ từ variant để lọc và sắp xếp nhanh.
- **Enum lưu dạng chuỗi** (`Pending`, `COD`...) để dữ liệu dễ đọc; mọi thời gian lưu **UTC**.
- **API trả JSON chuẩn**: `{ "success": bool, "message": "...", "data": ..., "errors": [] }`, kể cả khi lỗi 404/401/403/500.

## 3. Cơ sở dữ liệu

44 bảng, gồm các bảng Identity (`AspNetUsers`, `AspNetRoles`, `AspNetUserRoles`...) và:

| Nhóm | Bảng |
|---|---|
| Catalog | `Categories` (cây 2 cấp: phòng → nhóm sản phẩm), `Products`, `ProductImages`, `ProductVariants`, `ProductColors`, `ProductMaterials`, `ProductSizes`, `ProductStyles`, `ProductVariantColors`, `ProductVariantMaterials`, `ProductVariantSizes`, `ProductPriceHistory`, `PriceRules` |
| Bán hàng | `Carts`, `CartItems`, `Orders`, `OrderItems`, `OrderAddresses`, `OrderStatusHistories`, `Payments`, `Coupons`, `CouponUsages` |
| Khách hàng | `CustomerAddresses`, `Wishlists`, `WishlistItems`, `Reviews`, `ReviewImages` |
| Giao tiếp | `ChatConversations`, `ChatMessages`, `ContactMessages`, `Notifications` |
| AI | `AIConversations`, `AIMessages`, `AIKnowledgeEntries`, `QuoteRequests` |
| Hệ thống | `AuditLogs`, `StoreInformation`, `HomeBanners` |

Mô hình variant: một sản phẩm có nhiều variant; mỗi variant có SKU, giá, giá cũ, tồn kho, ảnh riêng và nhiều
màu / chất liệu / kích thước qua bảng nối. Mỗi chiều có đúng một giá trị `IsPrimary` (dùng cho bộ chọn trên trang sản phẩm);
các giá trị phụ mô tả bộ phận, ví dụ bàn nâng hạ: *màu chính "Nâu óc chó" + màu "Đen" cho phần "Khung nâng hạ"*.

## 4. Connection string

Mặc định trong `src/FurnitureStore.Web/appsettings.json`:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=FurnitureStoreDb;Trusted_Connection=True;TrustServerCertificate=True"
}
```

Dùng SQL Server khác (không sửa file trong repo) — đặt bằng user-secrets hoặc biến môi trường:

```powershell
# SQL Server instance mặc định, Windows Authentication
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=FurnitureStoreDb;Trusted_Connection=True;TrustServerCertificate=True" --project src/FurnitureStore.Web

# SQL Authentication
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=FurnitureStoreDb;User Id=furniture_app;Password=<mật khẩu>;TrustServerCertificate=True" --project src/FurnitureStore.Web
```

## 5. Migration

Chạy các lệnh sau tại thư mục gốc solution:

```powershell
dotnet tool restore                      # cài dotnet-ef 8.0.31 (local tool)

# Tạo / cập nhật database theo migration mới nhất
dotnet ef database update --project src/FurnitureStore.Infrastructure --startup-project src/FurnitureStore.Web

# Tạo migration mới sau khi đổi entity/configuration
dotnet ef migrations add <TenMigration> --project src/FurnitureStore.Infrastructure --startup-project src/FurnitureStore.Web --output-dir Persistence/Migrations

# Kiểm tra model có thay đổi chưa tạo migration
dotnet ef migrations has-pending-model-changes --project src/FurnitureStore.Infrastructure --startup-project src/FurnitureStore.Web
```

Migration hiện có: `InitialCreate`, `AddProductSearchText` (cột tìm kiếm không dấu cho sản phẩm), `AddCouponIsPublic` (mã giảm giá công khai),
`AddCartItemSelection` (tích chọn từng sản phẩm trong giỏ hàng, mục 22), `AddHomeBannerAndLogoSubtitle` (banner trang chủ + dòng chữ nhỏ dưới logo, mục 24), `AddStoreLogo` (ảnh logo cửa hàng, mục 24).

## 6. Tạo database & seed dữ liệu

Có hai cách, chọn **một**:

### Cách A — Chạy file SQL có sẵn (không cần `dotnet ef`)

Thư mục [database/](database/) chứa script đã sinh sẵn:

| File | Nội dung |
|---|---|
| `FurnitureStoreDb_full.sql` | Tạo database `FurnitureStoreDb` (nếu chưa có) + toàn bộ schema + dữ liệu mẫu. **Chỉ cần chạy file này.** |
| `01_schema.sql` | Chỉ schema (script migration idempotent, chạy lại nhiều lần không lỗi) |
| `02_seed_data.sql` | Roles, catalog demo (23 danh mục, 43 sản phẩm, 124 variant), thông tin cửa hàng, 3 mã giảm giá, 6 mục nội dung chatbot AI, 60 tham số bảng giá đặt đóng. Chỉ chèn vào bảng còn trống |

```powershell
# SSMS: mở FurnitureStoreDb_full.sql rồi Execute (F5). Hoặc dùng sqlcmd (-f 65001 để giữ tiếng Việt):
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -f 65001 -i database\FurnitureStoreDb_full.sql
sqlcmd -S localhost -E -f 65001 -i database\FurnitureStoreDb_full.sql          # SQL Server thường
```

Script **không** chứa tài khoản người dùng (không phát tán password hash). Tài khoản admin được ứng dụng
tạo ở lần chạy đầu tiên từ `Seed:AdminPassword` (mục 7). Khi chạy ở Development, ứng dụng còn tạo thêm
dữ liệu hoạt động demo (đơn hàng, đánh giá...) như mô tả bên dưới.

**Nâng cấp database đã tạo từ trước** (ví dụ khi bản mới có thêm migration `AddCartItemSelection`): chạy lại
`01_schema.sql` — script chỉ áp các migration còn thiếu, dữ liệu hiện có được giữ nguyên, chạy lại nhiều lần không lỗi.
Ở Development, ứng dụng cũng tự áp migration khi khởi động.

```powershell
sqlcmd -S "(localdb)\MSSQLLocalDB" -E -f 65001 -d FurnitureStoreDb -i database\01_schema.sql
```

Sinh lại các file này sau khi thêm migration / đổi dữ liệu seed:

```powershell
powershell -ExecutionPolicy Bypass -File database\generate-sql.ps1
```

### Cách B — Để ứng dụng tự tạo

Ở môi trường **Development** (`appsettings.Development.json`), khi chạy ứng dụng sẽ tự động:

1. Áp các migration còn thiếu (`Database:ApplyMigrationsOnStartup = true`).
2. Tạo roles `ADMIN`, `USER`, `STAFF`, `CUSTOMER`.
3. Tạo tài khoản admin và tài khoản khách demo **nếu đã cấu hình mật khẩu** (xem mục 7).
4. Tạo thông tin cửa hàng từ `ApplicationSettings:Store`.
5. Seed catalog demo khi chưa có sản phẩm (`Database:SeedDemoData = true`):
   **23 danh mục, 43 sản phẩm, 124 variant**, 15 màu, 17 chất liệu, 8 phong cách, 30 kích thước;
   có sẵn sản phẩm khuyến mãi, sắp hết hàng và hết hàng để test.
6. Seed mã giảm giá: `CHAOBAN10` (10%, tối đa 2 triệu, đơn từ 5 triệu, 1 lần/khách), `GIAM500K` (đơn từ 10 triệu), `HETHAN` (đã hết hạn, để test).
7. Seed hoạt động demo (một lần, nhận biết qua tài khoản `khach01@furniture.local`): 12 khách hàng demo
   (không có mật khẩu, không đăng nhập được), ~90 đơn hàng trong 120 ngày với đủ trạng thái, ~70 đánh giá
   đã mua hàng và 3 tin nhắn liên hệ — để dashboard, biểu đồ và trang quản trị có dữ liệu.

Ở Production cả hai cờ mặc định là `false`: chạy `dotnet ef database update` thủ công (hoặc trong pipeline deploy).

Ảnh sản phẩm demo là **ảnh minh họa SVG tự vẽ** (`/images/placeholder/{loại}.svg?color=...`), không dùng ảnh có bản quyền.
Ảnh do admin / khách tải lên được lưu ở `wwwroot/uploads/` (đã loại khỏi git), kiểm tra đuôi file, dung lượng (≤ 50 MB) và chữ ký nhị phân,
rồi **tự thu nhỏ vừa khung hiển thị** (mục 23).

## 7. Tài khoản admin & tài khoản demo

Mật khẩu **không** nằm trong repo. Đặt bằng user-secrets (máy dev) trước lần chạy đầu tiên:

```powershell
dotnet user-secrets set "Seed:AdminPassword" "<mật khẩu admin>" --project src/FurnitureStore.Web
dotnet user-secrets set "Seed:DemoUserPassword" "<mật khẩu khách demo>" --project src/FurnitureStore.Web
```

| Tài khoản | Email | Role |
|---|---|---|
| Admin | `admin@furniture.local` | ADMIN |
| Khách demo | `khachhang@furniture.local` | USER |

Chính sách mật khẩu: tối thiểu 8 ký tự, có chữ hoa, chữ thường và chữ số. Sai mật khẩu 5 lần sẽ khóa 15 phút.
Nếu chưa đặt mật khẩu, ứng dụng vẫn chạy và ghi cảnh báo vào log (không bao giờ ghi mật khẩu ra log).
Tài khoản đã tồn tại sẽ không bị đổi mật khẩu khi khởi động lại.

### Email

Mặc định `Email:Mode = Pickup`: email (chào mừng, quên mật khẩu, xác nhận đơn hàng, đổi trạng thái đơn) được ghi
thành file `.html` trong `src/FurnitureStore.Web/App_Data/emails/` để xem khi phát triển. Muốn gửi thật, đặt
`Email:Mode = Smtp` và cấu hình `Email:Smtp:*` (mật khẩu SMTP đặt bằng user-secrets `Email:Smtp:Password`).

## 8. Chạy ứng dụng

```powershell
dotnet restore
dotnet build
dotnet run --project src/FurnitureStore.Web --launch-profile https
```

- Website: <https://localhost:7160> (HTTP <http://localhost:5243> tự chuyển sang HTTPS)
- Health check (gồm kết nối database): <https://localhost:7160/health>

Lần đầu có thể cần tin cậy chứng chỉ HTTPS dev: `dotnet dev-certs https --trust`.

### Chạy bằng Visual Studio 2022 (17.8 trở lên)

1. Mở `FurnitureStore.sln`, chờ Visual Studio tự restore NuGet (cần internet lần đầu).
2. Chuột phải **FurnitureStore.Web** → **Set as Startup Project**. Chỉ project này chạy được (web, trang quản trị, API, chat,
   trợ lý AI đều nằm trong nó); Domain / Application / Infrastructure là thư viện, Tests chạy trong **Test Explorer** —
   **không cần** "Multiple startup projects".
3. Trên thanh công cụ chọn profile **https** (đứng đầu danh sách nên thường được chọn sẵn; web chạy ở https://localhost:7160).
   Profile **http** (http://localhost:5243) cũng chạy được ở môi trường Development; khi triển khai thật luôn dùng HTTPS.
4. Chuột phải **FurnitureStore.Web** → **Manage User Secrets**, dán mật khẩu tài khoản (và connection string nếu database
   không nằm trên LocalDB):
   ```json
   {
     "Seed": { "AdminPassword": "<mật khẩu admin>", "DemoUserPassword": "<mật khẩu khách demo>" },
     "ConnectionStrings": { "DefaultConnection": "Server=.\\SQLEXPRESS;Database=FurnitureStoreDb;Trusted_Connection=True;TrustServerCertificate=True" }
   }
   ```
5. **Ctrl + F5** (chạy không debug) hoặc **F5** (debug). Lần đầu bấm **Yes** khi được hỏi tin cậy chứng chỉ HTTPS.

Chạy test:

```powershell
dotnet test
# Bỏ qua test cần SQL Server LocalDB (ví dụ trên CI Linux):
$env:SKIP_SQLSERVER_TESTS = "1"; dotnet test
```

## 9. Cấu hình AI

Trợ lý AI có **hai chế độ**:

| Chế độ | Khi nào | Hoạt động |
|---|---|---|
| **AI** | Đã đặt `AI:ApiKey` và `AI:Enabled = true` | Hệ thống tìm sản phẩm thật trong database theo nhu cầu của khách, gửi danh sách đó cho mô hình AI (OpenAI hoặc API tương thích) để chọn và giải thích. |
| **Tự động** | Chưa có API key, hoặc dịch vụ AI lỗi / quá tải | Chạy hoàn toàn trên server, không cần dịch vụ ngoài: trả lời các câu hỏi thường gặp từ dữ liệu thật của cửa hàng (xem bảng dưới) và tìm sản phẩm theo tiếng Việt (ngân sách "10 triệu", "12tr5", "5 - 8 triệu"; kích thước "1m8", "180x90x75"; "6 người", "20m2"; phòng, loại sản phẩm, màu, chất liệu, phong cách - có hoặc không dấu). |

**Câu hỏi chế độ Tự động trả lời được** (`LocalAssistant`, nhận câu có dấu hoặc không dấu, khớp theo cụm từ nguyên vẹn):

| Nhóm | Ví dụ | Dữ liệu dùng để trả lời |
|---|---|---|
| Cửa hàng | "mấy giờ mở cửa", "showroom ở đâu", "số hotline", "có zalo không" | Thông tin cửa hàng (`/admin/store`) |
| Chính sách | "phí ship bao nhiêu", "bảo hành bao lâu", "đổi trả thế nào", "có trả góp không", "nhận đóng theo yêu cầu không" | Nội dung chatbot (`/admin/ai-knowledge`); phí giao hàng: cửa hàng báo khi liên hệ |
| Mua hàng | "cách đặt hàng", "đơn hàng của tôi đến đâu rồi", "hủy đơn thế nào", "có mã giảm giá không", "quên mật khẩu" | **Đơn hàng của chính khách** (khi đã đăng nhập), mã giảm giá công khai đang chạy |
| Kiến thức | "gỗ sồi Nga có bền không", "gỗ sồi và óc chó khác gì", "MDF có tốt không", "phong cách Japandi là gì", "bàn ăn 6 người cần kích thước bao nhiêu", "bảo quản sofa da" | Kiến thức nội thất có sẵn (16 chất liệu đang bán, 8 phong cách, cỡ chuẩn các món) |
| Trang sản phẩm | "giá bao nhiêu", "còn màu nào khác", "kích thước thế nào", "còn hàng không", "mẫu nào rẻ hơn", "bảo hành bao lâu" | Dữ liệu của sản phẩm đang xem; "rẻ hơn" liệt kê mẫu cùng loại giá thấp hơn |
| Trò chuyện | "chào shop", "cảm ơn", "bạn là ai", "bạn giúp được gì" | - |

Câu không hiểu được trả lời thẳng là chưa hiểu, kèm gợi ý và lối gặp nhân viên (không lặp lại kết quả tìm kiếm cũ).
Câu hỏi **đơn hàng** và **mã giảm giá** luôn trả lời từ database kể cả khi có AI, vì mô hình không có dữ liệu này và không được tự đặt ra mã.
Muốn trợ lý hiểu thêm câu hỏi riêng của cửa hàng: thêm mục mới ở `/admin/ai-knowledge` với các từ khóa của câu hỏi.

Section `AI` trong `appsettings.json` (không chứa API key):

```json
"AI": {
  "Enabled": true,
  "Provider": "OpenAI",
  "BaseUrl": "https://api.openai.com/v1/",
  "Model": "gpt-4o-mini",
  "ApiKey": "",
  "MaxOutputTokens": 800,
  "Temperature": 0.3,
  "TimeoutSeconds": 60,
  "RequestsPerMinute": 10,
  "JsonMode": true,
  "TokenLimitParameter": "max_tokens",
  "SendTemperature": true
}
```

API key đặt bằng user-secrets hoặc biến môi trường - **không commit vào repo**:

```powershell
dotnet user-secrets set "AI:ApiKey" "<api key>" --project src/FurnitureStore.Web
# hoặc trên server: AI__ApiKey=<api key>
```

Dùng API tương thích OpenAI khác: đổi `BaseUrl` + `Model`, ví dụ Groq `https://api.groq.com/openai/v1/`, OpenRouter
`https://openrouter.ai/api/v1/`, Ollama chạy local `http://localhost:11434/v1/` (với Ollama đặt ApiKey bất kỳ, ví dụ `ollama`).
Nếu nhà cung cấp không hỗ trợ `response_format` đặt `JsonMode = false`; model mới của OpenAI chỉ nhận
`max_completion_tokens` thì đặt `TokenLimitParameter = "max_completion_tokens"`; model không cho đổi temperature thì `SendTemperature = false`.

**Bảo đảm AI không bịa:**
- AI chỉ được chọn sản phẩm trong danh sách hệ thống gửi. Server bỏ mọi id sản phẩm không có trong danh sách, tên / giá / ảnh / link trên thẻ sản phẩm luôn lấy từ database.
- `PriceGuard` quét câu trả lời: số tiền không trùng giá thật (hoặc số khách / chính sách đã nêu) bị thay bằng "(giá chính xác xem ở thẻ sản phẩm)".
- Màu và phong cách AI đề xuất phải thuộc bảng màu / phong cách cửa hàng đang bán.
- Giá đồ đặt đóng theo yêu cầu do `PriceCalculatorService` tính (Phase 9), AI chỉ giải thích.
- Giới hạn `AI:RequestsPerMinute` lần/phút cho mỗi tài khoản (hoặc mỗi IP với khách vãng lai); câu hỏi tối đa 1.000 ký tự.
- API key không bao giờ được ghi log; log chỉ ghi mã lỗi / số token / thời gian phản hồi.

**Nội dung chatbot** (chính sách giao hàng, bảo hành, đổi trả, thanh toán, đặt đóng theo yêu cầu, bảo quản) được seed sẵn và
admin sửa tại `/admin/ai-knowledge`; mục nào có **từ khóa** xuất hiện trong câu hỏi thì được đưa vào câu trả lời.
Admin xem mọi hội thoại AI, token đã dùng và các lần AI lỗi tại `/admin/ai`; khách xem lịch sử của mình tại `/account/ai-history`.

## 10. Biến môi trường

ASP.NET Core đọc biến môi trường với `__` thay cho `:`.

| Biến | Ý nghĩa |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Staging` / `Production` |
| `ConnectionStrings__DefaultConnection` | Connection string SQL Server |
| `Seed__AdminPassword` | Mật khẩu tài khoản admin được seed |
| `Seed__DemoUserPassword` | Mật khẩu tài khoản khách demo |
| `AI__ApiKey` | API key của nhà cung cấp AI |
| `AI__RequestsPerMinute` | Giới hạn số câu hỏi AI mỗi phút cho mỗi người dùng / IP |
| `AI__BaseUrl`, `AI__Model` | Đổi sang API AI tương thích khác |
| `Database__ApplyMigrationsOnStartup` | `true` để tự áp migration khi khởi động |
| `Database__SeedDemoData` | `true` để seed catalog demo khi DB trống và dữ liệu hoạt động demo |
| `Email__Mode` | `Pickup` (ghi file) hoặc `Smtp` |
| `Email__Smtp__Password` | Mật khẩu SMTP |
| `RateLimiting__AuthenticationPermitsPerMinute` | Số lần đăng nhập / đăng ký / quên mật khẩu tối đa mỗi phút mỗi IP (mặc định 10) |
| `RateLimiting__FormPermitsPerMinute` | Số lần gửi form liên hệ / đánh giá mỗi phút mỗi IP (mặc định 5) |
| `RateLimiting__ApiPermitsPerMinute` | Giới hạn chung cho mọi `/api/*` mỗi phút mỗi IP (mặc định 300) |
| `ApplicationSettings__BaseUrl` | Địa chỉ public của website (dùng cho canonical, sitemap, Open Graph, link trong email) |
| `ApplicationSettings__FocusCategorySlug` | Danh mục mặt hàng chính hiện đầu trang chủ và trên menu (mặc định `bo-ban-an`; để trống = ẩn) |
| `Seo__AllowIndexing` | `false` trên máy staging/test: robots.txt chặn toàn bộ và mọi trang `noindex` |
| `DataProtection__KeysPath` | Thư mục lưu khóa mã hóa cookie (mặc định `App_Data/keys`); dùng chung khi chạy nhiều server |
| `DataProtection__Dpapi` | Windows: mã hóa file khóa bằng DPAPI theo `CurrentUser` (mặc định), `LocalMachine` (hosting dùng chung như Somee, app pool không nạp user profile) hoặc `None` |
| `ReverseProxy__KnownProxies__0` | IP của reverse proxy tin cậy (nginx, load balancer) để lấy IP thật từ `X-Forwarded-For` |
| `SKIP_SQLSERVER_TESTS` | `1` để bỏ qua test cần SQL Server LocalDB |

Các section cấu hình khác: `ApplicationSettings` (tên site, khẩu hiệu, BaseUrl, thông tin cửa hàng), `Payment` (phương thức thanh toán
— trang liên hệ đặt hàng tạo đơn **COD** nên `EnabledMethods` phải có `COD`; thông tin chuyển khoản), `Storage` (thư mục upload, `MaxFileSizeMb` dung lượng tối đa mỗi ảnh — mặc định và tối đa 50, `MaxImageMegapixels` số điểm ảnh tối đa — mặc định 100,
định dạng ảnh cho phép). Cấu hình sai (ví dụ `BaseUrl`
không phải URL) sẽ làm ứng dụng dừng ngay khi khởi động với thông báo rõ ràng.

## 11. Xử lý sự cố

| Triệu chứng | Nguyên nhân / cách xử lý |
|---|---|
| `Login failed for user '...'` khi dùng `Server=localhost` | Tài khoản Windows chưa có login trong instance SQL Server đó. Dùng LocalDB (mặc định), cấp quyền cho tài khoản Windows, hoặc dùng SQL Authentication (mục 4). |
| `A network-related or instance-specific error` với LocalDB | Chạy `sqllocaldb start MSSQLLocalDB`; nếu chưa có: `sqllocaldb create MSSQLLocalDB`. |
| Log báo *"Account admin@furniture.local was not created..."* | Chưa đặt `Seed:AdminPassword` (mục 7). Đặt rồi khởi động lại. |
| Log báo *"Database has N pending migration(s)"* và không seed | Đang tắt `ApplyMigrationsOnStartup` (ví dụ môi trường Production). Chạy `dotnet ef database update`. |
| `dotnet ef` không nhận lệnh | Chạy `dotnet tool restore` ở thư mục gốc solution. |
| Build dùng nhầm SDK 9/10 | Kiểm tra `global.json`; cần cài .NET SDK 8.0.x. |
| Trình duyệt cảnh báo chứng chỉ HTTPS | `dotnet dev-certs https --trust`. |
| Muốn làm lại database từ đầu | `dotnet ef database drop --force --project src/FurnitureStore.Infrastructure --startup-project src/FurnitureStore.Web` rồi chạy lại ứng dụng. |
| Trang `/admin/chat` không hiện tin nhắn khách vừa gửi | Xem dòng trạng thái dưới bộ lọc: *"Mất kết nối trực tiếp"* nghĩa là realtime đang mất (server vừa khởi động lại, máy ngủ, mạng chặn WebSocket) — trang vẫn tự cập nhật mỗi 15 giây và tự kết nối lại. Khi thử, mở **khách và admin ở hai trình duyệt khác nhau** (hoặc một cửa sổ ẩn danh): cùng một trình duyệt dùng chung cookie đăng nhập, đăng nhập tài khoản khách sẽ đăng xuất admin. Khách gửi quá 15 tin/phút thì tin bị từ chối (khách thấy thông báo). |
| Trang có form báo lỗi 500 khi chạy bằng `http://` | Đã sửa cho môi trường Development (cookie theo giao thức của request). Production bắt buộc HTTPS. |
| Font chữ hiển thị khác thiết kế khi offline | Font Be Vietnam Pro / Playfair Display tải từ Google Fonts; khi offline trình duyệt dùng font hệ thống. Bootstrap và icon đã nằm sẵn trong `wwwroot/lib`. |

## 12. Tiến độ

| Phase | Nội dung | Trạng thái |
|---|---|---|
| 1 | Solution, cấu trúc project, DI, cấu hình, xử lý lỗi toàn cục, layout & trang chủ | ✅ Hoàn thành |
| 2 | Entities, DbContext, cấu hình, migration `InitialCreate`, seed, repository / unit of work | ✅ Hoàn thành |
| 3 | Đăng ký, đăng nhập, quên mật khẩu, phân quyền ADMIN / USER | ✅ Hoàn thành |
| 4 | Sản phẩm, danh mục, variant, CRUD Admin, danh sách & chi tiết sản phẩm | ✅ Hoàn thành |
| 5 | Giỏ hàng, checkout, đơn hàng, thanh toán (COD, chuyển khoản giả lập) | ✅ Hoàn thành |
| 6 | Admin dashboard, thống kê, quản lý đơn hàng & khách hàng, đánh giá, liên hệ | ✅ Hoàn thành |
| 7 | Chat realtime khách hàng - cửa hàng (SignalR) | ✅ Hoàn thành |
| 8 | Chatbot AI, gợi ý sản phẩm, tư vấn màu và phong cách | ✅ Hoàn thành |
| 9 | Báo giá nội thất theo yêu cầu (PriceCalculatorService, QuoteRequest) | ✅ Hoàn thành |
| 10 | SEO, responsive, bảo mật, logging, hiệu năng | ✅ Hoàn thành |
| 11 | Kiểm thử tổng thể, sửa lỗi, build Release | ✅ Hoàn thành |
| + | Mã giảm giá, mã QR, trợ lý không cần AI, **bàn ghế ăn làm chủ lực + liên hệ đặt hàng** (mục 19 – 21) | ✅ Hoàn thành |

## 13. Chức năng & đường dẫn chính

| Khu vực | Đường dẫn |
|---|---|
| Trang chủ | `/` |
| Sản phẩm (tìm kiếm, lọc, sắp xếp) | `/products?q=ban+go&category=phong-an&color=...&minPrice=...&sort=price-asc` |
| Chi tiết sản phẩm, chọn màu / chất liệu / kích thước, đánh giá | `/products/{slug}` |
| Bộ bàn ăn (mặt hàng chính) | `/products?category=bo-ban-an` · theo cỡ: `&size=set-4-ghe-120`, `&size=set-6-ghe-160` |
| Giỏ hàng / liên hệ đặt hàng | `/cart` (tích chọn sản phẩm muốn đặt), `/checkout` (trang "Liên hệ đặt hàng") |
| Tài khoản | `/account/login`, `/account/register`, `/account/forgot-password`, `/account/profile` |
| Đơn hàng, địa chỉ, yêu thích của tôi | `/account/orders`, `/account/addresses`, `/wishlist` |
| Liên hệ & thông tin cửa hàng | `/contact` |
| Chat với cửa hàng | Nút chat góc phải mọi trang (khách vãng lai nhập tên + SĐT/email; đã đăng nhập thì dùng hồ sơ) |
| Trả lời chat (ADMIN, STAFF) | `/admin/chat` |
| Trợ lý AI | Nút "Trợ lý AI" góc phải mọi trang; "AI tư vấn sản phẩm này" trên trang sản phẩm |
| Công cụ tư vấn AI (gợi ý theo phòng, phối màu, chọn phong cách) | `/tu-van` |
| Lịch sử tư vấn AI | `/account/ai-history` |
| Báo giá đồ đặt đóng | `/bao-gia` · khách theo dõi / đồng ý báo giá tại `/account/quotes` |
| Quản trị báo giá, bảng giá | `/admin/quotes`, `/admin/price-rules` |
| Mã giảm giá (ADMIN) | `/admin/coupons` — danh sách, `/admin/coupons/create`, `/admin/coupons/{id}` (thống kê), `/admin/coupons/{id}/edit` |
| Mã QR | Quét: `/q/p/{id}` (sản phẩm), `/q/o/{mã đơn}` (đơn hàng) · Ảnh: `/qr/products/{id}.svg\|.png`, `/qr/orders/{mã đơn}.svg\|.png` · In: `/admin/products/qrlabels`, `/admin/orders/print/{id}` |
| Áp mã giảm giá (khách) | Ô nhập mã + "Ưu đãi dành cho bạn" ở `/cart` và `/checkout` · API `POST` / `DELETE /api/cart/coupon` |
| Quản trị AI | `/admin/ai` (hội thoại), `/admin/ai-knowledge` (nội dung chatbot) |
| Quản trị (ADMIN) | `/admin` — dashboard, `/admin/orders`, `/admin/customers`, `/admin/products`, `/admin/categories`, `/admin/attributes/colors`, `/admin/reviews`, `/admin/contacts`, `/admin/store` (tên cửa hàng, logo), `/admin/banner` (banner trang chủ), `/admin/audit-logs`, `/admin/notifications` |
| API báo giá | `POST /api/ai/price-estimate` (tính giá, không lưu), `POST /api/quotes` (gửi yêu cầu), `GET /api/quotes`, `GET /api/quotes/{code}`, `POST /api/quotes/{code}/accept`, `/reject`, `/cancel` |
| API AI | `POST /api/ai/chat`, `/api/ai/recommend`, `/api/ai/color-recommend`, `/api/ai/style-recommend`, `GET /api/ai/conversations`, `/api/ai/status` |
| API chat | `/api/chat`, `/api/chat/conversations`, `/api/chat/start`, `/api/chat/messages`, `/api/admin/chat/conversations` · SignalR hub `/hubs/chat` |
| API địa chỉ (công khai) | `GET /api/locations/provinces`, `GET /api/locations/provinces/{mã tỉnh}/wards` |
| API chọn sản phẩm trong giỏ | `PUT /api/cart/items/{id}/selected { selected }`, `PUT /api/cart/selected { selected }` (chọn / bỏ tất cả), `POST /api/cart { variantId, quantity, buyNow }` |
| API | `/api/products`, `/api/products/{id}`, `/api/products/search?q=`, `/api/categories`, `/api/cart`, `/api/orders`, `/api/wishlist`, `/api/account/me`, `/api/admin/products` |

API trả về dạng chuẩn `{ "success": true|false, "message": "...", "data": ..., "errors": [] }`.
Mọi request POST/PUT/DELETE phải có anti-forgery token (form field hoặc header `X-CSRF-TOKEN`, lấy từ thẻ `<meta name="csrf-token">`).
Trang và API quản trị được kiểm tra quyền ở backend (policy `AdminOnly`), không chỉ ẩn nút trên giao diện.

## 14. Chat realtime (SignalR)

- Hub `/hubs/chat`; thư viện client `wwwroot/lib/microsoft-signalr` (8.0.7, MIT). Tự fallback sang long polling khi không có WebSocket; khi mất kết nối vẫn gửi được qua REST `/api/chat/messages`.
- Khách vãng lai được nhận diện bằng cookie ngẫu nhiên `.NhaMoc.Chat` (HttpOnly, Secure; riêng Development chạy bằng HTTP thì không Secure). Khi đăng nhập / đăng ký, cuộc trò chuyện đang có tự chuyển sang tài khoản.
- Nhân viên chat: gán role `STAFF` cho tài khoản (trang `/admin/customers` → chi tiết → vai trò). STAFF chỉ vào được `/admin/chat`; các trang quản trị khác vẫn chỉ dành cho ADMIN (kiểm tra ở server).
- Bảo mật: nội dung chỉ lưu và hiển thị dạng văn bản thuần (không render HTML); tối đa 2.000 ký tự; khách tối đa 15 tin/phút; hub từ chối kết nối từ origin khác (chống cross-site WebSocket hijacking); phương thức dành cho nhân viên yêu cầu policy `BackOffice`.
- Khi có tin nhắn mới: admin nhận thông báo (chuông) + badge ở menu "Chat khách hàng"; khách đã đăng nhập nhận thông báo khi cửa hàng trả lời.
- **Mất kết nối realtime** (server khởi động lại, máy ngủ, mạng chập chờn, WebSocket bị chặn): trang admin và khung chat của khách
  tự kết nối lại liên tục (2 → 30 giây/lần, thử ngay khi tab được mở lại), trong lúc chờ **tự cập nhật mỗi 15 giây** và tải bù các
  tin bị lỡ khi kết nối lại. Trang `/admin/chat` hiện trạng thái *"Đang nhận tin nhắn trực tiếp"* hoặc
  *"Mất kết nối trực tiếp - tự cập nhật mỗi 15 giây"*. (Trước đây, mất kết nối quá ~45 giây thì trang không nhận tin mới cho tới khi tải lại.)
- Nhiều server: SignalR mặc định giữ kết nối trong bộ nhớ của từng server. Khi chạy nhiều instance cần bật sticky session hoặc thêm backplane (Redis / Azure SignalR Service).

## 15. Báo giá nội thất đặt đóng

Khách mô tả món cần đóng (ví dụ *"Tôi muốn bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm"*) tại `/bao-gia` hoặc hỏi trong khung Trợ lý AI.
Hệ thống:

1. **Phân tích yêu cầu** - loại sản phẩm, kích thước, chất liệu, màu, phong cách, kiểu sơn, số lượng (bộ phân tích tiếng Việt; khi có AI key thì AI chỉ bổ sung các thông tin còn thiếu, số liệu khách viết luôn được ưu tiên). Thông tin thiếu được tạm tính theo kích thước phổ biến và **ghi rõ là giả định**.
2. **Tính giá bằng `PriceCalculatorService`** từ bảng `PriceRules` (AI không bao giờ quyết định giá):

   ```
   diện tích bao ngoài = 2 × (D·R + D·C + R·C)                    (m²)
   diện tích vật liệu = diện tích bao ngoài × hệ số vật liệu[loại]
   vật liệu    = diện tích vật liệu × đơn giá /m²[chất liệu]
   gia công    = công cơ bản[loại] + diện tích vật liệu × công /m²[loại]
   hoàn thiện  = diện tích vật liệu × đơn giá hoàn thiện /m²
   sơn         = diện tích vật liệu × đơn giá sơn /m²[kiểu sơn]
   phụ kiện    = phụ kiện[loại]
   chi phí chung = trực tiếp × %chi phí chung;  lợi nhuận = (trực tiếp + chung) × %lợi nhuận
   giá 1 sản phẩm = làm tròn lên 10.000đ;  tổng = giá × số lượng
   ```

   Mỗi tham số chọn dòng **cụ thể nhất** (loại sản phẩm / chất liệu / kiểu sơn), rồi độ ưu tiên cao nhất, trong thời gian hiệu lực.
   Ví dụ với bảng giá mặc định: bàn ăn gỗ óc chó 1800 × 900 × 750 mm, sơn PU = **17.800.000đ**.
3. **Giải thích** - văn bản mẫu, hoặc AI viết (mọi con số AI viết ra được kiểm tra với bảng chi phí, số lạ bị thay thế). Kèm phương án chất liệu khác cùng nhóm và mẫu có sẵn cùng loại để so sánh.
4. **Gửi yêu cầu** (`QuoteRequest`, mã `BG{yyMMdd}-XXXXX`) - giá luôn được **tính lại ở server**, không nhận giá từ trình duyệt. Admin nhận thông báo; khách nhận email.
5. **Admin** (`/admin/quotes`): xem chi tiết chi phí, tính lại khi đổi thông số, nhập **giá chính thức** và chuyển trạng thái
   Mới → Đang xem xét → Đã báo giá → Khách đồng ý / từ chối (có kiểm tra xung đột khi 2 người cùng sửa). Khách đăng nhập
   xem và bấm **Đồng ý / Từ chối** tại `/account/quotes`.
6. **Bảng giá** (`/admin/price-rules`): thêm / sửa / tắt / xóa tham số, đặt thời gian hiệu lực - thay đổi áp dụng ngay cho lần tính tiếp theo; mọi thay đổi ghi nhật ký.

## 16. SEO, bảo mật, hiệu năng

**SEO**
- URL thân thiện `/products/{slug}`; `<title>`, meta description, canonical, Open Graph và Twitter card trên mọi trang.
  Ảnh chia sẻ mặc định `images/og-default.png` (1200 × 630); sản phẩm có ảnh JPG / PNG / WebP thì dùng ảnh sản phẩm.
- `/sitemap.xml` sinh từ database (trang chủ, danh mục, mọi sản phẩm đang bán kèm `lastmod`, các trang tư vấn / báo giá / liên hệ), cache và tự làm mới khi admin sửa catalog.
- `/robots.txt` chặn `/admin`, `/account`, `/cart`, `/checkout`, `/wishlist`, `/api`, `/hubs` và trỏ tới sitemap. Các trang riêng tư có `noindex,nofollow`.
- Dữ liệu có cấu trúc JSON-LD: `FurnitureStore` + `WebSite` (ô tìm kiếm) ở trang chủ, `Product` + `BreadcrumbList` ở trang sản phẩm.

**Bảo mật** (ngoài các mục đã có: băm mật khẩu Identity, phân quyền role ở server, anti-forgery cho mọi POST, FluentValidation, EF Core tham số hóa, Razor mã hóa HTML, kiểm tra chữ ký file upload)
- **Content-Security-Policy** chặt: `script-src 'self'` (không có script nội tuyến hay script bên thứ ba; mọi JS nằm trong `wwwroot/js`), chỉ cho phép Google Fonts, bản đồ Google Maps và WebSocket tới chính website; `object-src 'none'`, `frame-ancestors 'self'`.
- Header: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`, HSTS (production); ẩn header `Server`.
- Cookie đăng nhập / giỏ hàng / chat / AI: `HttpOnly`, `Secure`, `SameSite=Lax`. Khóa Data Protection được lưu bền (và mã hóa DPAPI trên Windows) để người dùng không bị đăng xuất khi khởi động lại.
- Giới hạn tần suất: đăng nhập / đăng ký (10/phút), form liên hệ / đánh giá / báo giá (5/phút), AI (`AI:RequestsPerMinute`), toàn bộ API (300/phút). Chạy sau nginx / load balancer: khai báo `ReverseProxy:KnownProxies`.
- Log: access log chỉ ghi method, đường dẫn (không có query string), mã trạng thái, thời gian. Có test tự động đảm bảo log **không bao giờ** chứa mật khẩu, API key hay token.
- Thông báo lỗi luôn bằng tiếng Việt và không lộ chi tiết kỹ thuật: mọi lỗi API (kể cả 400 thiếu token, 415, JSON sai định dạng) trả về
  `{ success, message, data, errors }`; trang mở quá lâu / đăng nhập ở tab khác làm token CSRF hết hạn thì hiện
  "Phiên làm việc đã hết hạn. Vui lòng tải lại trang rồi thử lại."; giá trị sai kiểu trong form (chữ trong ô số, ô số để trống)
  không bao giờ bị lưu thành 0 mà form hiện lại với ô lỗi được tô đỏ.

**Hiệu năng**
- Nén Brotli / Gzip cho HTML, JSON, CSS, JS, SVG, XML (trang chủ ~24KB khi truyền).
- Cache trình duyệt: file có `?v=` 1 năm (`immutable`), thư viện và ảnh 7 ngày, ảnh upload 30 ngày.
- Ảnh upload được thu nhỏ khi lưu; thẻ sản phẩm tải bản 480 px (`srcset`, màn hình nét cao lấy bản 1200 px).
- Dữ liệu catalog dùng chung (menu, bộ lọc, trang chủ, sitemap) cache trong bộ nhớ, tự xóa khi admin thay đổi. Truy vấn nhiều collection dùng split query (test sẽ báo lỗi nếu có truy vấn gây "cartesian explosion").
- Đo trên máy dev (LocalDB, 10 kết nối song song): trang chủ ~620 req/s (p95 31 ms), danh sách sản phẩm ~860–1000 req/s (p95 12–17 ms), chi tiết sản phẩm ~560 req/s (p95 21 ms).

**Responsive & khả năng truy cập**: kiểm tra tự động bằng trình duyệt thật ở 360 / 390 / 1366 px trên 33 trang (khách, tài khoản, admin): không tràn ngang, không lỗi console / vi phạm CSP, không còn lỗi axe-core mức nghiêm trọng (tương phản màu, nhãn form, ARIA).

### Checklist triển khai production

1. `ASPNETCORE_ENVIRONMENT=Production`, `ApplicationSettings__BaseUrl=https://ten-mien-cua-ban`.
2. `ConnectionStrings__DefaultConnection` tới SQL Server; tạo database bằng `database/FurnitureStoreDb_full.sql` hoặc `dotnet ef database update`.
3. `Seed__AdminPassword` (mật khẩu mạnh) cho lần chạy đầu; đổi mật khẩu sau khi đăng nhập.
4. Email thật: `Email__Mode=Smtp` + `Email__Smtp__*`.
5. (Tuỳ chọn) `AI__ApiKey`.
6. `DataProtection__KeysPath` trỏ tới thư mục bền vững, có quyền ghi (dùng chung nếu nhiều server).
7. Chạy sau nginx / load balancer: `ReverseProxy__KnownProxies__0=<IP proxy>`; nhiều server cần sticky session hoặc backplane cho SignalR (mục 14).
8. Máy staging: `Seo__AllowIndexing=false`.
9. Chứng chỉ HTTPS hợp lệ (HSTS được bật ở production).

## 17. Kiểm thử

### Test tự động (xUnit)

```powershell
dotnet test                      # 705 test: unit, service và integration qua HTTP (SQLite in-memory), migration trên SQL Server LocalDB
dotnet test -c Release           # cùng bộ test trên bản build Release
```

Nhóm test chính: domain rules, tính giá báo giá, parser tiếng Việt của AI, giỏ hàng / checkout / tồn kho, quản lý và áp dụng mã giảm giá, luồng trạng thái đơn,
phân quyền từng trang và API admin, anti-forgery, CSP / header bảo mật / cache / nén, sitemap & SEO, log không chứa bí mật,
thông báo lỗi tiếng Việt, chat realtime (SignalR client thật), khởi động database.

### Kiểm thử trình duyệt (`tests/e2e`)

Chạy bằng Edge / Chrome headless (puppeteer-core). Cần **Node.js 18+** và Microsoft Edge (hoặc đặt `BROWSER_PATH` tới Chrome).

| Bộ | Kiểm tra |
|---|---|
| `dod-e2e.js` | Definition of Done: chặn truy cập admin, đăng ký / đăng xuất / đăng nhập, chọn variant, giỏ hàng, coupon, checkout COD, trừ tồn kho, email xác nhận, admin xử lý đơn đến "Đã giao", đánh giá sau khi mua, wishlist, admin tạo danh mục + sản phẩm 2 variant + ảnh, sửa giá, xóa, CSRF (43 bước) |
| `coupon-e2e.js` | Mã giảm giá: admin tạo mã (mã ngẫu nhiên, xem trước), khách thấy ưu đãi và áp bằng một cú nhấp ở giỏ hàng, bỏ / nhập sai / nhập đúng mã ở trang liên hệ đặt hàng không tải lại trang (giữ địa chỉ đang nhập), đặt hàng, thống kê lượt dùng, không xóa được mã đã dùng, tắt mã, hủy đơn trả lại lượt (23 bước) |
| `dining-e2e.js` | Bàn ghế ăn: trang chủ / menu ưu tiên bộ bàn ăn, lọc theo "Bàn 1m2 + 4 ghế" / "Bàn 1m6 + 6 ghế", giá theo cỡ, giỏ hàng và trang **liên hệ đặt hàng** không tính phí giao, đơn COD, email, admin nhập phí giao đã báo → tổng tiền, phiếu giao, trang đơn của khách cập nhật; phí âm bị từ chối; menu một hàng ở 1366 / 1100 px, điện thoại không tràn ngang; trợ lý trả lời phí giao / gỗ sồi Nga (40 bước) |
| `qr-e2e.js` | Mã QR: chụp mã đang hiển thị và **giải mã thật** (jsQR) ở trang sản phẩm, thẻ admin, tem in, trang đặt hàng thành công, trang đơn, phiếu giao hàng, email; mở địa chỉ giải được: trang sản phẩm (kể cả sau khi đổi URL), khách chưa đăng nhập → đăng nhập → đúng đơn, khách khác → 404, admin → trang quản lý đơn (18 bước) |
| `cart-select-e2e.js` | Giỏ hàng tích chọn: mặc định đã chọn, bỏ chọn / chọn tất cả cập nhật tổng tiền tại chỗ (không tải lại trang, giữ focus bàn phím), không chọn gì thì không vào được trang đặt hàng, chỉ sản phẩm đã chọn được đặt và sản phẩm còn lại ở lại giỏ; địa chỉ: chưa chọn tỉnh thì chưa chọn được phường / xã, danh sách phường / xã tải theo tỉnh (nhóm Phường / Xã / Đặc khu), địa chỉ đã lưu điền đúng tỉnh + phường, phường cũ trước 07/2025 được nhắc chọn lại, sổ địa chỉ không còn Quận / Huyện; điện thoại không tràn ngang (22 bước) |
| `uploads-e2e.js` | Ảnh tải lên: chọn avatar là tự tải lên (không cần nút), ảnh chụp > 20 MB được nhận và hiện ở hồ sơ + header (256 × 256), avatar admin hiện ở thanh trên trang quản trị, ảnh > 50 MB bị báo ngay trên trình duyệt; admin tải ảnh ngang + dọc: lưu 1200 × 900 / 900 × 1200 kèm bản 480 px, không méo; gallery hiện trọn ảnh dọc, thẻ sản phẩm dùng bản 480 px (1200 px trên màn hình nét cao); thông báo chưa đọc nổi bật, bấm vào thì mở trang liên quan, chuông giảm 1 và thông báo chuyển sang đã đọc (17 bước) |
| `branding-e2e.js` | Tên cửa hàng & banner: xem trước logo khi gõ tên, đổi tên thì logo header / footer / sidebar admin và tiêu đề trang đổi theo, tên rất dài tự xuống dòng không làm vỡ header (1440 / 1200 / 992 / 375 px); banner: xem trước đổi theo khi gõ, ảnh chọn được xem trước, link `javascript:` bị từ chối (giữ nội dung đã gõ), lưu thì trang chủ hiện đúng chữ / nút / số liệu / ảnh (1400 px kèm bản 700 px cho điện thoại, không méo), khôi phục mặc định xóa ảnh; logo: xem trước khung logo, ảnh chọn được xem trước, "logo đã có tên" ẩn chữ, logo ngang hiện cao 40 px giữ tỉ lệ ở header / sidebar admin (không dùng làm favicon), bỏ logo thì xóa ảnh; TikTok gõ `@tenshop` được lưu thành link đầy đủ và hiện ở chân trang (29 bước) |
| `forms-resubmit.js` | Mọi form sửa của admin / khách gửi lại nguyên trạng đều lưu được; giá trị sai kiểu bị từ chối, không lưu |
| `chat-e2e.js` | Khách chat từ trang sản phẩm ↔ admin trả lời realtime, chống chèn HTML, bố cục mobile |
| `chat-offline-e2e.js` | Chat khi mất realtime: chặn kết nối SignalR của admin → trang báo mất kết nối, cuộc trò chuyện / tin nhắn mới vẫn hiện (tự cập nhật), admin vẫn trả lời được; bỏ chặn → tự kết nối lại, tin nhắn tức thì; khách mất realtime vẫn nhận được trả lời (10 bước) |
| `ai-e2e.js` | Trợ lý AI (widget, trang tư vấn, gợi ý màu / phong cách), bố cục mobile |
| `local-ai-e2e.js` | Trợ lý không có AI: chip "Giao hàng & bảo hành", giờ mở cửa, hotline (gõ không dấu), mã giảm giá, so sánh gỗ, kích thước bàn ăn, "cảm ơn", câu không hiểu, tìm sản phẩm; trang sản phẩm (giao hàng, mẫu rẻ hơn, màu); khách đã đăng nhập hỏi đơn hàng của mình; giao diện điện thoại (16 bước) |
| `quote-e2e.js` | Báo giá đặt đóng: tính giá, phương án rẻ hơn, gửi yêu cầu, admin báo giá, khách đồng ý |
| `account-forms.js` | Đăng ký (điều khoản), cập nhật hồ sơ, đổi mật khẩu, quên mật khẩu |
| `audit.js` | 42 trang × (1366 / 390 / 360 px): lỗi console / vi phạm CSP, tràn ngang, lỗi axe-core nghiêm trọng |
| `load.js` | Tải thử 300 request × 7 URL, 10 kết nối song song (chạy riêng: `node load.js`) |

```powershell
# 1. Chạy website (terminal khác):  dotnet run --project src/FurnitureStore.Web --launch-profile https
# 2. Chạy toàn bộ (tự npm install lần đầu; mật khẩu admin đọc từ user-secrets Seed:AdminPassword hoặc biến ADMIN_PW):
powershell -ExecutionPolicy Bypass -File tests\e2e\run-all.ps1
# Chỉ vài bộ / website và database khác:
powershell -ExecutionPolicy Bypass -File tests\e2e\run-all.ps1 -Suites dod-e2e.js,audit.js -BaseUrl https://localhost:7443 -Database FS_Test
```

> Trợ lý giới hạn `AI:RequestsPerMinute` (mặc định 10) câu hỏi / phút cho mỗi người: khi chạy E2E, khởi động website với
> `$env:AI__RequestsPerMinute = "1000"` để các bộ `ai-e2e.js` / `local-ai-e2e.js` không bị báo "gửi quá nhiều yêu cầu".
>
> ⚠️ Các bộ kiểm thử **tạo dữ liệu thật** (tài khoản, đơn hàng, đánh giá, chat, báo giá, danh mục / sản phẩm thử) và dùng
> `sqlcmd` để đối chiếu database. Chỉ chạy trên database dev / test, **không** chạy trên website đang bán hàng.
> Ảnh chụp màn hình và `audit.json` nằm trong `tests/e2e/output/`.

## 18. Build Release & triển khai

```powershell
dotnet publish src/FurnitureStore.Web -c Release -o publish
```

Thư mục `publish/` (~24 MB) chứa ứng dụng, `wwwroot` (CSS / JS / ảnh / thư viện) và `web.config` cho IIS. Gói **không** chứa
`appsettings.Development.json`, ảnh khách upload (`wwwroot/uploads`), `App_Data` (email dev, khóa Data Protection) hay secret nào.
Cấu hình production đặt bằng biến môi trường (mục 10 và [checklist](#checklist-triển-khai-production)).

Quy trình đã kiểm tra:

1. Tạo database bằng `database/FurnitureStoreDb_full.sql` (chạy lại nhiều lần vẫn an toàn).
2. Chạy bản publish với `ASPNETCORE_ENVIRONMENT=Production` và `Seed__AdminPassword`: lần chạy đầu tạo tài khoản admin
   (Production không tự migrate, không tạo dữ liệu demo khách hàng / đơn hàng).
3. Đăng nhập `/admin`, đổi mật khẩu admin, cập nhật thông tin cửa hàng (`/admin/store`).

Ví dụ chạy thử bản publish trên Windows:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:ASPNETCORE_URLS = "https://localhost:7443"
$env:ConnectionStrings__DefaultConnection = "Server=.;Database=FurnitureStoreDb;Trusted_Connection=True;TrustServerCertificate=True"
$env:Seed__AdminPassword = "<mật khẩu mạnh>"
.\publish\FurnitureStore.Web.exe
```

- **IIS**: cài *ASP.NET Core 8 Hosting Bundle*, tạo site trỏ tới thư mục `publish`, application pool *No Managed Code*;
  cấp quyền ghi cho tài khoản app pool trên `wwwroot/uploads`, `App_Data` (hoặc thư mục đặt trong `DataProtection__KeysPath`,
  `Email__PickupDirectory`). Biến môi trường đặt trong `web.config` (`<environmentVariables>`) hoặc cấu hình app pool.
- **Cập nhật phiên bản**: dừng site, chép đè nội dung `publish/` mới lên thư mục cũ. **Không xóa** `wwwroot/uploads`
  (ảnh sản phẩm / đánh giá / avatar) và `App_Data` (khóa Data Protection); nên sao lưu hai thư mục này cùng database.
  Có migration mới thì chạy `database/01_schema.sql` mới (idempotent) trước khi khởi động lại.
- **Hosting dùng chung (Somee.com, …)**: xem mục [Triển khai lên Somee](#triển-khai-lên-somee) ngay dưới.
- **Linux**: `dotnet publish -c Release -r linux-x64 --self-contained false`, chạy bằng systemd sau nginx
  (nginx chuyển tiếp `/hubs/` với header `Upgrade` / `Connection` cho WebSocket; khai báo `ReverseProxy__KnownProxies__0`).

### Triển khai lên Somee

Lỗi **"HTTP Error 500.30 - ASP.NET Core app failed to start"** nghĩa là ứng dụng dừng ngay khi khởi động. Nguyên nhân thường gặp:
connection string vẫn là `(localdb)\MSSQLLocalDB` (chỉ có trên máy dev - lúc khởi động ứng dụng kết nối database để kiểm tra
migration nên dừng luôn), hoặc file `appsettings.Production.json` sai cú pháp JSON.

1. **Database**: trong trang quản lý Somee tạo *MS SQL database*, ghi lại connection string Somee đưa
   (dạng `workstation id=...;packet size=4096;user id=...;pwd=...;data source=....mssql.somee.com;persist security info=False;initial catalog=...;TrustServerCertificate=True`).
   Mở công cụ chạy SQL của Somee (hoặc SSMS kết nối tới server Somee) và chạy **lần lượt** `database/01_schema.sql` rồi
   `database/02_seed_data.sql`. **Không** dùng `FurnitureStoreDb_full.sql` (có `CREATE DATABASE` / `USE [FurnitureStoreDb]`,
   tên database trên Somee khác và tài khoản không có quyền tạo database).
2. **Publish**: `dotnet publish src/FurnitureStore.Web -c Release -o publish`, rồi tải **toàn bộ nội dung** thư mục `publish/`
   (gồm `web.config`, thư mục `wwwroot`, `logs`) lên thư mục gốc của site (FTP hoặc File Manager của Somee).
3. **Cấu hình trên server** (không commit, đã có trong `.gitignore`): tạo file `appsettings.Production.json` cạnh `web.config`:

   ```json
   {
     "ConnectionStrings": {
       "DefaultConnection": "<connection string Somee đưa>"
     },
     "Seed": {
       "AdminPassword": "<mật khẩu admin mạnh, ≥ 8 ký tự, có hoa / thường / số>"
     },
     "ApplicationSettings": {
       "BaseUrl": "https://<tên-site>.somee.com"
     },
     "DataProtection": {
       "Dpapi": "LocalMachine"
     }
   }
   ```

   - Dấu `\` trong JSON phải viết `\\`; thiếu dấu phẩy / thừa dấu phẩy cũng làm ứng dụng không khởi động (500.30).
   - `DataProtection:Dpapi = LocalMachine`: hosting dùng chung thường không nạp user profile cho app pool, mã hóa khóa
     theo user (mặc định) sẽ lỗi khi đăng nhập / gửi form.
   - Lần chạy đầu tạo tài khoản `admin@furniture.local` với mật khẩu trên. Đăng nhập, đổi mật khẩu, rồi xóa dòng
     `AdminPassword` khỏi file.
   - (Tùy chọn) `"AI": { "ApiKey": "..." }` nếu dùng trợ lý AI bằng OpenAI.
4. Mở `https://<tên-site>.somee.com/health` → `Healthy` là kết nối database đã đúng.

**Vẫn gặp 500.30**: sửa `web.config` trên server thành `stdoutLogEnabled="true"`, tải lại trang, mở file mới nhất trong thư mục
`logs` (`stdout_*.log`). Dòng `crit:` cho biết nguyên nhân, ví dụ
`Cannot open or prepare the database at startup (server '...', database '...')` (log chỉ ghi tên server / database,
không ghi mật khẩu). Xem xong đặt lại `stdoutLogEnabled="false"` (file log lớn dần).

**Cập nhật bản mới**: chép đè nội dung `publish/` mới (giữ `appsettings.Production.json`, `wwwroot/uploads`, `App_Data`);
có migration mới thì chạy `database/01_schema.sql` mới trên database Somee trước (chạy lại nhiều lần vẫn an toàn).

## 19. Mã giảm giá

**Quản trị** (`/admin/coupons`, chỉ ADMIN; mọi thay đổi được ghi nhật ký hoạt động):

- Danh sách tìm theo mã / tên, lọc theo trạng thái được tính tự động: *Đang chạy*, *Chưa bắt đầu*, *Hết hạn*, *Hết lượt*, *Đã tắt*.
- Thêm / sửa: mã (3–30 ký tự, tự viết hoa; có nút tạo mã ngẫu nhiên), tên chương trình, mô tả cho khách,
  giảm theo **%** (có thể đặt mức giảm tối đa) hoặc **số tiền cố định**, đơn tối thiểu, thời gian bắt đầu / kết thúc (giờ Việt Nam),
  tổng số lượt, số lượt mỗi khách, bật / tắt, **công khai** (hiện cho khách) hay riêng tư (khách phải tự nhập).
  Form có dòng *"Khách sẽ thấy: …"* xem trước ngay khi nhập.
- Trang chi tiết: lượt đã dùng / tổng lượt, tổng tiền đã giảm, số đơn và doanh thu các đơn dùng mã, danh sách đơn (liên kết tới đơn hàng).
- Ràng buộc để giữ đúng lịch sử: mã đã dùng cho đơn hàng thì **không đổi mã và không xóa được** (chỉ tắt);
  tổng số lượt không được nhỏ hơn số lượt đã dùng. Đổi mã chưa dùng / xóa mã thì giỏ hàng đang giữ mã đó được cập nhật theo.

**Khách hàng**:

- Nhập mã ở giỏ hàng hoặc ở **trang liên hệ đặt hàng** (áp / bỏ mã không tải lại trang nên thông tin giao hàng đang nhập được giữ nguyên).
- Mục **"Ưu đãi dành cho bạn"** liệt kê tối đa 5 mã công khai đang chạy: mã dùng được có nút *Áp dụng* và số tiền tiết kiệm;
  mã chưa đủ điều kiện ghi rõ lý do (ví dụ *"Mua thêm 1.200.000₫ để dùng mã này"*, *"Bạn đã sử dụng hết lượt…"*).
- Điều kiện được kiểm tra lại ở server khi đặt hàng; lượt dùng được trừ nguyên tử (hai người đặt cùng lúc không vượt tổng lượt)
  và được trả lại khi đơn bị hủy. Mã giảm giá hiện trong email xác nhận, trang đơn hàng của khách và của admin.

Dữ liệu mẫu: `CHAOBAN10` (10%, tối đa 2 triệu, đơn từ 5 triệu, mỗi khách 1 lần) và `GIAM500K` (đơn từ 10 triệu, 100 lượt) là mã công khai;
`HETHAN` là mã riêng tư đã hết hạn dùng để kiểm thử.

## 20. Mã QR sản phẩm & đơn hàng

Mỗi sản phẩm và mỗi đơn hàng **tự có mã QR riêng**, sinh ngay khi cần (không lưu file, không cần thao tác thêm).
Thư viện: `Net.Codecrete.QrCodeGenerator` (MIT, không phụ thuộc thư viện đồ họa), mức sửa lỗi M (15%) để tem in bị trầy vẫn quét được.

| | Sản phẩm | Đơn hàng |
|---|---|---|
| Mã chứa | `{BaseUrl}/q/p/{id}` | `{BaseUrl}/q/o/{mã đơn}` |
| Quét mở | Trang chi tiết sản phẩm hiện tại. Mã **không đổi** khi đổi tên / đường dẫn (slug) nên tem đã in vẫn dùng được. Sản phẩm nháp / ẩn: khách thấy 404, admin được đưa vào trang sửa | Admin → trang quản lý đơn; khách đặt đơn → trang đơn của mình; chưa đăng nhập → trang đăng nhập rồi quay lại; người khác → 404 |
| Hiện ở | Nút *"Mã QR sản phẩm"* trên trang sản phẩm (tải PNG), thẻ QR trong trang sửa sản phẩm của admin, **tem in** | Trang đặt hàng thành công, trang đơn của khách, email xác nhận, thẻ QR trong trang đơn của admin, **phiếu giao hàng** |

- **In tem QR** (admin): nút *"In tem QR"* ở danh sách sản phẩm in toàn bộ sản phẩm đang lọc (tối đa 100 tem, khổ A4, 3 tem mỗi hàng:
  QR, tên, SKU, giá, cửa hàng); nút *"In tem QR"* trong trang sửa sản phẩm in một tem. Dán tại showroom để khách quét xem chi tiết.
- **Phiếu giao hàng** (admin, trang đơn → *"In phiếu giao hàng"*): QR đơn hàng, người nhận, sản phẩm, tổng tiền, số tiền thu hộ COD
  (gồm phí giao hàng đã nhập; nếu chưa nhập, phiếu ghi rõ *"Chưa gồm phí giao hàng & lắp đặt"*), ô ký nhận.
- **Bảo mật**: mã đơn chỉ chứa địa chỉ, không chứa thông tin khách. Khi chưa đăng nhập, hệ thống không tra cứu đơn nên không thể dò
  mã nào tồn tại; ảnh QR đơn hàng được vẽ cho mọi mã đúng định dạng vì cùng lý do (và để email hiển thị được).
- Địa chỉ trong mã lấy từ `ApplicationSettings:BaseUrl` — **phải là tên miền thật khi triển khai**. Muốn thử bằng điện thoại trong mạng LAN
  khi phát triển: đặt `ApplicationSettings__BaseUrl=http://<IP máy>:5243` và chạy `dotnet run --urls http://0.0.0.0:5243`.

## 21. Bàn ghế ăn & liên hệ đặt hàng

**Mặt hàng chính: bộ bàn ghế ăn gỗ sồi Nga, màu óc chó**

- Trang chủ mở đầu bằng *"Bộ bàn ăn gỗ sồi Nga - màu óc chó"*, ngay sau đó là mục **Bộ bàn ăn** (các bộ bán chạy) với lối tắt chọn theo cỡ
  *"Bàn 1m2 + 4 ghế"*, *"Bàn 1m6 + 6 ghế"*. Menu chính có mục **Bộ bàn ăn**. Các nhóm khác (sofa, giường, tủ...) vẫn bán bình thường.
  Danh mục được ưu tiên đặt bằng `ApplicationSettings:FocusCategorySlug` (mặc định `bo-ban-an`).
- Dữ liệu mẫu thêm chất liệu **Gỗ sồi Nga**, 2 kích thước bộ **Bàn 1m2 + 4 ghế** (`set-4-ghe-120`) và **Bàn 1m6 + 6 ghế** (`set-6-ghe-160`),
  cùng 6 bộ bàn ăn màu **Nâu óc chó**: An Gia, Phúc Lộc, Mộc Nhiên (thêm màu gỗ tự nhiên), Tâm An, Thịnh Gia (mặt đá ceramic),
  Bình Minh (ghế nệm). **Giá chỉ để minh họa** (khoảng 6,9 – 11,5 triệu bản 4 ghế; 10,5 – 16,9 triệu bản 6 ghế) — sửa trong
  `/admin/products` (giá, tồn kho từng phiên bản). Bảng giá báo giá đặt đóng có thêm đơn giá *Vật liệu: Gỗ sồi Nga* (`/admin/price-rules`).

**Trang "Liên hệ đặt hàng" thay cho trang thanh toán** (`/checkout`, vẫn **bắt buộc đăng nhập**)

- Khách chọn mẫu → giỏ hàng → *"Liên hệ đặt hàng"* (hoặc nút *"Liên hệ đặt hàng"* ngay trên trang sản phẩm) → điền họ tên, số điện thoại,
  email, địa chỉ, ghi chú → *"Gửi yêu cầu đặt hàng"*. **Không có bước thanh toán** và không nhập thông tin thẻ; đơn được tạo ở trạng thái
  *Chờ xác nhận*, hình thức *thanh toán khi nhận hàng* (hoặc chuyển khoản khi nhân viên hướng dẫn).
- Trang thành công, email xác nhận (*"Đã nhận yêu cầu đặt hàng …"*) và thông báo cho admin (*"… Gọi lại xác nhận và báo phí giao hàng"*)
  đều nói rõ cửa hàng sẽ gọi lại.

**Không tự tính phí vận chuyển**

- Giỏ hàng, trang liên hệ đặt hàng, email hiển thị *"Giao hàng & lắp đặt: Cửa hàng báo khi liên hệ"*; tổng là **tổng tiền hàng**
  (sau giảm giá). Cấu hình `Shipping` cũ (miễn phí từ 10 triệu, phí 300.000₫) đã bỏ.
- Sau khi gọi khách, admin nhập **phí giao hàng & lắp đặt đã báo** ở trang chi tiết đơn (`/admin/orders/details/{id}`, ô *"Phí giao hàng & lắp đặt"*;
  0 = miễn phí). Tổng đơn, số tiền thu hộ trên phiếu giao, trang đơn của khách được cập nhật; khách nhận thông báo và email; thao tác được ghi
  nhật ký. Chỉ sửa được khi đơn chưa giao, chưa hủy, chưa thanh toán; phí 0 – 100 triệu; kiểm tra ở backend (quyền ADMIN) và chống sửa đè
  khi hai người cùng cập nhật.
- Trợ lý tự động trả lời "phí ship bao nhiêu", "cách đặt hàng", "thanh toán thế nào", "gỗ sồi Nga có bền không" theo cách bán mới.

**Database đã có từ trước** (đã chạy file SQL cũ): chạy ứng dụng ở môi trường **Development** (mặc định khi bấm Run trong Visual Studio,
`Database:SeedDemoData = true`) một lần — ứng dụng tự **bổ sung** chất liệu, 2 cỡ bộ bàn ăn, 6 bộ bàn ăn mới và đơn giá gỗ sồi Nga,
đồng thời cập nhật các nội dung mặc định nhắc tới "miễn phí giao hàng" (chỉ những nội dung admin **chưa sửa**). Sản phẩm admin đã xóa không bị
thêm lại. Ở Production (`SeedDemoData = false`) không có dữ liệu mẫu nào được thêm. Database mới thì chỉ cần chạy file SQL mới trong `database/`.

## 22. Giỏ hàng chọn từng sản phẩm & địa chỉ Tỉnh → Phường / Xã

**Tích chọn sản phẩm trong giỏ hàng**

- Mỗi sản phẩm trong giỏ có ô tích, kèm ô *"Chọn tất cả"*. Sản phẩm mới thêm được chọn sẵn. Tạm tính, giảm giá, tổng tiền và điều kiện
  mã giảm giá **chỉ tính các sản phẩm đã chọn**; nút ghi rõ số lượng: *"Liên hệ đặt hàng (2)"*.
- Trang liên hệ đặt hàng chỉ liệt kê và chỉ đặt các sản phẩm đã chọn; sản phẩm không chọn **vẫn ở lại giỏ hàng** (lựa chọn được lưu ở
  server nên giữ nguyên khi tải lại trang hay đổi thiết bị). Không chọn gì thì không vào được trang đặt hàng.
- Nút *"Liên hệ đặt hàng"* trên trang sản phẩm chỉ chọn đúng sản phẩm đó (các sản phẩm khác trong giỏ được giữ lại cho lần sau).
- Sản phẩm hết hàng / ngừng bán không chọn được; nếu đang chọn mà hết hàng thì cửa hàng nhắc bỏ chọn, sản phẩm không chọn không chặn đơn.
- Tích / bỏ tích cập nhật ngay tại chỗ (không tải lại trang); không có JavaScript thì dùng form thường.

**Địa chỉ 2 cấp: Tỉnh / Thành phố → Phường / Xã** (từ 01/07/2025 không còn cấp Quận / Huyện)

- Đã **bỏ ô Quận / Huyện** ở trang liên hệ đặt hàng và sổ địa chỉ. Địa chỉ cũ có quận / huyện vẫn hiển thị như trước.
- Chọn tỉnh / thành rồi chọn phường / xã từ danh sách (nhóm *Phường*, *Xã*, *Đặc khu*, sắp xếp theo tiếng Việt; gõ chữ cái đầu tên để nhảy nhanh).
  Danh sách lấy qua API của chính website:
  `GET /api/locations/provinces` (34 tỉnh / thành) và `GET /api/locations/provinces/{mã}/wards` (3.321 phường / xã / đặc khu).
- Dữ liệu đóng gói sẵn trong ứng dụng (`src/FurnitureStore.Application/Sales/Data/vietnam-administrative-units.json`, nguồn
  provinces.open-api.vn — mã của Tổng cục Thống kê), nên không phụ thuộc dịch vụ bên ngoài khi chạy. Server kiểm tra phường / xã
  **phải thuộc** tỉnh đã chọn.
- Địa chỉ đã lưu với tên phường cũ (ví dụ "Phường Bến Nghé", nay thuộc "Phường Sài Gòn") được nhắc chọn lại.

**Database đã có**: cần thêm cột `CartItems.IsSelected` (migration `AddCartItemSelection`). Chạy app ở Development (tự áp migration) hoặc chạy
`database/01_schema.sql` mới. Các sản phẩm đang có trong giỏ được giữ ở trạng thái đã chọn.

## 23. Ảnh tải lên tự thu nhỏ & thông báo quản trị

**Ảnh tải lên** (ảnh sản phẩm, ảnh đánh giá, ảnh đại diện) — người dùng chỉ cần chọn ảnh, hệ thống tự xử lý:

| Loại | Lưu ở kích thước | Bản nhỏ | Cách vừa khung |
|---|---|---|---|
| Ảnh sản phẩm | tối đa 1200 × 1200 | 480 px (thẻ sản phẩm, ảnh thu nhỏ, giỏ hàng) | Giữ nguyên tỉ lệ; trang chi tiết hiện trọn ảnh (ảnh dọc có lề hai bên) |
| Ảnh đánh giá | tối đa 1280 × 1280 | 320 px (ảnh nhỏ dưới đánh giá) | Giữ nguyên tỉ lệ |
| Ảnh đại diện | 256 × 256 | — | Cắt vuông chính giữa |

- **Chỉ thu nhỏ, không bao giờ phóng to** (ảnh nhỏ giữ nguyên kích thước) và thu nhỏ bằng bộ lọc Lanczos3 nên ảnh không méo, không vỡ.
- Ảnh chụp bằng điện thoại được **xoay đúng chiều** theo EXIF; **thông tin máy ảnh / vị trí GPS bị xóa**. Ảnh thường lưu JPEG (chất lượng 85),
  ảnh có nền trong suốt lưu WebP. Một ảnh chụp 10 – 20 MB thường còn khoảng 100 – 300 KB.
- Dung lượng tối đa **50 MB mỗi ảnh** (mọi ảnh điện thoại / máy ảnh đều dưới mức này; chỉnh bằng `Storage:MaxFileSizeMb`, tối đa 50) và
  100 megapixel. Vẫn giữ một mức trần để máy chủ không bị gửi tệp khổng lồ làm treo. Chọn ảnh quá lớn thì trình duyệt báo ngay, không cần tải lên.
- Thư viện: **SixLabors.ImageSharp 2.1.13** (Apache-2.0, viết hoàn toàn bằng C#, chạy giống nhau trên Windows / Linux, không cần thư viện native).
- Chạy trên IIS: `web.config` trong project đã nâng giới hạn request của IIS (mặc định 30 MB) để tải được nhiều ảnh lớn cùng lúc.
- Ảnh đã tải lên trước bản này vẫn hiển thị bình thường (không có bản nhỏ).
- **Ảnh đại diện** (`/account/profile`): bấm vào ảnh (hoặc ô chọn tệp), chọn ảnh là **tự tải lên ngay** — không cần bấm thêm nút
  (nút "Tải lên" chỉ hiện khi trình duyệt tắt JavaScript). Ảnh hiện ở hồ sơ, menu tài khoản trên header và thanh trên của trang quản trị.

**Thông báo quản trị** (`/admin/notifications`, chuông ở thanh trên)

- Bấm vào một thông báo: thông báo đó được đánh dấu **đã đọc**, số trên chuông **giảm 1**, rồi mở trang liên quan (đơn hàng, chat, liên hệ...).
  Nút *"Đánh dấu đã đọc tất cả"* vẫn dùng được.
- Thông báo **chưa đọc nổi bật**: nền vàng sáng, viền cam bên trái, chấm đỏ, tiêu đề in đậm, nhãn *"Mới"*; đã đọc thì nền trắng, chữ nhạt.
- Chỉ mở được thông báo của quản trị (kiểm tra ở server, có anti-forgery); liên kết trong thông báo chỉ được mở nếu là trang của chính website.


## 24. Tên cửa hàng, logo & banner trang chủ

**Logo cửa hàng** (`/admin/store`, thẻ *Logo & tên cửa hàng*): tải ảnh PNG (nền trong suốt là đẹp nhất), JPG hoặc WEBP ≤ 50 MB —
ảnh tự thu nhỏ (tối đa 640 × 160 px, giữ tỉ lệ) và hiện **cao 40 px** thay biểu tượng ngôi nhà ở header, footer và sidebar quản trị.

- Ô *"Ảnh logo đã có tên cửa hàng - ẩn chữ bên cạnh"*: dùng khi ảnh đã viết sẵn tên (logo dạng chữ).
- Logo **vuông** còn được dùng làm biểu tượng tab trình duyệt (favicon) và ảnh đại diện khung chat; logo **ngang** thì giữ biểu tượng ngôi nhà ở 2 chỗ đó (thu nhỏ 16 px sẽ không đọc được).
- *"Bỏ ảnh logo"* quay về biểu tượng ngôi nhà và xóa ảnh khỏi `wwwroot/uploads/logos`. Ảnh cũ bị thay cũng được xóa.
- Khung **xem trước logo** đổi theo khi chọn ảnh / gõ tên.

**Mạng xã hội** (Facebook, TikTok, Zalo): hiện thành biểu tượng ở chân trang và trang Liên hệ, trợ lý AI cũng dùng để trả lời
"liên hệ qua đâu". Không cần gõ đủ link: `@tenshop` (TikTok) → `https://www.tiktok.com/@tenshop`, số điện thoại (Zalo) →
`https://zalo.me/0900000000`, `facebook.com/tenshop` → `https://facebook.com/tenshop`. Link `http://`, `javascript:`... vẫn bị từ chối.
(Bản trước, ô nhập TikTok bị Razor render ẩn đi do chuỗi `@@` trong placeholder - đã sửa và có test chống tái phát.)

**Tên cửa hàng** (`/admin/store`) giờ được dùng ở **mọi nơi**: logo ở header / footer / sidebar quản trị, tiêu đề tab trình duyệt
(`Trang | Tên cửa hàng`), thẻ chia sẻ mạng xã hội, khung chat, trợ lý AI, email gửi khách (đơn hàng, báo giá, tài khoản).
Lưu xong là thấy ngay, không cần khởi động lại.

- **Dòng chữ nhỏ dưới logo** (mới, tùy chọn): nếu tên kết thúc bằng chữ này thì logo tách thành 2 dòng —
  tên `Nhà Mộc Furniture` + dòng nhỏ `Furniture` → logo **Nhà Mộc** / FURNITURE. Tên không kết thúc bằng chữ đó thì logo hiện cả tên
  và dòng nhỏ bên dưới; để trống thì logo chỉ hiện tên. Form có ô **xem trước logo** đổi theo khi gõ.
- Tên dài tự xuống dòng (tối đa 2 dòng) trong logo, không đẩy menu hay làm trang bị cuộn ngang trên điện thoại.
- Database cũ được migration gán sẵn dòng nhỏ `Furniture` (logo trước đây luôn hiện chữ này).
- Email người gửi (`Email:FromName` trong cấu hình) vẫn đọc từ appsettings / biến môi trường.

**Banner trang chủ** (`/admin/banner`, menu *Hệ thống → Banner trang chủ*, chỉ ADMIN):

| Phần | Ghi chú |
|---|---|
| Dòng chữ nhỏ phía trên, tiêu đề, phần cuối tiêu đề (chữ nghiêng màu gỗ), mô tả | Tiêu đề bắt buộc; ô trống thì phần đó được ẩn; mô tả giữ xuống dòng |
| Nút chính / nút phụ: chữ + đường dẫn | Trang trong website (`/products?onSale=true`), link `https://...` hoặc `tel:0900000000`. Nút chính để trống đường dẫn → trang mặt hàng chính. Xóa chữ để ẩn nút. Link `javascript:` / `//...` bị từ chối |
| 3 số liệu nổi bật (con số + mô tả) | Để trống cả hai ô để ẩn |
| Ảnh bên phải | JPG / PNG / WEBP ≤ 50 MB, **tự thu nhỏ** tối đa 1400 px (kèm bản 700 px cho điện thoại), giữ tỉ lệ, xóa EXIF / GPS; PNG nền trong suốt lưu WebP. Có ô mô tả ảnh (alt) và lựa chọn quay lại ảnh minh họa mặc định |

- Phần **Xem trước** ở đầu trang đổi theo ngay khi gõ / chọn ảnh; trang chủ chỉ đổi khi bấm **Lưu banner**.
- **Khôi phục banner mặc định**: xóa nội dung đã chỉnh và ảnh đã tải lên, trang chủ quay về banner gốc.
- Chưa lưu lần nào thì trang chủ dùng banner gốc (bảng `HomeBanners` trống). Mỗi lần lưu / khôi phục được ghi vào nhật ký hoạt động.
- Ảnh cũ bị thay hoặc bỏ được xóa khỏi `wwwroot/uploads/banners`.
