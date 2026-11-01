using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Simulator.Core.Native;

internal sealed class SimHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public SimHandle() : base(ownsHandle: true) { }

    protected override bool ReleaseHandle()
    {
        SimcoreNative.sim_destroy(handle);
        return true;
    }
}

// P/Invoke declarations for native/include/simcore/simcore.h.
internal static partial class SimcoreNative
{
    private const string Library = "simcore";

    public const int ExpectedApiVersion = 1;

    [LibraryImport(Library)]
    public static partial int simcore_api_version();

    [LibraryImport(Library)]
    public static partial SimHandle sim_create(int vehicleType);

    [LibraryImport(Library)]
    public static partial void sim_destroy(IntPtr sim);

    [LibraryImport(Library)]
    public static partial SimStatus sim_reset(SimHandle sim, in SimInitialConditionsNative ic);

    [LibraryImport(Library)]
    public static partial SimStatus sim_set_controls(SimHandle sim, in SimControlsNative controls);

    [LibraryImport(Library)]
    public static partial SimStatus sim_get_controls(SimHandle sim, out SimControlsNative controls);

    [LibraryImport(Library)]
    public static partial SimStatus sim_set_wind(SimHandle sim, double north, double east, double down);

    [LibraryImport(Library)]
    public static partial SimStatus sim_step(SimHandle sim, double dt);

    [LibraryImport(Library)]
    public static partial SimStatus sim_get_state(SimHandle sim, out SimStateNative state);

    [LibraryImport(Library)]
    public static partial SimStatus sim_trim(SimHandle sim, double airspeed, double altitude,
                                             out SimControlsNative controls);
}
