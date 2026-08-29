namespace CodexHp.Core.Positioning;

public readonly record struct PhysicalPoint(int X, int Y);

public readonly record struct PhysicalRect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;

    public PhysicalPoint Center => new(Left + (Width / 2), Top + (Height / 2));

    public bool Contains(PhysicalPoint point) =>
        point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Contains(PhysicalRect rectangle) =>
        rectangle.Width > 0
        && rectangle.Height > 0
        && rectangle.Left >= Left
        && rectangle.Top >= Top
        && rectangle.Right <= Right
        && rectangle.Bottom <= Bottom;

    public bool IntersectsWith(PhysicalRect rectangle) =>
        Width > 0
        && Height > 0
        && rectangle.Width > 0
        && rectangle.Height > 0
        && rectangle.Left < Right
        && rectangle.Right > Left
        && rectangle.Top < Bottom
        && rectangle.Bottom > Top;
}

public sealed record MonitorGeometry(
    string Id,
    PhysicalRect Bounds,
    PhysicalRect WorkArea,
    double ScaleX,
    double ScaleY,
    bool IsPrimary,
    string? PersistentId = null);

public sealed record DisplayEnvironment(
    MonitorGeometry Monitor,
    PhysicalRect? TaskbarBounds);

public sealed record OverlayPlacement(
    string MonitorId,
    int PhysicalLeft,
    int PhysicalTop,
    int PhysicalWidth,
    int PhysicalHeight,
    int RelativeX,
    int RelativeY)
{
    public PhysicalRect Bounds => new(
        this.PhysicalLeft,
        this.PhysicalTop,
        this.PhysicalWidth,
        this.PhysicalHeight);
}
