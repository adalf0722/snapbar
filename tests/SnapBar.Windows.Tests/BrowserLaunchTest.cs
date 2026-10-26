using System.Net;
using System.Net.Sockets;
using System.Text;
using SnapBar.Windows;

internal static class BrowserLaunchTest
{
    public static async Task<int> RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var destination = new Uri($"http://127.0.0.1:{port}/snapbar-web-smoke?value=fixture%20only");
        try
        {
            BrowserLauncher.Open(destination);
            using var client = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = client.GetStream();
            var buffer = new byte[8192]; int total = 0;
            while (total < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(total), timeout.Token);
                if (read == 0) break;
                total += read;
                if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n")) break;
            }
            var request = Encoding.ASCII.GetString(buffer, 0, total).Split("\r\n")[0];
            var body = Encoding.UTF8.GetBytes("<!doctype html><meta charset=utf-8><title>SnapBar Browser Smoke</title><h1>SnapBar browser handoff OK</h1><p>Local disposable test. No search data was sent to an external site.</p>");
            var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token);
            await stream.WriteAsync(body, timeout.Token);
            await stream.FlushAsync(timeout.Token);
            if (request != "GET /snapbar-web-smoke?value=fixture%20only HTTP/1.1")
                throw new InvalidOperationException("Browser request did not preserve the disposable test URL");
            Console.WriteLine("PASS default browser receives the exact HTTP loopback URL via system launcher");
            return 0;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        { Console.WriteLine("FAIL default browser loopback handoff (" + e.GetType().Name + ")"); return 1; }
    }
}
