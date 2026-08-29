using CodexHp.Core.Positioning;

namespace CodexHp.App.Presentation.Settings;

public static class SettingsWindowPlacementCalculator
{
    public static PhysicalRect Resolve(
        PhysicalRect workArea,
        double scaleX,
        double scaleY,
        double desiredWidthDip,
        double desiredHeightDip)
    {
        if (workArea.Width <= 0
            || workArea.Height <= 0
            || !double.IsFinite(scaleX)
            || !double.IsFinite(scaleY)
            || scaleX <= 0
            || scaleY <= 0
            || !double.IsFinite(desiredWidthDip)
            || !double.IsFinite(desiredHeightDip)
            || desiredWidthDip <= 0
            || desiredHeightDip <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workArea));
        }

        var desiredWidth = Math.Max(1, (int)Math.Round(
            desiredWidthDip * scaleX,
            MidpointRounding.AwayFromZero));
        var desiredHeight = Math.Max(1, (int)Math.Round(
            desiredHeightDip * scaleY,
            MidpointRounding.AwayFromZero));
        var width = Math.Min(desiredWidth, workArea.Width);
        var height = Math.Min(desiredHeight, workArea.Height);
        return new PhysicalRect(
            workArea.Left + ((workArea.Width - width) / 2),
            workArea.Top + ((workArea.Height - height) / 2),
            width,
            height);
    }
}
