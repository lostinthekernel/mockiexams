using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MockiCore;

namespace MockiExams;

/// <summary>One run of an exam: answering, then results. Used by the player and by the Designer preview.</summary>
public sealed record RunRequest(Exam Exam, SessionOptions Options, bool FullScreen = false, ExamStyle Style = ExamStyle.Modern)
{
    public Guid Id { get; } = Guid.NewGuid();
    /// <summary>Label for leaving the results: the Library, or closing a Designer preview.</summary>
    public string DoneLabel { get; init; } = "Back to Library";
    public bool AllowsRetry { get; init; } = true;
}

public sealed class RunView : Screen
{
    readonly RunRequest request;
    public RunRequest Request => request;
    /// <summary>Called with the graded result; the player records history here, previews don't.</summary>
    readonly Action<SessionResult> onFinish;
    readonly Action<IReadOnlyList<string>> onRetry;
    readonly Action onClose;
    readonly Session session;
    SessionResult? result;
    Screen? inner;
    bool enteredFullScreen;

    public RunView(RunRequest request, Action<SessionResult> onFinish, Action<IReadOnlyList<string>> onRetry, Action onClose)
    {
        this.request = request;
        this.onFinish = onFinish;
        this.onRetry = onRetry;
        this.onClose = onClose;
        session = new Session(request.Exam, request.Options);
        Content = Build();
    }

    protected override Control Build()
    {
        inner = result is { } r
            ? new ResultsView(r, request.AllowsRetry && r.MissedIds.Count > 0 ? () => onRetry(r.MissedIds) : null, Close, request.DoneLabel)
            : request.Options.Mode == SessionMode.Exam
                ? new ExamSessionView(session, request.Style, Finish, Close)
                : new PracticeView(session, Finish, Close);
        return inner;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (request.FullScreen && TopLevel.GetTopLevel(this) is Window w && w.WindowState != WindowState.FullScreen)
        {
            enteredFullScreen = true;
            w.WindowState = WindowState.FullScreen;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        LeaveFullScreen();
        base.OnDetachedFromVisualTree(e);
    }

    void Finish()
    {
        if (result is not null) return;
        result = session.Grade();
        onFinish(result);
        LeaveFullScreen();
        Content = Build();
    }

    void Close()
    {
        LeaveFullScreen();
        onClose();
    }

    void LeaveFullScreen()
    {
        if (enteredFullScreen && TopLevel.GetTopLevel(this) is Window { WindowState: WindowState.FullScreen } w) w.WindowState = WindowState.Normal;
        enteredFullScreen = false;
    }

    public override bool HandleKey(KeyEventArgs e) => inner?.HandleKey(e) ?? false;
}

/// <summary>Practice mode: answers are checked as you go, with a question grid on the left.</summary>
public sealed class PracticeView : Screen
{
    readonly Session session;
    readonly Action onFinish;
    readonly Action onQuit;
    readonly DispatcherTimer timer;
    bool showCase = true;
    QuestionBodyView? body;
    readonly ContentControl bodyHost = new();
    TextBlock? clock;

    public PracticeView(Session session, Action onFinish, Action onQuit)
    {
        this.session = session;
        this.onFinish = onFinish;
        this.onQuit = onQuit;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Tick();
        Content = Build();
    }

    Question Q => session.CurrentQuestion;
    bool IsLast => session.Current == session.Questions.Count - 1;
    CaseStudy? Case => session.Exam.CaseStudy(Q.CaseStudyId);
    bool Typing => QuestionBodyView.IsTyping(Q);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { timer.Stop(); base.OnDetachedFromVisualTree(e); }

    void Tick()
    {
        var now = DateTime.UtcNow;
        if (session.IsExpired(now)) { timer.Stop(); onFinish(); return; }
        if (clock is null) return;
        if (session.Remaining(now) is { } remaining)
        {
            clock.Text = UI.Duration(remaining);
            clock.Foreground = remaining < 60 ? Palette.Red : Palette.Text;
        }
        else clock.Text = UI.Duration((now - session.StartedAt).TotalSeconds);
    }

