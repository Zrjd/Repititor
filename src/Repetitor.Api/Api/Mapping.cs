using Repetitor.Api.Api.Dto;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Infrastructure.Services;

namespace Repetitor.Api.Api;

public static class Mapping
{
    public static UserResponse ToResponse(this User user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.Role.ToString(),
        user.AvatarUrl,
        user.TargetLanguageId,
        user.TargetLanguage?.Code ?? string.Empty,
        user.TargetLanguage?.NameRussian ?? user.TargetLanguage?.NameEnglish ?? string.Empty,
        user.InterfaceLanguageId,
        user.Level.ToString(),
        user.TargetLevel.ToString(),
        user.DailyGoalXp,
        user.SpeechRate,
        user.TotalXp,
        user.CurrentStreak,
        user.LongestStreak,
        user.PreferredAiProvider,
        user.EmailConfirmed,
        user.CreatedAt);

    public static LanguageResponse ToResponse(this Language language) => new(
        language.Id,
        language.Code,
        language.NameEnglish,
        language.NameRussian,
        language.NativeName,
        language.FlagEmoji,
        language.IsEnabled);

    public static LexicalUnitResponse ToResponse(this LexicalUnit unit, double? similarity = null) => new(
        unit.Id,
        unit.LanguageId,
        unit.TranslationLanguageId,
        unit.Text,
        unit.Transcription,
        unit.Translation,
        unit.AlternativeTranslations,
        unit.PartOfSpeech.ToString(),
        unit.Gender,
        unit.PluralForm,
        unit.PastTense,
        unit.AudioUrl,
        unit.ExampleTarget,
        unit.ExampleNative,
        unit.Notes,
        unit.Tags,
        unit.MinLearnerLevel.ToString(),
        unit.FrequencyRank,
        unit.Status.ToString(),
        unit.CreatedAt,
        unit.UpdatedAt,
        similarity);

    public static UserWordResponse ToResponse(this UserLexicalUnit uw) => new(
        uw.Id,
        uw.LexicalUnitId,
        uw.LexicalUnit?.Text ?? string.Empty,
        uw.LexicalUnit?.Translation,
        uw.LexicalUnit?.Transcription,
        uw.LexicalUnit?.AudioUrl,
        uw.LexicalUnit?.PartOfSpeech.ToString(),
        uw.LexicalUnit?.ExampleTarget,
        uw.State.ToString(),
        uw.Suspended,
        uw.MasteryScore,
        uw.CorrectStreak,
        uw.IncorrectStreak,
        uw.PersonalNote,
        uw.Source.ToString(),
        uw.AddedAt,
        uw.LastReviewedAt);

    public static DeckResponse ToResponse(this Deck deck, int due, int newCount, int mastered) => new(
        deck.Id,
        deck.Name,
        deck.Description,
        deck.LanguageCode,
        deck.CoverEmoji,
        deck.Tags,
        deck.IsArchived,
        deck.Cards.Count,
        due,
        newCount,
        mastered,
        deck.CreatedAt,
        deck.UpdatedAt);

    public static DueCardResponse ToResponse(this DueCard card) => new(
        card.ReviewCardId,
        card.LexicalUnitId,
        card.Text,
        card.Translation,
        card.Transcription,
        card.AudioUrl,
        card.PartOfSpeech,
        card.ExampleTarget,
        card.State.ToString(),
        card.DueAt,
        card.IntervalDays,
        card.Repetitions);

    public static ReviewResultResponse ToResponse(this ReviewResult result) => new(
        result.ReviewCardId,
        result.State.ToString(),
        result.DueAt,
        result.IntervalDays,
        result.EaseFactor,
        result.WasCorrect,
        result.MasteryScore,
        result.CorrectTranslation,
        result.XpEarned);

    public static ChatSessionResponse ToResponse(this ChatSession session) => new(
        session.Id,
        session.Title,
        session.Mode.ToString(),
        session.Level.ToString(),
        session.Scenario,
        session.Provider,
        session.Model,
        session.UseDictionaryContext,
        session.IsArchived,
        session.Messages.Count,
        session.TotalInputTokens,
        session.TotalOutputTokens,
        session.CreatedAt,
        session.LastMessageAt);

    public static ChatMessageResponse ToResponse(this ChatMessage message) => new(
        message.Id,
        message.Role.ToString(),
        message.Content,
        message.AudioUrl,
        message.Provider,
        message.Model,
        message.InputTokens,
        message.OutputTokens,
        message.LatencyMs,
        message.RagContextRefs,
        message.FeedbackRating,
        message.CreatedAt);

    public static SemanticMatch[] ToResponses(this IReadOnlyList<SemanticMatch> matches) =>
        matches.Select(m => new SemanticMatch(
            m.LexicalUnitId, m.Text, m.Translation, m.Transcription, m.PartOfSpeech,
            m.Similarity, m.MinLearnerLevel, m.ExampleTarget)).ToArray();
}
