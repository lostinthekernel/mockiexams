using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MockiExams;

/// <summary>The root of every window: the page, plus a layer for sheets and dialogs drawn on top,
/// so they work the same in full screen and under Wine.</summary>
public sealed class ModalHost : Grid
{
    readonly ContentControl page = new();
    readonly Panel overlay = new() { IsVisible = false };
    readonly List<Sheet> stack = [];

    /// <summary>Where in-app drags draw the tile that follows the pointer.</summary>
    public Canvas DragLayer { get; } = new() { IsHitTestVisible = false };

    public ModalHost()
    {
        Children.Add(page);
        Children.Add(DragLayer);
        Children.Add(overlay);
    }

    public Control? Page { get => page.Content as Control; set => page.Content = value; }
    public bool HasModal => stack.Count > 0;

    public static ModalHost? Of(Visual? v) => v is null ? null : (TopLevel.GetTopLevel(v) as Window)?.Content as ModalHost;

    public Sheet Present(Control content, double width = double.NaN, double height = double.NaN, Action? onEscape = null)
    {
        var sheet = new Sheet(this, content, width, height, onEscape);
        stack.Add(sheet);
        overlay.Children.Add(sheet.Layer);
        overlay.IsVisible = true;
        page.IsEnabled = false;
        foreach (var s in stack) s.Layer.IsHitTestVisible = s == sheet;
        sheet.FocusFirst();
        return sheet;
    }

    internal void Remove(Sheet sheet)
    {
        stack.Remove(sheet);
        overlay.Children.Remove(sheet.Layer);
        overlay.IsVisible = stack.Count > 0;
        page.IsEnabled = stack.Count == 0;
        if (stack.Count > 0) stack[^1].Layer.IsHitTestVisible = true;
    }

    /// <summary>Esc and Return for the topmost sheet. Returns true if a sheet handled the key.</summary>
    public bool HandleKey(KeyEventArgs e)
    {
        if (stack.Count == 0) return false;
        var top = stack[^1];
        if (top.OnKey?.Invoke(e) == true) return true;
        if (e.Key == Key.Escape && top.OnEscape is { } esc) { esc(); return true; }
        if (e.Key == Key.Enter && top.OnReturn is { } ret && FocusIsNotMultiline()) { ret(); return true; }
        return top.Content.IsKeyboardFocusWithin == false && e.Key is Key.Escape or Key.Enter;
    }

    bool FocusIsNotMultiline() =>
        TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is not TextBox { AcceptsReturn: true };
}

public sealed class Sheet
{
    readonly ModalHost host;
    public Control Content { get; }
    public Panel Layer { get; }
    public Action? OnEscape { get; set; }
    public Action? OnReturn { get; set; }
    /// <summary>Other keys for this sheet (e.g. zoom); return true when handled.</summary>
    public Func<KeyEventArgs, bool>? OnKey { get; set; }

    internal Sheet(ModalHost host, Control content, double width, double height, Action? onEscape)
    {
        this.host = host;
        Content = content;
        OnEscape = onEscape;
        var card = new Border
        {
            Child = content, Background = Palette.Window, CornerRadius = new CornerRadius(12),
            BorderBrush = Palette.ConsoleLine, BorderThickness = new Thickness(1),
            BoxShadow = BoxShadows.Parse("0 12 40 0 #55000000"), ClipToBounds = true,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Width = width, Height = height, MaxWidth = 1400, Margin = new Thickness(24),
        };
        Layer = new Panel { Background = new SolidColorBrush(Color.FromArgb(Palette.IsDark ? (byte)140 : (byte)90, 0, 0, 0)) };
        Layer.Children.Add(card);
    }

    public void Close() => host.Remove(this);

    public void FocusFirst() => Dispatcher(() =>
    {
        var first = Content.GetVisualDescendants().OfType<InputElement>().FirstOrDefault(c => c is TextBox && c.IsEffectivelyEnabled);
        (first ?? Content.GetVisualDescendants().OfType<Button>().LastOrDefault())?.Focus();
    });

    static void Dispatcher(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a, Avalonia.Threading.DispatcherPriority.Loaded);
}

public sealed record DialogButton(string Label, ButtonKind Kind = ButtonKind.Normal, bool IsCancel = false, bool IsDefault = false);

public static class Dialogs
{
    /// <summary>A confirmation dialog. Returns the chosen button's label, or null when dismissed.</summary>
    public static Task<string?> Ask(Visual owner, string title, string? message, params DialogButton[] buttons)
    {
        var host = ModalHost.Of(owner);
        var done = new TaskCompletionSource<string?>();
        if (host is null) { done.SetResult(null); return done.Task; }
        Sheet? sheet = null;
        void Finish(string? r) { sheet?.Close(); done.TrySetResult(r); }

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons)
        {
            var btn = UI.Btn(b.Label, () => Finish(b.Label), b.IsDefault && b.Kind == ButtonKind.Normal ? ButtonKind.Prominent : b.Kind);
            btn.MinWidth = 90;
            btn.HorizontalContentAlignment = HorizontalAlignment.Center;
            row.Children.Add(btn);
        }
        var body = UI.V(10,
            UI.Txt(title, 15, FontWeight.SemiBold),
            message is null ? null : UI.Txt(message, 13, color: Palette.Secondary),
            row.Pad(0, 8, 0, 0));
        body.Margin = new Thickness(22, 20);
        sheet = host.Present(body, 440, double.NaN, () => Finish(buttons.FirstOrDefault(b => b.IsCancel)?.Label));
        var def = buttons.FirstOrDefault(b => b.IsDefault);
        if (def is not null) sheet.OnReturn = () => Finish(def.Label);
        return done.Task;
    }

    public static Task Alert(Visual owner, string message, string? title = null) =>
        Ask(owner, title ?? message, title is null ? null : message, new DialogButton("OK", IsCancel: true, IsDefault: true));

    /// <summary>A text prompt. Returns the text, or null when cancelled.</summary>
    public static Task<string?> Prompt(Visual owner, string title, string? message, string initial = "", string placeholder = "", string ok = "Apply")
    {
        var host = ModalHost.Of(owner);
        var done = new TaskCompletionSource<string?>();
        if (host is null) { done.SetResult(null); return done.Task; }
        Sheet? sheet = null;
        var field = new TextBox { Text = initial, Watermark = placeholder };
        void Finish(string? r) { sheet?.Close(); done.TrySetResult(r); }
        var body = UI.V(10,
            UI.Txt(title, 15, FontWeight.SemiBold),
            message is null ? null : UI.Secondary(message),
            field,
            UI.H(8, UI.Btn("Cancel", () => Finish(null)), UI.Btn(ok, () => Finish(field.Text ?? ""), ButtonKind.Prominent)).Do(h => h.HorizontalAlignment = HorizontalAlignment.Right));
        body.Margin = new Thickness(22, 20);
        sheet = host.Present(body, 420, double.NaN, () => Finish(null));
        sheet.OnReturn = () => Finish(field.Text ?? "");
        return done.Task;
    }
}
