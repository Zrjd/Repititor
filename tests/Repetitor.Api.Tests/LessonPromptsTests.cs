using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Tests;

public sealed class LessonPromptsTests
{
    private static readonly Language RussianInterface = new()
    {
        Id = Guid.NewGuid(),
        Code = "ru",
        NameRussian = "Русский",
        NameEnglish = "Russian",
        NativeName = "Русский"
    };

    [Fact]
    public void CustomPrompt_DoesNotDropTheJsonContract()
    {
        var system = LessonPrompts.BuildSystem(
            "Ты — опытный преподаватель иностранного языка.",
            "French",
            RussianInterface,
            CefrLevel.A2);

        Assert.Contains("Ты — опытный преподаватель иностранного языка.", system);
        Assert.Contains("\"content_markdown\"", system);
        Assert.Contains("\"key_vocabulary\"", system);
        Assert.Contains("\"summary\"", system);
        Assert.Contains("French", system);
        Assert.Contains("CEFR A2", system);
        Assert.Contains("Russian", system);
    }

    [Fact]
    public void Contract_TellsTheModelToNameTheTopic()
    {
        var system = LessonPrompts.BuildSystem(null, "English", RussianInterface, CefrLevel.B1);

        Assert.Contains("topic from the last line", system);
        Assert.Contains("must name that topic", system);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyCustomPrompt_LeavesOnlyTheContract(string? custom)
    {
        var system = LessonPrompts.BuildSystem(custom, "English", RussianInterface, CefrLevel.A1);

        Assert.StartsWith("You are an expert English course author.", system);
        Assert.Contains("\"content_markdown\"", system);
    }

    [Fact]
    public void User_PutsTheTopicLastAndImperative()
    {
        var user = LessonPrompts.BuildUser(
            "Английский для начинающих", "Numbers one to ten", CefrLevel.A2, 5, "Числительные", "с диалогом");

        var lines = user.Split('\n');
        Assert.Equal("Lesson topic (mandatory, do not change, do not replace with another theme): Numbers one to ten", lines[^1]);
        Assert.Contains("Course: Английский для начинающих", user);
        Assert.Contains("Level: CEFR A2", user);
        Assert.Contains("Duration: 5 minutes", user);
        Assert.Contains("Lesson summary: Числительные", user);
        Assert.Contains("Additional requirements: с диалогом", user);
    }

    [Fact]
    public void User_FallsBackWhenTopicIsMissing()
    {
        var user = LessonPrompts.BuildUser("Course", "   ", CefrLevel.A1, null, null, null);

        var lines = user.Split('\n');
        Assert.Equal("Lesson topic: choose one fitting topic for this course and level.", lines[^1]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void User_KeepsTopicLastWithoutOptionalFields()
    {
        var user = LessonPrompts.BuildUser("Course", "Present Simple", CefrLevel.A1, 0, "  ", "");

        var lines = user.Split('\n');
        Assert.Equal(3, lines.Length);
        Assert.EndsWith("Present Simple", lines[^1]);
    }
}
