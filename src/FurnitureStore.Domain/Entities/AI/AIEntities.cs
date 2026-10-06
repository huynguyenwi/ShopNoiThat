using FurnitureStore.Domain.Common;
using FurnitureStore.Domain.Enums;

namespace FurnitureStore.Domain.Entities;

/// <summary>A conversation with the AI assistant (chatbot, advice, recommendation, quote).</summary>
public class AIConversation : AuditableEntity
{
    public string? UserId { get; set; }

    /// <summary>Cookie id of a guest visitor.</summary>
    public string? AnonymousId { get; set; }

    public AIConversationType Type { get; set; } = AIConversationType.General;
    public string Title { get; set; } = string.Empty;

    /// <summary>Product in context ("AI tư vấn sản phẩm này").</summary>
    public int? ProductId { get; set; }
    public Product? Product { get; set; }

    public DateTime LastMessageAt { get; set; }
    public int MessageCount { get; set; }
    public int TotalTokens { get; set; }

    public ICollection<AIMessage> Messages { get; set; } = new List<AIMessage>();
}

public class AIMessage : BaseEntity
{
    public int ConversationId { get; set; }
    public AIConversation Conversation { get; set; } = null!;

    public AIMessageRole Role { get; set; }
    public string Content { get; set; } = string.Empty;

    /// <summary>Structured data attached to the message (e.g. recommended product ids) as JSON.</summary>
    public string? MetadataJson { get; set; }

    public string? Model { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public bool IsError { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Store knowledge managed by admins (shipping, warranty, payment policies, FAQ...)
/// that is injected into the chatbot's context. Prices never come from here - only from the catalog.
/// </summary>
public class AIKnowledgeEntry : AuditableEntity
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Keywords { get; set; }
    public bool IsActive { get; set; } = true;
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Custom furniture quote request. The estimate breakdown is computed by the backend price
/// calculator (never by the AI); an admin can review and set the final quoted price.
/// </summary>
public class QuoteRequest : AuditableEntity, IConcurrencyAware
{
    public string QuoteCode { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }

    /// <summary>Free text written by the customer, e.g. "bàn gỗ óc chó dài 2m2 rộng 1m cao 75cm".</summary>
    public string RawRequest { get; set; } = string.Empty;

    public FurnitureType FurnitureType { get; set; }
    public string ProductTypeName { get; set; } = string.Empty;
    public int LengthMm { get; set; }
    public int WidthMm { get; set; }
    public int HeightMm { get; set; }

    public int? MaterialId { get; set; }
    public ProductMaterial? Material { get; set; }
    public string? MaterialName { get; set; }
    public string? ColorName { get; set; }
    public int? StyleId { get; set; }
    public ProductStyle? Style { get; set; }
    public FinishType? FinishType { get; set; }
    public int Quantity { get; set; } = 1;

    // Estimate breakdown (per unit) produced by PriceCalculatorService
    public decimal MaterialCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal FinishingCost { get; set; }
    public decimal PaintCost { get; set; }
    public decimal AccessoryCost { get; set; }
    public decimal OverheadCost { get; set; }
    public decimal ProfitAmount { get; set; }
    public decimal EstimatedUnitPrice { get; set; }
    public decimal EstimatedTotal { get; set; }

    /// <summary>Final price confirmed by an admin; null until quoted.</summary>
    public decimal? FinalQuotedPrice { get; set; }

    public QuoteStatus Status { get; set; } = QuoteStatus.New;
    public string? AiExplanation { get; set; }
    public string? CustomerNote { get; set; }
    public string? AdminNote { get; set; }
    public DateTime? QuotedAt { get; set; }
    public string? QuotedBy { get; set; }

    public Guid Version { get; set; }
}
