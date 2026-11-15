using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BigBlueFish.Windows;
using SkiaSharp;

// The remote model path as macOS runs it (CompanionBrain with COMPANION_OCR and COMPANION_MAC, CompanionRemoteScreen)
// against a fake OpenAI-compatible server: API key, model name, image support from /v1/models and llama.cpp's /props,
// the connection test's messages, the window image (foreground only, JPEG, long side 1568), the text retry after a
// refused image, and that nothing of the desktop or the full screen is captured or read for the server.
// "live <url> [api-key] [model]" runs the connection test and one window-image turn against a real server instead.
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL " + name); checks++; Console.WriteLine("PASS " + name); }
CompanionBrain NewBrain() => new(_ => null, TimeSpan.FromSeconds(1), weatherClient: new HttpClient(), newsRoll: () => 1);

// A window capture as screencapture returns it: large, with transparent rounded corners and a line of text.
CompanionWindowImage Capture(int width, int height)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
    surface.Canvas.Clear(SKColors.Transparent);
    using var paint = new SKPaint { Color = new SKColor(250, 250, 245), IsAntialias = true };
    surface.Canvas.DrawRoundRect(new SKRect(0, 0, width, height), 60, 60, paint);
    using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, height / 12f);
    surface.Canvas.DrawText("Shopping list: milk, eggs, bread", width / 12f, height / 3f, font, ink);
    using var image = surface.Snapshot();
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), width, height);
}
LinuxCapture.Image = () => Capture(3000, 1800);

if (args.Length >= 2 && args[0] == "live")
{
    using var live = NewBrain();
    live.UseRemoteModel(args[1], args.Length > 2 ? args[2] : "", args.Length > 3 ? args[3] : "");
    Console.WriteLine("儲存並測試連線：" + await live.TestRemote());
    Console.WriteLine("VisionAvailable: " + live.VisionAvailable);
    live.ScreenImages = true;
    var watch = Stopwatch.StartNew();
    var reply = await live.Generate(false, true, CompanionNative.Foreground);
    Console.WriteLine($"Generate ({watch.ElapsedMilliseconds} ms, captures {LinuxCapture.Calls.Count}, OCR {CompanionOcr.Calls.Count}): {reply}");
    Console.WriteLine("Status: " + live.Status);
    return 0;
}

const string Key = "test-key", Vision = """{"modalities":{"vision":true,"audio":false}}""";
const string TextOnly = """{"object":"list","data":[{"id":"qwen","object":"model","owned_by":"llamacpp"}]}""";
using var server = new FakeServer { Key = Key, ModelsJson = TextOnly };
string Url = server.Url;
int captures = 0, ocrs = 0;
// What this step captured and read, and the chat requests it sent.
(List<(IntPtr Window, int Maximum, bool FullScreen)> Captures, List<(IntPtr Handle, bool FullScreen)> Ocr, List<FakeServer.Record> Chats) Step()
{
    var result = (LinuxCapture.Calls.Skip(captures).ToList(), CompanionOcr.Calls.Skip(ocrs).ToList(),
        server.Take().Where(r => r.Path == "/v1/chat/completions").ToList());
    captures = LinuxCapture.Calls.Count; ocrs = CompanionOcr.Calls.Count;
    return result;
}
static (string Model, string? Image, string Text) Chat(FakeServer.Record record)
{
    using var document = JsonDocument.Parse(record.Body);
    string? image = null; var text = new StringBuilder();
    foreach (var message in document.RootElement.GetProperty("messages").EnumerateArray())
    {
        var content = message.GetProperty("content");
        if (content.ValueKind == JsonValueKind.String) { text.AppendLine(content.GetString()); continue; }
        foreach (var part in content.EnumerateArray())
            if (part.GetProperty("type").GetString() == "image_url") image = part.GetProperty("image_url").GetProperty("url").GetString();
            else text.AppendLine(part.GetProperty("text").GetString());
    }
    return (document.RootElement.GetProperty("model").GetString()!, image, text.ToString());
}

