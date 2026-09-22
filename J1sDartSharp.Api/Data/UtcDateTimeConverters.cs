using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace J1sDartSharp.Api.Data;

// SQLite stores DateTime as text with no Kind; reads come back as
// DateTimeKind.Unspecified, which then serialize to JSON without a "Z"
// and get misread as local time on the client. These force UTC on read.

public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
