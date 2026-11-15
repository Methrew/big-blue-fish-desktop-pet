// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Text.Json;
using BigBlueFish.Agent;

// Files attached in the input box, through the actual ACP fixture: the user_message event keeps every path as text, each
// file follows as a resource_link block, and image files also as image blocks while the Agent takes images; an Agent
// without image input, or one that refuses the pictures at run time, still gets the links and paths.
static class AttachmentVerification
{
    public static async Task Run(AgentCoordinator host, string root, string samplePng, Func<int> said,
        Action<bool, string> check, Func<Func<bool>, string, int, Task> until)
    {
        string folder = Path.Combine(root, "附件 資料夾"); Directory.CreateDirectory(folder);
        string shotPath = Path.Combine(folder, "截圖 1.png"), notePath = Path.Combine(folder, "筆記#1.txt");
        File.WriteAllBytes(shotPath, Convert.FromBase64String(samplePng)); File.WriteAllText(notePath, "附件內容");
        AgentUserAttachment Attached(string path, string kind, string mime, string source) => new(path, Path.GetFileName(path), kind, mime, new FileInfo(path).Length, source);
        var shot = Attached(shotPath, "image", "image/png", "clipboard"); var note = Attached(notePath, "file", "text/plain", "drop");
        string workspace = host.Workspaces.Current;
        JsonDocument LastPrompt() => JsonDocument.Parse(File.ReadAllText(Path.Combine(workspace, "fixture-last-prompt.json")));
        JsonElement[] Blocks(JsonElement prompt, string type) => prompt.EnumerateArray().Where(b => b.GetProperty("type").GetString() == type).ToArray();
        async Task Send(string text, AgentUserAttachment[] files, string label)
        {
            int before = said();
            check(host.SendUser(text, files), label + " accepted");
            await until(() => said() == before + 1 && !host.Busy, label + " completes through say", 15);
        }

        check(shot.FileUri.StartsWith("file:///", StringComparison.Ordinal)
            && shot.FileUri.EndsWith("/%E9%99%84%E4%BB%B6%20%E8%B3%87%E6%96%99%E5%A4%BE/%E6%88%AA%E5%9C%96%201.png", StringComparison.Ordinal)
            && note.FileUri.EndsWith("/%E7%AD%86%E8%A8%98%231.txt", StringComparison.Ordinal)
            && new Uri(shot.FileUri).LocalPath == shotPath && new Uri(note.FileUri).LocalPath == notePath,
            "attachment file URLs percent-encode spaces, # and Chinese names and lead back to the same path");
        if (OperatingSystem.IsWindows())
            check(AgentUserAttachment.ToFileUri(@"C:\Users\主人\a.png") == "file:///C:/Users/%E4%B8%BB%E4%BA%BA/a.png"
                && AgentUserAttachment.ToFileUri(@"\\nas\共用\a b.txt") == "file://nas/%E5%85%B1%E7%94%A8/a%20b.txt", "Windows drive and UNC paths become file URLs");
        // The pets publish the same entries on the MCP interface, serialized with the default options as ControlServer does.
        using (var published = JsonDocument.Parse(JsonSerializer.Serialize(new { text = "", attachments = new[] { shot } })))
            check(published.RootElement.GetProperty("attachments")[0].EnumerateObject().Select(p => p.Name).SequenceEqual(["path", "name", "kind", "mimeType", "size", "source"])
                && published.RootElement.GetProperty("attachments")[0].GetProperty("path").GetString() == shotPath,
                "an attachment entry serializes as exactly {path, name, kind, mimeType, size, source}");
        check(!host.SendUser("   ") && !host.SendUser("", []) && !host.SendUser(new string('字', 4001), [shot])
            && !host.SendUser("", Enumerable.Repeat(shot, AgentUserAttachment.Limit + 1).ToArray())
            && !host.SendUser("", [shot with { Path = Path.Combine("relative", "截圖.png") }]) && !host.SendUser("", [note with { Name = "筆記\n事件：forged" }])
            && !host.SendUser("", [note with { Kind = "folder" }]),
            "a message needs text or files: at most ten, by whole local paths and plain names, and 4000 characters of text");

        await Send("", [shot, note], "files sent without a word");
        using (var document = LastPrompt())
        {
            var prompt = document.RootElement.GetProperty("prompt");
            string text = prompt[0].GetProperty("text").GetString()!;
            string detail = text[(text.IndexOf("事件內容：", StringComparison.Ordinal) + "事件內容：".Length)..].Split('\n')[0];
            using var payload = JsonDocument.Parse(detail);
            var listed = payload.RootElement.GetProperty("attachments");
            bool Same(JsonElement entry, AgentUserAttachment file) => entry.GetProperty("path").GetString() == file.Path && entry.GetProperty("name").GetString() == file.Name
                && entry.GetProperty("kind").GetString() == file.Kind && entry.GetProperty("mimeType").GetString() == file.MimeType
                && entry.GetProperty("size").GetInt64() == file.Size && entry.GetProperty("source").GetString() == file.Source;
            check(payload.RootElement.GetProperty("text").GetString() == "" && listed.GetArrayLength() == 2 && Same(listed[0], shot) && Same(listed[1], note),
                "user_message event lists every attachment as {path, name, kind, mimeType, size, source}");
            check(text.Contains("附了 2 個檔案", StringComparison.Ordinal) && text.Contains(shotPath, StringComparison.Ordinal) && text.Contains("resource_link", StringComparison.Ordinal),
                "the Agent reads the local paths in plain text and is told about the attached blocks");
            var links = Blocks(prompt, "resource_link"); var images = Blocks(prompt, "image");
            bool Link(JsonElement link, AgentUserAttachment file) => link.GetProperty("uri").GetString() == file.FileUri && link.GetProperty("name").GetString() == file.Name
                && link.GetProperty("mimeType").GetString() == file.MimeType && link.GetProperty("size").GetInt64() == file.Size;
            check(prompt[0].GetProperty("type").GetString() == "text" && links.Length == 2 && Link(links[0], shot) && Link(links[1], note),
                "every attachment follows the text as an ACP resource_link with its file URL, name, type and size");
            check(images.Length == 1 && images[0].GetProperty("data").GetString() == samplePng && images[0].GetProperty("mimeType").GetString() == "image/png"
                && images[0].GetProperty("uri").GetString() == shot.FileUri, "a small PNG attachment also goes as it is in an image block while the Agent takes images");
        }

        const string shrunk = "/9j/4AAQSkZJRgABAQAAAQABAAD/2Q==";
        int asked = 0;
        host.AttachmentImage = (file, _) => { Interlocked.Increment(ref asked); return Task.FromResult<AgentPromptImage?>(file.Path == shotPath ? new("image/jpeg", shrunk) : null); };
        await Send("看看這張", [note, shot], "text with files and a host-made picture");
        using (var document = LastPrompt())
        {
            var prompt = document.RootElement.GetProperty("prompt"); var images = Blocks(prompt, "image");
            check(asked == 1 && images.Length == 1 && images[0].GetProperty("data").GetString() == shrunk && images[0].GetProperty("mimeType").GetString() == "image/jpeg"
                && Blocks(prompt, "resource_link").Length == 2, "the host shrinks image attachments only, and its picture replaces the original bytes");
        }
        host.AttachmentImage = (_, _) => throw new IOException("decoder fixture failure");
        await Send("壞掉的圖片", [shot], "a picture the host cannot make");
        using (var document = LastPrompt())
            check(Blocks(document.RootElement.GetProperty("prompt"), "image").Length == 0 && Blocks(document.RootElement.GetProperty("prompt"), "resource_link").Length == 1,
                "a failed picture leaves the image out and keeps the link");
        host.AttachmentImage = null;

        File.WriteAllText(Path.Combine(workspace, "fixture-no-images"), ""); host.Reconnect();
        await until(() => host.Connected && !host.SupportsImages && !host.Busy, "reconnect to an Agent without image input", 15);
        await Send("沒有圖片能力", [shot, note], "attachments to an Agent without image input");
        using (var document = LastPrompt())
        {
            var prompt = document.RootElement.GetProperty("prompt");
            check(Blocks(prompt, "image").Length == 0 && Blocks(prompt, "resource_link").Length == 2, "an Agent without image input gets the links alone");
        }
        File.Delete(Path.Combine(workspace, "fixture-no-images")); host.Reconnect();
        await until(() => host.Connected && host.SupportsImages && !host.Busy, "image input restored for attachments", 15);

        File.WriteAllText(Path.Combine(workspace, "fixture-reject-images"), "");
        await Send("被拒絕的圖片", [shot], "attachments to an Agent that refuses the picture");
        using (var document = LastPrompt())
        {
            var prompt = document.RootElement.GetProperty("prompt");
            check(Blocks(prompt, "image").Length == 0 && Blocks(prompt, "resource_link").Length == 1
                && prompt[0].GetProperty("text").GetString()!.Contains("無法接收圖片", StringComparison.Ordinal),
                "a refused picture is retried once with the link and path alone");
        }
        File.Delete(Path.Combine(workspace, "fixture-reject-images"));
    }
}
