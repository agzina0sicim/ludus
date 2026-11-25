using Ludus.Domain.Common;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ludus.Infrastructure.Persistence;

public sealed class SecretConverter() : ValueConverter<ProtectedSecret, string>(
    secret => secret.Ciphertext,
    ciphertext => new ProtectedSecret(ciphertext));

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
