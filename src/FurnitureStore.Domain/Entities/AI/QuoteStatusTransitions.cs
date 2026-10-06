using FurnitureStore.Domain.Enums;
using FurnitureStore.Domain.Exceptions;

namespace FurnitureStore.Domain.Entities;

/// <summary>Workflow of a custom-furniture quote request.</summary>
public static class QuoteStatusTransitions
{
    private static readonly Dictionary<QuoteStatus, QuoteStatus[]> Allowed = new()
    {
        [QuoteStatus.New] = [QuoteStatus.Reviewing, QuoteStatus.Quoted, QuoteStatus.Cancelled],
        [QuoteStatus.Reviewing] = [QuoteStatus.Quoted, QuoteStatus.Cancelled],
        // A quoted price can be revised (back to Reviewing) until the customer answers.
        [QuoteStatus.Quoted] = [QuoteStatus.Accepted, QuoteStatus.Rejected, QuoteStatus.Reviewing, QuoteStatus.Cancelled],
        [QuoteStatus.Accepted] = [],
        [QuoteStatus.Rejected] = [QuoteStatus.Reviewing],
        [QuoteStatus.Cancelled] = []
    };

    public static bool CanTransition(QuoteStatus from, QuoteStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyList<QuoteStatus> NextStatuses(QuoteStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];

    public static string DisplayName(QuoteStatus status) => status switch
    {
        QuoteStatus.New => "Mới",
        QuoteStatus.Reviewing => "Đang xem xét",
        QuoteStatus.Quoted => "Đã báo giá",
        QuoteStatus.Accepted => "Khách đồng ý",
        QuoteStatus.Rejected => "Khách từ chối",
        QuoteStatus.Cancelled => "Đã hủy",
        _ => status.ToString()
    };

    /// <summary>Moves the quote to <paramref name="to"/>, enforcing the workflow and the quoted-price rule.</summary>
    public static void Apply(QuoteRequest quote, QuoteStatus to, DateTime utcNow, string? by)
    {
        if (quote.Status == to)
        {
            return;
        }

        if (!CanTransition(quote.Status, to))
        {
            throw new DomainException($"Không thể chuyển báo giá từ \"{DisplayName(quote.Status)}\" sang \"{DisplayName(to)}\".");
        }

        if (to == QuoteStatus.Quoted)
        {
            if (quote.FinalQuotedPrice is not > 0)
            {
                throw new DomainException("Vui lòng nhập giá báo cho khách trước khi chuyển sang \"Đã báo giá\".");
            }

            quote.QuotedAt = utcNow;
            quote.QuotedBy = by;
        }

        quote.Status = to;
    }
}
