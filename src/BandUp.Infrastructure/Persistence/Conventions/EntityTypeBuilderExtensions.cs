using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BandUp.Infrastructure.Persistence.Conventions;

/// <summary>
/// Helpers for the PostgreSQL conventions: enums stored as text with a CHECK constraint,
/// and optimistic concurrency through the system column <c>xmin</c>.
/// </summary>
public static class EntityTypeBuilderExtensions
{
    /// <summary>
    /// Stores the enum as text and adds a CHECK constraint listing its members.
    /// </summary>
    /// <remarks>
    /// Identifiers are snake-cased here rather than left to <see cref="SnakeCaseNaming"/>,
    /// because constraint SQL is opaque to EF and is never rewritten during the model pass.
    /// </remarks>
    public static EntityTypeBuilder<TEntity> HasEnumCheckConstraint<TEntity, TEnum>(
        this EntityTypeBuilder<TEntity> builder,
        string columnName,
        string? constraintName = null)
        where TEntity : class
        where TEnum : struct, Enum
    {
        string tableName = builder.Metadata.GetTableName()
            ?? throw new InvalidOperationException(
                $"Entity '{typeof(TEntity).Name}' is not mapped to a table, so it cannot carry a CHECK constraint.");

        string column = SnakeCaseNaming.ToSnakeCase(columnName);
        string allowedValues = string.Join(", ", Enum.GetNames<TEnum>().Select(name => $"'{name}'"));
        string name = constraintName
            ?? $"{SnakeCaseNaming.ToSnakeCase(tableName)}_{column}_check";

        builder.ToTable(table => table.HasCheckConstraint(name, $"{column} IN ({allowedValues})"));

        return builder;
    }

    /// <summary>
    /// Makes the PostgreSQL system column <c>xmin</c> the concurrency token for the entity.
    /// </summary>
    public static EntityTypeBuilder<TEntity> UseXminConcurrency<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder
            .Property<uint>("xmin")
            .HasColumnName("xmin")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return builder;
    }
}