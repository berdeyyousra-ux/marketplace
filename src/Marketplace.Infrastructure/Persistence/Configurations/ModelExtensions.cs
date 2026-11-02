using Marketplace.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.Infrastructure.Persistence.Configurations;

internal static class ModelExtensions
{
    // Money maps to numeric(12,2) plus char(3) currency (D-11). Every table in section 7.2 defaults currency to 'EUR'.
    public static ComplexPropertyBuilder<Money> MapMoney<TEntity>(
        this EntityTypeBuilder<TEntity> builder, System.Linq.Expressions.Expression<Func<TEntity, Money>> property, string amountColumn)
        where TEntity : class
    {
        var money = builder.ComplexProperty(property);
        money.Property(m => m.Amount).HasColumnName(amountColumn).HasPrecision(12, 2);
        money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsFixedLength().WithDatabaseDefault("'EUR'");
        return money;
    }

    // Declares the section 7.2 column default in the schema while EF always sends the value itself.
    public static PropertyBuilder<T> WithDatabaseDefault<T>(this PropertyBuilder<T> property, string sql) =>
        property.HasDefaultValueSql(sql).ValueGeneratedNever();

    public static ComplexTypePropertyBuilder<T> WithDatabaseDefault<T>(this ComplexTypePropertyBuilder<T> property, string sql) =>
        property.HasDefaultValueSql(sql).ValueGeneratedNever();

    public static PropertyBuilder<TEnum> AsText<TEnum>(this PropertyBuilder<TEnum> property) =>
        property.HasConversion<string>().HasColumnType("text");

    public static PropertyBuilder<TEnum?> AsText<TEnum>(this PropertyBuilder<TEnum?> property)
        where TEnum : struct, Enum =>
        property.HasConversion<string?>().HasColumnType("text");
}
