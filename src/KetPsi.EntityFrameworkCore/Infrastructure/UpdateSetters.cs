using System.ComponentModel;

namespace KetPsi.EntityFrameworkCore.Infrastructure;

/// <summary>
/// Records the <c>SetProperty</c> calls of an <c>ExecuteUpdate</c> whose setters are a delegate, as EF Core 10 and later
/// take them: the generated interceptor runs the delegate, and the generated interceptor of each <c>SetProperty</c> call
/// adds its setter instead of calling EF Core. Setters added in <c>if</c> statements or loops are recorded as they run.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class UpdateSetters
{
    [ThreadStatic]
    private static List<UpdateSetter>? t_recorded;

    /// <summary>Runs <paramref name="setters"/> on <paramref name="builder"/> and returns the setters it added, in order.</summary>
    /// <typeparam name="TBuilder">EF Core's builder of setters.</typeparam>
    /// <param name="setters">The delegate passed to <c>ExecuteUpdate</c>.</param>
    /// <param name="builder">The builder the delegate is given.</param>
    public static IReadOnlyList<UpdateSetter> Record<TBuilder>(Action<TBuilder> setters, TBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(setters);

        var outer = t_recorded;
        var recorded = new List<UpdateSetter>();
        t_recorded = recorded;
        try
        {
            setters(builder);
        }
        finally
        {
            t_recorded = outer;
        }

        return recorded;
    }

    /// <summary>
    /// Adds a setter to the <c>ExecuteUpdate</c> being recorded on this thread; <see langword="false"/> when none is, and
    /// the caller should call EF Core's own <c>SetProperty</c>.
    /// </summary>
    public static bool TryAdd(UpdateSetter setter)
    {
        ArgumentNullException.ThrowIfNull(setter);
        if (t_recorded is not { } recorded)
        {
            return false;
        }

        recorded.Add(setter);
        return true;
    }
}
