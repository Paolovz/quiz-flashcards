using Recall.Models;
using Recall.Services;

namespace Recall.Tests;

public class SrsTests
{
    private static readonly DateTime Now = new(2026, 11, 13, 10, 0, 0);

    [Fact]
    public void Good_on_a_new_card_schedules_it_one_day_out()
    {
        var card = new Card();

        Srs.Apply(card, Grade.Good, Now);

        Assert.Equal(1, card.IntervalDays);
        Assert.Equal(Now.Date.AddDays(1).AddHours(4), card.Due);
        Assert.Equal(1, card.Reps);
        Assert.Equal(1, card.Correct);
    }

    [Fact]
    public void Easy_on_a_new_card_schedules_it_three_days_out()
    {
        var card = new Card();

        Srs.Apply(card, Grade.Easy, Now);

        Assert.Equal(3, card.IntervalDays);
    }

    [Fact]
    public void Again_resets_reps_and_returns_the_card_in_ten_minutes()
    {
        var card = new Card { Reps = 4, IntervalDays = 30, Ease = 2.5 };

        Srs.Apply(card, Grade.Again, Now);

        Assert.Equal(0, card.Reps);
        Assert.Equal(1, card.Lapses);
        Assert.Equal(Now.AddMinutes(10), card.Due);
        Assert.Equal(0, card.Correct);
        Assert.Equal(1, card.Attempts);
        Assert.Equal(2.3, card.Ease, 3);
    }

    [Fact]
    public void Repeated_good_grades_grow_the_interval()
    {
        var card = new Card();

        Srs.Apply(card, Grade.Good, Now);
        var first = card.IntervalDays;
        Srs.Apply(card, Grade.Good, Now);
        var second = card.IntervalDays;
        Srs.Apply(card, Grade.Good, Now);
        var third = card.IntervalDays;

        Assert.True(first < second, $"{first} !< {second}");
        Assert.True(second < third, $"{second} !< {third}");
        Assert.Equal(1, first);
        Assert.Equal(3, second);
        Assert.True(third >= 6);
    }

    [Fact]
    public void Ease_stays_inside_its_bounds()
    {
        var card = new Card { Ease = 1.35 };

        for (var i = 0; i < 10; i++) Srs.Apply(card, Grade.Hard, Now);
        Assert.True(card.Ease >= 1.3);

        var generous = new Card { Ease = 3.15 };
        for (var i = 0; i < 10; i++) Srs.Apply(generous, Grade.Easy, Now);
        Assert.True(generous.Ease <= 3.2);
    }

    [Fact]
    public void Hard_is_graded_correct_but_schedules_a_shorter_interval()
    {
        var hard = new Card();
        var good = new Card();

        Srs.Apply(hard, Grade.Hard, Now);
        Srs.Apply(good, Grade.Good, Now);

        Assert.Equal(1, hard.Correct);
        Assert.True(hard.IntervalDays <= good.IntervalDays);
        Assert.True(hard.Ease < good.Ease);
    }

    [Fact]
    public void A_new_card_is_due_and_a_scheduled_card_is_not()
    {
        var card = new Card();
        Assert.True(card.IsDue(Now));

        Srs.Apply(card, Grade.Good, Now);
        Assert.False(card.IsDue(Now));
        Assert.True(card.IsDue(Now.AddDays(2)));
    }
}
