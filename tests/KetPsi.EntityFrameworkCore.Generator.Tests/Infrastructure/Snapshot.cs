using System.Runtime.CompilerServices;

namespace KetPsi.EntityFrameworkCore.Generator.Tests.Infrastructure;

/// <summary>
/// Compares generated source with a committed <c>Snapshots/&lt;name&gt;.verified.cs</c> file next to the test.
/// Set <c>KETPSI_UPDATE_SNAPSHOTS=1</c> to write the actual output instead, then review the diff before committing.
/// </summary>
internal static class Snapshot
{
    public const string UpdateVariable = "KETPSI_UPDATE_SNAPSHOTS";

    private const string Directory = "Snapshots";
    private const string Extension = ".verified.cs";

    public static void Match(string actual, string name, [CallerFilePath] string callerFilePath = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFilePath)!, Directory);
        var path = Path.Combine(directory, name + Extension);
        var normalized = Normalize(actual);

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            System.IO.Directory.CreateDirectory(directory);
            File.WriteAllText(path, normalized);
            return;
        }

        Assert.True(File.Exists(path), $"The snapshot '{path}' does not exist. Run the tests with {UpdateVariable}=1 to create it.");
        Assert.Equal(Normalize(File.ReadAllText(path)), normalized);
    }

    // Git may check snapshots out with either line ending.
    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
