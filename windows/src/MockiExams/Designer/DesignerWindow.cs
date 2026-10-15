using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MockiCore;

namespace MockiExams;

public enum DesignerPage { Overview, Bank, Sections, CaseStudies, Assets, Settings }

/// <summary>The designer window: a left navigation of pages, a page header with actions, and an issues panel.</summary>
public sealed class DesignerWindow : AppWindow
{
    public ProjectDocument Document { get; }
    public List<Issue> Issues { get; private set; } = [];
    public DesignerPage Page { get; private set; } = DesignerPage.Overview;
    public HashSet<int> BankSelection { get; } = [];
    public string Search { get; set; } = "";

    bool showIssues;
    readonly DispatcherTimer validateLater = new() { Interval = TimeSpan.FromMilliseconds(250) };
    readonly List<Live> chrome = [];
    Screen? page;
    bool closingConfirmed;

    public DesignerWindow(ProjectDocument document)
    {
        Document = document;
        Width = 1280;
        Height = 820;
        MinWidth = 1100;
        MinHeight = 700;
        validateLater.Tick += (_, _) => { validateLater.Stop(); Revalidate(); };
        document.Changed += OnChanged;
        Closing += OnClosing;
        Issues = document.Validate().Issues;
        UpdateTitle();
        Render();
    }

    public int ErrorCount => Issues.Count(i => i.Severity == Severity.Error);
    string ProjectName => Document.DisplayName;

    void UpdateTitle() => Title = $"{ProjectName}{(Document.IsDirty ? " *" : "")} — MockiExams Designer";

    void OnChanged()
    {
        UpdateTitle();
        validateLater.Stop();
        validateLater.Start();
        RefreshChrome();
        page?.Invalidate();
    }

    void Revalidate()
    {
        Issues = Document.Validate().Issues;
        RefreshChrome();
        page?.Invalidate();
    }

    void RefreshChrome() { foreach (var l in chrome) l.Update(); }

    Live Chrome(Func<Control?> build)
    {
        var l = new Live(build);
        chrome.Add(l);
        return l;
    }

    /// <summary>Redraws the current page, e.g. after undo or a page switch.</summary>
    public void ShowPage(DesignerPage p)
    {
        Page = p;
        Render();
    }

