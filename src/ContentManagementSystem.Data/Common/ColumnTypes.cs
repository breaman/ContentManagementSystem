namespace ContentManagementSystem.Data.Common;

/// <summary>
/// SQL Server column types applied consistently across the model.
/// </summary>
/// <remarks>
/// Centralising these avoids the classic defect where one money column is <c>decimal(18,2)</c> and
/// another defaults to <c>decimal(18,0)</c>, silently truncating cents.
/// </remarks>
public static class ColumnTypes
{
    /// <summary>Monetary amounts.</summary>
    public const string Money = "decimal(18,2)";

    /// <summary>UTC instants.</summary>
    public const string Timestamp = "datetimeoffset(7)";

    /// <summary>Business dates with no time component, such as a needed-by date.</summary>
    public const string BusinessDate = "date";
}