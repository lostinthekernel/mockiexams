namespace MockiCore;

public sealed class Exam
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public int PassPercent { get; init; }
    public ScoreScale? Scale { get; init; }
    /// <summary>Seconds, or null for untimed.</summary>
    public double? TimeLimit { get; init; }
    /// <summary>Exam mode only: false means no going back to earlier questions.</summary>
    public bool AllowBack { get; init; }
    /// <summary>Blueprint target per topic, in percent. Empty when the exam doesn't define one.</summary>
    public IReadOnlyDictionary<string, double> TopicWeights { get; init; } = new Dictionary<string, double>();
    public IReadOnlyList<CaseStudy> CaseStudies { get; init; } = [];
    public IReadOnlyList<ExamSection> Sections { get; init; } = [];
    /// <summary>The whole question bank, in file order.</summary>
    public IReadOnlyList<Question> Questions { get; init; } = [];
    /// <summary>Folder that relative asset paths resolve against.</summary>
    public string? AssetBase { get; set; }

    /// <summary>The questions the exam asks: section order when there are sections, otherwise the bank.</summary>
    public IReadOnlyList<Question> ExamQuestions
    {
        get
        {
            if (Sections.Count == 0) return Questions;
            var byId = Questions.ToDictionary(q => q.Id);
            return Sections.SelectMany(s => s.QuestionIds.Where(byId.ContainsKey).Select(id => byId[id])).ToList();
        }
    }

    public ExamSection? Section(string questionId) => Sections.FirstOrDefault(s => s.QuestionIds.Contains(questionId));

    public IReadOnlyList<string> Topics => ExamQuestions.Select(q => q.Topic).Distinct().ToList();

    public double TotalPoints => ExamQuestions.Sum(q => q.Points);

    /// <summary>The topic's blueprint weight, or its share of the exam's points when no blueprint is set.</summary>
    public double Weight(string topic)
    {
        if (TopicWeights.TryGetValue(topic, out var w)) return w;
        var total = TotalPoints;
        if (total <= 0) return 0;
        return ExamQuestions.Where(q => q.Topic == topic).Sum(q => q.Points) / total * 100;
    }

    public CaseStudy? CaseStudy(string? id) => id is null ? null : CaseStudies.FirstOrDefault(c => c.Id == id);

    public string? AssetPath(string path) => AssetBase is null ? null : Path.Combine(AssetBase, path.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Every relative file path the exam references, for copying and validation.</summary>
    public IReadOnlyList<string> AssetPaths
    {
        get
        {
            var exhibits = CaseStudies.SelectMany(c => c.Exhibits).Concat(Questions.SelectMany(q => q.Exhibits))
                .Select(e => e.ImagePath).OfType<string>();
            var hotspots = Questions.Select(q => q.Body is HotspotBody h ? h.Image : null).OfType<string>();
            return exhibits.Concat(hotspots).Distinct().ToList();
        }
    }
}

/// <summary>Linear map from percent to a vendor-style scaled score. Real vendors don't scale linearly.</summary>
public sealed record ScoreScale(int Min, int Max, int Pass)
{
    public int Scaled(double fraction) => Min + (int)Math.Round((Max - Min) * fraction, MidpointRounding.AwayFromZero);
}

public sealed record ExamSection(string Id, string Title, IReadOnlyList<string> QuestionIds, bool Shuffle);

public sealed record CaseStudy(string Id, string Title, string Scenario, IReadOnlyList<Exhibit> Exhibits);

/// <summary>Text shown monospaced, or an image/PDF path relative to the exam folder.</summary>
public sealed record Exhibit(string Title, string? Text, string? ImagePath);

public enum QuestionKind { Single, Multiple, TrueFalse, YesNo, Ordering, Matching, Dropdowns, FillIn, Hotspot }

public static class QuestionKinds
{
    public static readonly IReadOnlyList<QuestionKind> All = Enum.GetValues<QuestionKind>();

    public static string Raw(this QuestionKind kind) => kind switch
    {
        QuestionKind.Single => "single",
        QuestionKind.Multiple => "multiple",
        QuestionKind.TrueFalse => "trueFalse",
        QuestionKind.YesNo => "yesNo",
        QuestionKind.Ordering => "ordering",
        QuestionKind.Matching => "matching",
        QuestionKind.Dropdowns => "dropdowns",
        QuestionKind.FillIn => "fillIn",
        _ => "hotspot",
    };

    public static QuestionKind? FromRaw(string? raw) => All.Cast<QuestionKind?>().FirstOrDefault(k => k!.Value.Raw() == raw);