    protected override Control BuildPage()
    {
        chrome.Clear();
        page = Page switch
        {
            DesignerPage.Bank => new QuestionBankPage(this),
            DesignerPage.Sections => new SimplePage(this, "Sections", "Sections set which bank questions the exam asks, and in what order.", () => new SectionsEditor(this)),
            DesignerPage.CaseStudies => new CaseStudiesPage(this),
            DesignerPage.Assets => new SimplePage(this, $"Assets ({Document.Assets.Count})", "Images and PDFs used as exhibits and hotspots.", () => new AssetsView(this)),
            DesignerPage.Settings => new SimplePage(this, "Exam settings", null, () => new ExamSettingsView(this)),
            _ => new OverviewPage(this),
        };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("230,Auto,*,Auto") };
        var sidebar = new Border { Background = Palette.Sidebar, Child = Chrome(Sidebar) };
        var line = UI.Divider(true);
        var pageHost = new Border { Background = Palette.ConsolePage, Child = page };
        var issues = Chrome(() => showIssues ? new Border
        {
            Width = 320, Background = Palette.Window, BorderBrush = Palette.ConsoleLine, BorderThickness = new Thickness(1, 0, 0, 0),
            Child = IssuesView(),
        } : null);
        Grid.SetColumn(line, 1);
        Grid.SetColumn(pageHost, 2);
        Grid.SetColumn(issues, 3);
        body.Children.Add(sidebar);
        body.Children.Add(line);
        body.Children.Add(pageHost);
        body.Children.Add(issues);
        return UI.Rows("Auto,Auto,Auto,*", Chrome(MenuBar), Chrome(Toolbar), UI.Divider(), body);
    }

    // MARK: Chrome

    Control MenuBar()
    {
        var menu = new Menu { Background = Palette.Window };
        menu.Items.Add(LibraryWindow.MenuOf("_File",
            LibraryWindow.Item("_New Exam Project", "Ctrl+N", App.NewProject),
            LibraryWindow.Item("_Open Project…", "Ctrl+O", () => App.OpenProject(this)),
            null,
            LibraryWindow.Item("_Save", "Ctrl+S", () => _ = Save()),
            LibraryWindow.Item("Save _As…", "Ctrl+Shift+S", () => _ = SaveAs()),
            null,
            LibraryWindow.Item("_Settings…", "Ctrl+OemComma", () => SettingsSheet.Show(this)),
            null,
            LibraryWindow.Item("_Close", "Ctrl+W", Close)));
        menu.Items.Add(LibraryWindow.MenuOf("_Edit",
            LibraryWindow.Item("_Undo", "Ctrl+Z", Undo, Document.CanUndo),
            LibraryWindow.Item("_Redo", "Ctrl+Y", Redo, Document.CanRedo)));
        menu.Items.Add(LibraryWindow.MenuOf("E_xam",
            LibraryWindow.Item("_Create Question…", "Ctrl+Shift+N", CreateQuestion),
            LibraryWindow.Item("_Preview Exam", "Ctrl+R", () => Preview(SessionMode.Exam)),
            LibraryWindow.Item("Preview _Practice", "Ctrl+Shift+R", () => Preview(SessionMode.Practice)),
            null,
            LibraryWindow.Item("_Install to Library", "Ctrl+Shift+E", Install)));
        menu.Items.Add(LibraryWindow.MenuOf("_Window", LibraryWindow.Item("_Library", "Ctrl+Shift+L", App.ShowLibrary)));
        return menu;
    }

    public void ShowIssuesPanel() { showIssues = true; RefreshChrome(); }

    void Undo() { Document.Undo(); Render(); }
    void Redo() { Document.Redo(); Render(); }

    Control Toolbar()
    {
        var search = new TextBox { Text = Search, Watermark = "Search questions, topics, ids", Width = 260 };
        search.TextChanged += (_, _) =>
        {
            Search = search.Text ?? "";
            if (Search.Length > 0 && Page != DesignerPage.Bank) { ShowPage(DesignerPage.Bank); return; }
            page?.Invalidate();
        };
        var errors = ErrorCount;
        return UI.Bar(
            [UI.Btn(UI.Label("plus", "Create Question"), CreateQuestion).Tip("Create a question step by step"),
             UI.Btn(UI.Label("play", "Preview Exam"), () => Preview(SessionMode.Exam)).Tip("Run the whole exam in the simulator"),
             UI.Btn(UI.Label("tray.down", "Install to Library"), Install).Tip("Validate and copy this exam into the player library"),
             UI.Btn(UI.Label(errors == 0 ? "seal.check" : "warning", errors == 0 ? "No Errors" : $"{errors} Error{(errors == 1 ? "" : "s")}", errors == 0 ? Palette.Ok : Palette.Warn),
                 () => { showIssues = !showIssues; RefreshChrome(); }).Tip("Show validation issues")],
            [search]).Pad(12, 8);
    }

    Control Sidebar()
    {
        Control NavRow(DesignerPage p, string icon, string title, int? count)
        {
            var on = p == Page;
            var color = on ? Brushes.White : Palette.Text;
            return new Border
            {
                Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), Background = on ? Palette.Accent : Brushes.Transparent,
                Child = UI.Columns("Auto,*,Auto", Icons.Make(icon, 15, color).Pad(0, 0, 8, 0), UI.Txt(title, 13, color: color, wrap: false),
                    count is { } n ? UI.Txt($"{n}", 12.5, color: on ? Brushes.White : Palette.Secondary) : null),
            }.OnTap(() => ShowPage(p));
        }
        var exam = Document.Exam;
        var previewMenu = new MenuFlyout();
        foreach (var (label, mode, style, full) in new[]
        {
            ("Practice", SessionMode.Practice, ExamStyle.Modern, false),
            ("Exam simulation", SessionMode.Exam, ExamStyle.Modern, false),
            ("Exam simulation, classic screen", SessionMode.Exam, ExamStyle.Classic, false),
            ("Exam simulation, full screen", SessionMode.Exam, ExamStyle.Modern, true),
        })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => Preview(mode, null, style, full);
            previewMenu.Items.Add(item);
        }
        var previewRow = new Button
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(10, 6), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = UI.H(8, Icons.Make("play", 15), UI.Txt("Preview exam", wrap: false), Icons.Make("chevron.down", 10, Palette.Secondary)),
            Flyout = previewMenu,
        };
        var install = new Border { Padding = new Thickness(10, 6), Child = UI.H(8, Icons.Make("tray.down", 15), UI.Txt("Install to Library", wrap: false)) }.OnTap(Install);
        return UI.Scroll(UI.V(2,
            UI.Caption("Project").Pad(10, 4, 0, 4),
            NavRow(DesignerPage.Overview, "grid", "Overview", null),
            NavRow(DesignerPage.Bank, "list", "Question bank", Document.Questions.Count),
            NavRow(DesignerPage.Sections, "list.number", "Sections", exam.Sections?.Count ?? 0),
            NavRow(DesignerPage.CaseStudies, "briefcase", "Case studies", exam.CaseStudies?.Count ?? 0),
            NavRow(DesignerPage.Assets, "photo", "Assets", Document.Assets.Count),
            NavRow(DesignerPage.Settings, "gear", "Exam settings", null),
            UI.Caption("Publish").Pad(10, 16, 0, 4),
            previewRow,
            install).Pad(8, 8));
    }

    Control IssuesView()
    {
        var list = new StackPanel { Spacing = 4, Margin = new Thickness(12) };
        list.Children.Add(UI.Headline("Issues").Pad(0, 0, 0, 6));
        if (Issues.Count == 0)
            list.Children.Add(UI.H(6, Icons.Make("seal.check", 16, Palette.Green), UI.Txt("No issues. Ready to preview and install.", color: Palette.Green)));
        foreach (var issue in Issues)
        {
            var err = issue.Severity == Severity.Error;
            list.Children.Add(new Border
            {
                Padding = new Thickness(6), CornerRadius = new CornerRadius(6), Background = Brushes.Transparent,
                Child = UI.Columns("Auto,*",
                    Icons.Make(err ? "octagon.x" : "warning", 15, err ? Palette.Red : Palette.Yellow).Pad(0, 1, 8, 0).Do(c => c.VerticalAlignment = VerticalAlignment.Top),
                    UI.V(2, UI.Txt(issue.Message), UI.Txt(issue.Path, 11.5, color: Palette.Secondary).Do(t => t.FontFamily = Fonts.Mono))),
            }.OnTap(() => Select(issue)));
        }
        return UI.Scroll(list);
    }

    /// <summary>Jumps to whatever an issue path points at.</summary>
    void Select(Issue issue)
    {
        var m = Regex.Match(issue.Path, @"^questions\[(\d+)\]");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var i) && i < Document.Questions.Count)
        {
            Search = "";
            BankSelection.Clear();
            BankSelection.Add(i);
            ShowPage(DesignerPage.Bank);
        }
        else if (issue.Path.StartsWith("caseStudies")) ShowPage(DesignerPage.CaseStudies);
        else if (issue.Path.StartsWith("sections")) ShowPage(DesignerPage.Sections);
        else if (issue.Path == "assets") ShowPage(DesignerPage.Assets);
        else if (issue.Path.StartsWith("topicWeights")) ShowPage(DesignerPage.Overview);
        else ShowPage(DesignerPage.Settings);
    }

    // MARK: Actions

    public void CreateQuestion() => CreateQuestionFlow.Show(this, index =>
    {
        BankSelection.Clear();
        BankSelection.Add(index);
        ShowPage(DesignerPage.Bank);
    });

    public async void Preview(SessionMode mode, int? only = null, ExamStyle style = ExamStyle.Modern, bool fullScreen = false)
    {
        var (exam, issues) = Document.Materialize();
        if (exam is null)
        {
            Issues = issues.ToList();
            showIssues = true;
            RefreshChrome();
            await Dialogs.Alert(this, "Fix the errors in the Issues panel before previewing.");
            return;
        }
        var options = new SessionOptions { Mode = only is null ? mode : SessionMode.Practice, ShuffleQuestions = only is null, ShuffleChoices = true };
        var title = $"Preview: {exam.Title}";
        if (only is { } i && Document.Questions.ElementAtOrDefault(i)?.Id is { } id)
        {
            options = options with { QuestionIds = new HashSet<string> { id } };
            title = $"Preview: {id}";
        }
        new PreviewWindow(new RunRequest(exam, options, fullScreen, style) { DoneLabel = "Close Preview", AllowsRetry = false }, title).Show();
    }

    public async void Install()
    {
        var (exam, issues) = Document.Materialize();
        if (exam?.AssetBase is not { } folder)
        {
            Issues = issues.ToList();
            showIssues = true;
            RefreshChrome();
            await Dialogs.Alert(this, "Fix the errors in the Issues panel before installing.");
            return;
        }
        try
        {
            var installed = LibraryService.Shared.Store.Install(folder);
            LibraryService.Shared.Reload(installed.Id);
            await Dialogs.Alert(this, $"Installed \"{installed.Title}\" ({installed.ExamQuestions.Count} questions) in the Library.");
        }
        catch (Exception e) when (e is ExamLoadException or IOException)
        {
            await Dialogs.Alert(this, e.Message);
        }
    }

    public async Task<bool> Save()
    {
        if (Document.Path is null) return await SaveAs();
        try { Document.Save(Document.Path); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Alert(this, e.Message, "Couldn't save the project");
            return false;
        }
    }

    public async Task<bool> SaveAs()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save exam project", SuggestedFileName = $"{Converter.Slug(ProjectName).NonEmptyOr("exam")}.mockiexam",
            DefaultExtension = "mockiexam", ShowOverwritePrompt = true,
            FileTypeChoices = [new FilePickerFileType("MockiExams project") { Patterns = ["*.mockiexam"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return false;
        if (!path.EndsWith(".mockiexam", StringComparison.OrdinalIgnoreCase)) path += ".mockiexam";
        // The picker may have created an empty file; a project is a folder.
        if (File.Exists(path) && new FileInfo(path).Length == 0) File.Delete(path);
        try { Document.Save(path); UpdateTitle(); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            await Dialogs.Alert(this, e.Message, "Couldn't save the project");
            return false;
        }
    }

    async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (closingConfirmed || !Document.IsDirty) return;
        e.Cancel = true;
        var choice = await Dialogs.Ask(this, $"Do you want to save the changes you made to \"{ProjectName}\"?", "Your changes will be lost if you don't save them.",
            new DialogButton("Cancel", IsCancel: true), new DialogButton("Don't Save", ButtonKind.Destructive), new DialogButton("Save", IsDefault: true));
        if (choice == "Don't Save" || (choice == "Save" && await Save()))
        {
            closingConfirmed = true;
            Close();
        }
    }

    protected override bool HandleKey(KeyEventArgs e) => page?.HandleKey(e) ?? false;
}

