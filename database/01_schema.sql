SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AIKnowledgeEntries] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(200) NOT NULL,
        [Content] nvarchar(4000) NOT NULL,
        [Category] nvarchar(50) NOT NULL,
        [Keywords] nvarchar(500) NULL,
        [IsActive] bit NOT NULL,
        [DisplayOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_AIKnowledgeEntries] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoles] (
        [Id] nvarchar(450) NOT NULL,
        [Description] nvarchar(256) NULL,
        [Name] nvarchar(256) NULL,
        [NormalizedName] nvarchar(256) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUsers] (
        [Id] nvarchar(450) NOT NULL,
        [FullName] nvarchar(150) NOT NULL,
        [AvatarUrl] nvarchar(500) NULL,
        [DateOfBirth] date NULL,
        [Gender] nvarchar(20) NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [LastLoginAt] datetime2 NULL,
        [UserName] nvarchar(256) NULL,
        [NormalizedUserName] nvarchar(256) NULL,
        [Email] nvarchar(256) NULL,
        [NormalizedEmail] nvarchar(256) NULL,
        [EmailConfirmed] bit NOT NULL,
        [PasswordHash] nvarchar(max) NULL,
        [SecurityStamp] nvarchar(max) NULL,
        [ConcurrencyStamp] nvarchar(max) NULL,
        [PhoneNumber] nvarchar(max) NULL,
        [PhoneNumberConfirmed] bit NOT NULL,
        [TwoFactorEnabled] bit NOT NULL,
        [LockoutEnd] datetimeoffset NULL,
        [LockoutEnabled] bit NOT NULL,
        [AccessFailedCount] int NOT NULL,
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NULL,
        [UserName] nvarchar(256) NULL,
        [Action] nvarchar(40) NOT NULL,
        [EntityName] nvarchar(100) NOT NULL,
        [EntityId] nvarchar(100) NULL,
        [OldValues] nvarchar(max) NULL,
        [NewValues] nvarchar(max) NULL,
        [Description] nvarchar(1000) NULL,
        [IpAddress] nvarchar(45) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL IDENTITY,
        [ParentId] int NULL,
        [Name] nvarchar(150) NOT NULL,
        [Slug] nvarchar(170) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ImageUrl] nvarchar(500) NULL,
        [IconCssClass] nvarchar(60) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [ShowOnHomePage] bit NOT NULL,
        [MetaTitle] nvarchar(200) NULL,
        [MetaDescription] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Categories_Categories_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ContactMessages] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [Subject] nvarchar(200) NULL,
        [Message] nvarchar(4000) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [UserId] nvarchar(450) NULL,
        [IpAddress] nvarchar(45) NULL,
        [AdminNote] nvarchar(1000) NULL,
        [RepliedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ContactMessages] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Coupons] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(50) NOT NULL,
        [Name] nvarchar(150) NOT NULL,
        [Description] nvarchar(500) NULL,
        [DiscountType] nvarchar(40) NOT NULL,
        [DiscountValue] decimal(18,2) NOT NULL,
        [MaxDiscountAmount] decimal(18,2) NULL,
        [MinOrderAmount] decimal(18,2) NOT NULL,
        [StartsAt] datetime2 NULL,
        [EndsAt] datetime2 NULL,
        [UsageLimit] int NULL,
        [UsageLimitPerUser] int NULL,
        [UsedCount] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Coupons] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Coupons_Dates] CHECK ([StartsAt] IS NULL OR [EndsAt] IS NULL OR [StartsAt] <= [EndsAt]),
        CONSTRAINT [CK_Coupons_DiscountValue] CHECK ([DiscountValue] >= 0)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductColors] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(120) NOT NULL,
        [HexCode] nvarchar(7) NOT NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ProductColors] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductMaterials] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(120) NOT NULL,
        [Group] nvarchar(40) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ProductMaterials] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductSizes] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(120) NOT NULL,
        [LengthMm] int NOT NULL,
        [WidthMm] int NOT NULL,
        [HeightMm] int NOT NULL,
        [FurnitureType] nvarchar(40) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ProductSizes] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductSizes_Dimensions] CHECK ([LengthMm] > 0 AND [WidthMm] > 0 AND [HeightMm] > 0)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductStyles] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(100) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [Slug] nvarchar(120) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [ImageUrl] nvarchar(500) NULL,
        [DisplayOrder] int NOT NULL,
        [IsActive] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ProductStyles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [StoreInformation] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Tagline] nvarchar(200) NULL,
        [About] nvarchar(4000) NULL,
        [Address] nvarchar(300) NOT NULL,
        [WorkshopAddress] nvarchar(300) NULL,
        [Hotline] nvarchar(30) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [OpeningHours] nvarchar(150) NULL,
        [FacebookUrl] nvarchar(300) NULL,
        [TikTokUrl] nvarchar(300) NULL,
        [ZaloUrl] nvarchar(300) NULL,
        [GoogleMapsEmbedUrl] nvarchar(1000) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_StoreInformation] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetRoleClaims] (
        [Id] int NOT NULL IDENTITY,
        [RoleId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserClaims] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [ClaimType] nvarchar(max) NULL,
        [ClaimValue] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserLogins] (
        [LoginProvider] nvarchar(450) NOT NULL,
        [ProviderKey] nvarchar(450) NOT NULL,
        [ProviderDisplayName] nvarchar(max) NULL,
        [UserId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserRoles] (
        [UserId] nvarchar(450) NOT NULL,
        [RoleId] nvarchar(450) NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AspNetUserTokens] (
        [UserId] nvarchar(450) NOT NULL,
        [LoginProvider] nvarchar(450) NOT NULL,
        [Name] nvarchar(450) NOT NULL,
        [Value] nvarchar(max) NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Carts] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NULL,
        [AnonymousId] nvarchar(64) NULL,
        [CouponCode] nvarchar(50) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Carts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Carts_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [CustomerAddresses] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [Label] nvarchar(50) NULL,
        [RecipientName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [Ward] nvarchar(100) NOT NULL,
        [District] nvarchar(100) NULL,
        [Province] nvarchar(100) NOT NULL,
        [IsDefault] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_CustomerAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomerAddresses_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Notifications] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NULL,
        [RecipientRole] nvarchar(50) NULL,
        [Type] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [Message] nvarchar(1000) NOT NULL,
        [Link] nvarchar(500) NULL,
        [IsRead] bit NOT NULL,
        [ReadAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_Notifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Notifications_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Wishlists] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Wishlists] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Wishlists_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Orders] (
        [Id] int NOT NULL IDENTITY,
        [OrderCode] nvarchar(30) NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [PaymentMethod] nvarchar(40) NOT NULL,
        [PaymentStatus] nvarchar(40) NOT NULL,
        [Subtotal] decimal(18,2) NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [ShippingFee] decimal(18,2) NOT NULL,
        [TotalAmount] decimal(18,2) NOT NULL,
        [CouponId] int NULL,
        [CouponCode] nvarchar(50) NULL,
        [CustomerName] nvarchar(150) NOT NULL,
        [CustomerPhone] nvarchar(20) NOT NULL,
        [CustomerEmail] nvarchar(256) NOT NULL,
        [CustomerNote] nvarchar(1000) NULL,
        [AdminNote] nvarchar(1000) NULL,
        [CancelReason] nvarchar(500) NULL,
        [PlacedAt] datetime2 NOT NULL,
        [ConfirmedAt] datetime2 NULL,
        [ShippedAt] datetime2 NULL,
        [DeliveredAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Orders] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Orders_Amounts] CHECK ([Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [ShippingFee] >= 0 AND [TotalAmount] >= 0),
        CONSTRAINT [FK_Orders_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Orders_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [PriceRules] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(60) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [RuleType] nvarchar(40) NOT NULL,
        [FurnitureType] nvarchar(40) NULL,
        [MaterialId] int NULL,
        [FinishType] nvarchar(40) NULL,
        [Value] decimal(18,4) NOT NULL,
        [Unit] nvarchar(30) NOT NULL,
        [Priority] int NOT NULL,
        [IsActive] bit NOT NULL,
        [Description] nvarchar(1000) NULL,
        [EffectiveFrom] datetime2 NULL,
        [EffectiveTo] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_PriceRules] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_PriceRules_Value] CHECK ([Value] >= 0),
        CONSTRAINT [FK_PriceRules_ProductMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [ProductMaterials] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] int NOT NULL IDENTITY,
        [CategoryId] int NOT NULL,
        [StyleId] int NULL,
        [Name] nvarchar(200) NOT NULL,
        [Slug] nvarchar(220) NOT NULL,
        [Sku] nvarchar(50) NOT NULL,
        [ShortDescription] nvarchar(500) NULL,
        [Description] nvarchar(max) NULL,
        [FurnitureType] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [BasePrice] decimal(18,2) NOT NULL,
        [DiscountPrice] decimal(18,2) NULL,
        [StockQuantity] int NOT NULL,
        [IsFeatured] bit NOT NULL,
        [SoldCount] int NOT NULL,
        [ViewCount] int NOT NULL,
        [AverageRating] decimal(3,2) NOT NULL,
        [ReviewCount] int NOT NULL,
        [LengthMm] int NULL,
        [WidthMm] int NULL,
        [HeightMm] int NULL,
        [WeightKg] decimal(10,2) NULL,
        [WarrantyMonths] int NOT NULL,
        [Origin] nvarchar(150) NULL,
        [CareInstructions] nvarchar(1000) NULL,
        [MetaTitle] nvarchar(200) NULL,
        [MetaDescription] nvarchar(500) NULL,
        [PublishedAt] datetime2 NULL,
        [IsDeleted] bit NOT NULL,
        [DeletedAt] datetime2 NULL,
        [DeletedBy] nvarchar(256) NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Products_AverageRating] CHECK ([AverageRating] >= 0 AND [AverageRating] <= 5),
        CONSTRAINT [CK_Products_BasePrice] CHECK ([BasePrice] >= 0),
        CONSTRAINT [CK_Products_DiscountPrice] CHECK ([DiscountPrice] IS NULL OR [DiscountPrice] >= 0),
        CONSTRAINT [FK_Products_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Products_ProductStyles_StyleId] FOREIGN KEY ([StyleId]) REFERENCES [ProductStyles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [QuoteRequests] (
        [Id] int NOT NULL IDENTITY,
        [QuoteCode] nvarchar(30) NOT NULL,
        [UserId] nvarchar(450) NULL,
        [CustomerName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [Email] nvarchar(256) NULL,
        [RawRequest] nvarchar(2000) NOT NULL,
        [FurnitureType] nvarchar(40) NOT NULL,
        [ProductTypeName] nvarchar(100) NOT NULL,
        [LengthMm] int NOT NULL,
        [WidthMm] int NOT NULL,
        [HeightMm] int NOT NULL,
        [MaterialId] int NULL,
        [MaterialName] nvarchar(100) NULL,
        [ColorName] nvarchar(100) NULL,
        [StyleId] int NULL,
        [FinishType] nvarchar(40) NULL,
        [Quantity] int NOT NULL,
        [MaterialCost] decimal(18,2) NOT NULL,
        [LaborCost] decimal(18,2) NOT NULL,
        [FinishingCost] decimal(18,2) NOT NULL,
        [PaintCost] decimal(18,2) NOT NULL,
        [AccessoryCost] decimal(18,2) NOT NULL,
        [OverheadCost] decimal(18,2) NOT NULL,
        [ProfitAmount] decimal(18,2) NOT NULL,
        [EstimatedUnitPrice] decimal(18,2) NOT NULL,
        [EstimatedTotal] decimal(18,2) NOT NULL,
        [FinalQuotedPrice] decimal(18,2) NULL,
        [Status] nvarchar(40) NOT NULL,
        [AiExplanation] nvarchar(4000) NULL,
        [CustomerNote] nvarchar(1000) NULL,
        [AdminNote] nvarchar(1000) NULL,
        [QuotedAt] datetime2 NULL,
        [QuotedBy] nvarchar(256) NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_QuoteRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_QuoteRequests_Dimensions] CHECK ([LengthMm] > 0 AND [WidthMm] > 0 AND [HeightMm] > 0),
        CONSTRAINT [CK_QuoteRequests_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_QuoteRequests_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QuoteRequests_ProductMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [ProductMaterials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_QuoteRequests_ProductStyles_StyleId] FOREIGN KEY ([StyleId]) REFERENCES [ProductStyles] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [CouponUsages] (
        [Id] int NOT NULL IDENTITY,
        [CouponId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [OrderId] int NOT NULL,
        [DiscountAmount] decimal(18,2) NOT NULL,
        [UsedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CouponUsages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CouponUsages_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CouponUsages_Coupons_CouponId] FOREIGN KEY ([CouponId]) REFERENCES [Coupons] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_CouponUsages_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderAddresses] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [AddressType] nvarchar(40) NOT NULL,
        [RecipientName] nvarchar(150) NOT NULL,
        [Phone] nvarchar(20) NOT NULL,
        [Email] nvarchar(256) NULL,
        [AddressLine] nvarchar(300) NOT NULL,
        [Ward] nvarchar(100) NOT NULL,
        [District] nvarchar(100) NULL,
        [Province] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_OrderAddresses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderAddresses_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderStatusHistories] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [FromStatus] nvarchar(40) NULL,
        [ToStatus] nvarchar(40) NOT NULL,
        [Note] nvarchar(500) NULL,
        [ChangedBy] nvarchar(256) NULL,
        [ChangedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_OrderStatusHistories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OrderStatusHistories_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Payments] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [Method] nvarchar(40) NOT NULL,
        [Status] nvarchar(40) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Provider] nvarchar(50) NULL,
        [TransactionCode] nvarchar(100) NULL,
        [ProviderResponseCode] nvarchar(50) NULL,
        [FailureReason] nvarchar(500) NULL,
        [PaidAt] datetime2 NULL,
        [Note] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Payments] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Payments_Amount] CHECK ([Amount] >= 0),
        CONSTRAINT [FK_Payments_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AIConversations] (
        [Id] int NOT NULL IDENTITY,
        [UserId] nvarchar(450) NULL,
        [AnonymousId] nvarchar(64) NULL,
        [Type] nvarchar(40) NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [ProductId] int NULL,
        [LastMessageAt] datetime2 NOT NULL,
        [MessageCount] int NOT NULL,
        [TotalTokens] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_AIConversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AIConversations_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_AIConversations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ChatConversations] (
        [Id] int NOT NULL IDENTITY,
        [CustomerId] nvarchar(450) NULL,
        [GuestKey] nvarchar(64) NULL,
        [CustomerName] nvarchar(150) NOT NULL,
        [CustomerEmail] nvarchar(256) NULL,
        [CustomerPhone] nvarchar(20) NULL,
        [AssignedStaffId] nvarchar(450) NULL,
        [ProductId] int NULL,
        [Subject] nvarchar(200) NULL,
        [Status] nvarchar(40) NOT NULL,
        [LastMessageAt] datetime2 NOT NULL,
        [LastMessagePreview] nvarchar(200) NULL,
        [CustomerUnreadCount] int NOT NULL,
        [StaffUnreadCount] int NOT NULL,
        [ClosedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ChatConversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatConversations_AspNetUsers_AssignedStaffId] FOREIGN KEY ([AssignedStaffId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ChatConversations_AspNetUsers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ChatConversations_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductVariants] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [StyleId] int NULL,
        [Name] nvarchar(200) NOT NULL,
        [Sku] nvarchar(60) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [OriginalPrice] decimal(18,2) NULL,
        [StockQuantity] int NOT NULL,
        [LowStockThreshold] int NOT NULL,
        [WeightKg] decimal(10,2) NULL,
        [IsDefault] bit NOT NULL,
        [IsActive] bit NOT NULL,
        [DisplayOrder] int NOT NULL,
        [Version] uniqueidentifier NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_ProductVariants] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ProductVariants_OriginalPrice] CHECK ([OriginalPrice] IS NULL OR [OriginalPrice] >= 0),
        CONSTRAINT [CK_ProductVariants_Price] CHECK ([Price] >= 0),
        CONSTRAINT [CK_ProductVariants_Stock] CHECK ([StockQuantity] >= 0),
        CONSTRAINT [FK_ProductVariants_ProductStyles_StyleId] FOREIGN KEY ([StyleId]) REFERENCES [ProductStyles] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductVariants_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [WishlistItems] (
        [Id] int NOT NULL IDENTITY,
        [WishlistId] int NOT NULL,
        [ProductId] int NOT NULL,
        [AddedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_WishlistItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WishlistItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WishlistItems_Wishlists_WishlistId] FOREIGN KEY ([WishlistId]) REFERENCES [Wishlists] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [AIMessages] (
        [Id] int NOT NULL IDENTITY,
        [ConversationId] int NOT NULL,
        [Role] nvarchar(40) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [MetadataJson] nvarchar(max) NULL,
        [Model] nvarchar(100) NULL,
        [PromptTokens] int NULL,
        [CompletionTokens] int NULL,
        [IsError] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_AIMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AIMessages_AIConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [AIConversations] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ChatMessages] (
        [Id] int NOT NULL IDENTITY,
        [ConversationId] int NOT NULL,
        [SenderId] nvarchar(450) NULL,
        [SenderType] nvarchar(40) NOT NULL,
        [SenderName] nvarchar(150) NOT NULL,
        [Content] nvarchar(2000) NOT NULL,
        [SentAt] datetime2 NOT NULL,
        [IsRead] bit NOT NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatMessages_AspNetUsers_SenderId] FOREIGN KEY ([SenderId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ChatMessages_ChatConversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [ChatConversations] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [CartItems] (
        [Id] int NOT NULL IDENTITY,
        [CartId] int NOT NULL,
        [ProductVariantId] int NOT NULL,
        [Quantity] int NOT NULL,
        [AddedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_CartItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_CartItems_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_CartItems_Carts_CartId] FOREIGN KEY ([CartId]) REFERENCES [Carts] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CartItems_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [OrderItems] (
        [Id] int NOT NULL IDENTITY,
        [OrderId] int NOT NULL,
        [ProductId] int NULL,
        [ProductVariantId] int NULL,
        [ProductName] nvarchar(200) NOT NULL,
        [VariantName] nvarchar(200) NULL,
        [Sku] nvarchar(60) NOT NULL,
        [ImageUrl] nvarchar(500) NULL,
        [ColorName] nvarchar(100) NULL,
        [MaterialName] nvarchar(100) NULL,
        [SizeName] nvarchar(100) NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Quantity] int NOT NULL,
        [LineTotal] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_OrderItems] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_OrderItems_Amounts] CHECK ([UnitPrice] >= 0 AND [LineTotal] >= 0),
        CONSTRAINT [CK_OrderItems_Quantity] CHECK ([Quantity] > 0),
        CONSTRAINT [FK_OrderItems_Orders_OrderId] FOREIGN KEY ([OrderId]) REFERENCES [Orders] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OrderItems_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_OrderItems_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductImages] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [ProductVariantId] int NULL,
        [Url] nvarchar(500) NOT NULL,
        [AltText] nvarchar(250) NULL,
        [DisplayOrder] int NOT NULL,
        [IsPrimary] bit NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductImages_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ProductImages_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductPriceHistory] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [ProductVariantId] int NULL,
        [OldPrice] decimal(18,2) NOT NULL,
        [NewPrice] decimal(18,2) NOT NULL,
        [OldOriginalPrice] decimal(18,2) NULL,
        [NewOriginalPrice] decimal(18,2) NULL,
        [Reason] nvarchar(500) NULL,
        [ChangedBy] nvarchar(256) NULL,
        [ChangedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_ProductPriceHistory] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProductPriceHistory_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ProductPriceHistory_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductVariantColors] (
        [ProductVariantId] int NOT NULL,
        [ColorId] int NOT NULL,
        [IsPrimary] bit NOT NULL,
        [Part] nvarchar(60) NULL,
        CONSTRAINT [PK_ProductVariantColors] PRIMARY KEY ([ProductVariantId], [ColorId]),
        CONSTRAINT [FK_ProductVariantColors_ProductColors_ColorId] FOREIGN KEY ([ColorId]) REFERENCES [ProductColors] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductVariantColors_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductVariantMaterials] (
        [ProductVariantId] int NOT NULL,
        [MaterialId] int NOT NULL,
        [IsPrimary] bit NOT NULL,
        [Part] nvarchar(60) NULL,
        CONSTRAINT [PK_ProductVariantMaterials] PRIMARY KEY ([ProductVariantId], [MaterialId]),
        CONSTRAINT [FK_ProductVariantMaterials_ProductMaterials_MaterialId] FOREIGN KEY ([MaterialId]) REFERENCES [ProductMaterials] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductVariantMaterials_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductVariantSizes] (
        [ProductVariantId] int NOT NULL,
        [SizeId] int NOT NULL,
        [IsPrimary] bit NOT NULL,
        [Part] nvarchar(60) NULL,
        CONSTRAINT [PK_ProductVariantSizes] PRIMARY KEY ([ProductVariantId], [SizeId]),
        CONSTRAINT [FK_ProductVariantSizes_ProductSizes_SizeId] FOREIGN KEY ([SizeId]) REFERENCES [ProductSizes] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductVariantSizes_ProductVariants_ProductVariantId] FOREIGN KEY ([ProductVariantId]) REFERENCES [ProductVariants] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [Reviews] (
        [Id] int NOT NULL IDENTITY,
        [ProductId] int NOT NULL,
        [UserId] nvarchar(450) NOT NULL,
        [ReviewerName] nvarchar(150) NOT NULL,
        [OrderItemId] int NULL,
        [Rating] int NOT NULL,
        [Title] nvarchar(200) NULL,
        [Comment] nvarchar(2000) NOT NULL,
        [IsVerifiedPurchase] bit NOT NULL,
        [IsHidden] bit NOT NULL,
        [AdminReply] nvarchar(2000) NULL,
        [AdminRepliedAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_Reviews] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Reviews_Rating] CHECK ([Rating] >= 1 AND [Rating] <= 5),
        CONSTRAINT [FK_Reviews_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Reviews_OrderItems_OrderItemId] FOREIGN KEY ([OrderItemId]) REFERENCES [OrderItems] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_Reviews_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE TABLE [ReviewImages] (
        [Id] int NOT NULL IDENTITY,
        [ReviewId] int NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [DisplayOrder] int NOT NULL,
        CONSTRAINT [PK_ReviewImages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ReviewImages_Reviews_ReviewId] FOREIGN KEY ([ReviewId]) REFERENCES [Reviews] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AIConversations_AnonymousId] ON [AIConversations] ([AnonymousId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AIConversations_ProductId] ON [AIConversations] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AIConversations_UserId_LastMessageAt] ON [AIConversations] ([UserId], [LastMessageAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AIKnowledgeEntries_IsActive_Category_DisplayOrder] ON [AIKnowledgeEntries] ([IsActive], [Category], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AIMessages_ConversationId_CreatedAt] ON [AIMessages] ([ConversationId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_CreatedAt] ON [AspNetUsers] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityName_EntityId] ON [AuditLogs] ([EntityName], [EntityId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CartItems_CartId_ProductVariantId] ON [CartItems] ([CartId], [ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CartItems_ProductVariantId] ON [CartItems] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Carts_AnonymousId] ON [Carts] ([AnonymousId]) WHERE [AnonymousId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Carts_UserId] ON [Carts] ([UserId]) WHERE [UserId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Categories_ParentId_DisplayOrder] ON [Categories] ([ParentId], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Categories_Slug] ON [Categories] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatConversations_AssignedStaffId] ON [ChatConversations] ([AssignedStaffId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatConversations_CustomerId] ON [ChatConversations] ([CustomerId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatConversations_GuestKey] ON [ChatConversations] ([GuestKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatConversations_ProductId] ON [ChatConversations] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatConversations_Status_LastMessageAt] ON [ChatConversations] ([Status], [LastMessageAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_ConversationId_SentAt] ON [ChatMessages] ([ConversationId], [SentAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_SenderId] ON [ChatMessages] ([SenderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ContactMessages_Status_CreatedAt] ON [ContactMessages] ([Status], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Coupons_Code] ON [Coupons] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CouponUsages_CouponId_UserId] ON [CouponUsages] ([CouponId], [UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_CouponUsages_OrderId] ON [CouponUsages] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CouponUsages_UserId] ON [CouponUsages] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_CustomerAddresses_UserId] ON [CustomerAddresses] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_RecipientRole_IsRead_CreatedAt] ON [Notifications] ([RecipientRole], [IsRead], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Notifications_UserId_IsRead_CreatedAt] ON [Notifications] ([UserId], [IsRead], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OrderAddresses_OrderId_AddressType] ON [OrderAddresses] ([OrderId], [AddressType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_OrderId] ON [OrderItems] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_ProductId] ON [OrderItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderItems_ProductVariantId] ON [OrderItems] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_CouponId] ON [Orders] ([CouponId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Orders_OrderCode] ON [Orders] ([OrderCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_PlacedAt] ON [Orders] ([PlacedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_Status_PlacedAt] ON [Orders] ([Status], [PlacedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Orders_UserId_PlacedAt] ON [Orders] ([UserId], [PlacedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OrderStatusHistories_OrderId_ChangedAt] ON [OrderStatusHistories] ([OrderId], [ChangedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_OrderId] ON [Payments] ([OrderId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Payments_TransactionCode] ON [Payments] ([TransactionCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PriceRules_Code] ON [PriceRules] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PriceRules_MaterialId] ON [PriceRules] ([MaterialId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PriceRules_RuleType_IsActive] ON [PriceRules] ([RuleType], [IsActive]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductColors_Name] ON [ProductColors] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductColors_Slug] ON [ProductColors] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductImages_ProductId_DisplayOrder] ON [ProductImages] ([ProductId], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductImages_ProductVariantId] ON [ProductImages] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductMaterials_Name] ON [ProductMaterials] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductMaterials_Slug] ON [ProductMaterials] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductPriceHistory_ProductId_ChangedAt] ON [ProductPriceHistory] ([ProductId], [ChangedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductPriceHistory_ProductVariantId] ON [ProductPriceHistory] ([ProductVariantId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_CategoryId] ON [Products] ([CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_FurnitureType] ON [Products] ([FurnitureType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_IsFeatured] ON [Products] ([IsFeatured]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_Name] ON [Products] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_PublishedAt] ON [Products] ([PublishedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Products_Sku] ON [Products] ([Sku]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Products_Slug] ON [Products] ([Slug]) WHERE [IsDeleted] = 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_SoldCount] ON [Products] ([SoldCount]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_Status_CategoryId] ON [Products] ([Status], [CategoryId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_StyleId] ON [Products] ([StyleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductSizes_FurnitureType] ON [ProductSizes] ([FurnitureType]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductSizes_Slug] ON [ProductSizes] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductStyles_Code] ON [ProductStyles] ([Code]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductStyles_Slug] ON [ProductStyles] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductVariantColors_ColorId] ON [ProductVariantColors] ([ColorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductVariantMaterials_MaterialId] ON [ProductVariantMaterials] ([MaterialId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductVariants_ProductId_IsActive_DisplayOrder] ON [ProductVariants] ([ProductId], [IsActive], [DisplayOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProductVariants_Sku] ON [ProductVariants] ([Sku]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductVariants_StyleId] ON [ProductVariants] ([StyleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductVariantSizes_SizeId] ON [ProductVariantSizes] ([SizeId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuoteRequests_MaterialId] ON [QuoteRequests] ([MaterialId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_QuoteRequests_QuoteCode] ON [QuoteRequests] ([QuoteCode]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuoteRequests_Status_CreatedAt] ON [QuoteRequests] ([Status], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuoteRequests_StyleId] ON [QuoteRequests] ([StyleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_QuoteRequests_UserId] ON [QuoteRequests] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ReviewImages_ReviewId] ON [ReviewImages] ([ReviewId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Reviews_OrderItemId] ON [Reviews] ([OrderItemId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Reviews_ProductId_IsHidden_CreatedAt] ON [Reviews] ([ProductId], [IsHidden], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Reviews_UserId_ProductId] ON [Reviews] ([UserId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_WishlistItems_ProductId] ON [WishlistItems] ([ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_WishlistItems_WishlistId_ProductId] ON [WishlistItems] ([WishlistId], [ProductId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Wishlists_UserId] ON [Wishlists] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929100036_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929100036_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929104144_AddProductSearchText'
)
BEGIN
    ALTER TABLE [Products] ADD [SearchText] nvarchar(1000) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260929104144_AddProductSearchText'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260929104144_AddProductSearchText', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930005757_AddCouponIsPublic'
)
BEGIN
    ALTER TABLE [Coupons] ADD [IsPublic] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930005757_AddCouponIsPublic'
)
BEGIN
    UPDATE Coupons SET IsPublic = 1 WHERE Code IN ('CHAOBAN10', 'GIAM500K')
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260930005757_AddCouponIsPublic'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260930005757_AddCouponIsPublic', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006184854_AddCartItemSelection'
)
BEGIN
    ALTER TABLE [CartItems] ADD [IsSelected] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261006184854_AddCartItemSelection'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261006184854_AddCartItemSelection', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007083827_AddHomeBannerAndLogoSubtitle'
)
BEGIN
    ALTER TABLE [StoreInformation] ADD [LogoSubtitle] nvarchar(40) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007083827_AddHomeBannerAndLogoSubtitle'
)
BEGIN
    UPDATE [StoreInformation] SET [LogoSubtitle] = N'Furniture' WHERE [LogoSubtitle] IS NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007083827_AddHomeBannerAndLogoSubtitle'
)
BEGIN
    CREATE TABLE [HomeBanners] (
        [Id] int NOT NULL IDENTITY,
        [Eyebrow] nvarchar(60) NULL,
        [Title] nvarchar(120) NOT NULL,
        [TitleHighlight] nvarchar(80) NULL,
        [Description] nvarchar(400) NULL,
        [PrimaryButtonText] nvarchar(40) NULL,
        [PrimaryButtonUrl] nvarchar(300) NULL,
        [SecondaryButtonText] nvarchar(40) NULL,
        [SecondaryButtonUrl] nvarchar(300) NULL,
        [Stat1Value] nvarchar(20) NULL,
        [Stat1Label] nvarchar(40) NULL,
        [Stat2Value] nvarchar(20) NULL,
        [Stat2Label] nvarchar(40) NULL,
        [Stat3Value] nvarchar(20) NULL,
        [Stat3Label] nvarchar(40) NULL,
        [ImageUrl] nvarchar(500) NULL,
        [ImageWidth] int NULL,
        [ImageHeight] int NULL,
        [ImageAlt] nvarchar(200) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_HomeBanners] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007083827_AddHomeBannerAndLogoSubtitle'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007083827_AddHomeBannerAndLogoSubtitle', N'8.0.31');
END;
GO

COMMIT;
GO

