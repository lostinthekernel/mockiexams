using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MockiCore;

namespace MockiExams;

/// <summary>Where a project opens: validation status, blueprint coverage, and what the bank contains.</summary>
public sealed class OverviewPage(DesignerWindow window) : Screen
{
    ProjectDocument Doc => window.Document;

    protected override Control Build() => LiveRegion(() => ConsolePage.Make(["Projects"], Doc.DisplayName, ProjectDocument.NonEmpty(Doc.Exam.Description),
        [UI.Btn("Preview Exam", () => window.Preview(SessionMode.Exam)),
         UI.Btn("Install to Library", window.Install, ButtonKind.Prominent, Palette.BrandFill)],
        UI.Scroll(UI.V(14,
            StatusBanner(),
            UI.Columns("*,14,320", Coverage(), null, UI.V(14, QuestionsCard(), AssetsCard(), SetupCard()))).Pad(22, 0, 22, 22))))!;

    Control StatusBanner()
    {
        var errors = window.ErrorCount;
        var warnings = window.Issues.Count - errors;
        string Plural(int n, string w) => $"{n} {w}{(n == 1 ? "" : "s")}";
        return new Border
        {
            Padding = new Thickness(14, 10), CornerRadius = new CornerRadius(8),
            Background = errors == 0 ? Palette.OkSoft : Palette.ErrSoft, BorderBrush = errors == 0 ? Palette.Ok : Palette.Err, BorderThickness = new Thickness(1),
            Child = UI.Bar(
                [Icons.Make(errors == 0 ? "checkmark.circle.fill" : "octagon.x", 16, errors == 0 ? Palette.Ok : Palette.Err),
                 UI.Txt(errors == 0
                    ? $"Valid. {(warnings == 0 ? "No warnings." : $"{Plural(warnings, "warning")}.")} Ready to preview and install."
                    : $"{Plural(errors, "error")} and {Plural(warnings, "warning")} to fix before this exam can be previewed or installed.")],
                [window.Issues.Count == 0 ? null : UI.Btn("View Issues", window.ShowIssuesPanel, ButtonKind.Link)]),
        };
    }

    sealed record Row(string Topic, double Actual, double? Target);

    List<Row> Rows()
    {
        var asked = Doc.AskedQuestions;
        var total = asked.Sum(q => q.Points ?? 1);
        var weights = Doc.Exam.TopicWeights ?? [];
        return Doc.Topics.Select(topic =>
        {
            var pts = asked.Where(q => (ProjectDocument.NonEmpty(q.Topic) ?? "General") == topic).Sum(q => q.Points ?? 1);
            return new Row(topic, total == 0 ? 0 : pts / total * 100, weights.TryGetValue(topic, out var w) ? w : null);
        }).ToList();
    }

