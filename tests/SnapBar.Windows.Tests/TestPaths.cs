using System.IO;

internal static class TestPaths
{
    public static string Repository
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory != null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "SnapBar.slnx"))) return directory.FullName;
            throw new DirectoryNotFoundException("Run desktop tests from a SnapBar checkout.");
        }
    }

    public static string AppExecutable => Path.Combine(Repository, "src/SnapBar.App/bin/Release/net10.0-windows/SnapBar.App.exe");
    public static string ProbeExecutable => Path.Combine(Repository, "src/SnapBar.Probe/bin/Release/net10.0-windows/SnapBar.Probe.exe");
    public static string FixtureReadme => Path.Combine(Path.GetDirectoryName(AppExecutable)!, "README.md");
    public static string RunnerExecutable => Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
}