    protected override Control Build()
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("168,Auto,*,Auto"), Background = Palette.Window };
        var questionGrid = LiveRegion(() => new QuestionGrid(session, i => { session.Jump(i); Navigate(); }));
        var main = UI.Rows("Auto,Auto,*,Auto,Auto",
            LiveRegion(Header), UI.Divider(),
            bodyHost,
            UI.Divider(), LiveRegion(Footer));
        var inspector = LiveRegion(() => Case is { } c && showCase
            ? new Border { Width = 380, BorderBrush = Palette.ConsoleLine, BorderThickness = new Thickness(1, 0, 0, 0), Child = ExhibitViews.CaseStudyPanel(session.Exam, c) }
            : null);
        Grid.SetColumn(questionGrid, 0);
        var divider = UI.Divider(true);
        Grid.SetColumn(divider, 1);
        Grid.SetColumn(main, 2);
        Grid.SetColumn(inspector, 3);
        grid.Children.Add(questionGrid);
        grid.Children.Add(divider);
        grid.Children.Add(main);
        grid.Children.Add(inspector);
        bodyHost.Content = BodyArea();
        return grid;
    }

    /// <summary>A new question: rebuild the question area, then everything else.</summary>
    void Navigate()
    {
        bodyHost.Content = BodyArea();
        Invalidate();
    }

    /// <summary>The answer area redraws itself; refresh everything else that reflects answers.</summary>
    void Changed() => Invalidate();

    Control Header()
    {
        clock = UI.Txt("", wrap: false);
        var timerLabel = UI.H(5, Icons.Make(session.TimeLimit is null ? "clock" : "timer", 14, Palette.Secondary), clock);
        Tick();
        return UI.Bar(
            [UI.Headline($"Question {session.Current + 1} of {session.Questions.Count}"),
             UI.Capsule(Q.Topic),
             Q.Points != 1 ? UI.Caption($"{UI.Points(Q.Points)} pts") : null],
            [timerLabel,
             Case is null ? null : UI.Btn(UI.Label("briefcase", "Case Study", showCase ? Palette.Accent : Palette.Text), () => { showCase = !showCase; Invalidate(); }).Tip("Show or hide the case study"),
             UI.Btn(UI.Label(session.Flagged.Contains(Q.Id) ? "flag.fill" : "flag", session.Flagged.Contains(Q.Id) ? "Flagged" : "Flag",
                    session.Flagged.Contains(Q.Id) ? Palette.Flagged : Palette.Text), ToggleFlag).Tip("Flag for review (F)"),
             UI.Btn("Quit", onQuit)]).Pad(20, 12);
    }

    Control BodyArea()
    {
        body = new QuestionBodyView(session, Q, false, Changed);
        var feedback = LiveRegion(Feedback);
        return UI.Scroll(UI.V(16,
            Markdown.Rich(Q.Prompt, 17),
            ExhibitViews.List(session.Exam, Q.Exhibits),
            UI.Secondary(QuestionBodyView.Instruction(Q), 12.5),
            body,
            feedback).MaxW(900).Left().Pad(24, 24));
    }

    Control? Feedback()
    {
        if (!session.IsLocked(Q)) return null;
        var r = session.Response(Q);
        var right = Q.IsCorrect(r);
        var fraction = Q.Fraction(r);
        var color = right ? Palette.Green : fraction > 0 ? Palette.Orange : Palette.Red;
        var soft = right ? Palette.GreenSoft : fraction > 0 ? Palette.OrangeSoft : Palette.RedSoft;
        return UI.Box(UI.V(8,
            UI.H(6, Icons.Make(right ? "checkmark.circle.fill" : "xmark.circle.fill", 17, color),
                UI.Txt(right ? "Correct" : fraction > 0 ? $"Partly correct ({(int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero)}%)" : "Incorrect", 14, FontWeight.SemiBold, color)),
            !right && Q.Body is not (SingleBody or MultipleBody) ? Markdown.Inline($"Correct answer: {Q.DescribeAnswer()}") : null,
            Q.Explanation is { } e ? Markdown.Rich(e) : null), soft, 10, new Thickness(16));
    }

    Control Footer()
    {
        var primary = PrimaryLabel;
        var main = UI.Btn(primary, Primary, ButtonKind.Prominent).Disabled(primary == "Check" && session.Response(Q) is null);
        return UI.Bar(
            [session.CanGoBack
                ? UI.Btn(UI.Label("chevron.left", "Previous"), () => { session.Previous(); Navigate(); }).Disabled(session.Current == 0)
                : UI.Label("lock", "Review is not allowed in this exam", Palette.Secondary, 11.5),
             IsLast ? null : UI.Btn("Skip", () => { session.Next(); Navigate(); })],
            [IsLast ? null : UI.Btn("Finish…", ConfirmFinish), main]).Pad(20, 12);
    }

    string PrimaryLabel => !session.IsLocked(Q) ? "Check" : IsLast ? "Finish…" : "Next";

    void Primary()
    {
        switch (PrimaryLabel)
        {
            case "Check":
                if (session.Response(Q) is null) return;
                session.Reveal();
                body?.Refresh();
                Invalidate();
                break;
            case "Next":
                session.Next();
                Navigate();
                break;
            default:
                ConfirmFinish();
                break;
        }
    }

    void ToggleFlag() { session.ToggleFlag(); Changed(); }

    async void ConfirmFinish()
    {
        var n = session.UnansweredCount;
        var choice = await Dialogs.Ask(this, "Finish the exam?",
            n == 0 ? "All questions are answered." : $"{n} question{(n == 1 ? " is" : "s are")} unanswered and will be marked wrong.",
            new DialogButton("Keep Going", IsCancel: true), new DialogButton("Finish and Grade", IsDefault: true));
        if (choice == "Finish and Grade") onFinish();
    }

    public override bool HandleKey(KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key == Key.Enter) { Primary(); return true; }
        if (Typing) return false;
        if (KeyNumber(e.Key) is { } n) return body?.Press(n) ?? false;
        switch (e.Key)
        {
            case Key.F: ToggleFlag(); return true;
            case Key.Left when session.CanGoBack && session.Current > 0: session.Previous(); Navigate(); return true;
            case Key.Right when !IsLast: session.Next(); Navigate(); return true;
        }
        return false;
    }

    public static int? KeyNumber(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };
}

