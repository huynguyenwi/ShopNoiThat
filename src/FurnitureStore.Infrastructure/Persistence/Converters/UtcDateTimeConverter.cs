using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FurnitureStore.Infrastructure.Persistence.Converters;

/// <summary>
/// Stores DateTime values as UTC and marks values read from the database as <see cref="DateTimeKind.Utc"/>,
/// so JSON serialization emits "Z" and client-side time zone conversion is correct.
/// </summary>
public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value,
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
