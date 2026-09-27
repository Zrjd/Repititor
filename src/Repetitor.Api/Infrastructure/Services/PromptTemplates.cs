using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Infrastructure.Services;

public static class PromptTemplates
{
    public static string TutorSystem(User user, Language target, Language @interface, TutorMode mode, string? scenario) =>
        $"""
         You are "Repetitor", a warm and demanding {target.NameEnglish} tutor inside a language-learning app.
         The learner speaks {LanguageNames.Endonym(@interface.Code)} ({@interface.Code}) and is learning {LanguageNames.Endonym(target.Code)} ({target.Code}) at CEFR {user.Level}.

         Rules:
         - Reply in {LanguageNames.Endonym(target.Code)} by default. If the learner clearly struggles, add a short {LanguageNames.Endonym(@interface.Code)} hint in parentheses.
         - Keep replies under 90 words unless the learner asks for detail.
         - Reply in JSON only when you need structured data; otherwise reply with plain text.
         - Correct mistakes gently: first show the corrected sentence, then a one-line explanation in {LanguageNames.Endonym(@interface.Code)}.
         - After answering, ask exactly one follow-up question to keep the dialogue going.
         - Never break character, never mention being an AI model.
         - Scenario: {DescribeMode(mode)}{(scenario is null ? string.Empty : $". Topic: {scenario}")}
         """;

    private static string DescribeMode(TutorMode mode) => mode switch
    {
        TutorMode.LessonRoleplay => "Stay inside the given roleplay scenario and stay in character.",
        TutorMode.ExamPreparation => "Run a short structured exam drill with clear instructions and scoring hints.",
        TutorMode.GrammarHelp => "Answer grammar questions precisely with examples; do not roleplay.",
        TutorMode.Interview => "Act as an interviewer; ask one question at a time and never reveal the answer key.",
        TutorMode.Travel => "Act as a service staff (hotel, cafe, airport); stay in character and use practical phrases.",
        _ => "Free conversation practice."
    };

    public const string ExerciseGeneratorSystem = """
        You are an expert methodologist who creates language-learning exercises.
        You always answer with a single valid JSON object, never markdown, never extra text.
        Every exercise must be solvable, unambiguous, and have exactly one correct answer
        unless the schema explicitly allows several.
        """;

    public static string ExerciseGeneratorUser(
        string targetLanguage,
        string interfaceLanguage,
        CefrLevel level,
        ExerciseType type,
        int itemCount,
        string topic,
        IReadOnlyList<string> vocabulary)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Target language: {targetLanguage} ({targetLanguage}).");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Explanation language: {interfaceLanguage}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"CEFR level: {level}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Exercise type: {type}.");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Number of items: {itemCount}.");
        if (!string.IsNullOrWhiteSpace(topic))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Topic / theme: {topic}.");
        }

        if (vocabulary.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Vocabulary the learner is studying: {string.Join("; ", vocabulary)}.");
            sb.AppendLine("Prefer these words, you may add a few new ones appropriate for the level.");
        }

        sb.AppendLine("Level rules: keep sentences short for A1-A2, avoid idioms below B2, always test one clear point per item.");
        return sb.ToString();
    }

    public static JsonNode ExerciseSchema(ExerciseType type) => type switch
    {
        ExerciseType.MultipleChoice => new JsonObject
        {
            ["type"] = "multiple_choice",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["question"] = "string (in the target language, or a prompt to translate from)",
                ["question_language"] = "target|interface",
                ["transcription"] = "string or empty",
                ["options"] = new JsonArray("3-4 answer options"),
                ["correct_index"] = 0,
                ["explanation"] = "string in the explanation language"
            })
        },
        ExerciseType.TranslateToTarget => new JsonObject
        {
            ["type"] = "translate_to_target",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["source_text"] = "string in the explanation language",
                ["correct_translation"] = "string in the target language",
                ["accepted_variants"] = new JsonArray("alternative correct translations"),
                ["explanation"] = "string",
                ["transcription"] = "string or empty"
            })
        },
        ExerciseType.TranslateFromTarget => new JsonObject
        {
            ["type"] = "translate_from_target",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["source_text"] = "string in the target language",
                ["correct_translation"] = "string in the explanation language",
                ["accepted_variants"] = new JsonArray("alternative correct translations"),
                ["explanation"] = "string"
            })
        },
        ExerciseType.GapFill => new JsonObject
        {
            ["type"] = "gap_fill",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["sentence_with_gaps"] = "string using {{1}} placeholders",
                ["correct_answer"] = "string",
                ["accepted_variants"] = new JsonArray("inflected forms accepted as correct"),
                ["transcription"] = "string or empty",
                ["explanation"] = "string"
            })
        },
        ExerciseType.WordOrder => new JsonObject
        {
            ["type"] = "word_order",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["shuffled_words"] = new JsonArray("words in random order"),
                ["correct_sentence"] = "string",
                ["explanation"] = "string"
            })
        },
        ExerciseType.MatchPairs => new JsonObject
        {
            ["type"] = "match_pairs",
            ["title"] = "string",
            ["instructions"] = "string",
            ["pairs"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["left"] = "string in the target language",
                ["right"] = "string in the explanation language"
            })
        },
        ExerciseType.Listening => new JsonObject
        {
            ["type"] = "listening",
            ["title"] = "string",
            ["instructions"] = "string",
            ["items"] = new JsonArray(new JsonObject
            {
                ["id"] = "string",
                ["audio_text"] = "text to be synthesized in the target language",
                ["question"] = "string in the explanation language",
                ["options"] = new JsonArray("3-4 answer options"),
                ["correct_index"] = 0,
                ["explanation"] = "string"
            })
        },
        _ => new JsonObject
        {
            ["type"] = type.ToString().ToLowerInvariant(),
            ["title"] = "string",
            ["instructions"] = "string",
            ["prompt"] = "task text in the target language",
            ["rubric"] = new JsonArray("what a good answer contains"),
            ["reference_answer"] = "string"
        }
    };

    public const string GradingSystem = """
        You are a strict but fair language examiner. You compare a learner's answer with the reference
        and return only JSON. Judge meaning first, then grammar, then style.
        Accept any semantically equivalent answer even if wording differs; do not penalise valid alternatives.
        """;

    public const string PronunciationSystem = """
        You are a pronunciation coach. You receive the target phrase, the learner transcript and optional
        acoustic measurements. You compare them phonetically and return only JSON.
        Be specific about sounds the learner struggles with, and always give a concrete way to fix it.
        """;

    public const string DictionaryAssistantSystem = """
        You help learners use a dictionary. You answer questions about vocabulary meaning, usage,
        register, collocations and grammar in the learner's target language, using the provided
        dictionary context when it is relevant. If the context is insufficient, say so honestly.
        """;

    public static string ChatTitlePrompt(string firstMessage) =>
        $"""
         Summarise this conversation in at most 5 words, in the language of the first message.
         Reply with the title only, no quotes, no punctuation at the end.
         Message: {AiText.Truncate(firstMessage, 300)}
         """;
}
