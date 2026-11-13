using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure.Templates;

/// <summary>A method or member of a value, such as <c>x.Name.ToUpper()</c> or <c>x.Name.Length</c>.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class FunctionValueTemplate : ValueTemplate
{
    /// <summary>Creates the template.</summary>
    /// <param name="function">The function.</param>
    /// <param name="arguments">The value the function is called on, then its arguments, as <see cref="ValueFunction"/> lists them.</param>
    public FunctionValueTemplate(ValueFunction function, params ValueTemplate[] arguments)
        : base(MaxValueCount(arguments ?? []))
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!Enum.IsDefined(function))
        {
            throw new ArgumentOutOfRangeException(nameof(function), function, message: null);
        }

        var (min, max) = function switch
        {
            ValueFunction.Substring => (2, 3),
            ValueFunction.Replace => (3, 3),
            ValueFunction.IndexOf => (2, 2),
            _ => (1, 1),
        };

        if (arguments.Length < min || arguments.Length > max)
        {
            throw new ArgumentException(function + " takes " + (min == max ? min.ToString(System.Globalization.CultureInfo.InvariantCulture) : min + " or " + max) + " argument(s).", nameof(arguments));
        }

        foreach (var argument in arguments)
        {
            ArgumentNullException.ThrowIfNull(argument, nameof(arguments));
        }

        Function = function;
        Arguments = arguments;
    }

    /// <summary>The function.</summary>
    public ValueFunction Function { get; }

    /// <summary>The value the function is called on, then its arguments.</summary>
    public IReadOnlyList<ValueTemplate> Arguments { get; }
}
