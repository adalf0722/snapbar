using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SnapBar.Core;
using SnapBar.Windows;

internal static class AiRuntimeTest
{
    private const string Responses = "{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"first\"},{\"type\":\"output_text\",\"text\":\" second\"}]},{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\" third\"}]}]}";
    private const string Chat = "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"chat result\"}}]}";
    private static readonly AiProviderProfile OpenAi = new("openai", "OpenAI", AiProtocol.Responses, "https://example.invalid/full/responses", "configured-model");
    private static readonly AiProviderProfile Compatible = new("compatible", "Local", AiProtocol.ChatCompletions, "http://localhost:11434/full/chat", "local-model");

    public static async Task<int> RunAsync()
    {
        int passed = 0, failed = 0;
        async Task Check(string name, Func<Task<bool>> test)
        {
            try { if (!await test()) throw new Exception("Assertion failed"); Console.WriteLine("PASS " + name); passed++; }
            catch (Exception) { Console.WriteLine("FAIL " + name); failed++; }
        }
        const string messagesBody = """{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"done"}]}""";
        var claude = AiProviderCatalog.Find("claude")!.Profile with { Model = "fixture-model" };
        foreach (var definition in AiProviderCatalog.Definitions)
        {
            var profile = definition.Profile with { Model = "fixture-model" };
            await Check("AI provider wire contract " + profile.Id, async () =>
            {
                var valid = false;
                using var http = new HttpClient(new Handler(async (request, token) =>
                {
                    using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
                    var root = json.RootElement;
                    valid = request.RequestUri!.AbsoluteUri == profile.Endpoint && root.GetProperty("model").GetString() == "fixture-model";
                    if (profile.Protocol == AiProtocol.Messages)
                        valid &= request.Headers.Authorization == null && request.Headers.GetValues("x-api-key").Single() == "fake-key"
                            && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01"
                            && root.GetProperty("system").GetString()!.EndsWith("shorten") && root.GetProperty("max_tokens").GetInt32() == 8192
                            && !root.GetProperty("stream").GetBoolean() && root.GetProperty("messages")[0].GetProperty("content").GetString() == "literal source";
                    else
                        valid &= request.Headers.Authorization?.Parameter == "fake-key" && (profile.Protocol == AiProtocol.Responses
                            ? !root.GetProperty("store").GetBoolean() && root.GetProperty("instructions").GetString()!.EndsWith("shorten") && root.GetProperty("input")[0].GetProperty("content").GetString() == "literal source"
                            : !root.GetProperty("stream").GetBoolean() && root.GetProperty("messages")[0].GetProperty("role").GetString() == "system" && root.GetProperty("messages")[1].GetProperty("content").GetString() == "literal source");
                    return Response(profile.Protocol == AiProtocol.Messages ? messagesBody : profile.Protocol == AiProtocol.Responses ? Responses : Chat);
                }));
                using var client = new AiHttpClient(http);
                return (await client.ExecuteAsync(profile, "fake-key", "literal source", "shorten", default)).Success && valid;
            });
            await Check("AI provider empty key policy " + profile.Id, async () =>
            {
                var sent = 0;
                using var http = new HttpClient(new Handler((_, _) => { sent++; return Task.FromResult(Response(Chat)); }));
                using var client = new AiHttpClient(http);
                var result = await client.ExecuteAsync(profile, "", "source", "rewrite", default);
                return profile.Id == "compatible" ? result.Success && sent == 1 : result.Status == "key-required" && sent == 0;
            });
        }
        foreach (var endpoint in new[] { "https://example.invalid/chat", "http://127.0.0.1:11434/chat", "https://api.openai.com/v1/chat/completions" })
            await Check("AI compatible empty key endpoint " + endpoint, async () =>
            {
                var sent = 0;
                using var http = new HttpClient(new Handler((_, _) => { sent++; return Task.FromResult(Response(Chat)); }));
                using var client = new AiHttpClient(http);
                var result = await client.ExecuteAsync(Compatible with { Endpoint = endpoint }, "", "source", "rewrite", default);
                return endpoint.Contains("api.openai.com") ? result.Status == "key-required" && sent == 0 : result.Success && sent == 1;
            });
        foreach (var (body, status, text) in new[]
        {
            (messagesBody, "", "done"),
            (messagesBody.Replace("end_turn", "max_tokens"), "incomplete", ""),
            (messagesBody.Replace("end_turn", "refusal"), "refused", ""),
            (messagesBody.Replace("end_turn", "tool_use"), "invalid-response", ""),
            (messagesBody.Replace("end_turn", "unknown"), "invalid-response", ""),
            (messagesBody.Replace("\"message\"", "false"), "invalid-response", ""),
            ("{}", "invalid-response", ""),
            (messagesBody.Replace("\"end_turn\"", "42"), "invalid-response", ""),
            (messagesBody.Replace("\"content\":[{\"type\":\"text\",\"text\":\"done\"}]", "\"content\":null"), "invalid-response", ""),
            (messagesBody.Replace("\"text\":\"done\"", "\"missing\":\"done\""), "invalid-response", ""),
            (messagesBody.Replace("{\"type\":\"text\",\"text\":\"done\"}", "null"), "invalid-response", ""),
            (messagesBody.Replace("\"text\":\"done\"", "\"text\":42"), "invalid-response", ""),
            (messagesBody.Replace("\"text\",\"text\"", "\"tool_use\",\"text\""), "invalid-response", ""),
            (messagesBody.Replace("\"text\",\"text\"", "\"unknown\",\"text\""), "invalid-response", ""),
            (messagesBody.Replace("[{", "[{\"type\":\"thinking\",\"thinking\":\"hidden\"},{\"type\":\"redacted_thinking\"},{\"type\":\"text\",\"text\":\"first \"},{"), "", "first done"),
            (messagesBody.Replace("done", "  "), "invalid-response", "")
        })
            await Check("AI Messages final text " + status + " length " + body.Length, async () =>
            { var result = await Execute(claude, body); return status == "" ? result.Success && result.Text == text : result.Status == status; });
        var minimax = AiProviderCatalog.Find("minimax")!.Profile with { Model = "fixture-model" };
        foreach (var (content, expected) in new[] { ("final", "final"), (" <think>secret</think> <think>more</think> final", "final"), ("<think>open", ""), ("<think>hidden</think>  ", ""), ("normal <think>tag</think>", "normal <think>tag</think>") })
            await Check("AI MiniMax final text length " + content.Length, async () =>
            {
                var body = JsonSerializer.Serialize(new { choices = new[] { new { finish_reason = "stop", message = new { content, reasoning_content = "hidden" } } } });
                var result = await Execute(minimax, body);
                return expected == "" ? result.Status == "invalid-response" : result.Success && result.Text == expected;
            });
        await Check("AI other Chat preserves leading think tags", async () =>
            (await Execute(Compatible, Chat.Replace("chat result", "<think>literal</think> final"))).Text == "<think>literal</think> final");
        foreach (var profile in new[] { OpenAi, Compatible, claude })
        {
            var body = profile.Protocol == AiProtocol.Messages ? messagesBody : profile.Protocol == AiProtocol.Responses ? Responses : Chat;
            var value = profile.Protocol == AiProtocol.Messages ? "done" : profile.Protocol == AiProtocol.Responses ? "first" : "chat result";
            await Check("AI protocol output cap " + profile.Protocol, async () => (await Execute(profile, body.Replace(value, new string('x', 262145)))).Status == "too-large");
            await Check("AI protocol output limit accepts boundary " + profile.Protocol, async () =>
            {
                var size = 262144 - (profile.Protocol == AiProtocol.Responses ? " second third".Length : 0);
                var result = await Execute(profile, body.Replace(value, new string('x', size)));
                return result.Success && result.Text!.Length == 262144;
            });
            await Check("AI protocol body cap " + profile.Protocol, async () => (await Execute(profile, new string('x', 2 * 1024 * 1024 + 1))).Status == "too-large");
            await Check("AI protocol fake timeout " + profile.Protocol, async () =>
            {
                using var http = new HttpClient(new Handler((_, _) => throw new TaskCanceledException()));
                using var client = new AiHttpClient(http);
                return (await client.ExecuteAsync(profile, "fake", "source", "rewrite", default)).Status == "timeout";
            });
            await Check("AI protocol cancellation " + profile.Protocol, async () =>
            {
                using var cancellation = new CancellationTokenSource();
                using var http = new HttpClient(new Handler((_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(Response(body)); }));
                using var client = new AiHttpClient(http);
                try { await client.ExecuteAsync(profile, "fake", "source", "rewrite", cancellation.Token); return false; }
                catch (OperationCanceledException e) { return e.CancellationToken == cancellation.Token; }
            });
        }
        await Check("AI Responses serializes literal source with store false and full endpoint", async () =>
        {
            bool valid = false;
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var root = json.RootElement;
                valid = request.RequestUri!.AbsoluteUri == OpenAi.Endpoint && request.Headers.Authorization?.Parameter == "fake-key"
                    && root.GetProperty("model").GetString() == OpenAi.Model && !root.GetProperty("store").GetBoolean()
                    && root.GetProperty("input")[0].GetProperty("content").GetString() == "literal \"source\"\n中文"
                    && root.GetProperty("instructions").GetString()!.EndsWith("translate") && !root.TryGetProperty("tools", out _);
                return Response(Responses);
            }));
            using var client = new AiHttpClient(http);
            var result = await client.ExecuteAsync(OpenAi, "fake-key", "literal \"source\"\n中文", "translate", default);
            return valid && result.Success && result.Text == "first second third";
        });
        await Check("AI compatible Chat format separates system from source and permits no key", async () =>
        {
            bool valid = false;
            using var http = new HttpClient(new Handler(async (request, token) =>
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync()); var root = json.RootElement;
                var messages = root.GetProperty("messages");
                valid = request.RequestUri!.AbsoluteUri == Compatible.Endpoint && request.Headers.Authorization == null
                    && !root.GetProperty("stream").GetBoolean() && !root.TryGetProperty("input", out _)
                    && messages[0].GetProperty("role").GetString() == "system" && messages[0].GetProperty("content").GetString()!.EndsWith("shorten")
                    && messages[1].GetProperty("role").GetString() == "user" && messages[1].GetProperty("content").GetString() == "English source";
                return Response(Chat);
            }));
            using var client = new AiHttpClient(http);
            var result = await client.ExecuteAsync(Compatible, "", "English source", "shorten", default);
            return valid && result.Success && result.Text == "chat result";
        });
        foreach (var (code, status) in new[] { (401, "authentication"), (403, "authentication"), (429, "rate-limit"), (408, "timeout"), (504, "timeout"), (500, "network"), (302, "network") })
            await Check("AI HTTP fixed status " + code, async () => (await Execute(OpenAi, "sensitive ignored error body", (HttpStatusCode)code)).Status == status);
        foreach (var (body, status) in new[]
        {
            ("{", "invalid-response"), ("null", "invalid-response"), ("{}", "invalid-response"),
            ("{\"status\":\"incomplete\"}", "incomplete"), ("{\"status\":\"failed\"}", "invalid-response"),
            ("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"refusal\",\"refusal\":\"no\"}]}]}", "refused"),
            ("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":[{\"type\":\"output_text\",\"text\":\"  \"}]}]}", "invalid-response"),
            ("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"status\":\"incomplete\",\"content\":[]}]}", "incomplete"),
            ("{\"status\":\"completed\",\"output\":[{\"type\":\"message\",\"content\":null}]}", "invalid-response")
        })
            await Check("AI Responses rejects " + status + " body length " + body.Length, async () => (await Execute(OpenAi, body)).Status == status);
        foreach (var (body, status) in new[]
        {
            ("{\"choices\":[]}", "invalid-response"), (Chat.Replace("stop", "length"), "incomplete"), (Chat.Replace("stop", "content_filter"), "refused"),
            (Chat.Replace("stop", "tool_calls"), "invalid-response"), (Chat.Replace("\"chat result\"", "null"), "invalid-response"),
            (Chat.Replace("\"chat result\"", "[]"), "invalid-response"), (Chat.Replace("\"chat result\"", "\" \""), "invalid-response"),
            (Chat.Replace("\"content\":", "\"refusal\":\"no\",\"content\":"), "refused"),
            (Chat.Replace("\"content\":", "\"tool_calls\":[],\"content\":"), "invalid-response")
        })
            await Check("AI Chat rejects " + status + " body length " + body.Length, async () => (await Execute(Compatible, body)).Status == status);
        await Check("AI request rejects unset model missing key and oversized source before send", async () =>
        {
            int sent = 0;
            using var http = new HttpClient(new Handler((_, _) => { sent++; return Task.FromResult(Response(Responses)); }));
            using var client = new AiHttpClient(http);
            return (await client.ExecuteAsync(OpenAi with { Model = "" }, "fake", "source", "rewrite", default)).Status == "settings-required"
                && (await client.ExecuteAsync(OpenAi, "", "source", "rewrite", default)).Status == "key-required"
                && (await client.ExecuteAsync(OpenAi, "fake", new string('x', 262145), "rewrite", default)).Status == "too-large" && sent == 0;
        });
        await Check("AI timeout maps to fixed status without raw error text", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => throw new TaskCanceledException("sensitive endpoint")));
            using var client = new AiHttpClient(http);
            return (await client.ExecuteAsync(OpenAi, "fake", "source", "rewrite", default)).Status == "timeout";
        });
        await Check("AI network errors map to fixed status", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException("sensitive endpoint")));
            using var client = new AiHttpClient(http);
            return (await client.ExecuteAsync(OpenAi, "fake", "source", "rewrite", default)).Status == "network";
        });
        await Check("AI user cancellation propagates without endpoint text", async () =>
        {
            using var cancellation = new CancellationTokenSource();
            using var http = new HttpClient(new Handler((_, token) => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult(Response(Responses)); }));
            using var client = new AiHttpClient(http);
            try { await client.ExecuteAsync(OpenAi, "fake", "source", "rewrite", cancellation.Token); return false; }
            catch (OperationCanceledException e) { return e.CancellationToken == cancellation.Token && !e.Message.Contains("sensitive"); }
        });
        await Check("AI output cap rejects output below body cap", async () =>
            (await Execute(Compatible, Chat.Replace("chat result", new string('x', 262145)))).Status == "too-large");
        await Check("AI advertised body cap rejects without reading body", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) =>
            {
                var response = Response("{}"); response.Content.Headers.ContentLength = 2 * 1024 * 1024 + 1; return Task.FromResult(response);
            }));
            using var client = new AiHttpClient(http);
            return (await client.ExecuteAsync(OpenAi, "fake", "source", "rewrite", default)).Status == "too-large";
        });
        await Check("AI unknown-length streaming body cap rejects oversized data", async () =>
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StreamContent(new UnknownLengthStream(2 * 1024 * 1024 + 1)) })));
            using var client = new AiHttpClient(http);
            return (await client.ExecuteAsync(OpenAi, "fake", "source", "rewrite", default)).Status == "too-large";
        });
        await Check("AI secret DPAPI scope separates endpoint protocol id but excludes model", () => Task.FromResult(SecretChecks()));
        Console.WriteLine($"{passed} passed, {failed} failed"); return failed == 0 ? 0 : 1;
    }

    private static bool SecretChecks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "SnapBar.Ai.Secret." + Guid.NewGuid().ToString("N"));
        var store = new AiSecretStore(directory);
        try
        {
            if (store.Get(OpenAi) != null) return false;
            store.Set(OpenAi, "fake-secret-中文");
            var bytes = File.ReadAllBytes(Directory.GetFiles(directory).Single());
            if (Encoding.UTF8.GetString(bytes).Contains("fake-secret") || Encoding.Unicode.GetString(bytes).Contains("fake-secret")) return false;
            if (store.Get(OpenAi) != "fake-secret-中文" || store.Get(OpenAi with { Model = "other" }) != "fake-secret-中文"
                || store.Get(OpenAi with { Endpoint = "https://example.invalid/other/responses" }) != null
                || store.Get(OpenAi with { Protocol = AiProtocol.ChatCompletions }) != null
                || store.Get(OpenAi with { Id = "different" }) != null) return false;
            store.Set(OpenAi with { Endpoint = "https://EXAMPLE.invalid/full/responses" }, "canonical");
            if (store.Get(OpenAi) != "canonical" || Directory.GetFiles(directory).Length != 1) return false;
            store.Set(OpenAi, ""); if (store.Get(OpenAi) != null || Directory.GetFiles(directory).Length != 0) return false;
            store.Set(OpenAi, "new"); store.Set(OpenAi, null); if (Directory.GetFiles(directory).Length != 0) return false;
            store.Set(OpenAi, new string('x', 8192)); if (store.Get(OpenAi)?.Length != 8192) return false;
            var lockedPath = Directory.GetFiles(directory).Single(); var original = File.ReadAllBytes(lockedPath);
            using (var held = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { store.Set(OpenAi, "replacement"); return false; } catch (InvalidOperationException) { }
                if (!File.ReadAllBytes(lockedPath).SequenceEqual(original) || Directory.GetFiles(directory).Length != 1) return false;
            }
            try { store.Set(OpenAi, new string('x', 8193)); return false; } catch (ArgumentException) { }
            store.Set(OpenAi, "new"); var path = Directory.GetFiles(directory).Single(); File.WriteAllBytes(path, new byte[64 * 1024 + 1]);
            if (store.Get(OpenAi) != null) return false;
            File.WriteAllBytes(path, [1, 2, 3]); return store.Get(OpenAi) == null;
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static async Task<AiTextResult> Execute(AiProviderProfile profile, string body, HttpStatusCode code = HttpStatusCode.OK)
    {
        using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Response(body, code))));
        using var client = new AiHttpClient(http);
        return await client.ExecuteAsync(profile, "fake-key", "source", "rewrite", default);
    }
    private static HttpResponseMessage Response(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken); }
    private sealed class UnknownLengthStream(int remaining) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        { var size = Math.Min(remaining, count); Array.Fill(buffer, (byte)'x', offset, size); remaining -= size; return size; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
