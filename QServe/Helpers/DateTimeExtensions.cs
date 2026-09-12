namespace QServe.Helpers;

public static class DateTimeExtensions
{
    private static readonly TimeZoneInfo IndianTimeZone;

    static DateTimeExtensions()
    {
        try
        {
            IndianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }
        catch
        {
            try
            {
                IndianTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
            }
            catch
            {
                IndianTimeZone = TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5), "India Standard Time", "India Standard Time");
            }
        }
    }

    public static DateTime ToIst(this DateTime utcDateTime)
    {
        if (utcDateTime.Kind == DateTimeKind.Unspecified)
            utcDateTime = DateTime.SpecifyKind(utcDateTime, DateTimeKind.Utc);
        else if (utcDateTime.Kind == DateTimeKind.Local)
            utcDateTime = utcDateTime.ToUniversalTime();

        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, IndianTimeZone);
    }

    public static string ToIstString(this DateTime utcDateTime, string format = "MMM d, yyyy h:mm tt")
    {
        return utcDateTime.ToIst().ToString(format) + " IST";
    }

    public static string ToIstTimeString(this DateTime utcDateTime, string format = "h:mm tt")
    {
        return utcDateTime.ToIst().ToString(format) + " IST";
    }
}
