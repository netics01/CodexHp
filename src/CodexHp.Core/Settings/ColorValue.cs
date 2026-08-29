using System.Globalization;

namespace CodexHp.Core.Settings;

public readonly record struct ColorValue(byte Red, byte Green, byte Blue)
{
    public static ColorValue Parse(string value)
    {
        if (!TryParse(value, out var color))
        {
            throw new FormatException($"Invalid RGB color value: {value}");
        }

        return color;
    }

    public static bool TryParse(string? value, out ColorValue color)
    {
        color = default;
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        if (!byte.TryParse(value.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red)
            || !byte.TryParse(value.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green)
            || !byte.TryParse(value.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            return false;
        }

        color = new ColorValue(red, green, blue);
        return true;
    }

    public string ToHex() => $"#{this.Red:X2}{this.Green:X2}{this.Blue:X2}";

    public override string ToString() => this.ToHex();
}
