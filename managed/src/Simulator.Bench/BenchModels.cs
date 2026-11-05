using Simulator.Core;

namespace Simulator.Bench;

/// <summary>Normalised pilot inputs: throttle [0, 1]; pitch/roll/yaw [-1, 1], + = nose up / roll right / nose right.</summary>
public sealed record BenchControls(double Throttle, double Pitch, double Roll, double Yaw)
{
    public static readonly BenchControls Neutral = new(0, 0, 0, 0);

    public BenchControls Clamped() => new(
        Math.Clamp(Throttle, 0, 1), Math.Clamp(Pitch, -1, 1), Math.Clamp(Roll, -1, 1), Math.Clamp(Yaw, -1, 1));
}

/// <summary>
/// One visualisation sample. Position is local to the start point (north, east, up), m;
/// angles in degrees; speeds in m/s. Channels hold every raw scalar the model exposes.
/// </summary>
public sealed record BenchFrame(
    double Time,
    double North, double East, double Up,
    double Roll, double Pitch, double Yaw,
    double Airspeed, double VerticalSpeed,
    bool OnGround,
    IReadOnlyDictionary<string, double> Channels);

public sealed record BenchAction(string Id, string Label);

/// <summary>A dynamics model the bench can drive in real time.</summary>
public interface IBenchModel : IDisposable
{
    string Name { get; }

    /// <summary>"helicopter" or "fixed-wing": selects the 3D shape.</summary>
    string Kind { get; }

    /// <summary>Fixed step the bench advances the model by, s.</summary>
    double StepSize { get; }

    IReadOnlyList<BenchAction> Actions { get; }

    /// <summary>Restores the initial state and returns the controls to start from.</summary>
    BenchControls Reset();

    void Step(BenchControls controls);

    /// <summary>Runs a model-specific action; returns new controls when the action changes them.</summary>
    BenchControls? RunAction(string id);

    BenchFrame Read();
}

/// <summary>Built-in C++ core models (simcore): light helicopter and light aircraft.</summary>
public sealed class SimcoreBenchModel : IBenchModel
{
    private readonly Simulation _sim;
    private readonly double _trimSpeed;
    private readonly double _trimAltitude;
    private readonly VehicleType _vehicle;

    public SimcoreBenchModel(VehicleType vehicle, double trimSpeed, double trimAltitude)
    {
        _vehicle = vehicle;
        _trimSpeed = trimSpeed;
        _trimAltitude = trimAltitude;
        _sim = Simulation.Create(vehicle);
        Actions =
        [
            new BenchAction("trim", "Трим у повітрі"),
            new BenchAction("ground", "Поставити на землю"),
        ];
    }

    public string Name => _vehicle == VehicleType.Helicopter ? "simcore: вертоліт (Bo-105 клас)" : "simcore: літак (C172 клас)";
    public string Kind => _vehicle == VehicleType.Helicopter ? "helicopter" : "fixed-wing";
    public double StepSize => 0.01;
    public IReadOnlyList<BenchAction> Actions { get; }

    public BenchControls Reset()
    {
        var c = _sim.Trim(_trimSpeed, _trimAltitude);
        return new BenchControls(c.Throttle, c.Pitch, c.Roll, c.Yaw);
    }

    public void Step(BenchControls controls)
    {
        _sim.Controls = new Controls(controls.Throttle, controls.Pitch, controls.Roll, controls.Yaw);
        _sim.Step(StepSize);
    }

    public BenchControls? RunAction(string id)
    {
        switch (id)
        {
            case "trim":
                return Reset();
            case "ground":
                // Gear height minus the 5 cm static deflection of the ground spring: sits without a drop.
                _sim.Reset(new InitialConditions(Altitude: (_vehicle == VehicleType.Helicopter ? 1.0 : 1.2) - 0.05));
                return BenchControls.Neutral;
            default:
                throw new ArgumentException($"Unknown action '{id}'.");
        }
    }

    public BenchFrame Read()
    {
        var s = _sim.State;
        const double deg = 180.0 / Math.PI;
        var channels = new Dictionary<string, double>
        {
            ["altitude_m"] = s.Altitude,
            ["airspeed_mps"] = s.Airspeed,
            ["alpha_deg"] = s.Alpha * deg,
            ["beta_deg"] = s.Beta * deg,
            ["roll_deg"] = s.Roll * deg,
            ["pitch_deg"] = s.Pitch * deg,
            ["yaw_deg"] = s.Yaw * deg,
            ["p_dps"] = s.P * deg,
            ["q_dps"] = s.Q * deg,
            ["r_dps"] = s.R * deg,
            ["u_mps"] = s.U,
            ["v_mps"] = s.V,
            ["w_mps"] = s.W,
            ["vel_north_mps"] = s.VelNorth,
            ["vel_east_mps"] = s.VelEast,
            ["vel_down_mps"] = s.VelDown,
        };
        return new BenchFrame(s.Time, s.PosNorth, s.PosEast, s.Altitude,
            s.Roll * deg, s.Pitch * deg, s.Yaw * deg, s.Airspeed, -s.VelDown, s.OnGround, channels);
    }

    public void Dispose() => _sim.Dispose();
}