// --- 儲存並測試連線: API key, address, model name and the image support it reports.
using (var brain = NewBrain())
{
    brain.UseRemoteModel(Url + "/v1/", "wrong-key", "");
    server.PropsJson = Vision;
    Check(await brain.TestRemote() == "遠端模型拒絕存取，請檢查 API Key", "a wrong key is reported by /props (older llama.cpp answers /v1/models without one)");
    var requests = server.Take();
    Check(requests.Any(r => r.Path == "/v1/models") && requests.All(r => !r.Path.StartsWith("/v1/v1")) && requests.All(r => r.Authorization == "Bearer wrong-key"),
        "a trailing /v1/ is dropped and every request carries the key as a Bearer token");
    server.PropsJson = null;
    brain.UseRemoteModel(Url, "wrong-key", "");
    Check(await brain.TestRemote() == "遠端模型拒絕存取，請檢查 API Key", "a wrong key is reported by the chat request when the server has no /props");
    server.Take();
    server.ModelsNeedKey = true;
    Check(await brain.TestRemote() == "遠端模型拒絕存取，請檢查 API Key" && server.Take().Count == 1, "a wrong key is reported by /v1/models (llama.cpp b10809 asks for it there)");
    server.ModelsNeedKey = false;

    brain.UseRemoteModel(Url, Key, "");
    Check(await brain.TestRemote() == "遠端模型就緒：qwen（僅文字）" && !brain.VisionAvailable, "a server without image input tests as text only");
    requests = server.Take();
    Check(requests.Count == 3 && requests.All(r => r.Authorization == "Bearer " + Key) && requests.Any(r => r.Path == "/props?model=qwen"),
        "the test reads /v1/models and /props?model=… and sends one chat request, all with the key");

    server.PropsJson = Vision;
    brain.UseRemoteModel(Url, Key, "");
    Check(await brain.TestRemote() == "遠端模型就緒：qwen（支援圖片）" && brain.VisionAvailable, "llama.cpp /props modalities.vision counts as image input");
    var probe = server.Take().Where(r => r.Path == "/v1/chat/completions").Select(Chat).ToList();
    Check(probe.Count == 2 && probe[0].Image == null && probe[1].Image?.StartsWith("data:image/png;base64,iVBORw0KGgo") == true,
        "with image input the test also sends the blank test image, nothing from the screen");

    server.PropsJson = null;
    server.ModelsJson = """{"models":[{"name":"qwen","model":"qwen","capabilities":["completion","multimodal"]}],"object":"list","data":[{"id":"qwen","object":"model"}]}""";
    brain.UseRemoteModel(Url, Key, "");
    Check(await brain.TestRemote() == "遠端模型就緒：qwen（支援圖片）" && !server.Take().Any(r => r.Path.StartsWith("/props")),
        "llama.cpp models[].capabilities multimodal counts as image input, without asking /props");
    server.ModelsJson = """{"data":[{"id":"qwen","architecture":{"input_modalities":["text","image"]}}]}""";
    brain.UseRemoteModel(Url, Key, "");
    Check(await brain.TestRemote() == "遠端模型就緒：qwen（支援圖片）", "data[].architecture.input_modalities image counts as image input");
    server.Take();

    server.ModelsJson = """{"data":[{"id":"alpha"},{"id":"beta"}]}""";
    foreach (var (name, expected) in new[] { ("", "alpha"), ("beta", "beta"), (" custom-q4 ", "custom-q4"), ("/models/Qwen3.5-4B-Q4_K_M.gguf", "/models/Qwen3.5-4B-Q4_K_M.gguf") })
    {
        brain.UseRemoteModel(Url, Key, name);
        var result = await brain.TestRemote();
        var models = server.Take().Where(r => r.Path == "/v1/chat/completions").Select(r => Chat(r).Model).Distinct().ToArray();
        Check(result.StartsWith("遠端模型就緒：" + Path.GetFileName(expected) + "（") && models.SequenceEqual([expected]), $"model name \"{name}\" sends model {expected}");
    }

    server.Key = "";
    brain.UseRemoteModel(Url, "", "");
    await brain.TestRemote();
    Check(server.Take().All(r => r.Authorization == null), "without an API key no Authorization header is sent");
    server.Key = Key;

    server.ModelsJson = TextOnly;
    server.ChatStatus = 500;
    brain.UseRemoteModel(Url, Key, "");
    Check(await brain.TestRemote() == "遠端模型回傳 HTTP 500", "a server error is shown with its HTTP status");
    server.ChatStatus = 200;
    server.Take();
    using var closed = new TcpListener(IPAddress.Loopback, 0);
    closed.Start(); int unused = ((IPEndPoint)closed.LocalEndpoint).Port; closed.Stop();
    brain.UseRemoteModel($"http://127.0.0.1:{unused}", Key, "");
    var refused = await brain.TestRemote();
    Check(refused == $"無法連線遠端模型：伺服器拒絕連線（127.0.0.1:{unused}），請確認伺服器正在執行，並檢查網址和連接埠。", "a refused connection is reported in Traditional Chinese, without the system's English text: " + refused);
}

