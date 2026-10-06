namespace FurnitureStore.Domain.Enums;

public enum ChatSenderType
{
    Customer = 1,
    Staff = 2,
    System = 3
}

public enum ConversationStatus
{
    Open = 1,
    Closed = 2
}

public enum ContactMessageStatus
{
    New = 0,
    Read = 1,
    Replied = 2,
    Archived = 3
}

public enum NotificationType
{
    General = 0,
    OrderPlaced = 1,
    OrderStatusChanged = 2,
    NewChatMessage = 3,
    NewContactMessage = 4,
    NewQuoteRequest = 5,
    QuoteUpdated = 6,
    LowStock = 7,
    NewReview = 8
}

public enum AIMessageRole
{
    System = 0,
    User = 1,
    Assistant = 2,
    Tool = 3
}

public enum AIConversationType
{
    General = 1,
    ProductAdvice = 2,
    Recommendation = 3,
    ColorAdvice = 4,
    StyleAdvice = 5,
    PriceQuote = 6
}

public enum QuoteStatus
{
    New = 0,
    Reviewing = 1,
    Quoted = 2,
    Accepted = 3,
    Rejected = 4,
    Cancelled = 5
}

/// <summary>Kind of parameter stored in PriceRules and consumed by the price calculator.</summary>
public enum PriceRuleType
{
    /// <summary>VND per m² of board / surface, per material.</summary>
    MaterialCostPerSquareMeter = 1,
    /// <summary>Fraction of the bounding-box surface actually used as material, per furniture type.</summary>
    MaterialUsageFactor = 2,
    /// <summary>Fixed labor cost per piece, per furniture type.</summary>
    LaborBaseCost = 3,
    /// <summary>VND per m² of labor, per furniture type.</summary>
    LaborCostPerSquareMeter = 4,
    /// <summary>VND per m² for sanding / assembly / detailing.</summary>
    FinishingCostPerSquareMeter = 5,
    /// <summary>VND per m² of paint / coating, per finish type.</summary>
    PaintCostPerSquareMeter = 6,
    /// <summary>Hardware (hinges, rails, screws...) per piece, per furniture type.</summary>
    AccessoryCost = 7,
    /// <summary>Workshop overhead as a percentage of direct cost.</summary>
    OverheadPercent = 8,
    /// <summary>Profit margin as a percentage of total cost.</summary>
    ProfitMarginPercent = 9
}

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3,
    Login = 4,
    Logout = 5,
    LoginFailed = 6,
    StatusChange = 7,
    Lock = 8,
    Unlock = 9,
    Other = 99
}
