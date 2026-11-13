using Recall.Models;

namespace Recall.Services;

public enum Grade
{
    Again = 0,
    Hard = 1,
    Good = 2,
    Easy = 3
}

/// SM-2 variant. Sub-day intervals for lapses so a missed card comes back in the same session.
public static class Srs
{
    /// <summary>
    /// Applies a grade and returns true when the card has just been suspended for
    /// repeated lapses, so the caller can say so on screen.
    /// </summary>
    public static bool Apply(Card card, Grade grade, DateTime now)
    {
        card.Attempts++;
        if (grade != Grade.Again) card.Correct++;

        if (grade == Grade.Again)
        {
            card.Lapses++;
            card.Reps = 0;
            card.IntervalDays = 0;
            card.Ease = Math.Max(1.3, card.Ease - 0.2);
            card.Due = now.AddMinutes(10);

            // A card that keeps failing is a leech: stop asking it every session and let
            // the reader rewrite it instead of drilling a broken card.
            if (card.Lapses >= Card.SrsLeechThreshold && !card.Suspended)
            {
                card.Suspended = true;
                return true;
            }
            return false;
        }

        card.Reps++;
        card.Ease = Math.Clamp(
            grade switch
            {
                Grade.Hard => card.Ease - 0.15,
                Grade.Easy => card.Ease + 0.15,
                _ => card.Ease
            }, 1.3, 3.2);

        var multiplier = grade switch
        {
            Grade.Hard => 0.85,
            Grade.Easy => 1.35,
            _ => 1.0
        };

        var interval = card.Reps switch
        {
            1 => grade switch { Grade.Hard => 1, Grade.Easy => 3, _ => 1 },
            2 => grade switch { Grade.Hard => 2, Grade.Easy => 6, _ => 3 },
            _ => (int)Math.Round(Math.Max(card.IntervalDays, 1) * card.Ease * multiplier)
        };

        card.IntervalDays = Math.Clamp(interval, 1, 365);
        card.Due = now.Date.AddDays(card.IntervalDays).AddHours(4);
        return false;
    }

    /// Label for the grade button, computed against the card's current state.
    public static string PreviewInterval(Card card, Grade grade, DateTime now)
    {
        if (grade == Grade.Again) return "10m";

        var ease = Math.Clamp(
            grade switch
            {
                Grade.Hard => card.Ease - 0.15,
                Grade.Easy => card.Ease + 0.15,
                _ => card.Ease
            }, 1.3, 3.2);

        var multiplier = grade switch
        {
            Grade.Hard => 0.85,
            Grade.Easy => 1.35,
            _ => 1.0
        };

        var reps = card.Reps + 1;
        var interval = reps switch
        {
            1 => grade switch { Grade.Hard => 1, Grade.Easy => 3, _ => 1 },
            2 => grade switch { Grade.Hard => 2, Grade.Easy => 6, _ => 3 },
            _ => (int)Math.Round(Math.Max(card.IntervalDays, 1) * ease * multiplier)
        };

        interval = Math.Clamp(interval, 1, 365);
        return interval < 30 ? $"{interval}d" : $"{interval / 30}mo";
    }
}
