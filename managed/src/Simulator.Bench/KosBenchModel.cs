using System.Text.Json;
using System.Text.Json.Serialization;
using Simulator.Core.Kos;

namespace Simulator.Bench;

// Port references in a profile name a port of the dynamics DLL ("P1") or, with the
// "engine:" prefix, of the engine DLL ("engine:eng_P1").

/// <summary>Reads one value from a model output: value = offset + scale * port (or 1 - that when inverted for flags).</summary>
public sealed record KosChannel(string Port, double Scale = 1, double Offset = 0, bool Invert = false);

/// <summary>
/// Maps a normalised control u to a model input. With Min/Max: value = Min + u * (Max - Min)
/// (throttle, u in [0, 1]). Otherwise value = trim + Offset + Scale * u, where trim is read from
/// the TrimFrom output after reset (e.g. AK13 RLX_BAL), or 0.
/// </summary>
public sealed record KosControl(
    string Port, double Scale = 1, double Offset = 0, double? Min = null, double? Max = null,
    string? TrimFrom = null, double Initial = 0);

public sealed record KosPosition(string Mode, string North, string East, string Up, double Scale = 1);

public sealed record KosReset(string Port, int HoldSteps = 2);

public sealed record KosProfileAction(string Id, string Label, Dictionary<string, double> Set, int HoldSteps = 0);

/// <summary>Copies a value every step: To = Offset + Scale * From.</summary>
public sealed record KosLink(string From, string To, double Scale = 1, double Offset = 0);

/// <summary>
/// Separate engine DLL (e.g. SharkMEngineEl, GorEngineEl) co-simulated with the dynamics.
/// Each bench step: links into the engine, engine step(s), links into the dynamics, dynamics step.
/// </summary>
public sealed record KosEngine(string Dll, List<KosLink> Links);

/// <summary>Binding of a kos_simulation DLL to the bench: which ports carry pose, speed and controls.</summary>
public sealed record KosProfile(
    string Name,
    string Kind,
    string? Dll,
    Dictionary<string, double>? Inputs,
    KosReset? Reset,
    KosChannel Roll, KosChannel Pitch, KosChannel Yaw,
    KosPosition Position,
    KosChannel? Airspeed,
    KosChannel? VerticalSpeed,
    KosChannel? OnGround,
    Dictionary<string, KosControl> Controls,
    List<KosProfileAction>? Actions,
    KosEngine? Engine,
    string? Note)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static KosProfile Load(string path) =>
        JsonSerializer.Deserialize<KosProfile>(File.ReadAllText(path), JsonOptions)
        ?? throw new InvalidDataException($"Empty profile {path}.");
}

/// <summary>Drives a kos_simulation model DLL (and optionally its engine DLL) through a <see cref="KosProfile"/>.</summary>
public sealed class KosBenchModel : IBenchModel
{
    private const string EnginePrefix = "engine:";

    private readonly string _dllPath;
    private readonly string? _engineDllPath;
    private readonly KosProfile _profile;
    private KosModel _model;
    private KosModel? _engine;
    private int _engineSubsteps = 1;
    private readonly Dictionary<string, double> _trim = new();
    private double _north, _east, _up;
    private Dictionary<string, double>? _pulse;
    private int _pulseLeft;

    public KosBenchModel(string dllPath, KosProfile profile, string? engineDllPath = null)
    {
        _dllPath = dllPath;
        _profile = profile;
        if (profile.Engine is not null)
        {
            _engineDllPath = engineDllPath
                ?? throw new ArgumentException($"Profile '{profile.Name}' has an engine; its DLL path is required.");
        }
        (_model, _engine) = LoadModels();
        Validate();
        Actions = (profile.Actions ?? []).Select(a => new BenchAction(a.Id, a.Label)).ToList();
    }

