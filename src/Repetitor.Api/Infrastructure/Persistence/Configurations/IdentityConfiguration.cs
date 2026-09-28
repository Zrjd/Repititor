using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Настраивает сопоставление сущности User с таблицей users.
    /// Определяет параметры пользователей, включая email, хэш пароля, роль и связи с языками.
    /// </summary>
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(320).IsRequired();
        b.HasIndex(x => x.Email).IsUnique();
        b.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
        b.Property(x => x.AvatarUrl).HasMaxLength(512);
        b.Property(x => x.Level).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.TargetLevel).HasConversion<string>().HasMaxLength(8);
        b.Property(x => x.SpeechRate).HasPrecision(4, 2);
        b.Property(x => x.PreferredAiProvider).HasMaxLength(64);
        b.Property(x => x.PreferredChatModel).HasMaxLength(128);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz");
        b.Property(x => x.LastLoginAt).HasColumnType("timestamptz");
        b.Property(x => x.LastActivityDate).HasColumnType("date");
        b.Property(x => x.AiCallsDate).HasColumnType("date");

        b.HasOne(x => x.InterfaceLanguage)
            .WithMany()
            .HasForeignKey(x => x.InterfaceLanguageId)
            .OnDelete(DeleteBehavior.Restrict);

        b.HasOne(x => x.TargetLanguage)
            .WithMany()
            .HasForeignKey(x => x.TargetLanguageId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <summary>
    /// Настраивает сопоставление сущности RefreshToken с таблицей refresh_tokens.
    /// Определяет параметры токенов обновления, включая хэш токена, срок действия и связь с пользователем.
    /// </summary>
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.ToTable("refresh_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => new { x.UserId, x.RevokedAt });
        b.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.RevokedAt).HasColumnType("timestamptz");
        b.Property(x => x.UserAgent).HasMaxLength(400);
        b.Property(x => x.IpAddress).HasMaxLength(64);
        b.HasOne(x => x.User).WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    /// <summary>
    /// Настраивает сопоставление сущности PasswordResetToken с таблицей password_reset_tokens.
    /// Определяет параметры токенов сброса пароля, включая хэш токена и срок действия.
    /// </summary>
    public void Configure(EntityTypeBuilder<PasswordResetToken> b)
    {
        b.ToTable("password_reset_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.Property(x => x.ExpiresAt).HasColumnType("timestamptz");
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz");
        b.Property(x => x.UsedAt).HasColumnType("timestamptz");
        b.HasOne(x => x.User).WithMany()
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
