using SnapBar.Core;

internal static class PathActionPolicyTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        foreach (var pair in new (string Input, string Expected)[]
        {
            (@"D:\中文 空白\a,b & 100%.txt", @"D:\中文 空白\a,b & 100%.txt"),
            (" \"D:\\資料\\file.txt\" ", @"D:\資料\file.txt"),
            ("D:/資料/one/../two.txt", @"D:\資料\two.txt"),
            (@"D:\", @"D:\"), (@"D:\folder\", @"D:\folder"),
            (@"\\server\share\中文 file.txt", @"\\server\share\中文 file.txt"),
            (@"~\folder\😀.txt", @"C:\Users\Fixture\folder\😀.txt"),
            (@"%userprofile%\folder", @"C:\Users\Fixture\folder"),
            ("~", @"C:\Users\Fixture"), ("%USERPROFILE%", @"C:\Users\Fixture")
        })
            check("path normalization accepts fixture: " + pair.Input, () =>
                PathActionPolicy.Resolve(pair.Input, @"C:\Users\Fixture").Path == pair.Expected);
        check("UNC share root is accepted", () => PathActionPolicy.Resolve(@"\\server\share").Success);
        check("nonexistent syntax is accepted without touching filesystem", () =>
            PathActionPolicy.Resolve(@"Z:\SnapBar-not-mounted\fixture.txt").Success);
        int invalidCase = 0;
        foreach (var input in new[]
        {
            "", " ", "relative.txt", @"C:relative.txt", @"\rooted.txt", "https://example.com/a",
            "file:///C:/test.txt", @"shell:Downloads", @"\\?\C:\test.txt", @"\\.\PhysicalDrive0",
            @"C:\file:stream", @"C:\file*", @"C:\file?", @"C:\bad|name", @"C:\bad<name",
            "C:\\bad\0name", "C:\\bad\nname", "C:\\bad\tname", "\"C:\\file.txt", "C:\\file.txt\"",
            @"%TEMP%\file.txt", @"C:\%APPDATA%\file.txt", "~other/file.txt", @"\\server",
            @"\\server\", @"\\\share\file", @"\\server\..\file", @"C:\CON", @"C:\nul.txt",
            @"C:\COM1.txt", @"C:\LPT².txt", @"C:\conin$", @"C:\trailing.\file", @"C:\trailing \file",
            "C:\\unpaired-\ud800.txt"
        })
            check("invalid path fixture is rejected: " + ++invalidCase, () =>
                !PathActionPolicy.Resolve(input, @"C:\Users\Fixture").Success);
        check("oversized path is rejected", () => !PathActionPolicy.Resolve(@"C:\" + new string('a', PathActionPolicy.MaximumLength)).Success);
    }
}