    public string Name => _engine is null
        ? $"{_profile.Name} ({Path.GetFileName(_dllPath)})"
        : $"{_profile.Name} ({Path.GetFileName(_dllPath)} + {Path.GetFileName(_engineDllPath)})";
    public string Kind => _profile.Kind;
    public double StepSize => _model.StepSize;
    public IReadOnlyList<BenchAction> Actions { get; }
    public KosModel Model => _model;
    public KosModel? Engine => _engine;

    public BenchControls Reset()
    {
        // A fresh instance is the only reliable way back to t = 0 for a generated model.
        _model.Dispose();
        _engine?.Dispose();
        (_model, _engine) = LoadModels();
        foreach (var (port, value) in _profile.Inputs ?? []) Set(port, value);
        _north = _east = _up = 0;
        _pulse = null;
        _model.Start();
        _engine?.Start();

        if (_profile.Reset is { } r)
        {
            Set(r.Port, 1);
            for (var i = 0; i < r.HoldSteps; i++) _model.Step();
            Set(r.Port, 0);
        }

        _trim.Clear();
        foreach (var c in _profile.Controls.Values)
            if (c.TrimFrom is not null) _trim[c.Port] = Get(c.TrimFrom);

        var ctl = _profile.Controls;
        return new BenchControls(
            ctl.GetValueOrDefault("throttle")?.Initial ?? 0,
            ctl.GetValueOrDefault("pitch")?.Initial ?? 0,
            ctl.GetValueOrDefault("roll")?.Initial ?? 0,
            ctl.GetValueOrDefault("yaw")?.Initial ?? 0);
    }

    public void Step(BenchControls controls)
    {
        Apply("throttle", controls.Throttle);
        Apply("pitch", controls.Pitch);
        Apply("roll", controls.Roll);
        Apply("yaw", controls.Yaw);

        if (_pulse is not null)
        {
            if (_pulseLeft-- > 0)
            {
                foreach (var (port, value) in _pulse) Set(port, value);
            }
            else
            {
                foreach (var port in _pulse.Keys) Set(port, 0);
                _pulse = null;
            }
        }

        if (_engine is not null)
        {
            var links = _profile.Engine!.Links;
            foreach (var l in links) if (IsEngine(l.To)) Set(l.To, l.Offset + l.Scale * Get(l.From));
            for (var i = 0; i < _engineSubsteps; i++) _engine.Step();
            foreach (var l in links) if (!IsEngine(l.To)) Set(l.To, l.Offset + l.Scale * Get(l.From));
        }

        _model.Step();

        if (_profile.Position.Mode.Equals("increments", StringComparison.OrdinalIgnoreCase))
        {
            var p = _profile.Position;
            _north += Get(p.North) * p.Scale;
            _east += Get(p.East) * p.Scale;
            _up += Get(p.Up) * p.Scale;
        }
    }

    public BenchControls? RunAction(string id)
    {
        var a = _profile.Actions?.FirstOrDefault(x => x.Id == id)
                ?? throw new ArgumentException($"Unknown action '{id}'.");
        if (a.HoldSteps > 0)
        {
            // Pulse: hold the inputs for HoldSteps steps, then set them back to 0.
            _pulse = a.Set;
            _pulseLeft = a.HoldSteps;
        }
        else
        {
            // Latch: inputs keep their value in model memory until changed.
            foreach (var (port, value) in a.Set) Set(port, value);
        }
        return null;
    }

    public BenchFrame Read()
    {
        var p = _profile.Position;
        double north, east, up;
        if (p.Mode.Equals("increments", StringComparison.OrdinalIgnoreCase))
        {
            (north, east, up) = (_north, _east, _up);
        }
        else
        {
            north = Get(p.North) * p.Scale;
            east = Get(p.East) * p.Scale;
            up = Get(p.Up) * p.Scale;
        }

        var channels = new Dictionary<string, double>(_model.Outputs.Count + (_engine?.Outputs.Count ?? 0));
        foreach (var port in _model.Outputs)
            if (port.IsDouble && port.Width == 1) channels[port.Name] = _model[port.Name];
        if (_engine is not null)
        {
            foreach (var port in _engine.Outputs)
                if (port.IsDouble && port.Width == 1) channels[EnginePrefix + port.Name] = _engine[port.Name];
        }

        return new BenchFrame(
            _model.Time, north, east, up,
            Value(_profile.Roll), Value(_profile.Pitch), Value(_profile.Yaw),
            _profile.Airspeed is { } a ? Value(a) : 0,
            _profile.VerticalSpeed is { } v ? Value(v) : 0,
            _profile.OnGround is { } g && Value(g) > 0.5,
            channels);
    }