// --- Speech turns: what reaches the server from the screen.
using (var brain = NewBrain())
{
    server.ModelsJson = TextOnly; server.PropsJson = Vision;
    brain.UseRemoteModel(Url, Key, "");
    brain.ScreenImages = true;
    var window = CompanionNative.Foreground;

    var reply = await brain.Generate(false, true, window);
    var (shots, read, chats) = Step();
    Check(reply == "主人，記得喝水喔～", "a window-image turn returns the streamed reply");
    Check(shots.SequenceEqual([(window, 1568, false)]) && read.Count == 0, "the image turn captures the foreground window only (long side 1568, no full screen) and runs no OCR");
    var sent = chats.Select(Chat).Single();
    Check(sent.Image?.StartsWith("data:image/jpeg;base64,") == true && sent.Model == "qwen", "the window image goes out as JPEG to the named model");
    var sentImage = sent.Image!;
    using (var jpeg = SKBitmap.Decode(Convert.FromBase64String(sentImage[(sentImage.IndexOf(',') + 1)..])))
    {
        Check(jpeg.Width == 1568 && jpeg.Height == 941, $"a 3000×1800 capture is sent as {jpeg.Width}×{jpeg.Height}");
        var corner = jpeg.GetPixel(2, 2);
        Check(corner.Red > 235 && corner.Green > 235 && corner.Blue > 235, "transparent window corners become white, not black");
    }
    Check(sent.Text.Contains("備忘錄") && sent.Text.Contains("購物清單") && !sent.Text.Contains(CompanionOcr.WindowText), "the image turn adds the window's app and title, no OCR text");

    reply = await brain.Generate(false, true, CompanionNative.Desktop);
    (shots, read, chats) = Step();
    sent = chats.Select(Chat).Single();
    Check(reply.Length > 0 && shots.Count == 0 && read.Count == 0 && sent.Image == null && sent.Text.Contains("macOS 桌面"),
        "with the desktop in front nothing is captured or read: the server only hears that it is the desktop");
    // No foreground window at all (the pet itself in front): GetForegroundWindow answers IntPtr.Zero.
    CompanionNative.Foreground = IntPtr.Zero;
    await brain.Generate(false, true);
    CompanionNative.Foreground = window;
    (shots, read, _) = Step();
    Check(shots.Count == 0 && read.Count == 0, "no foreground window at all (IntPtr.Zero) counts as the desktop");

    brain.ScreenImages = false;
    await brain.Generate(false, true, window);
    (shots, read, chats) = Step();
    sent = chats.Select(Chat).Single();
    Check(shots.Count == 0 && read.SequenceEqual([(window, false)]) && sent.Image == null && sent.Text.Contains(CompanionOcr.WindowText),
        "without the image option the server gets the window's OCR text, read without the full-screen fallback");

    await brain.Generate(false, false, window);
    (shots, read, chats) = Step();
    Check(shots.Count == 0 && read.Count == 0 && Chat(chats.Single()).Text.Contains("使用者正在使用電腦。"), "with foreground reading off nothing of the screen is read");

    server.PropsJson = null;
    brain.UseRemoteModel(Url, Key, "");
    brain.ScreenImages = true;
    await brain.Generate(false, true, window);
    (shots, read, chats) = Step();
    Check(shots.Count == 0 && read.SequenceEqual([(window, false)]) && Chat(chats.Single()).Image == null,
        "a text-only server gets OCR text even with the image option ticked");
}

