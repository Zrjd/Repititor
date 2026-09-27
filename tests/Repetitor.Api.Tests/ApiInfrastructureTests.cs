using Microsoft.AspNetCore.Mvc;
using Repetitor.Api.Api.Dto;
using Repetitor.Api.Api.Infrastructure;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Tests;

public sealed class PagedResponseTests
{
    [Fact]
    public void TotalPages_IsComputedWhenNotProvided()
    {
        var page = new PagedResponse<string>(["a", "b", "c"], 1, 3, 10);

        Assert.Equal(4, page.TotalPages);
    }

    [Fact]
    public void TotalPages_KeepsExplicitValue()
    {
        var page = new PagedResponse<string>([], 2, 10, 0, 7);

        Assert.Equal(7, page.TotalPages);
    }

    [Fact]
    public void TotalPages_HandlesZeroPageSize()
    {
        var page = new PagedResponse<string>([], 1, 0, 5);

        Assert.Equal(0, page.TotalPages);
    }

    [Fact]
    public void Create_ComputesTotalPages()
    {
        var page = PagedResponse<int>.Create([1, 2], 1, 2, 5);

        Assert.Equal(3, page.TotalPages);
        Assert.Equal(2, page.Items.Count);
    }
}

public sealed class ApiValidationTests
{
    [Fact]
    public void Invalid_WithField_Returns400ProblemDetails()
    {
        var result = Assert.IsType<ObjectResult>(ApiValidation.Invalid("email", "Некорректный email"));

        Assert.Equal(400, result.StatusCode);

        var details = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(400, details.Status);
        Assert.Equal("validation_failed", details.Extensions["code"]);
        Assert.Equal("Некорректный email", details.Errors["email"].Single());
        Assert.Equal("urn:repetitor:validation", details.Type);
    }

    [Fact]
    public void Invalid_WithMultipleFields_MergesErrors()
    {
        var result = Assert.IsType<ObjectResult>(ApiValidation.Invalid(new Dictionary<string, string[]>
        {
            ["password"] = ["слишком простой"],
            ["email"] = ["не найден", "неверный формат"]
        }));

        var details = Assert.IsType<ValidationProblemDetails>(result.Value);
        Assert.Equal(2, details.Errors.Count);
        Assert.Equal(2, details.Errors["email"].Length);
    }

    [Fact]
    public void Invalid_UsesProblemJsonContentType()
    {
        var result = Assert.IsType<ObjectResult>(ApiValidation.Invalid("field", "message"));

        Assert.Contains("application/problem+json", result.ContentTypes);
    }
}

public sealed class EmbeddingTextTests
{
    [Fact]
    public void ToVectorLiteral_ProducesOpenAiStyleArray()
    {
        var literal = EmbeddingService.ToVectorLiteral([1f, 0.5f, 0f], 3);

        Assert.Equal("[1,0.5,0]", literal);
    }

    [Fact]
    public void ToVectorLiteral_PadsShortVectorsWithZeros()
    {
        var literal = EmbeddingService.ToVectorLiteral([1f], 3);

        Assert.Equal("[1,0,0]", literal);
    }

    [Fact]
    public void ToVectorLiteral_TruncatesLongVectorsToExpectedDimensions()
    {
        var literal = EmbeddingService.ToVectorLiteral([1f, 2f, 3f, 4f], 2);

        Assert.Equal("[1,2]", literal);
    }

    [Fact]
    public void ToVectorLiteral_HandlesZeroDimensions()
    {
        Assert.Equal("[]", EmbeddingService.ToVectorLiteral([1f], 0));
    }

    [Fact]
    public void BuildEmbeddingText_SkipsMissingFields()
    {
        var text = EmbeddingService.BuildEmbeddingText("book", "книга", null, null, PartOfSpeech.Noun);

        Assert.Equal("book\nnoun\nкнига", text);
    }

    [Fact]
    public void BuildEmbeddingText_OmitsUnknownPartOfSpeech()
    {
        var text = EmbeddingService.BuildEmbeddingText("book", "книга", "A book.", "Книга.", PartOfSpeech.Unknown);

        Assert.Equal("book\nкнига\nA book.\nКнига.", text);
    }
}
