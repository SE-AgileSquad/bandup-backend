using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BandUp.Infrastructure.Persistence.Conventions;

/// <summary>
/// Renames tables, columns, keys, indexes and constraints to snake_case so that PostgreSQL
/// identifiers stay unquoted and predictable in hand-written SQL.
/// </summary>
public static class SnakeCaseNaming
{
    public static void Apply(IMutableModel model)
    {
        foreach (IMutableEntityType entityType in model.GetEntityTypes())
        {
            if (entityType.GetTableName() is null)
            {
                continue;
            }

            entityType.SetTableName(ToSnakeCase(entityType.GetTableName()!));

            foreach (IMutableProperty property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.GetColumnName()));
            }

            foreach (IMutableKey key in entityType.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (IMutableForeignKey foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.SetConstraintName(ToSnakeCase(foreignKey.GetConstraintName()!));
            }

            foreach (IMutableIndex index in entityType.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        StringBuilder builder = new(name.Length + 8);

        for (int index = 0; index < name.Length; index++)
        {
            char current = name[index];

            if (!char.IsUpper(current))
            {
                builder.Append(current);
                continue;
            }

            bool previousIsLowerOrDigit = index > 0 && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1]));
            bool previousIsUpper = index > 0 && char.IsUpper(name[index - 1]);
            bool nextIsLower = index + 1 < name.Length && char.IsLower(name[index + 1]);

            if (index > 0 && (previousIsLowerOrDigit || (previousIsUpper && nextIsLower)))
            {
                builder.Append('_');
            }

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.ToString();
    }
}