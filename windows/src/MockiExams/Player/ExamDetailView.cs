using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MockiCore;

namespace MockiExams;

/// <summary>An exam's start screen: what's in it, run options, topic accuracy and history.</summary>
public sealed class ExamDetailView : Screen
{
    readonly Exam exam;
    readonly Action<SessionOptions, bool, ExamStyle> onStart;
    SessionMode mode = SessionMode.Practice;
    bool shuffleQuestions = true, shuffleChoices = true;
    int drawCount;
    readonly HashSet<string> topics = [];

    public ExamDetailView(Exam exam, Action<SessionOptions, bool, ExamStyle> onStart)
    {
        this.exam = exam;
        this.onStart = onStart;
        HistoryStore.Shared.Changed += Invalidate;
        DetachedFromVisualTree += (_, _) => HistoryStore.Shared.Changed -= Invalidate;
        Content = Build();
    }

    IReadOnlyList<Question> Asked => exam.ExamQuestions;
    int Pool => Asked.Count(q => topics.Count == 0 || topics.Contains(q.Topic));
    IReadOnlyList<Attempt> Attempts => HistoryStore.Shared.For(exam.Id);

    IReadOnlyList<string> LastMissed
    {
        get
        {
            var known = Asked.Select(q => q.Id).ToHashSet();
            return (Attempts.FirstOrDefault()?.MissedQuestionIds ?? []).Where(known.Contains).ToList();
        }
    }

    string KindSummary => string.Join(", ", QuestionKinds.All
        .Select(k => (k, n: Asked.Count(q => q.Kind == k))).Where(p => p.n > 0).Select(p => $"{p.n} {p.k.Label().ToLowerInvariant()}"));

    protected override Control Build() => new Border
    {
        Background = Palette.ConsolePage,
        Child = UI.Scroll(UI.V(22, LiveRegion(Summary), LiveRegion(StartSection), LiveRegion(Accuracy), LiveRegion(History)).MaxW(820).Pad(28, 24)),
    };

    Control Summary() => UI.FormSection(null, [
        exam.Summary is { } s ? UI.Secondary(s) : null,
        UI.LabeledText("Questions", $"{Asked.Count}{(exam.Sections.Count == 0 ? "" : $" in {exam.Sections.Count} sections")}"),
        UI.LabeledText("Types", KindSummary),
        UI.LabeledText("Pass mark", exam.Scale is { } sc ? $"{sc.Pass} on a {sc.Min}–{sc.Max} scale" : $"{exam.PassPercent}%"),
        UI.LabeledText("Time limit", exam.TimeLimit is { } t ? UI.Duration(t) : "None"),
        exam.AllowBack ? null : UI.LabeledText("Navigation", "No going back in exam simulation"),
        exam.CaseStudies.Count == 0 ? null : UI.LabeledText("Case studies", $"{exam.CaseStudies.Count}"),
    ], headerControl: UI.Title(exam.Title).Pad(2, 0, 0, 4));

    Control StartSection()
    {
        var settings = AppSettings.Shared;
        var rows = new List<Control?>
        {
            UI.LabeledRow("Mode", UI.Segmented([(SessionMode.Practice, "Practice"), (SessionMode.Exam, "Exam simulation")], mode, m => { mode = m; Invalidate(); }, 120)),
            UI.Secondary(mode == SessionMode.Practice
                ? "Check each answer as you go and see the explanation."
                : $"Like the real thing: {(exam.TimeLimit is null ? "untimed" : "timed")}, graded at the end{(exam.AllowBack ? "" : ", no going back")}.", 12.5),
        };
        if (mode == SessionMode.Exam)
        {
            rows.Add(UI.LabeledRow("Exam screen", UI.Picker(ExamStyles.Options, settings.ExamStyle, s => { settings.ExamStyle = s; settings.Save(); }, 220)));
            rows.Add(UI.LabeledRow("Full screen", UI.Toggle("", settings.FullScreenExams, on => { settings.FullScreenExams = on; settings.Save(); })));
        }
        rows.Add(UI.LabeledRow("Shuffle question order", UI.Toggle("", shuffleQuestions, on => shuffleQuestions = on)));
        rows.Add(UI.LabeledRow("Shuffle answer order", UI.Toggle("", shuffleChoices, on => shuffleChoices = on)));
        var draws = new List<(int, string)> { (0, $"All {Pool}") };
        draws.AddRange(new[] { 10, 20, 30, 50, 65, 90 }.Where(n => n < Pool).Select(n => (n, $"Random {n}")));
        if (draws.All(d => d.Item1 != drawCount)) drawCount = 0;
        rows.Add(UI.LabeledRow("Questions", UI.Picker(draws, drawCount, n => drawCount = n, 160)));
        if (exam.Topics.Count > 1) rows.Add(UI.LabeledRow("Topics", TopicsMenu()));
        var missed = LastMissed;
        rows.Add(UI.H(8,
            UI.Btn("Start", () => Start(null), ButtonKind.Prominent).Tip("Start (Enter)"),
            missed.Count > 0 ? UI.Btn($"Retry {missed.Count} Missed", () => Start(missed.ToHashSet())) : null));
        return UI.FormSection("Start", rows);
    }

