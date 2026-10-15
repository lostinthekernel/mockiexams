using System.Text.RegularExpressions;

namespace MockiCore;

public static class Scoring
{
    /// <summary>Points earned as a fraction of Points, 0–1. All-or-nothing unless PartialCredit.</summary>
    public static double Score(this Question q, Response? response)
    {
        var f = q.Fraction(response);
        return q.PartialCredit ? f : (f >= 1 ? 1 : 0);
    }

    public static bool IsCorrect(this Question q, Response? response) => q.Fraction(response) >= 1;

    /// <summary>The share of the question answered correctly, ignoring the partial-credit setting.</summary>
    public static double Fraction(this Question q, Response? response)
    {
        switch (q.Body, response)
        {
            case (SingleBody b, SingleResponse r):
                return b.Answer == r.Choice ? 1 : 0;

            case (MultipleBody b, MultipleResponse r):
                return SetFraction(b.Answer, r.Choices);

            case (HotspotBody b, HotspotResponse r):
                return SetFraction(b.Answer, r.Regions);

            case (OrderingBody b, OrderingResponse r):
                var inPlace = b.Answer.Zip(r.Order).Count(p => p.First == p.Second);
                return (double)inPlace / Math.Max(b.Answer.Count, r.Order.Count);

            case (MatchingBody b, MatchingResponse r):
                var right = b.Items.Count(i => b.Answer.GetValueOrDefault(i.Id) == r.Placed.GetValueOrDefault(i.Id));
                return (double)right / b.Items.Count;

            case (DropdownsBody b, DropdownsResponse r):
                return (double)b.Rows.Count(row => r.Picked.GetValueOrDefault(row.Id) == row.Answer) / b.Rows.Count;

            case (FillInBody b, FillInResponse r):
                return (double)b.Blanks.Count(blank => blank.Accepts(r.Typed.GetValueOrDefault(blank.Id) ?? "")) / b.Blanks.Count;

            default:
                return 0;
        }
    }

    static double SetFraction(IReadOnlySet<string> answer, IReadOnlySet<string> picked)
    {
        var hits = picked.Count(answer.Contains);
        var wrong = picked.Count(p => !answer.Contains(p));
        return Math.Max(0, hits - wrong) / (double)answer.Count;
    }

    public static bool Accepts(this Blank blank, string input)
    {
        var given = Normalize(input);
        if (given.Length == 0) return false;
        var comparison = blank.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (blank.Accept.Any(a => string.Equals(Normalize(a), given, comparison))) return true;
        if (blank.Pattern is null || !TryRegex(blank.Pattern, !blank.CaseSensitive, out var regex)) return false;
        return regex!.IsMatch(given);
    }

    /// <summary>Trims and collapses runs of whitespace, so "ip  route " matches "ip route".</summary>
    public static string Normalize(string s) =>
        string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>A regex that must match the whole input.</summary>
    internal static bool TryRegex(string pattern, bool ignoreCase, out Regex? regex)
    {
        try
        {
            var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            regex = new Regex($"^(?:{pattern})$", options, TimeSpan.FromSeconds(1));
            return true;
        }
        catch (ArgumentException)
        {
            regex = null;
            return false;
        }
    }
}
