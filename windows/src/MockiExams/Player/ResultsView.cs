using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MockiCore;

namespace MockiExams;

public sealed class ResultsView : Screen
{
    enum Filter { All, Incorrect, Flagged }

    readonly SessionResult result;
    readonly Action? onRetry;
    readonly Action onDone;
    readonly string doneLabel;
    Filter filter = Filter.Incorrect;

    public ResultsView(SessionResult result, Action? onRetry, Action onDone, string doneLabel)
    {
        this.result = result;
        this.onRetry = onRetry;
        this.onDone = onDone;
        this.doneLabel = doneLabel;
        Content = Build();
    }

    protected override Control Build()
    {
        var header = new Border
        {
            Background = Palette.Sidebar, Padding = new Thickness(18, 10),
            Child = UI.Bar(
                [Icons.Make(result.Passed ? "seal.check" : "seal.x", 18, result.Passed ? Palette.Ok : Palette.Err),
                 UI.Headline($"{(result.Mode == SessionMode.Exam ? "Exam" : "Practice")} complete"),
                 UI.Secondary(result.Exam.Title).Do(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; })],
                [onRetry is not null ? UI.Btn($"Retry {result.MissedIds.Count} Missed", onRetry) : null,
                 UI.Btn(UI.Label("chevron.left", doneLabel, Brushes.White), onDone, ButtonKind.Prominent, Palette.BrandFill).Tip($"{doneLabel} (Esc)")]),
        };
        var report = UI.V(22,
            UI.FormSection(null, [new ScoreReport(result)],
                result.Exam.Scale is not null ? "The scaled score is a linear estimate from your percentage. Vendors don't publish their real scaling." : null),
            UI.FormSection("Performance by domain", [DomainTable()]),
            LiveRegion(Review)).MaxW(980).Pad(24, 20);
        report.HorizontalAlignment = HorizontalAlignment.Stretch;
        return UI.Rows("Auto,Auto,*", header, UI.Divider(), new Border { Background = Palette.ConsolePage, Child = UI.Scroll(report) });
    }

    Control Review()
    {
        var shown = filter switch
        {
            Filter.Incorrect => result.Items.Where(i => !i.IsCorrect).ToList(),
            Filter.Flagged => result.Items.Where(i => i.Flagged).ToList(),
            _ => result.Items.ToList(),
        };
        var rows = new List<Control?>
        {
            UI.Segmented([(Filter.All, "All"), (Filter.Incorrect, "Incorrect"), (Filter.Flagged, "Flagged")], filter, f => { filter = f; Invalidate(); }, 90),
        };
        if (shown.Count == 0) rows.Add(UI.Secondary("Nothing here."));
        foreach (var item in shown) { rows.Add(UI.Divider()); rows.Add(ReviewRow(item)); }
        return UI.FormSection("Review", rows);
    }

    static Control ReviewRow(ResultItem item)
    {
        var q = item.Question;
        var icon = item.IsCorrect ? "checkmark.circle.fill" : item.Score > 0 ? "circle.half" : "xmark.circle.fill";
        var color = item.IsCorrect ? Palette.Green : item.Score > 0 ? Palette.Orange : Palette.Red;
        return UI.V(6,
            UI.Columns("Auto,*,Auto,Auto",
                Icons.Make(icon, 15, color).Pad(0, 2, 8, 0).Do(c => c.VerticalAlignment = VerticalAlignment.Top),
                Markdown.Inline(q.Prompt.Split("```")[0].Trim()),
                q.Points != 1 || q.PartialCredit ? UI.Caption($"{UI.Points(item.Earned)}/{UI.Points(q.Points)} pts").Pad(8, 0, 0, 0) : null,
                item.Flagged ? Icons.Make("flag.fill", 13, Palette.Orange).Pad(8, 0, 0, 0) : null),
            Markdown.Inline($"Your answer: {q.Describe(item.Response)}", color: item.IsCorrect ? Palette.Secondary : Palette.Red),
            item.IsCorrect ? null : Markdown.Inline($"Correct: {q.DescribeAnswer()}", color: Palette.Green),
            q.Explanation is { } e ? Markdown.Inline(e, 12.5, Palette.Secondary) : null).Pad(0, 4);
    }

    /// <summary>Each topic's weight, score, and whether it meets the exam's passing standard.</summary>
    Control DomainTable()
    {
        var exam = result.Exam;
        var standard = exam.Scale is { } s ? (double)(s.Pass - s.Min) / (s.Max - s.Min) : exam.PassPercent / 100.0;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,Auto,Auto,*"), RowDefinitions = new RowDefinitions() };
        void Add(Control c, int row, int col) { Grid.SetRow(c, row); Grid.SetColumn(c, col); c.Margin = new Thickness(0, 5, 16, 5); grid.Children.Add(c); }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        string[] heads = ["Domain", "Weight", "Performance", "Result"];
        for (var c = 0; c < 4; c++) Add(UI.Txt(heads[c], 11.5, FontWeight.SemiBold, Palette.Secondary), 0, c);
        var r = 1;
        foreach (var t in result.Topics)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var meets = t.Fraction >= standard;
            Add(UI.Txt(t.Topic), r, 0);
            Add(UI.Secondary($"{(int)Math.Round(exam.Weight(t.Topic), MidpointRounding.AwayFromZero)}%"), r, 1);
            Add(UI.H(8, new ProgressBar { Value = t.Fraction * 100, Width = 140, Foreground = meets ? Palette.Brand : Palette.Err },
                UI.Txt($"{(int)Math.Round(t.Fraction * 100, MidpointRounding.AwayFromZero)}%").Width(40).Do(x => x.TextAlignment = TextAlignment.Right)), r, 2);
            Add(UI.Txt(meets ? "Meets the standard" : "Needs improvement", 13, meets ? FontWeight.Normal : FontWeight.SemiBold, meets ? Palette.Text : Palette.Err), r, 3);
            r++;
        }
        return grid;
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return false;
        onDone();
        return true;
    }
}

