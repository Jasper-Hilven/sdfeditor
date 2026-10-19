using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using SDFProject.Calculation;
using SDFProject.ProgramDefinition;

namespace SDFPipeline.Rendering;

public sealed record RenderOptions
{
    public const int DefaultSize = 2048;

    /// <summary>Width and height of the image in pixels; each of the four views gets a quarter.</summary>
    public int Size { get; init; } = DefaultSize;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>The materials the program's Material nodes refer to; unknown names render magenta.</summary>
    public MaterialLibrary Materials { get; init; } = MaterialLibrary.Empty;

    /// <summary>Direction towards the sun; null keeps the IDE's sun, up and to the front right.</summary>
    public V3? LightDirection { get; init; }

    /// <summary>The look: the IDE's realistic shading, or the cartoon look of the Unity toon shader.</summary>
    public RenderStyle Style { get; init; } = RenderStyle.Realistic;

    /// <summary>
    /// The perspective view's camera instead of the framed one: yaw and pitch in radians round the target, distance in metres, and
    /// optionally the target (which the axis views are centred on too). Null frames the model.
    /// </summary>
    public RenderView? View { get; init; }
}

/// <param name="Target">The point looked at; null keeps the middle of the model's bounds.</param>
public sealed record RenderView(float Yaw, float Pitch, float Distance, float[]? Target = null);

public enum RenderStyle
{
    /// <summary>The IDE's own shading: subsurface light, detail bumps, wet coat.</summary>
    Realistic,

    /// <summary>Flat colour in three bands, a hard highlight, a rim light and an ink outline, as Unity's SdfToon shader draws it.</summary>
    Toon,
}

public sealed record RenderResult(byte[] Png, RenderFraming Framing);

/// <summary>
/// Renders a program to a PNG with the IDE's own WebGL renderer, so the image matches what the IDE shows: headless Chrome loads a
/// page from a short-lived local server, draws the quad view (side, top, front and perspective) and posts the PNG back.
/// </summary>
public static class SdfRenderer
{
    public static async Task<RenderResult> RenderAsync(SDFProgram program, RenderOptions? options = null)
    {
        options ??= new RenderOptions();
        var framing = RenderFramer.Frame(program);
        if (options.View is { } view)
            framing = framing with
            {
                Camera = framing.Camera with { Yaw = view.Yaw, Pitch = view.Pitch, Distance = view.Distance, Target = view.Target ?? framing.Camera.Target },
            };
        var scene = JsonSerializer.SerializeToUtf8Bytes(new
        {
            code = new Webgl2CodeGenerator(options.Materials).GenerateShaderCode(program),
            size = options.Size,
            camera = new
            {
                target = framing.Camera.Target,
                distance = framing.Camera.Distance,
                yaw = framing.Camera.Yaw,
                pitch = framing.Camera.Pitch,
                orthoScale = framing.Camera.OrthoScale,
            },
            caption = framing.Bounds is { } bounds ? Caption(bounds.Size) : null,
            light = options.LightDirection is { } light ? new[] { light.x, light.y, light.z } : null,
            style = options.Style == RenderStyle.Toon ? "toon" : "realistic",
        });

        var port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var result = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serving = Serve(listener, scene, result);

        var profile = Directory.CreateTempSubdirectory("sdf-render-chrome-");
        using var chrome = StartChrome(port, options.Size, profile.FullName);
        try
        {
            var finished = await Task.WhenAny(result.Task, Task.Delay(options.Timeout), chrome.WaitForExitAsync());
            if (finished != result.Task)
                throw new TimeoutException(chrome.HasExited
                    ? $"Chrome stopped before rendering (exit code {chrome.ExitCode})"
                    : $"Chrome did not render within {options.Timeout.TotalSeconds:0} seconds");
            return new RenderResult(await result.Task, framing);
        }
        finally
        {
            if (!chrome.HasExited)
                chrome.Kill(entireProcessTree: true);
            await chrome.WaitForExitAsync();
            listener.Stop();
            await serving;
            TryDelete(profile);
        }
    }

    public static async Task<RenderResult> RenderToFileAsync(SDFProgram program, string pngPath, RenderOptions? options = null)
    {
        var rendered = await RenderAsync(program, options);
        await File.WriteAllBytesAsync(pngPath, rendered.Png);
        return rendered;
    }

    private static string Caption(System.Numerics.Vector3 size) =>
        string.Create(CultureInfo.InvariantCulture, $"Size {size.X:0.00} × {size.Y:0.00} × {size.Z:0.00} m (x × y × z)");

    private static async Task Serve(HttpListener listener, byte[] scene, TaskCompletionSource<byte[]> result)
    {
        while (listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync();
            }
            catch (Exception) when (!listener.IsListening)
            {
                return;
            }

            var response = context.Response;
            var path = context.Request.Url!.AbsolutePath.TrimStart('/');
            switch (context.Request.HttpMethod, path)
            {
                case ("GET", "" or "index.html"):
                    Send(response, Resource("render.html"), "text/html; charset=utf-8");
                    break;
                case ("GET", "render.js" or "sdf-renderer.js"):
                    Send(response, Resource(path), "text/javascript; charset=utf-8");
                    break;
                case ("GET", "scene.json"):
                    Send(response, scene, "application/json");
                    break;
                case ("POST", "result"):
                    result.TrySetResult(await ReadBody(context.Request));
                    Send(response, [], "text/plain");
                    break;
                case ("POST", "error"):
                    var message = Encoding.UTF8.GetString(await ReadBody(context.Request));
                    result.TrySetException(new InvalidOperationException($"Rendering failed: {message}"));
                    Send(response, [], "text/plain");
                    break;
                default:
                    response.StatusCode = 404;
                    response.Close();
                    break;
            }
        }
    }

    private static Process StartChrome(int port, int size, string profile)
    {
        var start = new ProcessStartInfo(ChromeLocator.Find())
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "--headless=new",
                     $"--user-data-dir={profile}",
                     "--no-first-run",
                     "--no-default-browser-check",
                     "--disable-extensions",
                     "--hide-scrollbars",
                     "--force-device-scale-factor=1",
                     $"--window-size={size},{size}",
                     $"http://127.0.0.1:{port}/",
                 })
            start.ArgumentList.Add(argument);

        var chrome = Process.Start(start) ?? throw new InvalidOperationException("Chrome could not be started");
        chrome.OutputDataReceived += (_, _) => { };
        chrome.ErrorDataReceived += (_, _) => { };
        chrome.BeginOutputReadLine();
        chrome.BeginErrorReadLine();
        return chrome;
    }

    private static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private static byte[] Resource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"SDFPipeline.Rendering.{name}")
                           ?? throw new InvalidOperationException($"The render page file {name} is not embedded");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static async Task<byte[]> ReadBody(HttpListenerRequest request)
    {
        using var memory = new MemoryStream();
        await request.InputStream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private static void Send(HttpListenerResponse response, byte[] content, string contentType)
    {
        response.ContentType = contentType;
        response.Headers["Cache-Control"] = "no-store";
        response.ContentLength64 = content.Length;
        response.OutputStream.Write(content);
        response.Close();
    }

    private static void TryDelete(DirectoryInfo directory)
    {
        try
        {
            directory.Delete(recursive: true);
        }
        catch (IOException)
        {
            // Chrome can still be letting go of its profile; a leftover folder in the temp directory is harmless.
        }
    }
}
