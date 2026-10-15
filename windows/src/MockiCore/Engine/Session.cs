namespace MockiCore;

public enum SessionMode { Practice, Exam }

public static class SessionModes
{
    public static string Label(this SessionMode mode) => mode == SessionMode.Practice ? "Practice" : "Exam simulation";
    public static string Raw(this SessionMode mode) => mode == SessionMode.Practice ? "practice" : "exam";
}

public sealed record SessionOptions
{
    public SessionMode Mode { get; init; } = SessionMode.Practice;
    public bool ShuffleQuestions { get; init; } = true;
    public bool ShuffleChoices { get; init; } = true;
    /// <summary>Only these questions (e.g. "retry missed"); null means all. Stale ids fall back to all.</summary>
    public IReadOnlySet<string>? QuestionIds { get; init; }
    /// <summary>Only these topics; null means all.</summary>
    public IReadOnlySet<string>? Topics { get; init; }
    /// <summary>Draw this many questions at random from the pool (case studies stay whole).</summary>
    public int? QuestionLimit { get; init; }
    public ulong Seed { get; init; } = (ulong)Random.Shared.NextInt64() ^ ((ulong)Random.Shared.Next() << 33);
}

public sealed class Session
{
    public Exam Exam { get; }
    public SessionOptions Options { get; }
    /// <summary>Questions in display order, each with choices in display order.</summary>
    public IReadOnlyList<Question> Questions { get; }
    public DateTime StartedAt { get; }

    readonly Dictionary<string, Response> responses = [];
    readonly HashSet<string> flagged = [];
    /// <summary>Practice mode: questions whose answer has been checked and locked.</summary>
    readonly HashSet<string> revealed = [];

    public IReadOnlySet<string> Flagged => flagged;
    public int Current { get; private set; }

    sealed class Unit(int section, bool shuffles, Question first)
    {
        public int Section { get; } = section;
        public bool Shuffles { get; } = shuffles;
        public List<Question> Questions { get; } = [first];
    }

    public Session(Exam exam, SessionOptions options, DateTime? now = null)
    {
        var rng = new SplitMix64(options.Seed);
        var asked = exam.ExamQuestions;
        var pool = asked.Where(q => (options.QuestionIds?.Contains(q.Id) ?? true) && (options.Topics?.Contains(q.Topic) ?? true)).ToList();
        if (pool.Count == 0) pool = asked.ToList();

        // Units are what moves when shuffling: a case study's questions stay together, in order,
        // and nothing leaves its section.
        var sectionIndex = new Dictionary<string, int>();
        for (var i = 0; i < exam.Sections.Count; i++)
            foreach (var id in exam.Sections[i].QuestionIds) sectionIndex.TryAdd(id, i);
        var units = new List<Unit>();
        var unitOfCase = new Dictionary<string, int>();
        foreach (var q in pool)
        {
            var s = sectionIndex.GetValueOrDefault(q.Id, 0);
            var key = q.CaseStudyId is null ? null : $"{s}/{q.CaseStudyId}";
            if (key is not null && unitOfCase.TryGetValue(key, out var existing)) { units[existing].Questions.Add(q); continue; }
            if (key is not null) unitOfCase[key] = units.Count;
            units.Add(new Unit(s, exam.Sections.Count == 0 || exam.Sections[s].Shuffle, q));
        }

        var fileOrder = asked.Select((q, i) => (q.Id, i)).ToDictionary(p => p.Id, p => p.i);
        if (options.QuestionLimit is { } limit && limit > 0 && limit < pool.Count)
        {
            rng.Shuffle(units);
            var taken = new List<Unit>();
            var count = 0;
            foreach (var u in units)
            {
                if (count >= limit) continue;
                taken.Add(u);
                count += u.Questions.Count;
            }
            units = taken.OrderBy(u => fileOrder[u.Questions[0].Id]).ToList();
        }
        if (options.ShuffleQuestions)
        {
            // Shuffle within each section, leaving non-shuffling sections in file order.
            var shuffled = new List<Unit>();
            foreach (var s in units.Select(u => u.Section).Distinct().Order())
            {
                var part = units.Where(u => u.Section == s).ToList();
                if (part.FirstOrDefault()?.Shuffles == true) rng.Shuffle(part);
                shuffled.AddRange(part);
            }
            units = shuffled;
        }
        else
        {
            units = units.OrderBy(u => u.Section).ThenBy(u => fileOrder[u.Questions[0].Id]).ToList();
        }
        Questions = units.SelectMany(u => u.Questions).Select(q => Arrange(q, options.ShuffleChoices, rng)).ToList();
        Exam = exam;
        Options = options;
        StartedAt = now ?? DateTime.UtcNow;
    }

