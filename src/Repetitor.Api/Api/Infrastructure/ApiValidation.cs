using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Repetitor.Api.Api.Infrastructure;

public static class ApiValidation
{
    public static ActionResult Invalid(string field, string message) =>
        Invalid(new Dictionary<string, string[]> { [field] = [message] });

    public static ActionResult Invalid(IDictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Ошибка валидации",
            Type = "urn:repetitor:validation"
        };
        details.Extensions["code"] = "validation_failed";

        return new ObjectResult(details)
        {
            StatusCode = StatusCodes.Status400BadRequest,
            ContentTypes = { "application/problem+json" }
        };
    }
}
