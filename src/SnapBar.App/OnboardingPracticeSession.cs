using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using SnapBar.Core;
using SnapBar.Windows;

namespace SnapBar.App;

internal sealed class OnboardingPracticeSession
{
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint handle, int command);
    private readonly CancellationTokenSource lifetime = new();
    private NamedPipeServerStream? pipe;
    private OnboardingChannel? channel;
    private Process? child;
    private bool stopped;
    private Task? reading;
    internal TargetIdentity? Target { get; private set; }
    internal bool Connected => !stopped && Target != null;
    internal event Func<OnboardingMessage, Task>? Message;
    internal event Action? Disconnected;

    internal async Task StartAsync(string? executable = null)
    {
        var name = "SnapBar.Onboarding." + Guid.NewGuid().ToString("N");
        pipe = new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(executable ?? Path.Combine(AppContext.BaseDirectory, "SnapBar.App.exe"))
            { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--onboarding-practice"); start.ArgumentList.Add(name);
        child = Process.Start(start) ?? throw new InvalidOperationException("Practice did not start");
        AllowSetForegroundWindow(child.Id);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        await pipe.WaitForConnectionAsync(deadline.Token);
        channel = new(pipe, fromChild: true);
        var ready = await channel.ReadAsync(deadline.Token);
        var identity = ready?.Kind == "ready" ? Native.GetIdentity((nint)ready.Window) : null;
        if (identity == null || child.HasExited || identity.ProcessId != child.Id
            || identity.ProcessStarted != child.StartTime.ToUniversalTime().Ticks)
            throw new InvalidDataException("Practice identity mismatch");
        Target = identity;
    }

    internal void Listen() => reading = ReadAsync();

    private async Task ReadAsync()
    {
        try
        {
            while (!lifetime.IsCancellationRequested && await channel!.ReadAsync(lifetime.Token) is { } message)
            {
                if (message.Kind == "ready") throw new InvalidDataException("Repeated ready");
                if (Message != null) await Message(message);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or System.Text.Json.JsonException
            or ObjectDisposedException or InvalidOperationException) { }
        finally { if (!stopped) Disconnected?.Invoke(); }
    }

    internal async Task SendAsync(OnboardingMessage message)
    {
        if (stopped || channel == null) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        await channel.SendAsync(message, deadline.Token);
    }

    internal void Activate()
    {
        if (Connected && Native.GetIdentity((nint)Target!.Window) == Target)
        {
            ShowWindow((nint)Target.Window, 9);
            Native.SetForegroundWindow((nint)Target.Window);
        }
    }

    internal async Task StopAsync()
    {
        if (stopped) return;
        stopped = true;
        try
        {
            if (channel != null)
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
                await channel.SendAsync(new(1, "close"), deadline.Token);
            }
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException) { }
        lifetime.Cancel(); channel?.Dispose(); pipe?.Dispose();
        if (child != null)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await child.WaitForExitAsync(deadline.Token);
            }
            catch (OperationCanceledException)
            {
                // The Process instance retains the original native handle; never kill by a rediscovered PID.
                try { if (!child.HasExited) child.Kill(); }
                catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
            catch (InvalidOperationException) { }
            finally { child.Dispose(); }
        }
    }
}
