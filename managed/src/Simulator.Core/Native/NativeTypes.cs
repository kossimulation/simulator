using System.Runtime.InteropServices;

namespace Simulator.Core.Native;

// Mirrors of the POD structs in native/include/simcore/simcore.h.
// Field order and types must match exactly.

internal enum SimStatus
{
    Ok = 0,
    NullArgument = -1,
    InvalidArgument = -2,
    NotConverged = -3,
    Numeric = -4,
    Internal = -5,
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimControlsNative
{
    public double Throttle;
    public double Pitch;
    public double Roll;
    public double Yaw;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimInitialConditionsNative
{
    public double PosNorth, PosEast, Altitude;
    public double U, V, W;
    public double Roll, Pitch, Yaw;
    public double P, Q, R;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SimStateNative
{
    public double Time;
    public double PosNorth, PosEast, PosDown;
    public double Altitude;
    public double U, V, W;
    public double VelNorth, VelEast, VelDown;
    public double Roll, Pitch, Yaw;
    public double P, Q, R;
    public double Qw, Qx, Qy, Qz;
    public double Airspeed;
    public double Alpha, Beta;
    public int OnGround;
    public int Reserved;
}