/// <summary>Pass/fail with the score placed on the exam's scale and the passing line marked.</summary>
public sealed class ScoreReport : ContentControl
{
    public ScoreReport(SessionResult result)
    {
        var scaled = result.Exam.Scale is { } s && result.ScaledScore is { } you;
        double min = scaled ? result.Exam.Scale!.Min : 0, max = scaled ? result.Exam.Scale!.Max : 100;
        double pass = scaled ? result.Exam.Scale!.Pass : result.Exam.PassPercent;
        double score = scaled ? result.ScaledScore!.Value : result.Percent;
        var unit = scaled ? "" : "%";
        var left = UI.V(6,
            UI.Upper("Score report"),
            UI.Txt(result.Exam.Title, 20, FontWeight.Bold),
            UI.Secondary($"{result.Mode.Label()} · {result.FinishedAt.ToLocalTime().ToString("MMM d, yyyy, HH:mm", CultureInfo.CurrentCulture)} · {UI.Duration(result.Duration)}"),
            UI.Txt(result.Passed ? "PASS" : "FAIL", 30, FontWeight.Black, result.Passed ? Palette.Ok : Palette.Err).Pad(0, 4, 0, 0));
        var right = UI.V(8,
            UI.Bar([UI.Headline($"Your score: {(int)score}{unit}")], [UI.Secondary($"Passing: {(int)pass}{unit}")]),
            new ScaleBar(min, max, pass, score, result.Passed) { Height = 30 },
            UI.Bar([UI.Caption($"{(int)min}")], [UI.Caption($"{(int)max}")]),
            UI.Secondary($"{result.CorrectCount} of {result.Items.Count} fully correct · {UI.Points(result.Earned)} of {UI.Points(result.Possible)} points · {result.Percent}%", 12.5)).Width(340);
        Content = UI.Columns("*,Auto", left, right).Pad(0, 8);
    }
}

public sealed class ScaleBar(double min, double max, double pass, double you, bool passed) : Control
{
    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        double X(double v) => (v - min) / (max - min) * w;
        var bar = new Rect(0, 14, w, 8);
        using (ctx.PushClip(new RoundedRect(bar, 4)))
        {
            ctx.FillRectangle(Palette.ErrSoft, new Rect(0, 14, X(pass), 8));
            ctx.FillRectangle(Palette.OkSoft, new Rect(X(pass), 14, w - X(pass), 8));
        }
        ctx.FillRectangle(Palette.Text, new Rect(X(pass) - 1, 9, 2, 18));
        var text = new FormattedText($"{(int)you}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), 10.5, Brushes.White);
        var bw = text.Width + 10;
        var cx = Math.Clamp(X(you), bw / 2, w - bw / 2);
        ctx.DrawRectangle(passed ? Palette.Ok : Palette.Err, null, new RoundedRect(new Rect(cx - bw / 2, 0, bw, 14), 3));
        ctx.DrawText(text, new Point(cx - text.Width / 2, 0.5));
    }
}
