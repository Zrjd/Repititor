using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Repetitor.Api.Infrastructure.Persistence;

public sealed class JsonNodeValueConverter : ValueConverter<JsonNode?, string?>
{
    public JsonNodeValueConverter()
        : base(v => v == null ? null : v.ToJsonString(AppDbContext.JsonOptions),
               v => string.IsNullOrWhiteSpace(v) ? null : Parse(v))
    {
    }

    private static JsonNode? Parse(string json) =>
        JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
}
