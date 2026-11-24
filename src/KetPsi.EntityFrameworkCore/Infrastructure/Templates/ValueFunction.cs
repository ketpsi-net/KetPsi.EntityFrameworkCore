namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>The C# methods and members a <see cref="FunctionValueTemplate"/> can represent, written as EF Core writes them.</summary>
public enum ValueFunction
{
    /// <summary><c>s.ToUpper()</c>: one argument, the string.</summary>
    ToUpper,

    /// <summary><c>s.ToLower()</c>: one argument, the string.</summary>
    ToLower,

    /// <summary><c>s.Trim()</c>: one argument, the string.</summary>
    Trim,

    /// <summary><c>s.TrimStart()</c>: one argument, the string.</summary>
    TrimStart,

    /// <summary><c>s.TrimEnd()</c>: one argument, the string.</summary>
    TrimEnd,

    /// <summary><c>s.Length</c>: one argument, the string. SQL Server's <c>LEN</c> does not count trailing spaces, as in EF Core.</summary>
    Length,

    /// <summary><c>s.Substring(start)</c> or <c>s.Substring(start, length)</c>: the string, the zero-based start, and the length.</summary>
    Substring,

    /// <summary><c>s.Replace(oldValue, newValue)</c>: the string and the two strings.</summary>
    Replace,

    /// <summary><c>s.IndexOf(value)</c>: the string and the string or character looked for; -1 when it is missing.</summary>
    IndexOf,

    /// <summary><c>x.ToString()</c> of a string or an integer: one argument; a null integer is an empty string, as in C#.</summary>
    ToString,

    /// <summary><c>d.Year</c> of a date: one argument.</summary>
    Year,

    /// <summary><c>d.Month</c>: one argument.</summary>
    Month,

    /// <summary><c>d.Day</c>: one argument.</summary>
    Day,

    /// <summary><c>d.Hour</c> of a date or time: one argument.</summary>
    Hour,

    /// <summary><c>d.Minute</c>: one argument.</summary>
    Minute,

    /// <summary><c>d.Second</c>: one argument.</summary>
    Second,

    /// <summary><c>d.Millisecond</c>: one argument.</summary>
    Millisecond,

    /// <summary><c>d.DayOfYear</c>: one argument.</summary>
    DayOfYear,

    /// <summary><c>d.Date</c>, the date at midnight: one argument.</summary>
    Date,

    /// <summary><c>d.TimeOfDay</c>: one argument.</summary>
    TimeOfDay,

    /// <summary><c>d.AddYears(n)</c>: the date and the number.</summary>
    AddYears,

    /// <summary><c>d.AddMonths(n)</c>: the date and the number.</summary>
    AddMonths,

    /// <summary><c>d.AddDays(n)</c>: the date and the number, truncated to a whole number as EF Core does.</summary>
    AddDays,

    /// <summary><c>d.AddHours(n)</c>: the date or time and the number, truncated to a whole number.</summary>
    AddHours,

    /// <summary><c>d.AddMinutes(n)</c>: the date or time and the number, truncated to a whole number.</summary>
    AddMinutes,

    /// <summary><c>d.AddSeconds(n)</c>: the date or time and the number, truncated to a whole number.</summary>
    AddSeconds,

    /// <summary><c>d.AddMilliseconds(n)</c>: the date or time and the number, truncated to a whole number.</summary>
    AddMilliseconds,

    /// <summary><c>d.DayNumber</c> of a <see cref="DateOnly"/>: one argument.</summary>
    DayNumber,

    /// <summary><c>DateOnly.FromDateTime(d)</c>: one argument.</summary>
    DateFromDateTime,

    /// <summary><c>EF.Functions.DateDiffYear(start, end)</c>: the start and the end.</summary>
    DateDiffYear,

    /// <summary><c>EF.Functions.DateDiffMonth(start, end)</c>: the start and the end.</summary>
    DateDiffMonth,

    /// <summary><c>EF.Functions.DateDiffWeek(start, end)</c>: the start and the end.</summary>
    DateDiffWeek,

    /// <summary><c>EF.Functions.DateDiffDay(start, end)</c>: the start and the end.</summary>
    DateDiffDay,

    /// <summary><c>EF.Functions.DateDiffHour(start, end)</c>: the start and the end.</summary>
    DateDiffHour,

    /// <summary><c>EF.Functions.DateDiffMinute(start, end)</c>: the start and the end.</summary>
    DateDiffMinute,

    /// <summary><c>EF.Functions.DateDiffSecond(start, end)</c>: the start and the end.</summary>
    DateDiffSecond,

    /// <summary><c>EF.Functions.DateDiffMillisecond(start, end)</c>: the start and the end.</summary>
    DateDiffMillisecond,

    /// <summary><c>DateTime.Now</c>, the database's clock: no argument.</summary>
    Now,

    /// <summary><c>DateTime.UtcNow</c>, the database's clock: no argument.</summary>
    UtcNow,

    /// <summary><c>DateTime.Today</c>, the database's clock: no argument.</summary>
    Today,

    /// <summary><c>Math.Abs(x)</c>: one argument.</summary>
    Abs,

    /// <summary><c>Math.Round(x)</c> or <c>Math.Round(x, decimals)</c>: the value and the decimals. SQL rounds halves away from zero, as EF Core's SQL does.</summary>
    Round,

    /// <summary><c>Math.Truncate(x)</c>: one argument.</summary>
    Truncate,

    /// <summary><c>Math.Floor(x)</c>: one argument.</summary>
    Floor,

    /// <summary><c>Math.Ceiling(x)</c>: one argument.</summary>
    Ceiling,

    /// <summary><c>Math.Pow(x, y)</c>: the value and the power.</summary>
    Pow,

    /// <summary><c>Math.Sqrt(x)</c>: one argument.</summary>
    Sqrt,

    /// <summary><c>Math.Log(x)</c>, the natural logarithm: one argument.</summary>
    Log,

    /// <summary><c>Math.Log10(x)</c>: one argument.</summary>
    Log10,

    /// <summary><c>Math.Exp(x)</c>: one argument.</summary>
    Exp,

    /// <summary><c>Math.Sign(x)</c>: one argument; an <see cref="int"/>.</summary>
    Sign,

    /// <summary><c>Math.Sin(x)</c>: one argument.</summary>
    Sin,

    /// <summary><c>Math.Cos(x)</c>: one argument.</summary>
    Cos,

    /// <summary><c>Math.Tan(x)</c>: one argument.</summary>
    Tan,

    /// <summary><c>Math.Asin(x)</c>: one argument.</summary>
    Asin,

    /// <summary><c>Math.Acos(x)</c>: one argument.</summary>
    Acos,

    /// <summary><c>Math.Atan(x)</c>: one argument.</summary>
    Atan,

    /// <summary><c>Math.Atan2(y, x)</c>: y and x.</summary>
    Atan2,
}
