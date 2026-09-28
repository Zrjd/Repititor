using Microsoft.Extensions.Logging;

namespace Repetitor.Api.Infrastructure.Services;

public interface IEmailService
{
    /// <summary>
    /// Отправляет письмо со ссылкой для сброса пароля.
    /// </summary>
    Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct = default);
}

public sealed class LoggingEmailService(ILogger<LoggingEmailService> logger) : IEmailService
{
    /// <summary>
    /// Отправляет письмо для сброса пароля.
    /// В текущей реализации письмо не отправляется по-настоящему: событие только записывается в журнал.
    /// </summary>
    public Task SendPasswordResetAsync(string email, string resetLink, CancellationToken ct = default)
    {
        logger.LogInformation("Password reset email to {Email}: {Link}", email, resetLink);
        return Task.CompletedTask;
    }
}
