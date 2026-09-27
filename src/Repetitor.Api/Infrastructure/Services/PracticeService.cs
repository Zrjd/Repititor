using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Ai;
using Repetitor.Api.Infrastructure.Auth;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record DueCard(
    Guid ReviewCardId,
    Guid LexicalUnitId,
    string Text,
    string? Translation,
    string? Transcription,
    string? AudioUrl,
    string? PartOfSpeech,
    string? ExampleTarget,
    CardState State,
    DateTimeOffset DueAt,
    double IntervalDays,
    int Repetitions);

public sealed record ReviewSubmission(Guid ReviewCardId, ReviewRating Rating, int DurationMs, string? GivenAnswer);

public sealed record ReviewResult(
    Guid ReviewCardId,
    CardState State,
    DateTimeOffset DueAt,
    double IntervalDays,
    double EaseFactor,
    bool WasCorrect,
    int MasteryScore,
    string? CorrectTranslation,
    int XpEarned);

public sealed record SessionSummary(
    int Reviewed,
    int Correct,
    int Again,
    int NewCardsSeen,
    int XpEarned,
    int AccuracyPercent,
    int RemainingDue,
    IReadOnlyList<DateOnly> Forecast);

public interface IPracticeService
{
    Task<IReadOnlyList<DueCard>> GetDueCardsAsync(Guid userId, Guid? deckId, int limit, CancellationToken ct = default);
    Task<IReadOnlyList<ReviewResult>> SubmitReviewsAsync(Guid userId, IReadOnlyList<ReviewSubmission> submissions, CancellationToken ct = default);
    Task<SessionSummary> BuildSummaryAsync(Guid userId, Guid? deckId, int reviewed, int correct, int again, int newSeen, int xp, CancellationToken ct = default);
    Task<int> CountDueAsync(Guid userId, Guid? deckId, CancellationToken ct = default);
}