    public void Dispose()
    {
        _model.Dispose();
        _engine?.Dispose();
    }

    private (KosModel Model, KosModel? Engine) LoadModels()
    {
        var model = KosModel.Load(_dllPath);
        if (_engineDllPath is null) return (model, null);
        KosModel engine;
        try
        {
            engine = KosModel.Load(_engineDllPath);
        }
        catch
        {
            model.Dispose();
            throw;
        }
        // Run the engine at its own rate inside one dynamics step (normally both are the SimCore frame).
        _engineSubsteps = Math.Max(1, (int)Math.Round(model.StepSize / engine.StepSize));
        return (model, engine);
    }

    private static bool IsEngine(string port) => port.StartsWith(EnginePrefix, StringComparison.Ordinal);

    private (KosModel Model, string Port) Resolve(string port)
    {
        if (!IsEngine(port)) return (_model, port);
        return (_engine ?? throw new InvalidDataException($"'{port}' refers to an engine, but the profile has none."),
                port[EnginePrefix.Length..]);
    }

    private double Get(string port)
    {
        var (m, name) = Resolve(port);
        return m[name];
    }

    private void Set(string port, double value)
    {
        var (m, name) = Resolve(port);
        m[name] = value;
    }

    private void Apply(string axis, double u)
    {
        if (!_profile.Controls.TryGetValue(axis, out var c)) return;
        var value = c.Min is { } min && c.Max is { } max
            ? min + u * (max - min)
            : _trim.GetValueOrDefault(c.Port) + c.Offset + c.Scale * u;
        Set(c.Port, value);
    }

    private double Value(KosChannel c)
    {
        var v = c.Offset + c.Scale * Get(c.Port);
        return c.Invert ? 1 - v : v;
    }

    // Fail at start-up, not mid-flight, when the profile names a port a DLL does not have.
    private void Validate()
    {
        var missing = new List<string>();
        void Need(string? port)
        {
            if (port is null) return;
            KosModel m;
            string name;
            try
            {
                (m, name) = Resolve(port);
            }
            catch (InvalidDataException)
            {
                missing.Add($"{port} (no engine)");
                return;
            }
            if (!m.HasPort(name)) missing.Add(port);
            else if (m.Port(name).Width != 1 || !m.Port(name).IsDouble) missing.Add($"{port} (not a scalar double)");
        }
        foreach (var port in (_profile.Inputs ?? []).Keys) Need(port);
        Need(_profile.Reset?.Port);
        Need(_profile.Roll.Port); Need(_profile.Pitch.Port); Need(_profile.Yaw.Port);
        Need(_profile.Position.North); Need(_profile.Position.East); Need(_profile.Position.Up);
        Need(_profile.Airspeed?.Port); Need(_profile.VerticalSpeed?.Port); Need(_profile.OnGround?.Port);
        foreach (var c in _profile.Controls.Values) { Need(c.Port); Need(c.TrimFrom); }
        foreach (var a in _profile.Actions ?? []) foreach (var port in a.Set.Keys) Need(port);
        foreach (var l in _profile.Engine?.Links ?? []) { Need(l.From); Need(l.To); }
        if (missing.Count > 0)
        {
            var dlls = _engine is null ? Path.GetFileName(_dllPath) : $"{Path.GetFileName(_dllPath)} / {Path.GetFileName(_engineDllPath)}";
            throw new InvalidDataException(
                $"Profile '{_profile.Name}' refers to ports missing or unusable in {dlls}: {string.Join(", ", missing.Distinct())}");
        }
    }
}
