using System.Data;

using ContentManagementSystem.Data.Models;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// Writes entity instances straight to their table with <c>SqlBulkCopy</c>.
/// </summary>
/// <remarks>
/// Table and column names come from the EF model rather than from string literals here, so a
/// renamed column breaks this at the first run against a real database instead of silently writing
/// into the wrong shape. Identities are kept: the seeder assigns them itself because a page's
/// materialized path contains its own id, and a second pass to fill paths in would double the
/// writing.
/// <para>
/// Deliberately not part of the general data layer. Nothing else in the application should be
/// bypassing the save interceptors, and a helper that makes it easy is only defensible for a tool
/// whose whole purpose is to write rows nobody authored.
/// </para>
/// </remarks>
/// <param name="context">The context whose model and connection are used.</param>
internal sealed class EntityBulkWriter(ApplicationDbContext context)
{
    /// <summary>Writes rows and returns how many were written.</summary>
    /// <typeparam name="TEntity">The entity type, which must be mapped to a table.</typeparam>
    /// <param name="rows">The instances to write.</param>
    /// <param name="batchSize">Rows per server-side batch.</param>
    /// <param name="cancellationToken">Token observed while writing.</param>
    /// <returns>The number of rows written.</returns>
    public async Task<int> WriteAsync<TEntity>(
        IReadOnlyCollection<TEntity> rows,
        int batchSize,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0) return 0;

        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not part of the model.");

        var target = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not mapped to a table.");

        var properties = Writable(entityType);
        var table = Build(properties, target, rows);

        var connection = (SqlConnection)context.Database.GetDbConnection();

        if (connection.State is not ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        using var copy = new SqlBulkCopy(
            connection,
            // CheckConstraints is not optional here. Without it SQL Server skips the foreign keys
            // and marks them untrusted afterwards, which changes the plans the optimizer produces —
            // and a load test run against different plans from production's measures the wrong
            // database. The cost is that rows have to be written parents first.
            SqlBulkCopyOptions.KeepIdentity | SqlBulkCopyOptions.TableLock | SqlBulkCopyOptions.CheckConstraints,
            externalTransaction: null)
        {
            DestinationTableName = target.Schema is { Length: > 0 } schema
                ? $"[{schema}].[{target.Name}]"
                : $"[{target.Name}]",
            BatchSize = batchSize,

            // Zero is no timeout. A hundred thousand rows over a laptop's Docker network can take
            // longer than the thirty-second default, and a seeding run that gives up half way
            // through leaves exactly the state this tool exists to avoid.
            BulkCopyTimeout = 0,
        };

        foreach (DataColumn column in table.Columns)
        {
            copy.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        }

        await copy.WriteToServerAsync(table, cancellationToken);

        return rows.Count;
    }

    /// <summary>
    /// Sets a table's identity counter to the highest value now in it.
    /// </summary>
    /// <typeparam name="TEntity">The entity type whose table to reseed.</typeparam>
    /// <param name="cancellationToken">Token observed while reseeding.</param>
    /// <remarks>
    /// Required, not tidiness. <c>SqlBulkCopy</c> with <c>KeepIdentity</c> inserts the values it is
    /// given without advancing the counter, so the next row the application inserts by hand reuses
    /// an id this run already wrote and fails on the primary key.
    /// </remarks>
    public async Task ReseedAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not part of the model.");

        var target = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not mapped to a table.");

        var qualified = target.Schema is { Length: > 0 } schema ? $"{schema}.{target.Name}" : target.Name;

        // DBCC CHECKIDENT takes the table as a string, so it cannot be parameterized in the usual
        // way; the name comes from the EF model rather than from anything a caller supplies, and is
        // escaped anyway so that this cannot become the one place a name reaches SQL unquoted.
        var sql = $"DBCC CHECKIDENT ('{qualified.Replace("'", "''", StringComparison.Ordinal)}', RESEED)";

        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    /// <summary>The bracketed, schema-qualified name of an entity's table.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <returns>Something like <c>[dbo].[Pages]</c>, ready to interpolate into a statement.</returns>
    public string QualifiedTableName<TEntity>()
        where TEntity : class
    {
        var target = Target<TEntity>();

        return target.Schema is { Length: > 0 } schema
            ? $"[{schema}].[{target.Name}]"
            : $"[{target.Name}]";
    }

    /// <summary>The bracketed column a property maps to.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="propertyName">Name of the CLR property.</param>
    /// <returns>Something like <c>[DraftVersionId]</c>.</returns>
    /// <exception cref="InvalidOperationException">The property is not mapped.</exception>
    public string ColumnName<TEntity>(string propertyName)
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not part of the model.");

        var property = entityType.FindProperty(propertyName)
            ?? throw new InvalidOperationException(
                $"'{typeof(TEntity).Name}' has no mapped property named '{propertyName}'.");

        var column = property.GetColumnName(Target<TEntity>())
            ?? throw new InvalidOperationException($"'{propertyName}' maps to no column.");

        return $"[{column}]";
    }

    private StoreObjectIdentifier Target<TEntity>()
        where TEntity : class
    {
        var entityType = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not part of the model.");

        return StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)
            ?? throw new InvalidOperationException($"'{typeof(TEntity).Name}' is not mapped to a table.");
    }

    /// <summary>
    /// The properties that are written: everything except what the database computes.
    /// </summary>
    /// <remarks>
    /// <c>rowversion</c> columns are generated on add and on update, which is what excludes them —
    /// SQL Server refuses an explicit value for one.
    /// </remarks>
    private static List<IProperty> Writable(IEntityType entityType) =>
        [.. entityType.GetProperties().Where(property =>
            property.GetComputedColumnSql() is null &&
            property.ValueGenerated is ValueGenerated.Never or ValueGenerated.OnAdd)];

    private static DataTable Build<TEntity>(
        List<IProperty> properties,
        StoreObjectIdentifier target,
        IReadOnlyCollection<TEntity> rows)
    {
        var table = new DataTable();
        var readers = new List<(DataColumn Column, Func<TEntity, object> Read)>(properties.Count);

        foreach (var property in properties)
        {
            var name = property.GetColumnName(target)
                ?? throw new InvalidOperationException(
                    $"'{property.Name}' on '{property.DeclaringType.Name}' maps to no column.");

            // A shadow property has no place on the object to read from, so the value would silently
            // be null and the column would silently be wrong. Nothing the seeder writes has one, and
            // this is what says so if that ever changes.
            var member = property.PropertyInfo
                ?? throw new InvalidOperationException(
                    $"'{property.Name}' on '{property.DeclaringType.Name}' is a shadow property, " +
                    "which this writer cannot read. Map it to a CLR property or exclude the entity.");

            var converter = property.GetValueConverter();
            var stored = converter?.ProviderClrType ?? property.ClrType;

            var column = new DataColumn(name, Nullable.GetUnderlyingType(stored) ?? stored)
            {
                AllowDBNull = property.IsNullable,
            };

            table.Columns.Add(column);

            readers.Add((column, entity =>
            {
                var value = member.GetValue(entity);

                if (value is null) return DBNull.Value;

                return converter is null ? value : converter.ConvertToProvider(value) ?? DBNull.Value;
            }));
        }

        foreach (var entity in rows)
        {
            var row = table.NewRow();

            foreach (var (column, read) in readers)
            {
                row[column] = read(entity);
            }

            table.Rows.Add(row);
        }

        return table;
    }
}
