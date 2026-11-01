namespace Simulator.Core;

public enum VehicleType
{
    FixedWing = 0,
    Helicopter = 1,
}

/// <summary>
/// Normalised pilot inputs. Throttle is engine throttle for the aircraft and
/// collective for the helicopter, [0, 1]. Pitch/Roll/Yaw are [-1, 1]:
/// positive = nose up / roll right / nose right.
/// </summary>
public readonly record struct Controls(double Throttle, double Pitch, double Roll, double Yaw);

/// <summary>Initial state. SI units, angles in radians, body-frame velocity/rates.</summary>
public readonly record struct InitialConditions(
    double PosNorth = 0, double PosEast = 0, double Altitude = 0,
    double U = 0, double V = 0, double W = 0,
    double Roll = 0, double Pitch = 0, double Yaw = 0,
    double P = 0, double Q = 0, double R = 0);

/// <summary>Snapshot of the simulated vehicle. SI units, angles in radians.</summary>
public readonly record struct SimState(
    double Time,
    double PosNorth, double PosEast, double Altitude,
    double U, double V, double W,
    double VelNorth, double VelEast, double VelDown,
    double Roll, double Pitch, double Yaw,
    double P, double Q, double R,
    double Airspeed, double Alpha, double Beta,
    bool OnGround);

public sealed class SimulationException : Exception
{
    public SimulationException(string message) : base(message) { }
}
