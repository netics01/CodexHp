using System.Collections.Concurrent;
using System.Globalization;
using Windows.Globalization.DateTimeFormatting;

namespace CodexHp.App.Infrastructure;

internal static class WindowsTooltipDateFormatting
{
    private static readonly ConcurrentDictionary<string, DateTimeFormatter> MonthDayFormatters = new();

    internal static string FormatMonthDay(DateTimeOffset localTime)
    {
        var culture = CultureInfo.CurrentCulture;
        if (culture.Name.Length == 0)
            return localTime.ToString("M", culture);

        return MonthDayFormatters.GetOrAdd(culture.IetfLanguageTag,
            static language => new DateTimeFormatter("month.abbreviated day", [language])).Format(localTime);
    }
}
