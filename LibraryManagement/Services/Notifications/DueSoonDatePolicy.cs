namespace LibraryManagement.Services;

public readonly record struct DueSoonDayWindow(DateTime StartInclusive, DateTime EndExclusive);

public static class DueSoonDatePolicy
{
    public const int ReminderDaysBeforeDue = 2;

    public static DateTime GetTargetDate(DateTime localToday) => localToday.Date.AddDays(ReminderDaysBeforeDue);

    public static DueSoonDayWindow GetDayWindow(DateTime targetDate)
    {
        DateTime start = targetDate.Date;
        return new DueSoonDayWindow(start, start.AddDays(1));
    }
}