    Control TopicsMenu()
    {
        var button = new Button { Content = topics.Count == 0 ? "All topics" : $"{topics.Count} selected", MinWidth = 160 };
        var menu = new MenuFlyout();
        var all = new MenuItem { Header = "All topics" };
        all.Click += (_, _) => { topics.Clear(); Invalidate(); };
        menu.Items.Add(all);
        menu.Items.Add(new Separator());
        foreach (var topic in exam.Topics)
        {
            var item = new MenuItem { Header = topic, ToggleType = MenuItemToggleType.CheckBox, IsChecked = topics.Contains(topic), StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                if (!topics.Remove(topic)) topics.Add(topic);
                item.IsChecked = topics.Contains(topic);
                button.Content = topics.Count == 0 ? "All topics" : $"{topics.Count} selected";
            };
            menu.Items.Add(item);
        }
        menu.Closed += (_, _) => Invalidate();
        button.Flyout = menu;
        return button;
    }

    Control? Accuracy()
    {
        var weak = HistoryStore.WeakTopics(Attempts);
        if (weak.Count == 0) return null;
        return UI.FormSection("Topic accuracy (all attempts)", weak.Select(TopicRow));
    }

    public static Control TopicRow(TopicScore score) => UI.Columns("*,Auto,Auto",
        UI.Txt(score.Topic),
        new ProgressBar
        {
            Value = score.Fraction * 100, Width = 140, VerticalAlignment = VerticalAlignment.Center,
            Foreground = score.Fraction >= 0.7 ? Palette.Green : score.Fraction >= 0.5 ? Palette.Yellow : Palette.Red,
        },
        UI.Secondary($"{UI.Points(score.Earned)}/{UI.Points(score.Possible)}").Width(60).Do(t => t.TextAlignment = TextAlignment.Right));

    Control History()
    {
        var attempts = Attempts;
        var rows = new List<Control?>();
        if (attempts.Count == 0) rows.Add(UI.Secondary("No attempts yet."));
        foreach (var a in attempts)
        {
            rows.Add(UI.Columns("Auto,52,*,Auto,Auto",
                Icons.Make(a.Passed ? "seal.check" : "seal.x", 16, a.Passed ? Palette.Green : Palette.Orange).Pad(0, 0, 6, 0),
                UI.Txt(a.ScaledScore?.ToString() ?? $"{a.Percent}%").Do(t => t.TextAlignment = TextAlignment.Right).Pad(0, 0, 10, 0),
                UI.Secondary($"{a.SessionMode.Label()}   {a.QuestionCount} q"),
                UI.Secondary(UI.Duration(a.Duration)).Pad(10, 0),
                UI.Secondary(a.Date.ToLocalTime().ToString("MMM d, HH:mm", CultureInfo.CurrentCulture))));
        }
        return UI.FormSection("History", rows);
    }

    void Start(IReadOnlySet<string>? ids)
    {
        var options = new SessionOptions
        {
            Mode = mode, ShuffleQuestions = shuffleQuestions, ShuffleChoices = shuffleChoices, QuestionIds = ids,
            Topics = topics.Count == 0 ? null : topics.ToHashSet(),
            QuestionLimit = ids is null && drawCount > 0 ? drawCount : null,
        };
        onStart(options, mode == SessionMode.Exam && AppSettings.Shared.FullScreenExams, AppSettings.Shared.ExamStyle);
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return false;
        Start(null);
        return true;
    }
}