// --- A refused image: once more as text, then text until the settings change or the connection is tested.
using (var brain = NewBrain())
{
    server.ModelsJson = TextOnly; server.PropsJson = Vision; server.RejectImages = true;
    brain.UseRemoteModel(Url, Key, "");
    brain.ScreenImages = true;
    var window = CompanionNative.Foreground;
    var reply = await brain.Generate(false, true, window);
    var (shots, read, chats) = Step();
    var sent = chats.Select(Chat).ToList();
    Check(reply == "主人，記得喝水喔～" && sent.Count == 2 && sent[0].Image != null && sent[1].Image == null && sent[1].Text.Contains(CompanionOcr.WindowText),
        "a server refusing the image gets the same turn again with the window's OCR text");
    Check(shots.SequenceEqual([(window, 1568, false)]) && read.SequenceEqual([(window, false)]), "the retry reads the same window, without the full-screen fallback");
    Check(!brain.VisionAvailable, "after a refusal the server is treated as text only");
    await brain.Generate(false, true, window);
    (shots, read, chats) = Step();
    Check(shots.Count == 0 && read.Count == 1 && chats.Count == 1 && Chat(chats[0]).Image == null, "the next turn goes straight to OCR text");
    var result = await brain.TestRemote();
    Check(result == "遠端模型就緒：qwen（伺服器拒收測試圖片（HTTP 400），會改傳視窗文字）" && !brain.VisionAvailable, "the test reports the refused test image: " + result);
    Step();
    server.RejectImages = false;
    Check(await brain.TestRemote() == "遠端模型就緒：qwen（支援圖片）" && brain.VisionAvailable, "testing again after the server takes images turns images back on");
    brain.UseRemoteModel(Url, Key, "");
    Step();
}

// --- Never the desktop or the full screen for a remote server, over every step above.
Check(LinuxCapture.Calls.Count > 0 && LinuxCapture.Calls.All(c => !c.FullScreen && !CompanionNative.IsDesktopWindow(c.Window) && c.Maximum == 1568),
    $"all {LinuxCapture.Calls.Count} captures were of the foreground window, none of the desktop or the full screen");
Check(CompanionOcr.Calls.Count > 0 && CompanionOcr.Calls.All(c => !c.FullScreen && !CompanionNative.IsDesktopWindow(c.Handle)),
    $"all {CompanionOcr.Calls.Count} OCR reads kept to the foreground window");

// Size and time for a busier window (text lines beside a photo with fine detail); informational only.
CompanionWindowImage Busy()
{
    using var bitmap = new SKBitmap(1568, 1000);
    var noise = new Random(7);
    for (int y = 0; y < 1000; y++)
        for (int x = 0; x < 1568; x++)
            bitmap.SetPixel(x, y, x < 900 ? SKColors.White : new SKColor((byte)(x / 7 + noise.Next(24)), (byte)(y / 5 + noise.Next(24)), (byte)(140 + noise.Next(24))));
    using var canvas = new SKCanvas(bitmap);
    using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
    using var font = new SKFont(SKTypeface.Default, 22);
    for (int line = 0; line < 30; line++) canvas.DrawText($"{line + 1}. The quick brown fox jumps over the lazy dog, line {line + 1}", 30, 40 + line * 31, font, ink);
    using var image = SKImage.FromBitmap(bitmap);
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return new("data:image/png;base64," + Convert.ToBase64String(png.ToArray()), 1568, 1000);
}
var big = Busy();
var clock = Stopwatch.StartNew();
for (int i = 0; i < 5; i++) CompanionRemoteScreen.ToJpeg(big, 1568);
Console.WriteLine($"INFO PNG to JPEG of a busy 1568×1000 window: {clock.Elapsed.TotalMilliseconds / 5:0} ms, {big.DataUrl.Length / 1024} KiB PNG -> {CompanionRemoteScreen.ToJpeg(big, 1568).DataUrl.Length / 1024} KiB JPEG (Base64)");
Console.WriteLine($"All {checks} remote model checks passed.");
return 0;

