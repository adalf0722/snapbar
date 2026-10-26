using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.Probe;

internal sealed class PathWorker : IDisposable
{
    private ShellPathSelection? prepared;

    public ProbeResponse Handle(ProbeRequest request)
    {
        if (request.Operation == "prepare-path")
        {
            Dispose();
            var candidate = PathActionPolicy.Resolve(request.Path);
            if (!candidate.Success) return new("invalid-path");
            try { prepared = new(candidate.Path!); return new("ok"); }
            catch (Exception e) when (e is not OutOfMemoryException) { return new("path-unavailable"); }
        }
        if (request.Operation == "reveal-path")
        {
            using var item = prepared;
            prepared = null; // A prepared item can be used at most once, including failures.
            if (item == null) return new("path-not-prepared");
            try { return new(item.Reveal(request.Target)); }
            catch (Exception e) when (e is not OutOfMemoryException) { return new("explorer-unavailable"); }
        }
        return new(request.Operation == "ping" ? "ok" : "invalid-request");
    }

    public void Dispose() { prepared?.Dispose(); prepared = null; }
}
