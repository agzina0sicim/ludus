using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Ludus.Infrastructure.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    public static void JsonList<TEntity, TValue>(this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, List<TValue>>> property) where TEntity : class
    {
        var builder = entity.Property(property).HasConversion(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => JsonSerializer.Deserialize<List<TValue>>(value, (JsonSerializerOptions?)null)!);
        builder.Metadata.SetValueComparer(new ValueComparer<List<TValue>>(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            value => value.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            value => value.ToList()));
        var column = builder.Metadata.Name;
        entity.ToTable(table => table.HasCheckConstraint($"CK_{typeof(TEntity).Name}_{column}_Json",
            $"json_valid(\"{column}\") AND json_type(\"{column}\") = 'array'"));
    }

    public static void Hash<TEntity>(this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, string?>> property, int length) where TEntity : class
    {
        var builder = entity.Property(property).HasMaxLength(length);
        var name = builder.Metadata.Name;
        entity.ToTable(table => table.HasCheckConstraint($"CK_{typeof(TEntity).Name}_{name}",
            $"\"{name}\" IS NULL OR (length(\"{name}\") = {length} AND \"{name}\" NOT GLOB '*[^0-9a-fA-F]*')"));
    }

    public static void Check<TEntity>(this EntityTypeBuilder<TEntity> entity, string name, string sql)
        where TEntity : class => entity.ToTable(table => table.HasCheckConstraint($"CK_{typeof(TEntity).Name}_{name}", sql));
}