    public static string Label(this QuestionKind kind) => kind switch
    {
        QuestionKind.Single => "Single choice",
        QuestionKind.Multiple => "Multiple choice",
        QuestionKind.TrueFalse => "True / false",
        QuestionKind.YesNo => "Yes / no series",
        QuestionKind.Ordering => "Ordering",
        QuestionKind.Matching => "Matching",
        QuestionKind.Dropdowns => "Dropdowns",
        QuestionKind.FillIn => "Fill in",
        _ => "Hotspot",
    };
}

public sealed record Question
{
    public required string Id { get; init; }
    /// <summary>The kind as written in the file; trueFalse and yesNo are stored as other bodies.</summary>
    public required QuestionKind Kind { get; init; }
    public required string Prompt { get; init; }
    public required string Topic { get; init; }
    public string? Explanation { get; init; }
    public IReadOnlyList<Exhibit> Exhibits { get; init; } = [];
    public string? CaseStudyId { get; init; }
    public double Points { get; init; } = 1;
    public bool PartialCredit { get; init; }
    public required QuestionBody Body { get; init; }
}

public abstract record QuestionBody;
public sealed record SingleBody(IReadOnlyList<Choice> Choices, string Answer) : QuestionBody;
public sealed record MultipleBody(IReadOnlyList<Choice> Choices, IReadOnlySet<string> Answer) : QuestionBody;
/// <summary>Answer may be a subset of Choices; the rest are distractors.</summary>
public sealed record OrderingBody(IReadOnlyList<Choice> Choices, IReadOnlyList<string> Answer) : QuestionBody;
/// <summary>Items missing from Answer are distractors and should stay unplaced.</summary>
public sealed record MatchingBody(IReadOnlyList<Choice> Items, IReadOnlyList<Choice> Targets, IReadOnlyDictionary<string, string> Answer) : QuestionBody;
public sealed record DropdownsBody(IReadOnlyList<DropdownRow> Rows) : QuestionBody;
public sealed record FillInBody(IReadOnlyList<Blank> Blanks) : QuestionBody;
public sealed record HotspotBody(string Image, IReadOnlyList<Region> Regions, IReadOnlySet<string> Answer) : QuestionBody;

public sealed record Choice(string Id, string Text);

public sealed record DropdownRow(string Id, string Label, IReadOnlyList<string> Options, string Answer);

public sealed record Blank(string Id, string? Label, IReadOnlyList<string> Accept, bool CaseSensitive, string? Pattern);

/// <summary>A clickable rectangle on a hotspot image, in fractions (0–1) of the image size.</summary>
public sealed record Region(string Id, string? Label, double X, double Y, double Width, double Height)
{
    public bool Contains(double px, double py) => px >= X && px <= X + Width && py >= Y && py <= Y + Height;
}

/// <summary>What the user entered; one kind per QuestionBody kind.</summary>
public abstract record Response
{
    public abstract bool IsEmpty { get; }
}

public sealed record SingleResponse(string Choice) : Response
{
    public override bool IsEmpty => Choice.Length == 0;
}

public sealed record MultipleResponse(IReadOnlySet<string> Choices) : Response
{
    public override bool IsEmpty => Choices.Count == 0;
    public bool Equals(MultipleResponse? other) => other is not null && Choices.SetEquals(other.Choices);
    public override int GetHashCode() => Choices.Count;
}

public sealed record OrderingResponse(IReadOnlyList<string> Order) : Response
{
    public override bool IsEmpty => Order.Count == 0;
    public bool Equals(OrderingResponse? other) => other is not null && Order.SequenceEqual(other.Order);
    public override int GetHashCode() => Order.Count;
}

public sealed record MatchingResponse(IReadOnlyDictionary<string, string> Placed) : Response
{
    public override bool IsEmpty => Placed.Count == 0;
    public bool Equals(MatchingResponse? other) => other is not null && Maps.Same(Placed, other.Placed);
    public override int GetHashCode() => Placed.Count;
}

public sealed record DropdownsResponse(IReadOnlyDictionary<string, string> Picked) : Response
{
    public override bool IsEmpty => Picked.Count == 0;
    public bool Equals(DropdownsResponse? other) => other is not null && Maps.Same(Picked, other.Picked);
    public override int GetHashCode() => Picked.Count;
}

public sealed record FillInResponse(IReadOnlyDictionary<string, string> Typed) : Response
{
    public override bool IsEmpty => Typed.Values.All(string.IsNullOrWhiteSpace);
    public bool Equals(FillInResponse? other) => other is not null && Maps.Same(Typed, other.Typed);
    public override int GetHashCode() => Typed.Count;
}

public sealed record HotspotResponse(IReadOnlySet<string> Regions) : Response
{
    public override bool IsEmpty => Regions.Count == 0;
    public bool Equals(HotspotResponse? other) => other is not null && Regions.SetEquals(other.Regions);
    public override int GetHashCode() => Regions.Count;
}

static class Maps
{
    public static bool Same(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);
}
