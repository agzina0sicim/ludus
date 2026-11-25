using System.Reflection;
using Ludus.Application.Security;
using Ludus.Domain.Common;

namespace Ludus.Api.Features;

public static class ConfigurationSupport
{
    public static object View(object entity)
    {
        var result = new Dictionary<string, object?>();
        foreach (var property in entity.GetType().GetProperties())
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            if (type == typeof(ProtectedSecret)) result[name + "Configured"] = property.GetValue(entity) is not null;
            else if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime) || type == typeof(long)
                || type == typeof(List<string>) || type == typeof(List<int>) || type == typeof(List<NotificationEvent>)) result[name] = property.GetValue(entity);
        }
        return result;
    }
    public static void Apply(object request, object entity)
    {
        foreach (var input in request.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            var output = entity.GetType().GetProperty(input.Name);
            if (output?.CanWrite == true && output.PropertyType == input.PropertyType) output.SetValue(entity, input.GetValue(request));
        }
    }
    public static ProtectedSecret? Secret(string? supplied, ProtectedSecret? existing, ISecretProtector protector)
        => supplied is null ? existing : supplied.Length == 0 ? null : protector.Protect(supplied);
    public static string BaseUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("An HTTP(S) URL without credentials, query or fragment is required.");
        return value.TrimEnd('/');
    }
}
