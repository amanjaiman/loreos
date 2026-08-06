namespace Lore.Agent.Lifecycle;

/// <summary>Where "today" starts.</summary>
public static class DayBoundary
{
    /// <summary>The most recent local midnight, as an absolute instant.</summary>
    /// <remarks>
    /// The day used to start at UTC midnight, which is defensible for a server and wrong for
    /// this. Lore runs on the user's own machine and the app says "today" meaning theirs — so at
    /// UTC-4 the counters reset at 8pm and the evening's captures appeared to vanish. The boundary
    /// follows the user's clock instead.
    ///
    /// The offset is read AT midnight rather than at the current moment, so a day containing a DST
    /// transition still starts where the user's clock says it does. Where local midnight does not
    /// exist at all (a spring-forward zone that skips it), <see cref="TimeZoneInfo.GetUtcOffset"/>
    /// yields the standard offset and the boundary lands an hour off for that one day — a rounding
    /// error in a display counter, and the alternative is a lot of machinery for it.
    /// </remarks>
    public static DateTimeOffset StartOfToday(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        TimeZoneInfo zone = time.LocalTimeZone;
        DateTime midnight = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).Date;
        return new DateTimeOffset(midnight, zone.GetUtcOffset(midnight));
    }
}
