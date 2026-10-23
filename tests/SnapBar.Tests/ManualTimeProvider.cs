internal sealed class ManualTimeProvider : TimeProvider
{
    private long ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => ticks;
    public void Advance(TimeSpan duration) => ticks += duration.Ticks;
    public Task Wait(TimeSpan duration, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Advance(duration);
        return Task.CompletedTask;
    }
}
