namespace FurnitureStore.Application.Common.Settings;

/// <summary>
/// Payment configuration, bound from the "Payment" section.
/// Gateway secrets (VNPay/MoMo hash keys) must come from User Secrets / environment variables, never from appsettings.json.
/// </summary>
public sealed class PaymentSettings
{
    public const string SectionName = "Payment";

    /// <summary>Payment methods shown at checkout, e.g. "COD", "BankTransfer".</summary>
    public string[] EnabledMethods { get; set; } = ["COD", "BankTransfer"];

    public BankTransferSettings BankTransfer { get; set; } = new();
}

public sealed class BankTransferSettings
{
    public string BankName { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    /// <summary>Prefix of the transfer note; the order code is appended, e.g. "NHAMOC DH240101001".</summary>
    public string TransferNotePrefix { get; set; } = "NHAMOC";
}
