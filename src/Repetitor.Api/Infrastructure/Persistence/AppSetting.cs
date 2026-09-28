using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed class AppSetting
{
    /// <summary>
    /// Ключ настройки приложения. Используется как первичный ключ в таблице app_settings.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>
    /// Значение настройки в формате JSON. Позволяет хранить различные типы данных в одном поле.
    /// </summary>
    public JsonNode? Value { get; set; }
    /// <summary>
    /// Дата и время последнего обновления настройки. Используется для отслеживания изменений.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed partial class AppDbContext
{
    /// <summary>
    /// Предоставляет доступ к настройкам приложения через DbSet.
    /// Позволяет выполнять LINQ-запросы к таблице app_settings.
    /// </summary>
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    /// <summary>
    /// Настраивает сопоставление сущности AppSetting с таблицей app_settings.
    /// Определяет ключ как первичный ключ и настраивает поле даты обновления.
    /// </summary>
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(120);
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
    }
}
