namespace MockiCore;

/// <summary>Plain-text renderings of answers, shared by the results review and the CLI.</summary>
public static class Describing
{
    public static string Describe(this Question q, Response? response)
    {
        if (response is null || response.IsEmpty) return "(unanswered)";
        return (q.Body, response) switch
        {
            (SingleBody b, SingleResponse r) => Texts(b.Choices, [r.Choice]),
            (MultipleBody b, MultipleResponse r) => Texts(b.Choices, b.Choices.Select(c => c.Id).Where(r.Choices.Contains)),
            (OrderingBody b, OrderingResponse r) => Texts(b.Choices, r.Order, " → "),
            (MatchingBody b, MatchingResponse r) => string.Join("; ", b.Items
                .Where(i => r.Placed.ContainsKey(i.Id))
                .Select(i => $"{i.Text} → {Texts(b.Targets, [r.Placed[i.Id]])}")),
            (DropdownsBody b, DropdownsResponse r) => string.Join("; ", b.Rows.Select(row => $"{row.Label}: {r.Picked.GetValueOrDefault(row.Id) ?? "—"}")),
            (FillInBody b, FillInResponse r) => string.Join("; ", b.Blanks.Select(bl =>
                $"{bl.Label ?? bl.Id}: {(r.Typed.TryGetValue(bl.Id, out var t) ? Scoring.Normalize(t) : "")}")),
            (HotspotBody b, HotspotResponse r) => string.Join(", ", b.Regions.Where(g => r.Regions.Contains(g.Id)).Select(g => g.Label ?? g.Id)),
            _ => "(unanswered)",
        };
    }

    public static string DescribeAnswer(this Question q) => q.Body switch
    {
        SingleBody b => Texts(b.Choices, [b.Answer]),
        MultipleBody b => Texts(b.Choices, b.Choices.Select(c => c.Id).Where(b.Answer.Contains)),
        OrderingBody b => Texts(b.Choices, b.Answer, " → "),
        MatchingBody b => string.Join("; ", b.Items
            .Where(i => b.Answer.ContainsKey(i.Id))
            .Select(i => $"{i.Text} → {Texts(b.Targets, [b.Answer[i.Id]])}")),
        DropdownsBody b => string.Join("; ", b.Rows.Select(row => $"{row.Label}: {row.Answer}")),
        FillInBody b => string.Join("; ", b.Blanks.Select(bl =>
            $"{bl.Label ?? bl.Id}: {bl.Accept.FirstOrDefault() ?? (bl.Pattern is null ? "" : $"/{bl.Pattern}/")}")),
        HotspotBody b => string.Join(", ", b.Regions.Where(g => b.Answer.Contains(g.Id)).Select(g => g.Label ?? g.Id)),
        _ => "",
    };

    static string Texts(IReadOnlyList<Choice> choices, IEnumerable<string> ids, string separator = ", ") =>
        string.Join(separator, ids.Select(id => choices.FirstOrDefault(c => c.Id == id)?.Text).OfType<string>());
}
