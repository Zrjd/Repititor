using Microsoft.EntityFrameworkCore;
using Repetitor.Api.Domain.Entities;
using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Persistence;
using Repetitor.Api.Infrastructure.Auth;

namespace Repetitor.Api.Infrastructure.Services;

public sealed record ReviewOutcome(
    CardState State,
    double IntervalDays,
    double EaseFactor,
    int Repetitions,
    int Lapses,
    int LearningStep,
    DateTimeOffset DueAt,
    bool WasCorrect,
    int MasteryDelta);

public interface ISrsService
{
    ReviewOutcome Schedule(ReviewCard card, ReviewRating rating, IClock clock);
    IReadOnlyList<DateOnly> Forecast(IEnumerable<ReviewCard> cards, IClock clock, int days);
}

public sealed class SrsService : ISrsService
{
    private const double MinEase = 1.3;
    private const double MaxEase = 3.2;
    private const double EasyBonus = 1.3;
    private const double HardFactor = 1.2;
    private const double LapseMultiplier = 0.4;

    public ReviewOutcome Schedule(ReviewCard card, ReviewRating rating, IClock clock)
    {
        var now = clock.UtcNow;
        var wasCorrect = rating != ReviewRating.Again;
        var previousState = card.State;
        var previousInterval = card.IntervalDays;
        var ease = Math.Clamp(card.EaseFactor <= 0 ? 2.5 : card.EaseFactor, MinEase, MaxEase);
        var repetitions = card.Repetitions;
        var lapses = card.Lapses;
        var learningStep = card.LearningStep;
        var state = card.State;
        double interval;

        if (state is CardState.New or CardState.Learning or CardState.Relearning)
        {
            interval = ApplyLearningStep(ref learningStep, rating, ref state, ref ease, now);
        }
        else
        {
            switch (rating)
            {
                case ReviewRating.Again:
                    lapses++;
                    state = CardState.Relearning;
                    learningStep = 0;
                    ease = Math.Max(MinEase, ease - 0.2);
                    interval = Math.Max(1, Math.Max(previousInterval, 1) * LapseMultiplier);
                    repetitions = 0;
                    break;
                case ReviewRating.Hard:
                    interval = Math.Max(1, Math.Max(previousInterval * HardFactor, previousInterval + 1));
                    ease = Math.Max(MinEase, ease - 0.15);
                    repetitions++;
                    break;
                case ReviewRating.Good:
                    interval = previousInterval <= 0
                        ? 1
                        : previousInterval * (state == CardState.Mastered ? Math.Max(ease, 2.0) : ease);
                    repetitions++;
                    if (repetitions >= 3 && interval >= 21)
                    {
                        state = CardState.Mastered;
                    }

                    break;
                case ReviewRating.Easy:
                    interval = Math.Max(previousInterval + 1, previousInterval * ease * EasyBonus);
                    ease = Math.Min(MaxEase, ease + 0.15);
                    repetitions++;
                    if (repetitions >= 2)
                    {
                        state = CardState.Mastered;
                    }

                    break;
                default:
                    interval = Math.Max(previousInterval, 1);
                    break;
            }
        }

        interval = Math.Round(Math.Clamp(interval, 0.0, card.MaxIntervalDays), 3);
        var masteryDelta = MasteryDelta(rating, previousState);

        return new ReviewOutcome(
            state,
            interval,
            Math.Round(ease, 3),
            repetitions,
            lapses,
            learningStep,
            now.AddDays(interval),
            wasCorrect,
            masteryDelta);
    }

    public IReadOnlyList<DateOnly> Forecast(IEnumerable<ReviewCard> cards, IClock clock, int days)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var buckets = new int[Math.Max(days, 1)];
        foreach (var card in cards.Where(c => c.SuspendedAt is null))
        {
            var due = DateOnly.FromDateTime(card.DueAt.UtcDateTime);
            var offset = due.DayNumber - today.DayNumber;
            if (offset < 0)
            {
                offset = 0;
            }

            if (offset < buckets.Length)
            {
                buckets[offset]++;
            }
        }

        var result = new DateOnly[buckets.Length];
        for (var i = 0; i < buckets.Length; i++)
        {
            result[i] = today.AddDays(i);
        }