    static Question Arrange(Question q, bool shuffle, SplitMix64 rng) => q.Body switch
    {
        SingleBody b when shuffle && q.Kind != QuestionKind.TrueFalse => q with { Body = b with { Choices = rng.Shuffled(b.Choices) } },
        MultipleBody b when shuffle => q with { Body = b with { Choices = rng.Shuffled(b.Choices) } },
        // The pool is always shuffled; an unshuffled pool would give the order away.
        OrderingBody b => q with { Body = b with { Choices = rng.Shuffled(b.Choices) } },
        MatchingBody b when shuffle => q with { Body = b with { Items = rng.Shuffled(b.Items) } },
        _ => q,
    };

    // MARK: State

    public Question CurrentQuestion => Questions[Current];

    /// <summary>Title of the section starting at index, if a new section begins there.</summary>
    public string? SectionStart(int index)
    {
        if (Exam.Sections.Count == 0 || index < 0 || index >= Questions.Count) return null;
        var s = Exam.Section(Questions[index].Id);
        if (index > 0 && Exam.Section(Questions[index - 1].Id)?.Id == s?.Id) return null;
        return s?.Title;
    }

    public double? TimeLimit => Options.Mode == SessionMode.Exam ? Exam.TimeLimit : null;
    public bool CanGoBack => Options.Mode == SessionMode.Practice || Exam.AllowBack;

    public double? Remaining(DateTime now) => TimeLimit is { } t ? Math.Max(0, t - (now - StartedAt).TotalSeconds) : null;

    public bool IsExpired(DateTime now) => Remaining(now) == 0;
    public Response? Response(Question q) => responses.GetValueOrDefault(q.Id);
    public bool IsLocked(Question q) => revealed.Contains(q.Id);
    public int UnansweredCount => Questions.Count(q => !responses.ContainsKey(q.Id));
    public int AnsweredCount => Questions.Count - UnansweredCount;

    // MARK: Answering

    /// <summary>Stores a response for a question; empty responses clear it. Locked questions ignore this.</summary>
    public void Respond(Response? response, Question q)
    {
        if (IsLocked(q)) return;
        if (response is null || response.IsEmpty) responses.Remove(q.Id);
        else responses[q.Id] = response;
    }

    /// <summary>Picks a choice on the current question: replaces for single, toggles for multiple.</summary>
    public void Choose(string choiceId)
    {
        var q = CurrentQuestion;
        switch (q.Body)
        {
            case SingleBody b when b.Choices.Any(c => c.Id == choiceId):
                Respond(new SingleResponse(choiceId), q);
                break;
            case MultipleBody b when b.Choices.Any(c => c.Id == choiceId):
                var set = responses.GetValueOrDefault(q.Id) is MultipleResponse m ? new HashSet<string>(m.Choices) : [];
                if (!set.Remove(choiceId)) set.Add(choiceId);
                Respond(new MultipleResponse(set), q);
                break;
        }
    }

    public void ToggleRegion(string regionId)
    {
        var q = CurrentQuestion;
        if (q.Body is not HotspotBody body) return;
        var set = responses.GetValueOrDefault(q.Id) is HotspotResponse h ? new HashSet<string>(h.Regions) : [];
        if (!set.Remove(regionId))
        {
            // Single-target hotspots behave like radio buttons.
            if (body.Answer.Count == 1) set.Clear();
            set.Add(regionId);
        }
        Respond(new HotspotResponse(set), q);
    }

    public void ToggleFlag()
    {
        var id = CurrentQuestion.Id;
        if (!flagged.Remove(id)) flagged.Add(id);
    }

    public void Reveal()
    {
        if (Options.Mode != SessionMode.Practice) return;
        revealed.Add(CurrentQuestion.Id);
    }

