using SnapBar.Core;

namespace SnapBar.Windows;

public interface IPathRevealSession : IDisposable
{
    Task<string> PrepareAsync(string path, CancellationToken cancellationToken);
    Task<string> RevealAsync(TargetIdentity target, CancellationToken cancellationToken);
}

public sealed class PathRevealSession(string executable) : IPathRevealSession
{
    private readonly ProbeClient worker = new(executable, pathWorker: true);
    public async Task<string> PrepareAsync(string path, CancellationToken cancellationToken) =>
        Status(await worker.RequestAsync(new("prepare-path", Path: path), cancellationToken: cancellationToken));
    public async Task<string> RevealAsync(TargetIdentity target, CancellationToken cancellationToken) =>
        Status(await worker.RequestAsync(new("reveal-path", Target: target), cancellationToken: cancellationToken));
    private static string Status(ProbeResponse response) => response.Status == "timeout" ? "path-timeout" : response.Status;
    public void Dispose() => worker.Dispose();
}