public static class StringExtensions
{
    public static string NonEmptyOr(this string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
}

/// <summary>Breadcrumbs, a title with optional info text and actions, then the page content.</summary>
public static class ConsolePage
{
    public static Control Make(string[] crumbs, string title, string? info, IEnumerable<Control?> actions, Control content)
    {
        var head = UI.V(6,
            UI.Caption(string.Join("  ›  ", crumbs.Append(title))).Do(t => { t.TextWrapping = TextWrapping.NoWrap; t.TextTrimming = TextTrimming.CharacterEllipsis; }),
            UI.Bar([UI.Txt(title, 21, FontWeight.SemiBold)], actions),
            info is null ? null : UI.Secondary(info, 12.5)).Pad(22, 16, 22, 12);
        return UI.Rows("Auto,*", head, content);
    }

    /// <summary>A bordered panel on the console background.</summary>
    public static Control Card(string? title, Control? trailing, params Control?[] content) => UI.Card(UI.V(10,
        title is null && trailing is null ? null : UI.Bar([title is null ? null : UI.Headline(title)], [trailing]),
        UI.V(10, content)), Palette.ConsolePanel, Palette.ConsoleLine, 8);
}

/// <summary>A page with a title header and one editor below.</summary>
public sealed class SimplePage(DesignerWindow window, string title, string? info, Func<Control> content) : Screen
{
    protected override Control Build() => ConsolePage.Make([window.Document.DisplayName], title, info, [], content());

    public override bool HandleKey(KeyEventArgs e) => false;
}

/// <summary>Preview runs in their own windows so the project stays editable alongside.</summary>
public sealed class PreviewWindow : AppWindow
{
    readonly RunRequest request;
    RunView? run;

    public PreviewWindow(RunRequest request, string title)
    {
        this.request = request;
        Title = title;
        Width = 1200;
        Height = 800;
        MinWidth = 820;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Render();
    }

    protected override Control BuildPage() => run ??= new RunView(request, _ => { }, _ => { }, Close);

    protected override bool HandleKey(KeyEventArgs e) => run?.HandleKey(e) ?? false;
}
