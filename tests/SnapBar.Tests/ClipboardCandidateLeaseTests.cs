using SnapBar.Core;

internal static class ClipboardCandidateLeaseTests
{
    public static void Run(Action<string, Func<bool>> check)
    {
        check("candidate is valid at 30 seconds, expires one tick later", () =>
        {
            var clock = new ManualTimeProvider(); var lease = new ClipboardCandidateLease(clock);
            lease.Start("first"); clock.Advance(TimeSpan.FromSeconds(30));
            var boundary = lease.IsValid("first"); clock.Advance(TimeSpan.FromTicks(1));
            return boundary && !lease.IsValid("first") && !lease.TryExtend("first", 60);
        });
        check("extended script lease survives original deadline and expires at extended boundary", () =>
        {
            var clock = new ManualTimeProvider(); var lease = new ClipboardCandidateLease(clock);
            lease.Start("first"); clock.Advance(TimeSpan.FromSeconds(5));
            if (!lease.TryExtend("first", 60)) return false;
            clock.Advance(TimeSpan.FromSeconds(60)); var boundary = lease.IsValid("first");
            clock.Advance(TimeSpan.FromTicks(1)); return boundary && !lease.IsValid("first");
        });
        check("extension cannot shorten a lease or accept out of range durations", () =>
        {
            var clock = new ManualTimeProvider(); var lease = new ClipboardCandidateLease(clock);
            lease.Start("first");
            if (lease.TryExtend("first", 0) || lease.TryExtend("first", 311) || !lease.TryExtend("first", 310)) return false;
            lease.TryExtend("first", 1); clock.Advance(TimeSpan.FromSeconds(310));
            return lease.IsValid("first");
        });
        check("release shortens extended lease to 30 seconds from cleanup", () =>
        {
            var clock = new ManualTimeProvider(); var lease = new ClipboardCandidateLease(clock);
            lease.Start("first"); lease.TryExtend("first", 120); clock.Advance(TimeSpan.FromSeconds(35));
            if (!lease.TryRelease("first")) return false;
            clock.Advance(TimeSpan.FromSeconds(30)); var boundary = lease.IsValid("first");
            clock.Advance(TimeSpan.FromTicks(1)); return boundary && !lease.IsValid("first");
        });
        check("release cannot extend or revive an expired lease", () =>
        {
            var clock = new ManualTimeProvider(); var lease = new ClipboardCandidateLease(clock);
            lease.Start("first"); clock.Advance(TimeSpan.FromSeconds(31)); lease.TryRelease("first");
            return !lease.IsValid("first") && !lease.TryExtend("first", 60);
        });
        check("replacement and clear reject old candidate operations", () =>
        {
            var lease = new ClipboardCandidateLease(new ManualTimeProvider());
            if (lease.IsValid(null) || lease.TryRelease(null)) return false;
            lease.Start("first"); lease.Start("second");
            if (lease.IsValid("first") || lease.TryExtend("first", 60) || lease.TryRelease("first")) return false;
            lease.Clear(); return !lease.IsValid("second") && !lease.TryExtend("second", 60);
        });
    }
}