        return result;
    }

    private double ApplyLearningStep(ref int step, ReviewRating rating, ref CardState state, ref double ease, DateTimeOffset now)
    {
        const int firstStepMinutes = 1;
        const int secondStepMinutes = 10;

        switch (rating)
        {
            case ReviewRating.Again:
                step = 0;
                state = CardState.Learning;
                return firstStepMinutes / 1440d;
            case ReviewRating.Hard:
                ease = Math.Max(MinEase, ease - 0.15);
                if (step == 0)
                {
                    step = 0;
                    state = CardState.Learning;
                    return (firstStepMinutes * 1.5) / 1440d;
                }

                step = 1;
                state = CardState.Learning;
                return (secondStepMinutes * 1.5) / 1440d;
            case ReviewRating.Good:
                if (step == 0)
                {
                    step = 1;
                    state = CardState.Learning;
                    return secondStepMinutes / 1440d;
                }

                state = CardState.Review;
                step = 0;
                return 1;
            case ReviewRating.Easy:
                ease = Math.Min(MaxEase, ease + 0.15);
                state = CardState.Review;
                step = 0;
                return 4;
            default:
                return 1;
        }
    }

    private static int MasteryDelta(ReviewRating rating, CardState previousState)
    {
        var baseValue = rating switch
        {
            ReviewRating.Easy => 20,
            ReviewRating.Good => 12,
            ReviewRating.Hard => 6,
            _ => -10
        };

        return previousState switch
        {
            CardState.New or CardState.Learning => baseValue * 2,
            CardState.Relearning => baseValue + 5,
            _ => baseValue
        };
    }
}

public sealed record XpResult(int TotalXp, int CurrentStreak, int LongestStreak, bool GoalReached, int TodayXp, int DailyGoal);

public interface IProgressService
{
    Task<XpResult> RegisterActivityAsync(
        Guid userId,
        int xp,
        int reviewsCompleted = 0,
        int correctAnswers = 0,
        int newWords = 0,
        int exercises = 0,
        int chatMessages = 0,
        int secondsStudied = 0,
        CancellationToken ct = default);
}

public sealed class ProgressService(IDbContextFactory<AppDbContext> dbFactory, IClock clock) : IProgressService
{
    public async Task<XpResult> RegisterActivityAsync(
        Guid userId,
        int xp,
        int reviewsCompleted = 0,
        int correctAnswers = 0,
        int newWords = 0,
        int exercises = 0,
        int chatMessages = 0,
        int secondsStudied = 0,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct)
                   ?? throw new InvalidOperationException($"User {userId} not found");

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var stat = await db.UserDailyStats.FirstOrDefaultAsync(s => s.UserId == userId && s.Date == today, ct);
        if (stat is null)
        {
            stat = new UserDailyStat { UserId = userId, Date = today };
            db.UserDailyStats.Add(stat);
        }

        stat.XpEarned += xp;
        stat.ReviewsCompleted += reviewsCompleted;
        stat.CorrectAnswers += correctAnswers;
        stat.NewWordsLearned += newWords;
        stat.ExercisesCompleted += exercises;
        stat.ChatMessagesSent += chatMessages;
        stat.MinutesStudied += secondsStudied / 60;

        var previousDate = user.LastActivityDate;
        user.TotalXp += xp;

        if (previousDate is not null)
        {
            var gap = today.DayNumber - previousDate.Value.DayNumber;
            if (gap == 1)
            {
                user.CurrentStreak++;
            }
            else if (gap > 1)
            {
                user.CurrentStreak = 1;
            }
        }
        else
        {
            user.CurrentStreak = Math.Max(1, user.CurrentStreak);
        }

        user.LongestStreak = Math.Max(user.LongestStreak, user.CurrentStreak);
        user.LastActivityDate = today;
        stat.GoalReached = stat.XpEarned >= user.DailyGoalXp;

        await db.SaveChangesAsync(ct);
        return new XpResult(user.TotalXp, user.CurrentStreak, user.LongestStreak, stat.GoalReached, stat.XpEarned, user.DailyGoalXp);
    }
}