// An OpenAI-compatible server as llama.cpp's llama-server answers with --api-key: /props and /v1/chat/completions only
// with the key, /v1/models without it as older builds do or with it as b10809 does (ModelsNeedKey); optionally refusing
// images like a server started without --mmproj (b10809 answers those with HTTP 500, this one with 400).
sealed class FakeServer : IDisposable
{
    internal sealed record Record(string Method, string Path, string? Authorization, string Body);
    readonly HttpListener listener = new();
    readonly List<Record> requests = [];
    internal string Url { get; }
    internal string Key = "";
    internal string ModelsJson = "";
    internal string? PropsJson;
    internal bool RejectImages, ModelsNeedKey;
    internal int ChatStatus = 200;
    internal string Reply = "主人，記得喝水喔～";

    internal FakeServer()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); int port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        Url = $"http://127.0.0.1:{port}";
        listener.Prefixes.Add(Url + "/");
        listener.Start();
        _ = Task.Run(Serve);
    }

    // The requests received since the last call.
    internal List<Record> Take() { lock (requests) { var taken = requests.ToList(); requests.Clear(); return taken; } }

    async Task Serve()
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); } catch { return; }
            try { await Handle(context); }
            catch (Exception error) { Console.Error.WriteLine("fake server: " + error.Message); try { context.Response.Abort(); } catch { } }
        }
    }

    async Task Handle(HttpListenerContext context)
    {
        var request = context.Request; var response = context.Response;
        string body;
        using (var reader = new StreamReader(request.InputStream, Encoding.UTF8)) body = await reader.ReadToEndAsync();
        string path = request.Url!.AbsolutePath;
        lock (requests) requests.Add(new(request.HttpMethod, request.Url.PathAndQuery, request.Headers["Authorization"], body));
        async Task Send(int status, string content, string type = "application/json")
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            response.StatusCode = status; response.ContentType = type; response.ContentLength64 = bytes.Length;
            await response.OutputStream.WriteAsync(bytes);
            response.Close();
        }
        if (path == "/v1/models" && !ModelsNeedKey) { await Send(200, ModelsJson); return; }
        // A server that is not llama.cpp has no /props at all, key or not.
        if (path == "/props" && PropsJson == null) { await Send(404, """{"error":{"code":404,"message":"File Not Found","type":"not_found_error"}}"""); return; }
        if (Key.Length > 0 && request.Headers["Authorization"] != "Bearer " + Key)
        {
            await Send(401, """{"error":{"code":401,"message":"Invalid API Key","type":"authentication_error"}}""");
            return;
        }
        if (path == "/v1/models") { await Send(200, ModelsJson); return; }
        if (path == "/props") { await Send(200, PropsJson!); return; }
        if (path != "/v1/chat/completions" || request.HttpMethod != "POST") { await Send(404, "{}"); return; }
        if (RejectImages && body.Contains("\"image_url\""))
        {
            await Send(400, """{"error":{"code":400,"message":"image input is not supported - hint: if this is unexpected, you may need to provide the mmproj","type":"invalid_request_error"}}""");
            return;
        }
        if (ChatStatus != 200) { await Send(ChatStatus, "{}"); return; }
        using var json = JsonDocument.Parse(body);
        if (!(json.RootElement.TryGetProperty("stream", out var stream) && stream.ValueKind == JsonValueKind.True))
        {
            await Send(200, JsonSerializer.Serialize(new { choices = new[] { new { index = 0, message = new { role = "assistant", content = Reply } } } }));
            return;
        }
        var events = new StringBuilder();
        foreach (var piece in new[] { Reply[..(Reply.Length / 2)], Reply[(Reply.Length / 2)..] })
            events.Append("data: ").Append(JsonSerializer.Serialize(new { choices = new[] { new { index = 0, delta = new { content = piece } } } })).Append("\n\n");
        events.Append("data: [DONE]\n\n");
        await Send(200, events.ToString(), "text/event-stream");
    }

    public void Dispose() { listener.Stop(); listener.Close(); }
}
