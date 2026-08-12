namespace ContentManagementSystem.Shared.Common;

/// <summary>
/// Canonical maximum storage lengths shared by EF Core configurations and API contracts.
/// Defined once here so a validation attribute and a column definition can never drift apart
/// (plan section 4.7).
/// </summary>
public static class FieldLengths
{
    /// <summary>Person and student first, middle, and last names.</summary>
    public const int PersonName = 100;

    /// <summary>Group, item, and promotion names.</summary>
    public const int EntityName = 200;

    /// <summary>Email addresses; matches the maximum practical RFC 5321 path length.</summary>
    public const int Email = 320;

    /// <summary>Phone numbers as entered, including punctuation.</summary>
    public const int Phone = 32;

    /// <summary>A single street address line.</summary>
    public const int AddressLine = 200;

    /// <summary>City or state/region name.</summary>
    public const int CityOrRegion = 100;

    /// <summary>Postal code.</summary>
    public const int PostalCode = 20;

    /// <summary>Country name.</summary>
    public const int Country = 100;

    /// <summary>Item and promotion descriptions. Plain text only in this release.</summary>
    public const int Description = 4000;

    /// <summary>Barcode prefix as configured on an item.</summary>
    public const int BarcodePrefix = 50;

    /// <summary>Generated inventory-unit barcode.</summary>
    public const int Barcode = 80;

    /// <summary>Promotion code, stored invariant-uppercase.</summary>
    public const int PromotionCode = 50;

    /// <summary>Free-text reason recorded against a status change or adjustment.</summary>
    public const int Reason = 500;

    /// <summary>Staff notification message body.</summary>
    public const int NotificationMessage = 1000;

    /// <summary>Payment method descriptor.</summary>
    public const int PaymentMethod = 100;

    /// <summary>External payment reference (never full card data).</summary>
    public const int ExternalReference = 200;

    /// <summary>Idempotency keys supplied by callers for replay-safe mutations.</summary>
    public const int IdempotencyKey = 128;

    /// <summary>Outbox/notification event type discriminator.</summary>
    public const int EventType = 200;
}