    // MARK: Navigation

    public bool CanJump(int index) => index >= 0 && index < Questions.Count && (CanGoBack || index >= Current);
    public void Jump(int index) { if (CanJump(index)) Current = index; }
    public void Next() => Jump(Math.Min(Current + 1, Questions.Count - 1));
    public void Previous() => Jump(Math.Max(Current - 1, 0));

    // MARK: Grading

    public SessionResult Grade(DateTime? now = null)
    {
        var items = Questions.Select(q =>
        {
            var r = responses.GetValueOrDefault(q.Id);
            return new ResultItem(q, r, q.Score(r), flagged.Contains(q.Id));
        }).ToList();
        var byTopic = new Dictionary<string, TopicScore>();
        foreach (var item in items)
        {
            var t = byTopic.GetValueOrDefault(item.Question.Topic) ?? new TopicScore(item.Question.Topic);
            byTopic[item.Question.Topic] = t with { Earned = t.Earned + item.Earned, Possible = t.Possible + item.Question.Points };
        }
        var order = Exam.Topics.ToList();
        int Rank(string topic) { var i = order.IndexOf(topic); return i < 0 ? int.MaxValue : i; }
        return new SessionResult(Exam, Options.Mode, StartedAt, now ?? DateTime.UtcNow, items,
            byTopic.Values.OrderBy(t => Rank(t.Topic)).ToList());
    }
}

public sealed record ResultItem(Question Question, Response? Response, double Score, bool Flagged)
{
    public string Id => Question.Id;
    public bool IsCorrect => Score >= 1;
    public double Earned => Score * Question.Points;
}

public sealed record TopicScore(string Topic, double Earned = 0, double Possible = 0)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public double Fraction => Possible == 0 ? 0 : Earned / Possible;
}

public sealed record SessionResult(Exam Exam, SessionMode Mode, DateTime StartedAt, DateTime FinishedAt,
    IReadOnlyList<ResultItem> Items, IReadOnlyList<TopicScore> Topics)
{
    public double Earned => Items.Sum(i => i.Earned);
    public double Possible => Items.Sum(i => i.Question.Points);
    public double Fraction => Possible == 0 ? 0 : Earned / Possible;
    public int Percent => (int)Math.Round(Fraction * 100, MidpointRounding.AwayFromZero);
    public int CorrectCount => Items.Count(i => i.IsCorrect);
    public int? ScaledScore => Exam.Scale?.Scaled(Fraction);
    public bool Passed => Exam.Scale is { } scale && ScaledScore is { } s ? s >= scale.Pass : Percent >= Exam.PassPercent;
    public double Duration => (FinishedAt - StartedAt).TotalSeconds;
    public IReadOnlyList<string> MissedIds => Items.Where(i => !i.IsCorrect).Select(i => i.Id).ToList();
}

/// <summary>Deterministic RNG so a seed reproduces a run's ordering. Shuffles the same way Swift's
/// shuffle(using:) does, so a seed gives the same order on macOS and Windows.</summary>
public sealed class SplitMix64(ulong seed)
{
    ulong state = seed;

    public ulong Next()
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15;
            var z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
            return z ^ (z >> 31);
        }
    }

    /// <summary>Lemire's nearly-divisionless bounded random, as in Swift's RandomNumberGenerator.next(upperBound:).</summary>
    public ulong Next(ulong upperBound)
    {
        var random = Next();
        var high = Math.BigMul(random, upperBound, out var low);
        if (low < upperBound)
        {
            var t = unchecked(0 - upperBound) % upperBound;
            while (low < t)
            {
                random = Next();
                high = Math.BigMul(random, upperBound, out low);
            }
        }
        return high;
    }

    public void Shuffle<T>(IList<T> list)
    {
        var amount = list.Count;
        var current = 0;
        while (amount > 1)
        {
            var random = (int)Next((ulong)amount);
            amount -= 1;
            (list[current], list[current + random]) = (list[current + random], list[current]);
            current += 1;
        }
    }

    public IReadOnlyList<T> Shuffled<T>(IReadOnlyList<T> items)
    {
        var copy = items.ToList();
        Shuffle(copy);
        return copy;
    }
}
