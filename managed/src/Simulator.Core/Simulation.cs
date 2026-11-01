using Simulator.Core.Native;

namespace Simulator.Core;

/// <summary>
/// Managed wrapper around one native simcore simulation instance.
/// Not thread-safe; use one instance per thread.
/// </summary>
public sealed class Simulation : IDisposable
{
    private readonly SimHandle _handle;

    private Simulation(SimHandle handle, VehicleType vehicle)
    {
        _handle = handle;
        Vehicle = vehicle;
    }

    public VehicleType Vehicle { get; }

    public static int NativeApiVersion => SimcoreNative.simcore_api_version();

    public static Simulation Create(VehicleType vehicle)
    {
        var version = NativeApiVersion;
        if (version != SimcoreNative.ExpectedApiVersion)
        {
            throw new SimulationException(
                $"simcore API version {version} does not match expected {SimcoreNative.ExpectedApiVersion}.");
        }

        var handle = SimcoreNative.sim_create((int)vehicle);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            throw new SimulationException($"Failed to create native simulation for {vehicle}.");
        }
        return new Simulation(handle, vehicle);
    }

    public Controls Controls
    {
        get
        {
            Check(SimcoreNative.sim_get_controls(_handle, out var c), nameof(Controls));
            return new Controls(c.Throttle, c.Pitch, c.Roll, c.Yaw);
        }
        set
        {
            var c = new SimControlsNative
            {
                Throttle = value.Throttle, Pitch = value.Pitch, Roll = value.Roll, Yaw = value.Yaw,
            };
            Check(SimcoreNative.sim_set_controls(_handle, c), nameof(Controls));
        }
    }

    public SimState State
    {
        get
        {
            Check(SimcoreNative.sim_get_state(_handle, out var s), nameof(State));
            return new SimState(
                s.Time,
                s.PosNorth, s.PosEast, s.Altitude,
                s.U, s.V, s.W,
                s.VelNorth, s.VelEast, s.VelDown,
                s.Roll, s.Pitch, s.Yaw,
                s.P, s.Q, s.R,
                s.Airspeed, s.Alpha, s.Beta,
                s.OnGround != 0);
        }
    }

    public void Reset(InitialConditions ic)
    {
        var n = new SimInitialConditionsNative
        {
            PosNorth = ic.PosNorth, PosEast = ic.PosEast, Altitude = ic.Altitude,
            U = ic.U, V = ic.V, W = ic.W,
            Roll = ic.Roll, Pitch = ic.Pitch, Yaw = ic.Yaw,
            P = ic.P, Q = ic.Q, R = ic.R,
        };
        Check(SimcoreNative.sim_reset(_handle, n), nameof(Reset));
    }

    public void SetWind(double north, double east, double down) =>
        Check(SimcoreNative.sim_set_wind(_handle, north, east, down), nameof(SetWind));

    /// <summary>Advances simulated time by <paramref name="dt"/> seconds.</summary>
    public void Step(double dt) => Check(SimcoreNative.sim_step(_handle, dt), nameof(Step));

    /// <summary>
    /// Trims for steady wings-level flight heading north at the given true
    /// airspeed (0 = hover) and resets the simulation to that point.
    /// </summary>
    public Controls Trim(double airspeed, double altitude)
    {
        Check(SimcoreNative.sim_trim(_handle, airspeed, altitude, out var c), nameof(Trim));
        return new Controls(c.Throttle, c.Pitch, c.Roll, c.Yaw);
    }

    public void Dispose() => _handle.Dispose();

    private static void Check(SimStatus status, string operation)
    {
        if (status == SimStatus.Ok) return;
        throw new SimulationException(status switch
        {
            SimStatus.NullArgument => $"{operation}: null argument.",
            SimStatus.InvalidArgument => $"{operation}: invalid argument.",
            SimStatus.NotConverged => $"{operation}: trim did not converge within control limits.",
            SimStatus.Numeric => $"{operation}: numerical failure (non-finite state).",
            _ => $"{operation}: native error {(int)status}.",
        });
    }
}
