using Erp.SharedKernel.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Erp.BuildingBlocks.Infrastructure.Persistence;

public static class ModelBuilderExtensions
{
    /// <summary>Stores every enum as its snake_case code (e.g. head_office), identical to the frontend literals and the JSON API.</summary>
    public static ModelBuilder UseSnakeCaseEnums(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                var enumType = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (!enumType.IsEnum || property.GetValueConverter() is not null)
                {
                    continue;
                }

                var converterType = typeof(SnakeCaseEnumConverter<>).MakeGenericType(enumType);
                property.SetValueConverter((ValueConverter)Activator.CreateInstance(converterType)!);
                property.SetMaxLength(40);
                property.SetIsUnicode(false);
            }
        }

        return modelBuilder;
    }
}

public sealed class SnakeCaseEnumConverter<TEnum>()
    : ValueConverter<TEnum, string>(v => ToCode(v), v => FromCode(v))
    where TEnum : struct, Enum
{
    private static readonly Dictionary<TEnum, string> ToCodes =
        Enum.GetValues<TEnum>().ToDictionary(v => v, v => SnakeCase.From(v.ToString()));

    private static readonly Dictionary<string, TEnum> FromCodes =
        ToCodes.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);

    private static string ToCode(TEnum value) => ToCodes[value];

    private static TEnum FromCode(string code) =>
        FromCodes.TryGetValue(code, out var value) ? value : Enum.Parse<TEnum>(code, ignoreCase: true);
}