    Control Coverage()
    {
        var rows = Rows();
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,140") };
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var gap = row.Target is { } t && Math.Abs(row.Actual - t) > 5;
            var name = UI.Txt(row.Topic, wrap: false).Do(x => x.MinWidth = 120).Pad(0, 5, 12, 5);
            var bar = new CoverageBar(row.Actual, row.Target) { Height = 10, VerticalAlignment = VerticalAlignment.Center };
            var label = UI.Txt(row.Target is { } tg ? $"{Math.Round(row.Actual)}% · target {Math.Round(tg)}%" : $"{Math.Round(row.Actual)}%", 11.5,
                color: gap ? Palette.Warn : Palette.Secondary).Do(x => x.TextAlignment = TextAlignment.Right);
            Grid.SetRow(name, i); Grid.SetRow(bar, i); Grid.SetRow(label, i);
            Grid.SetColumn(bar, 1); Grid.SetColumn(label, 2);
            grid.Children.Add(name); grid.Children.Add(bar); grid.Children.Add(label);
        }
        return ConsolePage.Card("Blueprint coverage",
            UI.Btn(Doc.Exam.TopicWeights is null ? "Set Targets" : "Edit Targets", EditBlueprint, ButtonKind.Link),
            UI.Secondary("Each topic's share of the exam's points, against its target weight.", 12.5),
            rows.Count == 0 ? UI.Secondary("No questions yet.") : grid,
            CoverageNote() is { } note ? UI.Txt(note, 12.5, color: Palette.Warn) : null,
            Doc.Exam.TopicWeights is null && rows.Count > 0
                ? UI.Secondary("Set target weights from the exam's official objectives to see where the bank is short or heavy.", 12.5) : null);
    }

    string? CoverageNote()
    {
        var asked = Doc.AskedQuestions;
        var worst = Rows().Where(r => r.Target is not null).OrderByDescending(r => Math.Abs(r.Actual - r.Target!.Value)).FirstOrDefault();
        if (worst?.Target is not { } target || Math.Abs(worst.Actual - target) <= 5) return null;
        var perQuestion = asked.Count == 0 ? 1 : 100.0 / asked.Count;
        var n = (int)Math.Ceiling(Math.Abs(target - worst.Actual) / perQuestion);
        return worst.Actual < target
            ? $"{worst.Topic} is about {n} question{(n == 1 ? "" : "s")} short of its target."
            : $"{worst.Topic} is about {n} question{(n == 1 ? "" : "s")} over its target.";
    }

    void EditBlueprint()
    {
        var host = ModalHost.Of(this);
        if (host is null) return;
        Sheet? sheet = null;
        var content = UI.Rows("*,Auto,Auto",
            UI.Scroll(new BlueprintEditor(window).Pad(20, 18)),
            UI.Divider(),
            UI.Btn("Done", () => { sheet?.Close(); Invalidate(); }, ButtonKind.Prominent).Do(b => b.HorizontalAlignment = HorizontalAlignment.Right).Pad(12, 12));
        sheet = host.Present(content, 520, 520, () => { sheet?.Close(); Invalidate(); });
    }

    Control QuestionsCard()
    {
        var qs = Doc.Questions;
        var points = qs.Sum(q => q.Points ?? 1);
        var byKind = QuestionKinds.All.Select(k => (k, n: qs.Count(q => q.Kind == k.Raw()))).Where(p => p.n > 0).ToList();
        var top = byKind.Count == 0 ? 1 : byKind.Max(p => p.n);
        var kinds = new StackPanel { Spacing = 5 };
        foreach (var (kind, n) in byKind)
        {
            kinds.Children.Add(UI.Columns("Auto,96,*,30",
                Icons.Make(Icons.Kind(kind), 14, Palette.Secondary).Pad(0, 0, 8, 0),
                UI.Txt(kind.Label(), 11.5, wrap: false),
                new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = Palette.Brand, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Left }
                    .Do(b => b.Width = Math.Max(4, 120.0 * n / top)),
                UI.Txt($"{n}", 11.5).Do(t => t.TextAlignment = TextAlignment.Right)));
        }
        var explained = qs.Count(q => !string.IsNullOrWhiteSpace(q.Explanation));
        return ConsolePage.Card("Questions", UI.Btn("Open Bank", () => window.ShowPage(DesignerPage.Bank), ButtonKind.Link),
            UI.H(6, UI.Txt($"{qs.Count}", 30, FontWeight.SemiBold), UI.Secondary($"· {UI.Points(points)} points")),
            kinds,
            explained < qs.Count ? UI.Caption($"{qs.Count - explained} without an explanation") : null);
    }

    Control AssetsCard()
    {
        var bytes = Doc.Assets.Values.Sum(a => (long)a.Length);
        var unused = Doc.Assets.Keys.Count(k => Doc.Usage(k) == 0);
        return ConsolePage.Card("Assets", UI.Btn("Manage", () => window.ShowPage(DesignerPage.Assets), ButtonKind.Link),
            UI.H(6, UI.Txt($"{Doc.Assets.Count}", 30, FontWeight.SemiBold), UI.Secondary($"· {FileSize(bytes)}")),
            UI.Txt(unused == 0 ? "All in use" : $"{unused} unused", 11.5, color: unused == 0 ? Palette.Secondary : Palette.Warn));
    }

    Control SetupCard()
    {
        var e = Doc.Exam;
        return ConsolePage.Card("Exam setup", UI.Btn("Edit", () => window.ShowPage(DesignerPage.Settings), ButtonKind.Link),
            UI.LabeledText("Time limit", e.TimeLimitMinutes is { } t ? $"{(int)t} min" : "None"),
            UI.LabeledText("Pass mark", e.Scale is { } s ? $"{s.Pass} of {s.Max}" : $"{e.PassPercent ?? 70}%"),
            UI.LabeledText("Sections", $"{e.Sections?.Count ?? 0}"),
            UI.LabeledText("Case studies", $"{e.CaseStudies?.Count ?? 0}"),
            UI.LabeledText("Going back", e.AllowBack == false ? "Not allowed" : "Allowed"));
    }

    public static string FileSize(long bytes) => bytes switch
    {
        < 1000 => $"{bytes} bytes",
        < 1_000_000 => $"{bytes / 1000.0:0} KB",
        _ => $"{bytes / 1_000_000.0:0.#} MB",
    };
}

