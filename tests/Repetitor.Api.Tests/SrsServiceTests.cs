using Repetitor.Api.Domain.Enums;
using Repetitor.Api.Infrastructure.Auth;
using Repetitor.Api.Infrastructure.Services;
using Repetitor.Api.Domain.Entities;

namespace Repetitor.Api.Tests;

public sealed class SrsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static ReviewCard NewCard(CardState state = CardState.New) => new()
    {
        Id = Guid.NewGuid(),
        UserLexicalUnitId = Guid.NewGuid(),
        State = state,
        EaseFactor = 2.5,
        IntervalDays = 0,
        Repetitions = 0,
        Lapses = 0,
        LearningStep = 0,
        DueAt = Now,
        MaxIntervalDays = 365
    };

    [Fact]
    public void Schedule_NewCardAgain_StaysInLearningWithShortInterval()
    {
        var outcome = new SrsService().Schedule(NewCard(), ReviewRating.Again, new FixedClock(Now));

        Assert.Equal(CardState.Learning, outcome.State);
        Assert.False(outcome.WasCorrect);
        Assert.True(outcome.IntervalDays > 0 && outcome.IntervalDays < 1);
        Assert.True(outcome.MasteryDelta < 0);
    }

    [Fact]
    public void Schedule_NewCardGood_MovesToSecondLearningStep()
    {
        var outcome = new SrsService().Schedule(NewCard(), ReviewRating.Good, new FixedClock(Now));

        Assert.Equal(CardState.Learning, outcome.State);
        Assert.Equal(1, outcome.LearningStep);
        Assert.True(outcome.IntervalDays > 0 && outcome.IntervalDays < 1);
    }

    [Fact]
    public void Schedule_EasyOnNewCard_GraduatesToReviewWithFourDays()
    {
        var outcome = new SrsService().Schedule(NewCard(), ReviewRating.Easy, new FixedClock(Now));

        Assert.Equal(CardState.Review, outcome.State);
        Assert.Equal(4, outcome.IntervalDays);
    }

    [Fact]
    public void Schedule_GoodOnReview_GrowsInterval()
    {
        var card = NewCard(CardState.Review);
        card.IntervalDays = 3;
        card.Repetitions = 1;

        var outcome = new SrsService().Schedule(card, ReviewRating.Good, new FixedClock(Now));

        Assert.Equal(CardState.Review, outcome.State);
        Assert.Equal(2, outcome.Repetitions);
        Assert.True(outcome.IntervalDays > card.IntervalDays);
    }

    [Fact]
    public void Schedule_AgainOnReview_CountsLapseAndReducesEase()
    {
        var card = NewCard(CardState.Review);
        card.IntervalDays = 10;
        card.Repetitions = 4;
        card.EaseFactor = 2.5;

        var outcome = new SrsService().Schedule(card, ReviewRating.Again, new FixedClock(Now));

        Assert.Equal(CardState.Relearning, outcome.State);
        Assert.Equal(1, outcome.Lapses);
        Assert.Equal(0, outcome.Repetitions);
        Assert.True(outcome.EaseFactor < 2.5);
        Assert.True(outcome.IntervalDays < card.IntervalDays);
    }

    [Fact]
    public void Schedule_KeepsEaseWithinBounds()
    {
        var service = new SrsService();
        var card = NewCard(CardState.Review);
        card.IntervalDays = 5;
        card.Repetitions = 2;
        card.EaseFactor = 1.2;

        var lower = service.Schedule(card, ReviewRating.Hard, new FixedClock(Now));
        Assert.InRange(lower.EaseFactor, 1.3, 3.2);

        var highCard = NewCard(CardState.Review);
        highCard.IntervalDays = 5;
        highCard.Repetitions = 2;
        highCard.EaseFactor = 3.2;

        var upper = service.Schedule(highCard, ReviewRating.Easy, new FixedClock(Now));
        Assert.InRange(upper.EaseFactor, 1.3, 3.2);
    }

    [Fact]
    public void Schedule_RespectsMaxIntervalDays()
    {
        var card = NewCard(CardState.Review);
        card.IntervalDays = 300;
        card.MaxIntervalDays = 30;
        card.Repetitions = 5;

        var outcome = new SrsService().Schedule(card, ReviewRating.Easy, new FixedClock(Now));

        Assert.Equal(30, outcome.IntervalDays);
    }

    [Fact]
    public void Schedule_SetsDueDateFromClock()
    {
        var card = NewCard(CardState.Review);
        card.IntervalDays = 2;

        var outcome = new SrsService().Schedule(card, ReviewRating.Good, new FixedClock(Now));

        Assert.Equal(Now.AddDays(outcome.IntervalDays).UtcDateTime, outcome.DueAt.UtcDateTime, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Forecast_ReturnsRequestedNumberOfConsecutiveDays()
    {
        var cards = new[] { NewCard(), NewCard() };

        var forecast = new SrsService().Forecast(cards, new FixedClock(Now), 7);

        Assert.Equal(7, forecast.Count);
        Assert.Equal(DateOnly.FromDateTime(Now.UtcDateTime), forecast[0]);
        Assert.Equal(forecast[0].AddDays(6), forecast[6]);
    }

    [Fact]
    public void Forecast_HandlesNegativeDaysWithoutThrowing()
    {
        var forecast = new SrsService().Forecast([], new FixedClock(Now), 0);

        Assert.Single(forecast);
    }
}