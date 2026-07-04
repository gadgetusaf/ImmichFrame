using System.Collections;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace ImmichFrame.Core.Api;

// The Immich API client is generated from a pinned OpenAPI spec, but Immich servers evolve faster
// than that spec. When a server returns an enum value the generated client doesn't know (e.g. a new
// album-user "owner" role added after the pinned version), the default StringEnumConverter throws and
// fails deserialization of the whole response — blanking the slideshow. These types make unknown enum
// values fall back to null/default instead of throwing, so newer Immich versions stay compatible.

public class TolerantStringEnumConverter : StringEnumConverter
{
    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }
        catch (JsonException)
        {
            var enumType = Nullable.GetUnderlyingType(objectType) ?? objectType;
            if (Nullable.GetUnderlyingType(objectType) != null || !enumType.IsEnum)
                return null;

            var values = Enum.GetValues(enumType);
            return values.Length > 0 ? values.GetValue(0) : null;
        }
    }
}

// The generated DTOs annotate each enum property with [JsonConverter(typeof(StringEnumConverter))],
// and property-level converters win over anything registered in JsonSerializerSettings.Converters.
// A contract resolver is the only place that can override that per-property converter, so we swap in
// the tolerant converter for every enum (and enum-collection) member across all generated DTOs.
public class TolerantEnumContractResolver : DefaultContractResolver
{
    private static readonly TolerantStringEnumConverter Converter = new();

    protected override JsonProperty CreateProperty(System.Reflection.MemberInfo member, MemberSerialization memberSerialization)
    {
        var property = base.CreateProperty(member, memberSerialization);
        var type = property.PropertyType;
        if (type == null)
            return property;

        if ((Nullable.GetUnderlyingType(type) ?? type).IsEnum)
        {
            property.Converter = Converter;
        }
        else if (GetEnumerableEnumElement(type))
        {
            property.ItemConverter = Converter;
        }

        return property;
    }

    private static bool GetEnumerableEnumElement(Type type)
    {
        if (type == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        foreach (var i in type.GetInterfaces())
        {
            if (i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var arg = i.GetGenericArguments()[0];
                if ((Nullable.GetUnderlyingType(arg) ?? arg).IsEnum)
                    return true;
            }
        }

        return false;
    }
}

public partial class ImmichApi
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerSettings settings)
    {
        settings.ContractResolver = new TolerantEnumContractResolver();
    }
}