/// <summary>Target bar: filled to the actual share, with a tick at the target.</summary>
public sealed class CoverageBar(double actual, double? target) : Control
{
    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        ctx.DrawRectangle(Palette.ConsoleLine, null, new RoundedRect(new Rect(0, 0, w, h), h / 2));
        ctx.DrawRectangle(Palette.Brand, null, new RoundedRect(new Rect(0, 0, Math.Max(3, w * Math.Min(actual, 100) / 100), h), h / 2));
        if (target is { } t) ctx.FillRectangle(Palette.Text, new Rect(w * Math.Min(t, 100) / 100 - 1, -3, 2, h + 6));
    }
}

/// <summary>Target weight per topic. Used on the overview sheet and in exam settings.</summary>
public sealed class BlueprintEditor : ContentControl
{
    readonly DesignerWindow window;

    public BlueprintEditor(DesignerWindow window)
    {
        this.window = window;
        Render();
    }

    ProjectDocument Doc => window.Document;

    void Render()
    {
        var weights = Doc.Exam.TopicWeights ?? [];
        var total = weights.Values.Sum();
        var rows = new List<Control?>();
        foreach (var topic in Doc.Topics)
        {
            var field = new TextBox { Text = weights.TryGetValue(topic, out var w) ? SwiftJson.Number(w) : "", Width = 70, TextAlignment = TextAlignment.Right, Watermark = "Weight" };
            field.TextChanged += (_, _) => Doc.Edit(() =>
            {
                var map = new Dictionary<string, double>(Doc.Exam.TopicWeights ?? []);
                if (double.TryParse(field.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var v)) map[topic] = v;
                else map.Remove(topic);
                Doc.Exam.TopicWeights = map.Count == 0 ? null : map;
            }, $"weight:{topic}");
            rows.Add(UI.LabeledRow(topic, UI.H(4, field, UI.Secondary("%"))));
        }
        rows.Add(UI.LabeledRow("Total", UI.Txt($"{UI.Points(total)}%", color: weights.Count == 0 || Math.Abs(total - 100) <= 0.5 ? Palette.Secondary : Palette.Warn)));
        rows.Add(UI.H(8,
            UI.Btn("Use Current Mix", () => { Doc.Edit(UseCurrentMix); Render(); }),
            UI.Btn("Clear Targets", () => { Doc.Edit(() => Doc.Exam.TopicWeights = null); Render(); }, ButtonKind.Destructive).Disabled(weights.Count == 0)));
        Content = UI.FormSection("Blueprint targets", rows,
            "Copy the domain weights from the exam's official objectives. Targets should add up to 100%. The score report uses them as each domain's weight.");
    }

    void UseCurrentMix()
    {
        var asked = Doc.AskedQuestions;
        var total = asked.Sum(q => q.Points ?? 1);
        if (total <= 0) return;
        var map = new Dictionary<string, double>();
        foreach (var topic in Doc.Topics)
        {
            var pts = asked.Where(q => (ProjectDocument.NonEmpty(q.Topic) ?? "General") == topic).Sum(q => q.Points ?? 1);
            if (pts > 0) map[topic] = Math.Round(pts / total * 100, MidpointRounding.AwayFromZero);
        }
        Doc.Exam.TopicWeights = map;
    }
}
