using CodexHp.Core.Positioning;

namespace CodexHp.App.Presentation;

internal enum TooltipTailEdge { Top, Bottom, Left, Right }

internal sealed record OverlayTooltipPlacement(PhysicalRect Bounds, TooltipTailEdge TailEdge, double TailOffset)
{
    // All geometry here is physical pixels. Only the WPF view uses DIP.
    internal static OverlayTooltipPlacement? Calculate(
        PhysicalRect anchor, PhysicalRect workArea, int desiredWidth, int desiredHeight, double scale)
    {
        var gap = Math.Max(1, (int)Math.Ceiling(6 * scale));
        var area = new PhysicalRect(workArea.Left + gap, workArea.Top + gap,
            Math.Max(0, workArea.Width - 2 * gap), Math.Max(0, workArea.Height - 2 * gap));
        var width = Math.Min(desiredWidth, area.Width);
        var above = Math.Max(0, Math.Min(area.Bottom, anchor.Top - gap) - area.Top);
        var below = Math.Max(0, area.Bottom - Math.Max(area.Top, anchor.Bottom + gap));
        var minimumHeight = (int)Math.Ceiling(80 * scale);
        PhysicalRect bounds;
        TooltipTailEdge edge;
        if (above >= desiredHeight || below >= desiredHeight || Math.Max(above, below) >= minimumHeight)
        {
            var useAbove = above >= desiredHeight || (below < desiredHeight && above >= below);
            var height = Math.Min(desiredHeight, useAbove ? above : below);
            var left = Math.Clamp(anchor.Center.X - width / 2, area.Left, area.Right - width);
            bounds = new(left, useAbove ? Math.Min(area.Bottom, anchor.Top - gap) - height
                : Math.Max(area.Top, anchor.Bottom + gap), width, height);
            edge = useAbove ? TooltipTailEdge.Bottom : TooltipTailEdge.Top;
        }
        else
        {
            var leftSpace = Math.Max(0, Math.Min(area.Right, anchor.Left - gap) - area.Left);
            var rightSpace = Math.Max(0, area.Right - Math.Max(area.Left, anchor.Right + gap));
            var useLeft = leftSpace >= rightSpace;
            width = Math.Min(width, useLeft ? leftSpace : rightSpace);
            var height = Math.Min(desiredHeight, area.Height);
            if (width < 120 * scale || height < minimumHeight) return null;
            bounds = new(useLeft ? Math.Min(area.Right, anchor.Left - gap) - width
                : Math.Max(area.Left, anchor.Right + gap),
                Math.Clamp(anchor.Center.Y - height / 2, area.Top, area.Bottom - height), width, height);
            edge = useLeft ? TooltipTailEdge.Right : TooltipTailEdge.Left;
        }
        if (bounds.Width <= 0 || bounds.Height <= 0 || bounds.IntersectsWith(anchor)) return null;
        var horizontalEdge = edge is TooltipTailEdge.Top or TooltipTailEdge.Bottom;
        var length = horizontalEdge ? bounds.Width : bounds.Height;
        var offset = horizontalEdge ? anchor.Center.X - bounds.Left : anchor.Center.Y - bounds.Top;
        var inset = Math.Min(22 * scale, length / 2d);
        return new(bounds, edge, Math.Clamp(offset, inset, length - inset));
    }
}
