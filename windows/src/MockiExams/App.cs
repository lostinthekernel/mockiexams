using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Controls.Presenters;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace MockiExams;

public sealed class App : Application
{
    public static readonly List<AppWindow> Windows = [];
    public static WindowIcon? WindowIcon { get; private set; }
    public static Bitmap? WindowBitmap { get; private set; }
    static IClassicDesktopStyleApplicationLifetime? Desktop => Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Styles.Add(new StyleInclude(new Uri("avares://MockiExams/")) { Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml") });
        // Icon and link buttons stay borderless when disabled; they just fade.
        Styles.Add(new Style(x => x.OfType<Button>().Class("flat").Class(":disabled").Template().OfType<ContentPresenter>())
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Avalonia.Media.Brushes.Transparent) },
        });
        Styles.Add(new Style(x => x.OfType<Button>().Class("flat").Class(":disabled")) { Setters = { new Setter(Visual.OpacityProperty, 0.35) } });
        try
        {
            WindowBitmap = new Bitmap(AssetLoader.Open(new Uri("avares://MockiExams/Assets/AppIcon.png")));
            WindowIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://MockiExams/Assets/AppIcon.ico")));
        }
        catch (Exception) { }
        ApplyAppearance();
        ActualThemeVariantChanged += (_, _) => { foreach (var w in Windows.ToList()) w.Render(); };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (Desktop is { } desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
            var library = new LibraryWindow();
            desktop.MainWindow = library;
            // A project folder passed on the command line opens in the Designer.
            foreach (var arg in desktop.Args ?? [])
                if (Directory.Exists(arg)) OpenProjectAt(arg);
        }
        base.OnFrameworkInitializationCompleted();
    }

    public static void ApplyAppearance()
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = AppSettings.Shared.Appearance switch
        {
            Appearance.Light => ThemeVariant.Light,
            Appearance.Dark => ThemeVariant.Dark,
            _ => ThemeVariant.Default,
        };
    }

    public static void ShowLibrary()
    {
        var library = Windows.OfType<LibraryWindow>().FirstOrDefault();
        if (library is null)
        {
            library = new LibraryWindow();
            if (Desktop is { } d) d.MainWindow = library;
        }
        library.Show();
        library.Activate();
    }

    public static void NewProject() => Show(new DesignerWindow(new ProjectDocument()));

    public static async void OpenProject(Window owner)
    {
        var picked = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open a .mockiexam project folder", AllowMultiple = false,
        });
        if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path) OpenProjectAt(path, owner: owner);
    }

    /// <summary>Opens a project folder. An untitled copy (e.g. "Open in Designer") asks where to save.</summary>
    public static void OpenProjectAt(string path, bool untitledCopy = false, Window? owner = null)
    {
        var existing = Windows.OfType<DesignerWindow>().FirstOrDefault(w => w.Document.Path is { } p && string.Equals(Path.GetFullPath(p), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { existing.Activate(); return; }
        try
        {
            var doc = ProjectDocument.Load(path);
            if (untitledCopy) doc.Path = null;
            Show(new DesignerWindow(doc));
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            var target = owner ?? Windows.FirstOrDefault();
            if (target is not null) _ = Dialogs.Alert(target, e.Message, "Couldn't open the project");
        }
    }

    static void Show(Window w)
    {
        w.Show();
        w.Activate();
    }

    public static void Quit()
    {
        foreach (var w in Windows.ToList()) w.Close();
    }
}
