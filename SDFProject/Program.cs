using System.Diagnostics;
using System.Net;

var port = args.FirstOrDefault(a => int.TryParse(a, out _)) is { } portArgument ? int.Parse(portArgument) : 5173;
var openBrowser = !args.Contains("--no-browser");

var root = Path.Combine(AppContext.BaseDirectory, "Resources", "SdfViewer");
if (!File.Exists(Path.Combine(root, "index.html")))
{
    Console.Error.WriteLine($"The viewer files are missing from {root}");
    return 1;
}

var contentTypes = new Dictionary<string, string>
{
    [".html"] = "text/html; charset=utf-8",
    [".js"] = "text/javascript; charset=utf-8",
    [".css"] = "text/css; charset=utf-8",
};

var url = $"http://localhost:{port}/";
using var listener = new HttpListener();
listener.Prefixes.Add(url);
listener.Start();
Console.WriteLine($"SDF viewer running at {url} (Ctrl+C or stop in Rider to quit)");

if (openBrowser)
    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    listener.Stop();
};

while (listener.IsListening)
{
    HttpListenerContext context;
    try
    {
        context = await listener.GetContextAsync();
    }
    catch (Exception) when (!listener.IsListening)
    {
        break;
    }

    var response = context.Response;
    var relativePath = Uri.UnescapeDataString(context.Request.Url!.AbsolutePath.TrimStart('/'));
    if (relativePath == "")
        relativePath = "index.html";

    var path = Path.GetFullPath(Path.Combine(root, relativePath));
    if (!path.StartsWith(root + Path.DirectorySeparatorChar) || !File.Exists(path))
    {
        response.StatusCode = 404;
        response.Close();
        continue;
    }

    response.ContentType = contentTypes.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
    response.Headers["Cache-Control"] = "no-store";
    var content = await File.ReadAllBytesAsync(path);
    response.ContentLength64 = content.Length;
    await response.OutputStream.WriteAsync(content);
    response.Close();
}

return 0;
