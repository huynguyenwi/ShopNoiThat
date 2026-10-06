using System.ComponentModel.DataAnnotations;

namespace FurnitureStore.Application.Common.Settings;

/// <summary>
/// Email delivery, bound from the "Email" section.
/// Mode "Pickup" writes each email as an .html file into <see cref="PickupDirectory"/> (development);
/// Mode "Smtp" sends through an SMTP server (password via user-secrets / Email__Smtp__Password).
/// </summary>
public sealed class EmailSettings
{
    public const string SectionName = "Email";

    [Required]
    [RegularExpression("^(Pickup|Smtp)$", ErrorMessage = "Email:Mode must be 'Pickup' or 'Smtp'.")]
    public string Mode { get; set; } = "Pickup";

    [Required, EmailAddress]
    public string FromAddress { get; set; } = "no-reply@furniture.local";

    public string FromName { get; set; } = "Nhà Mộc Furniture";

    /// <summary>Relative to the content root.</summary>
    public string PickupDirectory { get; set; } = "App_Data/emails";

    public SmtpSettings Smtp { get; set; } = new();
}

public sealed class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public string? UserName { get; set; }
    public string? Password { get; set; }
}