public sealed class PracticeService(
    IDbContextFactory<AppDbContext> dbFactory,
    ISrsService srs,
    IProgressService progress,
    IClock clock) : IPracticeService
{
    private const int XpPerReview = 2;

    public async Task<IReadOnlyList<DueCard>> GetDueCardsAsync(Guid userId, Guid? deckId, int limit, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        limit = Math.Clamp(limit, 1, 200);

        var query = from card in db.ReviewCards
            join ul in db.UserLexicalUnits on card.UserLexicalUnitId equals ul.Id
            join unit in db.LexicalUnits on ul.LexicalUnitId equals unit.Id
            where ul.UserId == userId
                  && card.SuspendedAt == null
                  && card.DueAt <= now
            select new { card, ul, unit };

        if (deckId is { } deck)
        {
            var cardIds = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(x => cardIds.Contains(x.ul.Id));
        }

        return await query
            .OrderBy(x => x.card.State)
            .ThenBy(x => x.card.DueAt)
            .Take(limit)
            .Select(x => new DueCard(
                x.card.Id, x.unit.Id, x.unit.Text, x.unit.Translation, x.unit.Transcription, x.unit.AudioUrl,
                x.unit.PartOfSpeech.ToString(), x.unit.ExampleTarget, x.card.State, x.card.DueAt,
                x.card.IntervalDays, x.card.Repetitions))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ReviewResult>> SubmitReviewsAsync(
        Guid userId,
        IReadOnlyList<ReviewSubmission> submissions,
        CancellationToken ct = default)
    {
        if (submissions.Count == 0)
        {
            return [];
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var cardIds = submissions.Select(s => s.ReviewCardId).Distinct().ToArray();

        var cards = await db.ReviewCards
            .Include(c => c.UserLexicalUnit).ThenInclude(u => u!.LexicalUnit)
            .Where(c => cardIds.Contains(c.Id) && c.UserLexicalUnit!.UserId == userId)
            .ToListAsync(ct);

        if (cards.Count == 0)
        {
            return [];
        }

        var byId = cards.ToDictionary(c => c.Id);
        var results = new List<ReviewResult>();
        int totalXp = 0;
        int correct = 0;

        foreach (var submission in submissions)
        {
            if (!byId.TryGetValue(submission.ReviewCardId, out var card))
            {
                continue;
            }

            var previousState = card.State;
            var previousInterval = card.IntervalDays;
            var outcome = srs.Schedule(card, submission.Rating, clock);

            card.State = outcome.State;
            card.IntervalDays = outcome.IntervalDays;
            card.EaseFactor = outcome.EaseFactor;
            card.Repetitions = outcome.Repetitions;
            card.Lapses = outcome.Lapses;
            card.LearningStep = outcome.LearningStep;
            card.DueAt = outcome.DueAt;
            card.LastReviewedAt = now;

            var userUnit = card.UserLexicalUnit!;
            userUnit.State = outcome.State;
            userUnit.LastReviewedAt = now;
            userUnit.MasteryScore = Math.Clamp(userUnit.MasteryScore + outcome.MasteryDelta, 0, 100);
            if (outcome.WasCorrect)
            {
                userUnit.CorrectStreak++;
                userUnit.IncorrectStreak = 0;
            }
            else
            {
                userUnit.IncorrectStreak++;
                userUnit.CorrectStreak = 0;
            }

            db.ReviewLogs.Add(new ReviewLog
            {
                UserId = userId,
                ReviewCardId = card.Id,
                LexicalUnitId = userUnit.LexicalUnitId,
                Rating = submission.Rating,
                PreviousIntervalDays = previousInterval,
                NewIntervalDays = outcome.IntervalDays,
                PreviousState = previousState,
                NewState = outcome.State,
                DurationMs = Math.Clamp(submission.DurationMs, 0, 600_000),
                WasCorrect = outcome.WasCorrect,
                GivenAnswer = submission.GivenAnswer is null ? null : AiText.Truncate(submission.GivenAnswer, 1000)
            });

            var xp = XpPerReview * (submission.Rating switch
            {
                ReviewRating.Easy => 2,
                ReviewRating.Good => 1,
                ReviewRating.Hard => 1,
                _ => 0
            });
            totalXp += xp;
            if (outcome.WasCorrect)
            {
                correct++;
            }

            results.Add(new ReviewResult(
                card.Id,
                outcome.State,
                outcome.DueAt,
                outcome.IntervalDays,
                outcome.EaseFactor,
                outcome.WasCorrect,
                userUnit.MasteryScore,
                userUnit.LexicalUnit?.Translation,
                xp));
        }

        await db.SaveChangesAsync(ct);

        if (results.Count > 0)
        {
            await progress.RegisterActivityAsync(userId, totalXp, results.Count, correct, 0, 0, 0,
                (int)submissions.Average(s => Math.Clamp(s.DurationMs, 0, 600_000)) * results.Count / 1000, ct);
        }

        return results;
    }

    public async Task<SessionSummary> BuildSummaryAsync(
        Guid userId,
        Guid? deckId,
        int reviewed,
        int correct,
        int again,
        int newSeen,
        int xp,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;

        var query = from card in db.ReviewCards
            join ul in db.UserLexicalUnits on card.UserLexicalUnitId equals ul.Id
            where ul.UserId == userId && card.SuspendedAt == null
            select card;

        if (deckId is { } deck)
        {
            var cardIds = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(c => cardIds.Contains(c.UserLexicalUnitId));
        }

        var cards = await query.ToListAsync(ct);
        var remaining = cards.Count(c => c.DueAt <= now);
        var forecast = srs.Forecast(cards, clock, 14).ToArray();

        return new SessionSummary(
            reviewed,
            correct,
            again,
            newSeen,
            xp,
            reviewed == 0 ? 0 : (int)Math.Round(correct * 100d / reviewed),
            remaining,
            forecast);
    }

    public async Task<int> CountDueAsync(Guid userId, Guid? deckId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;

        var query = from card in db.ReviewCards
            join ul in db.UserLexicalUnits on card.UserLexicalUnitId equals ul.Id
            where ul.UserId == userId && card.SuspendedAt == null && card.DueAt <= now
            select card.Id;

        if (deckId is { } deck)
        {
            var cardIds = db.DeckCards.Where(dc => dc.DeckId == deck).Select(dc => dc.UserLexicalUnitId);
            query = query.Where(id => cardIds.Contains(db.ReviewCards.First(rc => rc.Id == id).UserLexicalUnitId));
        }

        return await query.CountAsync(ct);
    }
}
