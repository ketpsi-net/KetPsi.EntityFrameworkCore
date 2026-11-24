namespace KetPsi.EntityFrameworkCore.Sql;

/// <summary>The SQL date and time functions the builder composes C#'s date members from.</summary>
public enum SqlDateFunction
{
    /// <summary>The year of a date, as an <see cref="int"/>: one argument, the date.</summary>
    Year,

    /// <summary>The month of a date: one argument, the date.</summary>
    Month,

    /// <summary>The day of the month of a date: one argument, the date.</summary>
    Day,

    /// <summary>The hour of a date or time: one argument, the date or time.</summary>
    Hour,

    /// <summary>The minute of a date or time: one argument, the date or time.</summary>
    Minute,

    /// <summary>The second of a date or time: one argument, the date or time.</summary>
    Second,

    /// <summary>The millisecond of a date or time: one argument, the date or time.</summary>
    Millisecond,

    /// <summary>The day of the year of a date: one argument, the date.</summary>
    DayOfYear,

    /// <summary>The date with years added: the number of years, then the date.</summary>
    AddYears,

    /// <summary>The date with months added, the day clamped to the month's last: the number of months, then the date.</summary>
    AddMonths,

    /// <summary>The date with days added: the number of days, then the date.</summary>
    AddDays,

    /// <summary>The date or time with hours added: the number of hours, then the date or time.</summary>
    AddHours,

    /// <summary>The date or time with minutes added: the number of minutes, then the date or time.</summary>
    AddMinutes,

    /// <summary>The date or time with seconds added: the number of seconds, then the date or time.</summary>
    AddSeconds,

    /// <summary>The date or time with milliseconds added: the number of milliseconds, then the date or time.</summary>
    AddMilliseconds,

    /// <summary>The number of year boundaries between two dates: the start, then the end.</summary>
    DiffYears,

    /// <summary>The number of month boundaries between two dates: the start, then the end.</summary>
    DiffMonths,

    /// <summary>The number of week boundaries between two dates: the start, then the end.</summary>
    DiffWeeks,

    /// <summary>The number of day boundaries between two dates: the start, then the end.</summary>
    DiffDays,

    /// <summary>The number of hour boundaries between two dates: the start, then the end.</summary>
    DiffHours,

    /// <summary>The number of minute boundaries between two dates: the start, then the end.</summary>
    DiffMinutes,

    /// <summary>The number of second boundaries between two dates: the start, then the end.</summary>
    DiffSeconds,

    /// <summary>The number of millisecond boundaries between two dates: the start, then the end.</summary>
    DiffMilliseconds,

    /// <summary>The date part of a date and time, at midnight: one argument.</summary>
    Date,

    /// <summary>The time part of a date and time: one argument.</summary>
    TimeOfDay,

    /// <summary>The date of a date and time, as a date without time: one argument.</summary>
    ToDate,

    /// <summary>The number of days since January 1 of year 1, as <c>DateOnly.DayNumber</c>: one argument.</summary>
    DayNumber,

    /// <summary>The current local date and time: no argument.</summary>
    Now,

    /// <summary>The current UTC date and time: no argument.</summary>
    UtcNow,

    /// <summary>The current local date at midnight: no argument.</summary>
    Today,
}
