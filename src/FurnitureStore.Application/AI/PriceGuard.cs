using System.Globalization;
using System.Text.RegularExpressions;

namespace FurnitureStore.Application.AI;

/// <summary>
/// Server-side protection against invented prices: every amount of money written by the AI model must match a price from
/// the database (or a number the customer / the store policies mentioned). Anything else is replaced by a neutral text,
/// and the real price stays visible on the product cards.
/// </summary>
public static partial class PriceGuard
{
    public const string Replacement = "(giá chính xác xem ở thẻ sản phẩm)";

    public static string Sanitize(string text, IEnumerable<decimal> allowedAmounts, out int replaced)
    {
        var allowed = allowedAmounts.Where(a => a > 0).Distinct().ToList();
        var count = 0;
        var result = AmountRegex().Replace(text, match =>
        {
            var value = ParseAmount(match);
            if (value is null || value < 1_000)
            {
                return match.Value; // not money ("2 người", "1m8"...)
            }

            // Tolerate rounding such as "12,5 triệu" for 12.490.000.
            if (allowed.Any(a => Math.Abs(a - value.Value) <= Math.Max(a * 0.01m, 1_000m)))
            {
                return match.Value;
            }

            count++;
            return Replacement;
        });

        replaced = count;
        return result;
    }

    /// <summary>All amounts of money written in a text (customer messages, store policies).</summary>
    public static IEnumerable<decimal> AmountsIn(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match match in AmountRegex().Matches(text))
        {
            if (ParseAmount(match) is decimal value && value >= 1_000) yield return value;
        }
    }

    private static decimal? ParseAmount(Match match)
    {
        var number = match.Groups["n"].Value;
        var unit = match.Groups["u"].Value.ToLowerInvariant();
        var normalized = ThousandsRegex().IsMatch(number) ? number.Replace(".", "").Replace(",", "") : number.Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        value *= unit switch
        {
            "tỷ" or "ty" => 1_000_000_000m,
            "triệu" or "trieu" or "tr" or "củ" => 1_000_000m,
            "nghìn" or "ngàn" or "k" => 1_000m,
            _ => 1m
        };

        var tail = match.Groups["t"].Value;
        if (tail.Length > 0 && unit is "triệu" or "trieu" or "tr")
        {
            value += int.Parse(tail, CultureInfo.InvariantCulture) * (decimal)Math.Pow(10, 6 - tail.Length);
        }

        // A bare number is money only when it is written like a furniture price ("12.500.000"), not a size ("1.800 mm").
        if (unit.Length == 0 && !match.Groups["cur"].Success)
        {
            return ThousandsRegex().IsMatch(number) && value >= 100_000 ? value : null;
        }

        return value;
    }

    [GeneratedRegex(@"(?<![\d.,])(?<n>\d{1,3}(?:[.,]\d{3})+|\d+(?:[.,]\d+)?)\s*(?:(?<u>tỷ|ty|triệu|trieu|tr|củ|nghìn|ngàn|k)(?:\s*(?<t>\d{1,3})(?![\d.,]))?)?\s*(?<cur>₫|đ|vnđ|vnd|đồng|dong)?(?![\p{L}\d])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AmountRegex();

    [GeneratedRegex(@"^\d{1,3}([.,]\d{3})+$")]
    private static partial Regex ThousandsRegex();
}
