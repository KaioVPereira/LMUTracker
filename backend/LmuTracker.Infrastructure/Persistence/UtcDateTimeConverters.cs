using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace LmuTracker.Infrastructure.Persistence;

// Tudo é gravado em UTC, mas o SQL Server devolve DateTime "sem tipo" (Unspecified) e a API acabaria
// serializando sem o 'Z' — o navegador leria como horário local. Na leitura marcamos como UTC.

internal sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc)) { }
}

internal sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
{
    public NullableUtcDateTimeConverter()
        : base(v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v) { }
}
