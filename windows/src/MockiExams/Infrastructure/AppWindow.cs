using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MockiExams;

/// <summary>Every app window: a modal layer for sheets, key routing to the current screen, and a full
/// redraw when the appearance changes.</summary>
public abstract class AppWindow : Window
{
    protected readonly ModalHost Host = new();

    protected AppWindow()
    {
        Content = Host;
        Background = Palette.Window;
        if (App.WindowIcon is { } icon) Icon = icon;
        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        App.Windows.Add(this);
        Closed += (_, _) => App.Windows.Remove(this);
    }

    /// <summary>Rebuilds the whole page.</summary>
    public void Render()
    {
        Background = Palette.Window;
        Host.Page = BuildPage();
    }

    protected abstract Control BuildPage();

    /// <summary>Keys the focused control doesn't own, for the current screen. Return true if handled.</summary>
    protected virtual bool HandleKey(KeyEventArgs e) => false;

    void OnKey(object? sender, KeyEventArgs e)
    {
        if (Host.HasModal)
        {
            if (Host.HandleKey(e)) e.Handled = true;
            return;
        }
        var focused = FocusManager?.GetFocusedElement();
        var bare = e.KeyModifiers == KeyModifiers.None;
        // Open dropdowns and menus live in popups; their keys are theirs.
        if (e.Source is Visual source && TopLevel.GetTopLevel(source) != this) return;
        if (focused is ComboBoxItem or ListBoxItem or MenuItem) return;
        switch (focused)
        {
            // Typing belongs to the text box; only Return (single-line) and Esc pass through.
            case TextBox { AcceptsReturn: true }: return;
            case TextBox when !(bare && e.Key is Key.Enter or Key.Escape): return;
            case ComboBox or DataGrid or ListBox or MenuItem or Slider or NumericUpDown: return;
        }
        if (e.Key == Key.OemComma && e.KeyModifiers == KeyModifiers.Control) { SettingsSheet.Show(this); e.Handled = true; return; }
        if (HandleKey(e)) e.Handled = true;
    }
}

public static class SettingsSheet
{
    /// <summary>MockiExams Settings: appearance, exam screen style, full screen.</summary>
    public static void Show(Visual owner)
    {
        var host = ModalHost.Of(owner);
        if (host is null) return;
        var s = AppSettings.Shared;
        Sheet? sheet = null;
        var body = new ContentControl();
        void Fill() => body.Content = UI.V(14,
            UI.Txt("Settings", 17, FontWeight.Bold),
            UI.FormSection(null, [
                UI.LabeledRow("Appearance", UI.Segmented([(Appearance.System, "System"), (Appearance.Light, "Light"), (Appearance.Dark, "Dark")], s.Appearance,
                    a =>
                    {
                        s.Appearance = a;
                        s.Save();
                        App.ApplyAppearance();
                        // Redraw the sheet itself in the new colors.
                        sheet?.Close();
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(owner), Avalonia.Threading.DispatcherPriority.Background);
                    }, 70)),
                UI.LabeledRow("Exam screen", UI.Picker(ExamStyles.Options, s.ExamStyle, v => { s.ExamStyle = v; s.Save(); }, 220)),
                UI.LabeledRow("Run exam simulations in full screen", UI.Toggle("", s.FullScreenExams, on => { s.FullScreenExams = on; s.Save(); })),
            ]),
            UI.Btn("Done", () => sheet?.Close(), ButtonKind.Prominent).Do(b => b.HorizontalAlignment = HorizontalAlignment.Right)).Pad(22, 20);
        Fill();
        sheet = host.Present(body, 500, double.NaN, () => sheet?.Close());
        sheet.OnReturn = () => sheet.Close();
    }
}

public static class AboutSheet
{
    public static void Show(Visual owner)
    {
        var host = ModalHost.Of(owner);
        if (host is null) return;
        Sheet? sheet = null;
        var version = typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}" : "1.0";
        var link = UI.Btn("github.com/lostinthekernel/mockiexams", () => Markdown.Open("https://github.com/lostinthekernel/mockiexams"), ButtonKind.Link);
        var body = UI.V(10,
            App.WindowBitmap is { } bmp ? new Image { Source = bmp, Width = 72, Height = 72 }.Center() : null,
            UI.Txt("MockiExams", 20, FontWeight.Bold).Center(),
            UI.Secondary($"Version {version}").Center(),
            UI.Secondary("A free, open-source exam simulator and designer.").Center(),
            link.Center(),
            UI.Btn("OK", () => sheet?.Close(), ButtonKind.Prominent).Center().Pad(0, 6, 0, 0)).Pad(28, 24);
        sheet = host.Present(body, 380, double.NaN, () => sheet?.Close());
        sheet.OnReturn = () => sheet.Close();
    }
}