/// <summary>The numbered grid of questions beside a practice run.</summary>
public sealed class QuestionGrid : ContentControl
{
    public QuestionGrid(Session session, Action<int> jump)
    {
        var panel = new StackPanel { Spacing = 10, Margin = new Thickness(12) };
        WrapPanel? cells = null;
        for (var i = 0; i < session.Questions.Count; i++)
        {
            var title = session.SectionStart(i);
            if (title is not null || cells is null)
            {
                if (title is not null) panel.Children.Add(UI.Txt(title, 11.5, FontWeight.Bold, Palette.Secondary).Do(t => t.MaxLines = 2));
                cells = new WrapPanel { ItemWidth = 42, ItemHeight = 36 };
                panel.Children.Add(cells);
            }
            var q = session.Questions[i];
            var index = i;
            var response = session.Response(q);
            var fill = session.IsLocked(q)
                ? (q.IsCorrect(response) ? Palette.GreenSoft : Palette.RedSoft)
                : response is null ? Palette.Quaternary : Palette.AccentSoft;
            var cell = new Border
            {
                Width = 36, Height = 30, CornerRadius = new CornerRadius(6), Background = fill,
                BorderBrush = index == session.Current ? Palette.Accent : Palette.Clear, BorderThickness = new Thickness(2),
                Child = new Grid
                {
                    Children =
                    {
                        UI.Txt($"{i + 1}", 11.5, wrap: false).Center(),
                        session.Flagged.Contains(q.Id) ? Icons.Make("flag.fill", 8, Palette.Orange).Do(f => { f.HorizontalAlignment = HorizontalAlignment.Right; f.VerticalAlignment = VerticalAlignment.Top; f.Margin = new Thickness(0, 1, 2, 0); }) : new Panel(),
                    },
                },
                Opacity = session.CanJump(index) ? 1 : 0.4,
            };
            if (session.CanJump(index)) cell.OnTap(() => jump(index));
            cells.Children.Add(cell);
        }
        Content = UI.Scroll(panel);
        Background = Palette.Sidebar;
    }
}
