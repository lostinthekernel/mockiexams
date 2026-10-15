using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MockiExams;

/// <summary>A place an in-app drag can drop onto. Set as a control's Tag.</summary>
public sealed record DropTarget(Action<string, Point> OnDrop, Action<bool> Highlight);

/// <summary>Dragging answer tiles inside the window, with the pointer alone. It never goes through the
/// system drag-and-drop service, so it behaves the same on every Windows setup (and under Wine).</summary>
public static class InAppDrag
{
    /// <summary>Drag with the mouse, or click (no movement) to run tap.</summary>
    public static void Attach(Control tile, string id, string label, Action? tap)
    {
        Point? start = null;
        Border? ghost = null;
        Control? hovered = null;
        Point grab = default;

        void SetHover(Control? zone)
        {
            if (zone == hovered) return;
            (hovered?.Tag as DropTarget)?.Highlight(false);
            hovered = zone;
            (hovered?.Tag as DropTarget)?.Highlight(true);
        }

        Control? ZoneAt(TopLevel top, Point p)
        {
            var hit = top.InputHitTest(p) as Visual;
            return hit?.GetSelfAndVisualAncestors().OfType<Control>().FirstOrDefault(c => c.Tag is DropTarget);
        }

        void End()
        {
            if (ghost?.Parent is Canvas layer) layer.Children.Remove(ghost);
            ghost = null;
            SetHover(null);
            start = null;
            tile.Opacity = 1;
        }

        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            start = e.GetPosition(tile);
            e.Pointer.Capture(tile);
            e.Handled = true;
        };
        tile.PointerMoved += (_, e) =>
        {
            if (start is not { } s || TopLevel.GetTopLevel(tile) is not { } top) return;
            var here = e.GetPosition(tile);
            if (ghost is null)
            {
                if (Math.Abs(here.X - s.X) + Math.Abs(here.Y - s.Y) < 5) return;
                if (ModalHost.Of(tile)?.DragLayer is not { } layer) return;
                grab = s;
                ghost = new Border
                {
                    Width = tile.Bounds.Width, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(7),
                    Background = Palette.Control, BorderBrush = Palette.Accent, BorderThickness = new Thickness(2), Opacity = 0.92,
                    BoxShadow = BoxShadows.Parse("0 6 18 0 #40000000"),
                    Child = UI.Columns("Auto,*", Icons.Make("line3", 13, Palette.Tertiary).Pad(0, 0, 8, 0), Markdown.Label(label)),
                };
                layer.Children.Add(ghost);
                tile.Opacity = 0.45;
            }
            var p = e.GetPosition(top);
            if (ghost.Parent is Visual parent && top.TranslatePoint(p, parent) is { } local)
            {
                Canvas.SetLeft(ghost, local.X - grab.X);
                Canvas.SetTop(ghost, local.Y - grab.Y);
            }
            SetHover(ZoneAt(top, p));
        };
        tile.PointerReleased += (_, e) =>
        {
            if (start is null) return;
            var dragged = ghost is not null;
            var zone = hovered;
            var top = TopLevel.GetTopLevel(tile);
            var point = top is not null ? e.GetPosition(top) : default;
            e.Pointer.Capture(null);
            End();
            e.Handled = true;
            if (!dragged) { if (e.InitialPressMouseButton == MouseButton.Left) tap?.Invoke(); return; }
            if (zone?.Tag is DropTarget target && top?.TranslatePoint(point, zone) is { } inZone) target.OnDrop(id, inZone);
        };
        tile.PointerCaptureLost += (_, _) => End();
    }
}
