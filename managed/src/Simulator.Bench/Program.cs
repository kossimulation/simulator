using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Simulator.Bench;
using Simulator.Core;

// Real-time test bench with browser visualisation.
//
//   simulator-bench                                   built-in helicopter, hover at 100 m
//   simulator-bench --model fixed-wing --speed 50     built-in aircraft
//   simulator-bench --profile profiles/gorel.json --dll C:\DynamicsNEW\GOR-El\DynamicsGorEl.dll
//
// Then open http://localhost:5080
BenchOptions options;
try
{
    options = BenchOptions.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    BenchOptions.PrintUsage();
    return 2;
}
if (options.Help)
{
    BenchOptions.PrintUsage();
    return 0;
}

IBenchModel model;
try
{
    model = options.CreateModel();
}
catch (Exception e) when (e is SimulationException or FileNotFoundException or InvalidDataException
                            or JsonException or KeyNotFoundException or BadImageFormatException)
{
    Console.Error.WriteLine(e.Message);
    return 1;
}

using var runner = new BenchRunner(model);

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
{
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);

app.MapGet("/api/info", () => Results.Json(new
{
    name = model.Name,
    kind = model.Kind,
    stepSize = model.StepSize,
    actions = model.Actions,
}, json));

app.MapGet("/api/state", () => Results.Json(runner.Snapshot(), json));

app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 400;
        return;
    }
    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
    var receive = ReceiveCommands(ws, runner, json, cts.Token);
    try
    {
        while (ws.State == WebSocketState.Open && !cts.IsCancellationRequested)
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(runner.Snapshot(), json);
            await ws.SendAsync(payload, WebSocketMessageType.Text, true, cts.Token);
            await Task.Delay(33, cts.Token);  // ~30 Hz to the browser
        }
    }
    catch (Exception e) when (e is OperationCanceledException or WebSocketException)
    {
    }
    cts.Cancel();
    try { await receive; } catch (Exception e) when (e is OperationCanceledException or WebSocketException) { }
});

Console.WriteLine($"Model: {model.Name}, step {model.StepSize.ToString(CultureInfo.InvariantCulture)} s");
Console.WriteLine($"Bench: http://{(options.Host == "0.0.0.0" ? "localhost" : options.Host)}:{options.Port}  (Ctrl+C to stop)");
await app.RunAsync();
return 0;

static async Task ReceiveCommands(WebSocket ws, BenchRunner runner, JsonSerializerOptions json, CancellationToken ct)
{
    var buffer = new byte[16 * 1024];
    while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
    {
        using var message = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buffer, ct);
            if (r.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, r.Count);
        } while (!r.EndOfMessage);

        try
        {
            var cmd = JsonSerializer.Deserialize<BenchCommand>(message.ToArray(), json);
            if (cmd is not null) cmd.Apply(runner);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            Console.Error.WriteLine($"Bad command: {Encoding.UTF8.GetString(message.ToArray())} ({e.Message})");
        }
    }
}

/// <summary>Message from the browser: {"type":"controls",...} | "pause" | "resume" | "reset" | "speed" | "action" | "step".</summary>
internal sealed record BenchCommand(
    string Type, double Throttle = 0, double Pitch = 0, double Roll = 0, double Yaw = 0,
    double Value = 0, string? Id = null)
{
    public void Apply(BenchRunner runner)
    {
        switch (Type)
        {
            case "controls": runner.SetControls(new BenchControls(Throttle, Pitch, Roll, Yaw)); break;
            case "pause": runner.SetPaused(true); break;
            case "resume": runner.SetPaused(false); break;
            case "reset": runner.Reset(); break;
            case "speed": runner.SetSpeed(Value); break;
            case "step": runner.StepOnce(Math.Max(1, (int)Value)); break;
            case "action": runner.RunAction(Id ?? throw new ArgumentException("action needs id")); break;
            default: throw new ArgumentException($"Unknown command type '{Type}'.");
        }
    }
}

internal sealed record BenchOptions(
    string Model = "helicopter",
    double Speed = 0,
    double Altitude = 100,
    string? Profile = null,
    string? Dll = null,
    string Host = "localhost",
    int Port = 5080,
    bool Help = false)
{
    public static BenchOptions Parse(string[] args)
    {
        var o = new BenchOptions();
        var speedGiven = false;
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "-h" or "--help") return o with { Help = true };
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {key}.");
            var value = args[++i];
            o = key switch
            {
                "--model" => o with { Model = value },
                "--speed" => o with { Speed = Num(key, value) },
                "--altitude" => o with { Altitude = Num(key, value) },
                "--profile" => o with { Profile = value, Model = "kos" },
                "--dll" => o with { Dll = value },
                "--host" => o with { Host = value },
                "--port" => o with { Port = (int)Num(key, value) },
                _ => throw new ArgumentException($"Unknown option {key}."),
            };
            speedGiven |= key == "--speed";
        }
        if (!speedGiven && o.Model is "fixed-wing" or "plane") o = o with { Speed = 50 };
        return o;
    }

    public IBenchModel CreateModel()
    {
        switch (Model)
        {
            case "helicopter" or "heli":
                return new SimcoreBenchModel(VehicleType.Helicopter, Speed, Altitude);
            case "fixed-wing" or "plane":
                return new SimcoreBenchModel(VehicleType.FixedWing, Speed, Altitude);
            case "kos":
                var profilePath = Profile ?? throw new ArgumentException("--profile is required for kos models.");
                if (!File.Exists(profilePath)) profilePath = Path.Combine(AppContext.BaseDirectory, profilePath);
                var profile = KosProfile.Load(profilePath);
                var dll = Dll ?? profile.Dll ?? throw new ArgumentException("--dll is required (profile has no \"dll\").");
                if (!Path.IsPathRooted(dll) && !File.Exists(dll))
                    dll = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profilePath))!, dll);
                return new KosBenchModel(dll, profile);
            default:
                throw new ArgumentException($"Unknown model '{Model}'. Use helicopter, fixed-wing or --profile.");
        }
    }

    private static double Num(string key, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new ArgumentException($"Invalid number for {key}: '{value}'.");

    public static void PrintUsage() => Console.WriteLine(
        """
        Usage: simulator-bench [options]
          --model helicopter|fixed-wing   built-in simcore model (default helicopter)
          --speed <m/s>                   trim airspeed (helicopter 0 = hover, aircraft 50)
          --altitude <m>                  trim altitude (default 100)
          --profile <file.json>           kos_simulation model DLL bound by a profile (profiles/*.json)
          --dll <model.dll>               DLL path (overrides "dll" in the profile)
          --host <addr>                   listen address (default localhost; 0.0.0.0 for LAN)
          --port <n>                      HTTP port (default 5080)
          x86 model DLLs need a 32-bit bench: dotnet publish managed/src/Simulator.Bench -c Release -r win-x86 --self-contained
        """);
}
