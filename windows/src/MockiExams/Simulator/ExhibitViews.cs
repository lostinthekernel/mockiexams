using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MockiCore;

namespace MockiExams;

public static class ExhibitViews
{
    /// <summary>Text exhibits inline; image exhibits as cards that open a zoomable viewer.</summary>
    public static Control? List(Exam exam, IReadOnlyList<Exhibit> exhibits)
    {
        if (exhibits.Count == 0) return null;
        var v = new StackPanel { Spacing = 10 };
        foreach (var exhibit in exhibits)
        {
            if (exhibit.Text is { } text)
            {
                v.Children.Add(UI.V(4, UI.Label("doc.text", exhibit.Title, Palette.Secondary, 11.5), Markdown.CodeBox(text)));
                continue;
            }
            var path = exhibit.ImagePath is { } p ? exam.AssetPath(p) : null;
            var isPdf = path?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
            Control preview = isPdf
                ? UI.V(6, Icons.Make("doc", 40, Palette.Secondary).Center(), UI.Secondary("PDF document").Center()).Pad(0, 20)
                : UI.LoadBitmap(path) is { } bmp
                    ? new Image { Source = bmp, Stretch = Stretch.Uniform, MaxHeight = 220 }
                    : UI.Unavailable("Missing image", "photo", exhibit.ImagePath);
            var card = new Border
            {
                Padding = new Thickness(10), CornerRadius = new CornerRadius(10), Background = Palette.Control,
                BorderBrush = Palette.Quaternary, BorderThickness = new Thickness(1),
                Child = UI.V(6, preview, UI.Label("magnifyingglass", isPdf ? $"{exhibit.Title} — click to open" : $"{exhibit.Title} — click to enlarge", Palette.Secondary, 11.5)),
            };
            card.OnTap(() =>
            {
                if (isPdf) { if (path is not null) OpenExternally(path); }
                else ShowViewer(card, exam, exhibit);
            });
            v.Children.Add(card);
        }
        return v;
    }

    public static void OpenExternally(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); } catch (Exception) { }
    }

    /// <summary>Opens an exhibit full size, with zoom from 50% to 400%.</summary>
    public static void ShowViewer(Visual owner, Exam exam, Exhibit exhibit)
    {
        var host = ModalHost.Of(owner);
        if (host is null) return;
        if (exhibit.ImagePath is { } p && exam.AssetPath(p) is { } file && file.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            OpenExternally(file);
            return;
        }
        var zoom = 1.0;
        var bitmap = exhibit.ImagePath is { } ip ? UI.LoadBitmap(exam.AssetPath(ip)) : null;
        var percent = UI.Txt("100%", wrap: false).Width(48).Do(t => t.TextAlignment = TextAlignment.Center);
        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
        Control content = bitmap is not null
            ? new Image { Source = bitmap, Stretch = Stretch.Uniform }
            : new SelectableTextBlock { Text = exhibit.Text ?? "", FontFamily = Fonts.Mono, FontSize = 13, Margin = new Thickness(16), Foreground = Palette.Text };
        scroller.Content = content;

        void Apply()
        {
            percent.Text = $"{(int)Math.Round(zoom * 100)}%";
            if (content is Image img)
            {
                var vp = scroller.Bounds.Size;
                img.Width = Math.Max(100, vp.Width * zoom);
                img.Height = Math.Max(100, vp.Height * zoom);
            }
            else if (content is SelectableTextBlock t) t.FontSize = 13 * zoom;
        }
        void ZoomBy(double d) { zoom = Math.Clamp(zoom + d, 0.5, 4); Apply(); }
        scroller.SizeChanged += (_, _) => Apply();
        scroller.AddHandler(InputElement.PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            ZoomBy(e.Delta.Y > 0 ? 0.25 : -0.25);
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        Sheet? sheet = null;
        var header = UI.Bar(
            [UI.Headline(exhibit.Title)],
            [UI.IconBtn("zoom.out", () => ZoomBy(-0.25), "Zoom out (Ctrl+-)"), percent,
             UI.IconBtn("zoom.in", () => ZoomBy(0.25), "Zoom in (Ctrl+=)"), UI.Btn("Done", () => sheet?.Close())]);
        header.Margin = new Thickness(12);
        var body = UI.Rows("Auto,Auto,*", header, UI.Divider(), scroller);
        var top = TopLevel.GetTopLevel(owner)?.Bounds.Size ?? new Size(1200, 800);
        sheet = host.Present(body, Math.Min(1100, top.Width - 60), Math.Min(760, top.Height - 60), () => sheet?.Close());
        sheet.OnKey = e =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return false;
            if (e.Key is Key.OemMinus or Key.Subtract) { ZoomBy(-0.25); return true; }
            if (e.Key is Key.OemPlus or Key.Add) { ZoomBy(0.25); return true; }
            return false;
        };
    }

    /// <summary>The scenario panel shown beside case-study questions.</summary>
    public static Control CaseStudyPanel(Exam exam, CaseStudy caseStudy) => UI.Scroll(UI.V(14,
        UI.H(8, Icons.Make("briefcase", 18), UI.Txt(caseStudy.Title, 17, FontWeight.Bold)),
        Markdown.Rich(caseStudy.Scenario),
        List(exam, caseStudy.Exhibits)).Pad(16, 16));
}
