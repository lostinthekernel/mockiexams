using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MockiCore;

namespace MockiExams;

enum ReviewFilter { All, Incomplete, Marked }

/// <summary>Exam simulation: item screens in the chosen style, a review screen, timer warnings and notes.</summary>
public sealed class ExamSessionView : Screen
{
    readonly Session session;
    readonly ExamStyle style;
    readonly Action onFinish;
    readonly Action onQuit;
    readonly DispatcherTimer timer;
    readonly HashSet<int> warned = [];
    readonly ContentControl stage = new();
    readonly List<Action> tickers = [];

    bool reviewing;
    /// <summary>Items being revisited from the review screen ("Review incomplete", "Review marked").</summary>
    List<int>? queue;
    string notes = "";
    bool showNotes;
    int caseTab;
    QuestionBodyView? body;

    public ExamSessionView(Session session, ExamStyle style, Action onFinish, Action onQuit)
    {
        this.session = session;
        this.style = style;
        this.onFinish = onFinish;
        this.onQuit = onQuit;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        timer.Tick += (_, _) => Tick();
        Content = Build();
    }

    Question Q => session.CurrentQuestion;
    bool IsLast => session.Current == session.Questions.Count - 1;
    CaseStudy? Case => session.Exam.CaseStudy(Q.CaseStudyId);
    bool Classic => style == ExamStyle.Classic;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e) { base.OnAttachedToVisualTree(e); timer.Start(); }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e) { timer.Stop(); base.OnDetachedFromVisualTree(e); }

    protected override Control Build()
    {
        Show();
        return stage;
    }

    /// <summary>Rebuilds the current item or review screen.</summary>
    void Show()
    {
        tickers.Clear();
        ResetRegions();
        body = null;
        stage.Content = reviewing ? ReviewScreen() : Classic ? ClassicItem() : ModernItem();
        Tick();
    }

    async void Tick()
    {
        var now = DateTime.UtcNow;
        if (session.IsExpired(now)) { timer.Stop(); onFinish(); return; }
        foreach (var t in tickers) t();
        if (session.TimeLimit is not { } limit || session.Remaining(now) is not { } remaining) return;
        foreach (var minutes in new[] { 15, 5 })
        {
            if (minutes * 60 < limit && remaining <= minutes * 60 && warned.Add(minutes))
                await Dialogs.Ask(this, $"{minutes} minutes remaining.", null, new DialogButton("Continue", IsCancel: true, IsDefault: true));
        }
    }

    string NextLabel
    {
        get
        {
            if (queue is not null) return queue.Any(i => i > session.Current) ? "Next" : "Review Screen";
            if (!IsLast) return "Next";
            return session.CanGoBack ? "Review Screen" : "End Exam";
        }
    }

    void GoNext()
    {
        if (queue is not null)
        {
            var next = queue.Where(i => i > session.Current).Cast<int?>().FirstOrDefault();
            if (next is { } n) { session.Jump(n); Show(); } else OpenReview();
        }
        else if (!IsLast) { session.Next(); Show(); }
        else if (session.CanGoBack) OpenReview();
        else ConfirmEnd();
    }

    void Previous() { if (session.CanGoBack && session.Current > 0) { session.Previous(); Show(); } }

    void OpenReview()
    {
        if (!session.CanGoBack) { ConfirmEnd(); return; }
        queue = null;
        reviewing = true;
        Show();
    }

    void JumpTo(int i)
    {
        queue = null;
        reviewing = false;
        session.Jump(i);
        Show();
    }

    void StartReview(ReviewFilter filter)
    {
        var indices = Enumerable.Range(0, session.Questions.Count).Where(i =>
        {
            var q = session.Questions[i];
            return filter switch
            {
                ReviewFilter.Incomplete => session.Response(q) is null,
                ReviewFilter.Marked => session.Flagged.Contains(q.Id),
                _ => true,
            };
        }).ToList();
        if (indices.Count == 0) return;
        queue = indices;
        reviewing = false;
        session.Jump(indices[0]);
        Show();
    }

    async void ConfirmEnd()
    {
        int incomplete = session.UnansweredCount, marked = session.Flagged.Count;
        var choice = await Dialogs.Ask(this, "End the exam?",
            incomplete == 0 && marked == 0 ? "All items are complete. You can't change answers after ending."
                : $"{incomplete} incomplete and {marked} marked item{(marked == 1 ? "" : "s")}. Incomplete items are scored as wrong.",
            new DialogButton("Return to Exam", IsCancel: true), new DialogButton("End Exam", IsDefault: true));
        if (choice == "End Exam") onFinish();
    }

    async void ConfirmQuit()
    {
        var choice = await Dialogs.Ask(this, "Leave without a score?", "Your answers won't be graded or saved to history.",
            new DialogButton("Return to Exam", IsCancel: true, IsDefault: true), new DialogButton("Leave Exam", ButtonKind.Destructive));
        if (choice == "Leave Exam") onQuit();
    }

    void ToggleFlag() { session.ToggleFlag(); Refresh(); }

    /// <summary>Answers changed: refresh counts, strip and flags without rebuilding the answer area.</summary>
    void Refresh() => Invalidate();

    // MARK: Modern

    Control ModernItem()
    {
        var header = LiveRegion(() => UI.Bar(
            [UI.Headline(session.Exam.Title), Case is { } c ? UI.Secondary(c.Title) : null],
            [UI.Btn(UI.Label("notes", "Notes", showNotes ? Palette.Accent : Palette.ExamInk), () => { showNotes = !showNotes; Show(); }),
             UI.Btn(UI.Label(session.Flagged.Contains(Q.Id) ? "flag.fill" : "flag", session.Flagged.Contains(Q.Id) ? "Flagged" : "Flag", Palette.Flagged), ToggleFlag).Tip("Flag for review (F)"),
             TimerPill(),
             UI.Btn("Quit", ConfirmQuit)]).Pad(18, 10));
        var strip = LiveRegion(ProgressStrip);
        body = new QuestionBodyView(session, Q, false, Refresh);
        var item = UI.Scroll(UI.V(16,
            UI.Secondary($"Question {session.Current + 1} of {session.Questions.Count}{(queue is not null ? " · reviewing" : "")}", 12.5),
            Markdown.Rich(Q.Prompt, 17, Palette.ExamInk),
            ExhibitViews.List(session.Exam, Q.Exhibits),
            UI.Secondary(QuestionBodyView.Instruction(Q), 12.5),
            body).MaxW(860).Left().Pad(24, 24));

        Control center = item;
        if (Case is { } cs)
        {
            var split = new Grid { ColumnDefinitions = new ColumnDefinitions("460,Auto,*") };
            var pane = CasePane(cs);
            var splitter = new GridSplitter { Width = 5, Background = Palette.ExamLine, ResizeDirection = GridResizeDirection.Columns };
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(item, 2);
            split.Children.Add(pane);
            split.Children.Add(splitter);
            split.Children.Add(item);
            center = split;
        }

        var footer = LiveRegion(() => UI.Bar(
            [session.CanGoBack
                ? UI.Btn(UI.Label("chevron.left", "Previous", Palette.ExamInk), Previous).Disabled(session.Current == 0)
                : UI.Label("lock", "You can't return to earlier questions in this exam", Palette.Secondary, 11.5)],
            [UI.Secondary($"{session.AnsweredCount} answered · {session.Flagged.Count} flagged", 12.5),
             session.CanGoBack ? UI.Btn("Review", OpenReview) : null,
             UI.Btn(NextLabel, GoNext, ButtonKind.Prominent, Palette.ExamInk).Do(b => b.Foreground = Palette.ExamPage)]).Pad(18, 10));

        var layout = UI.Rows("Auto,Auto,Auto,*,Auto,Auto",
            new Border { Background = Palette.ExamSurface, Child = header },
            new Border { Background = Palette.ExamSurface, Child = strip, Padding = new Thickness(18, 0, 18, 6) },
            UI.Divider(),
            center,
            UI.Divider(),
            new Border { Background = Palette.ExamSurface, Child = footer });
        layout.Background = Palette.ExamPage;

        if (!showNotes) return layout;
        var notesBox = new TextBox { Text = notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = Fonts.Mono };
        notesBox.TextChanged += (_, _) => notes = notesBox.Text ?? "";
        var inspector = new Border
        {
            Width = 300, Background = Palette.ExamSurface, BorderBrush = Palette.ExamLine, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(14),
            Child = UI.Rows("Auto,Auto,*", UI.Headline("Notes"), UI.Caption("Scratch space for this exam. Not saved or graded.").Pad(0, 4, 0, 8), notesBox),
        };
        return UI.Columns("*,Auto", layout, inspector);
    }

    Control CasePane(CaseStudy c)
    {
        var content = new ContentControl();
        void Fill() => content.Content = caseTab == 0
            ? UI.V(12, UI.Txt(c.Title, 17, FontWeight.Bold), Markdown.Rich(c.Scenario, color: Palette.ExamInk))
            : ExhibitViews.List(session.Exam, c.Exhibits);
        Fill();
        var tabs = new ContentControl();
        void Tabs()
        {
            var options = new List<(int, string)> { (0, "Scenario") };
            if (c.Exhibits.Count > 0) options.Add((1, $"Exhibits ({c.Exhibits.Count})"));
            tabs.Content = UI.Segmented(options, caseTab, t => { caseTab = t; Fill(); Tabs(); });
        }
        Tabs();
        return new Border
        {
            Background = Palette.ExamSurface, Padding = new Thickness(16), MinWidth = 300,
            Child = UI.Rows("Auto,*", tabs.Left().Pad(0, 0, 0, 10), UI.Scroll(content)),
        };
    }

    /// <summary>One segment per question: answered, flagged, current, or not yet answered. Click to jump.</summary>
    Control ProgressStrip()
    {
        var n = session.Questions.Count;
        var grid = new Grid { Height = 10 };
        for (var i = 0; i < n; i++) grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var i = 0; i < n; i++)
        {
            var q = session.Questions[i];
            var index = i;
            var color = i == session.Current ? Palette.ExamInk : session.Flagged.Contains(q.Id) ? Palette.Flagged
                : session.Response(q) is null ? Palette.Unanswered : Palette.Answered;
            var seg = new Border
            {
                Background = color, Height = i == session.Current ? 6 : 4, CornerRadius = new CornerRadius(2),
                Margin = new Thickness(i == 0 ? 0 : 1, 0, i == n - 1 ? 0 : 1, 0), VerticalAlignment = VerticalAlignment.Center,
            };
            var hit = new Border { Background = Brushes.Transparent, Child = seg }.Tip($"Question {i + 1}");
            if (session.CanJump(index)) hit.OnTap(() => JumpTo(index));
            Grid.SetColumn(hit, i);
            grid.Children.Add(hit);
        }
        return grid;
    }

    Control TimerPill()
    {
        var label = UI.Txt("", 12.5, FontWeight.SemiBold, wrap: false);
        var icon = new ContentControl();
        var pill = new Border
        {
            CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 4), BorderThickness = new Thickness(1),
            Child = UI.H(5, icon, label),
        };
        void Update()
        {
            var now = DateTime.UtcNow;
            var remaining = session.Remaining(now);
            bool low = (remaining ?? double.MaxValue) < 600, critical = (remaining ?? double.MaxValue) < 300;
            var ink = critical ? Palette.Err : low ? Palette.Warn : Palette.ExamInk;
            label.Text = remaining is { } r ? $"{UI.Duration(r)} left" : UI.Duration((now - session.StartedAt).TotalSeconds);
            label.Foreground = ink;
            icon.Content = Icons.Make(remaining is null ? "clock" : "timer", 13, ink);
            pill.Background = low ? Palette.TimerSoft : Brushes.Transparent;
            pill.BorderBrush = low ? Palette.Warn : Palette.ExamLine;
        }
        Update();
        tickers.Add(Update);
        return pill;
    }

    // MARK: Classic

    Control ClassicItem()
    {
        var info = LiveRegion(() =>
        {
            var mark = new CheckBox { Content = "Mark for Review", IsChecked = session.Flagged.Contains(Q.Id), FontFamily = Fonts.Classic, FontSize = 13 };
            mark.IsCheckedChanged += (_, _) => { if ((mark.IsChecked == true) != session.Flagged.Contains(Q.Id)) ToggleFlag(); };
            var time = ClassicClock(13);
            return UI.Bar(
                [time, UI.Txt($"Item {session.Current + 1} of {session.Questions.Count}", 13).Do(t => t.FontFamily = Fonts.Classic),
                 queue is not null ? UI.Txt("Reviewing", 13, color: Palette.Secondary).Do(t => t.FontFamily = Fonts.Classic) : null],
                [mark], 24).Pad(14, 4);
        });

        body = new QuestionBodyView(session, Q, true, Refresh);
        var exhibits = (Case?.Exhibits ?? []).Concat(Q.Exhibits).ToList();
        var buttons = new WrapPanel();
        if (Case is { } cs)
            buttons.Children.Add(ClassicButton("Case Study", () => ShowCase(cs)).Pad(0, 0, 8, 0));
        for (var i = 0; i < exhibits.Count; i++)
        {
            var e = exhibits[i];
            buttons.Children.Add(ClassicButton(exhibits.Count == 1 ? "Exhibit" : $"Exhibit {i + 1}", () => ExhibitViews.ShowViewer(this, session.Exam, e)).Tip(e.Title).Pad(0, 0, 8, 0));
        }
        var content = UI.V(16,
            Markdown.Rich(Q.Prompt, 15, font: Fonts.Classic),
            buttons.Children.Count > 0 ? buttons : null,
            body).MaxW(900).Left().Pad(26, 22);

        var stage = new Border
        {
            Background = Palette.ClassicStage, BorderBrush = Palette.ClassicLine, BorderThickness = new Thickness(1),
            Margin = new Thickness(12), Child = UI.Scroll(content),
        };

        var footer = UI.Bar(
            [session.CanGoBack ? ClassicButton("◀ Previous", Previous).Disabled(session.Current == 0) : null,
             ClassicButton(NextLabel == "Next" ? "Next ▶" : NextLabel, GoNext, NextLabel != "Next")],
            [UI.Txt("Answers are saved automatically.", 12, color: Palette.Secondary).Do(t => t.FontFamily = Fonts.Classic),
             session.CanGoBack ? ClassicButton("Review", OpenReview) : null], 8).Pad(14, 0, 14, 12);

        return new Border
        {
            Background = Palette.ClassicPage,
            Child = UI.Rows("Auto,Auto,*,Auto",
                ClassicTitleBar(session.Exam.Title, "Exam simulation", ConfirmQuit),
                new Border { Background = Palette.ClassicBar, BorderBrush = Palette.ClassicLine, BorderThickness = new Thickness(0, 0, 0, 1), Child = info },
                stage,
                footer),
        };
    }

    Control ClassicClock(double size)
    {
        var t = UI.Txt("", size, FontWeight.Bold, wrap: false).Do(x => x.FontFamily = Fonts.Classic);
        void Update()
        {
            var now = DateTime.UtcNow;
            t.Text = session.Remaining(now) is { } r ? $"Time Remaining {Clock(r)}" : $"Time Elapsed {Clock((now - session.StartedAt).TotalSeconds)}";
        }
        Update();
        tickers.Add(Update);
        return t;
    }

    void ShowCase(CaseStudy cs)
    {
        var host = ModalHost.Of(this);
        if (host is null) return;
        Sheet? sheet = null;
        var content = UI.Rows("*,Auto,Auto", ExhibitViews.CaseStudyPanel(session.Exam, cs), UI.Divider(),
            UI.Btn("Close", () => sheet?.Close()).Do(b => b.HorizontalAlignment = HorizontalAlignment.Right).Pad(12, 12));
        var size = TopLevel.GetTopLevel(this)?.Bounds.Size ?? new Size(1200, 800);
        sheet = host.Present(content, Math.Min(860, size.Width - 60), Math.Min(680, size.Height - 60), () => sheet?.Close());
    }

    static Control ClassicTitleBar(string title, string trailing, Action? quit)
    {
        var right = new List<Control?> { UI.Txt(trailing, 13, color: Brushes.White).Do(t => { t.FontFamily = Fonts.Classic; t.Opacity = 0.8; }) };
        if (quit is not null)
        {
            var q = UI.Txt("Quit", 12, color: Brushes.White).Do(t => { t.FontFamily = Fonts.Classic; t.TextDecorations = TextDecorations.Underline; t.Opacity = 0.85; });
            right.Add(q.OnTap(quit));
        }
        return new Border
        {
            Background = Palette.ClassicTitle, Padding = new Thickness(14, 7),
            Child = UI.Bar([UI.Txt(title, 13, FontWeight.Bold, Brushes.White).Do(t => t.FontFamily = Fonts.Classic)], right, 16),
        };
    }

    /// <summary>Square, gradient buttons in the style of testing-center software.</summary>
    static Button ClassicButton(string label, Action action, bool strong = false)
    {
        var b = new Button
        {
            Content = UI.Txt(label, 13, color: strong ? Brushes.White : Palette.Text, wrap: false).Do(t => t.FontFamily = Fonts.Classic),
            Padding = new Thickness(16, 6), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1),
            BorderBrush = strong ? new SolidColorBrush(Color.FromRgb(0x1C, 0x4F, 0x87)) : Palette.ClassicLine,
            Background = strong
                ? new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                    GradientStops = { new GradientStop(Color.FromRgb(0x40, 0x78, 0xBA), 0), new GradientStop(Color.FromRgb(0x29, 0x5E, 0x9E), 1) },
                }
                : Palette.ClassicStage,
        };
        b.Click += (_, _) => action();
        return b;
    }

    static string Clock(double seconds)
    {
        var s = (int)Math.Floor(seconds);
        return $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}";
    }

    // MARK: Review screen

    Control ReviewScreen()
    {
        int incomplete = session.UnansweredCount, marked = session.Flagged.Count;
        var summary = $"{session.Questions.Count} items · {session.AnsweredCount} complete · {incomplete} incomplete · {marked} marked";
        Control top;
        if (Classic)
        {
            top = UI.V(0,
                ClassicTitleBar(session.Exam.Title, "Review Screen", ConfirmQuit),
                new Border
                {
                    Background = Palette.ClassicBar, Padding = new Thickness(14, 6),
                    Child = UI.H(22, ClassicClock(13), UI.Txt(summary, 13).Do(t => t.FontFamily = Fonts.Classic)),
                });
        }
        else
        {
            top = UI.V(0, new Border
            {
                Background = Palette.ExamSurface, Padding = new Thickness(18, 12),
                Child = UI.Bar([UI.Txt("Review your answers", 17, FontWeight.Bold, Palette.ExamInk), UI.Secondary(summary)], [TimerPill(), UI.Btn("Quit", ConfirmQuit)]),
            }, UI.Divider());
        }

        var cells = new Grid();
        const int perRow = 5;
        for (var c = 0; c < perRow; c++) cells.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var rows = (session.Questions.Count + perRow - 1) / perRow;
        for (var r = 0; r < rows; r++) cells.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var line = Classic ? Palette.ClassicLine : Palette.ExamLine;
        for (var i = 0; i < session.Questions.Count; i++)
        {
            var q = session.Questions[i];
            var index = i;
            bool isMarked = session.Flagged.Contains(q.Id), isIncomplete = session.Response(q) is null;
            var status = isMarked ? "Marked" : isIncomplete ? "Incomplete" : "Complete";
            var section = session.SectionStart(i);
            var font = Classic ? Fonts.Classic : FontFamily.Default;
            var cell = new Border
            {
                BorderBrush = line, BorderThickness = new Thickness(0.5), Padding = new Thickness(10, 8), Background = Brushes.Transparent,
                Child = UI.Bar(
                    [UI.Txt($"{i + 1}", 13).Do(t => t.FontFamily = font), section is null ? null : UI.Caption(section).Do(t => { t.MaxLines = 1; t.TextTrimming = TextTrimming.CharacterEllipsis; })],
                    [isMarked ? Icons.Make("flag.fill", 11, Palette.Flagged) : null,
                     UI.Txt(status, 11.5, isMarked || isIncomplete ? FontWeight.Bold : FontWeight.Normal,
                        isIncomplete ? Palette.Err : isMarked ? Palette.Flagged : Palette.Secondary).Do(t => t.FontFamily = font)], 6),
            }.OnTap(() => JumpTo(index));
            Grid.SetRow(cell, i / perRow);
            Grid.SetColumn(cell, i % perRow);
            cells.Children.Add(cell);
        }
        var listing = UI.V(12,
            UI.Txt("Click an item to go to it, or use the buttons below. Marked and incomplete items can still be changed until you end the exam.",
                Classic ? 13 : 12.5, color: Palette.Secondary).Do(t => { if (Classic) t.FontFamily = Fonts.Classic; }),
            new Border { BorderBrush = line, BorderThickness = new Thickness(1), Child = cells }).Pad(18, 18);

        Button ReviewButton(string title, Action a) => Classic ? ClassicButton(title, a) : UI.Btn(title, a);
        var footer = UI.Bar(
            [ReviewButton("Review All", () => StartReview(ReviewFilter.All)),
             ReviewButton($"Review Incomplete ({incomplete})", () => StartReview(ReviewFilter.Incomplete)).Disabled(incomplete == 0),
             ReviewButton($"Review Marked ({marked})", () => StartReview(ReviewFilter.Marked)).Disabled(marked == 0)],
            [Classic ? ClassicButton("End Exam", ConfirmEnd, true) : UI.Btn("End Exam", ConfirmEnd, ButtonKind.Prominent, Palette.ExamInk).Do(b => b.Foreground = Palette.ExamPage)], 8).Pad(18, 10);

        return UI.Rows("Auto,*,Auto,Auto",
            top,
            new Border { Background = Classic ? Palette.ClassicStage : Palette.ExamPage, Child = UI.Scroll(listing) },
            UI.Divider(),
            new Border { Background = Classic ? Palette.ClassicPage : Palette.ExamSurface, Child = footer });
    }

    // MARK: Keys

    public override bool HandleKey(KeyEventArgs e)
    {
        if (reviewing || e.KeyModifiers != KeyModifiers.None) return false;
        if (e.Key == Key.Enter) { GoNext(); return true; }
        if (QuestionBodyView.IsTyping(Q)) return false;
        if (PracticeView.KeyNumber(e.Key) is { } n) return body?.Press(n) ?? false;
        switch (e.Key)
        {
            case Key.F: ToggleFlag(); if (Classic) Show(); return true;
            case Key.Left when session.CanGoBack && session.Current > 0: Previous(); return true;
        }
        return false;
    }
}
