using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed class AppSetting
{
    public string Key { get; set; } = string.Empty;
    public JsonNode? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed partial class AppDbContext
{
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
}

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.ToTable("app_settings");
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(120);
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
    }
}
