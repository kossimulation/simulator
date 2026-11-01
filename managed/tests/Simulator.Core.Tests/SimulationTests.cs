using System.Runtime.InteropServices;
using Simulator.Core.Native;
using Xunit;

namespace Simulator.Core.Tests;

public class SimulationTests
{
    [Fact]
    public void NativeStructLayoutsMatchHeader()
    {
        Assert.Equal(32, Marshal.SizeOf<SimControlsNative>());
        Assert.Equal(96, Marshal.SizeOf<SimInitialConditionsNative>());
        Assert.Equal(200, Marshal.SizeOf<SimStateNative>());
    }

    [Fact]
    public void ApiVersionMatches() =>
        Assert.Equal(SimcoreNative.ExpectedApiVersion, Simulation.NativeApiVersion);

    [Fact]
    public void FixedWingTrimHoldsLevelFlight()
    {
        using var sim = Simulation.Create(VehicleType.FixedWing);
        var trim = sim.Trim(50, 1000);
        Assert.InRange(trim.Throttle, 0.1, 0.9);

        for (var i = 0; i < 1000; i++) sim.Step(0.01);

        var s = sim.State;
        Assert.Equal(10.0, s.Time, 9);
        Assert.Equal(1000.0, s.Altitude, 0.5);
        Assert.Equal(50.0, s.Airspeed, 0.1);
        Assert.False(s.OnGround);
    }

    [Fact]
    public void HelicopterHoverAndCollectiveClimb()
    {
        using var sim = Simulation.Create(VehicleType.Helicopter);
        var trim = sim.Trim(0, 100);
        for (var i = 0; i < 200; i++) sim.Step(0.01);
        Assert.Equal(100.0, sim.State.Altitude, 0.01);

        sim.Controls = trim with { Throttle = trim.Throttle + 0.05 };
        for (var i = 0; i < 300; i++) sim.Step(0.01);
        Assert.True(sim.State.Altitude > 100.5);
        Assert.True(sim.State.VelDown < 0);
    }

    [Fact]
    public void ControlsAreClampedByNativeSide()
    {
        using var sim = Simulation.Create(VehicleType.FixedWing);
        sim.Controls = new Controls(2.0, -3.0, 0.5, 1.5);
        Assert.Equal(new Controls(1.0, -1.0, 0.5, 1.0), sim.Controls);
    }

    [Fact]
    public void ResetPlacesVehicle()
    {
        using var sim = Simulation.Create(VehicleType.Helicopter);
        sim.Reset(new InitialConditions(PosNorth: 10, Altitude: 250, Yaw: Math.PI / 2));
        var s = sim.State;
        Assert.Equal(10.0, s.PosNorth, 12);
        Assert.Equal(250.0, s.Altitude, 12);
        Assert.Equal(Math.PI / 2, s.Yaw, 12);
        Assert.Equal(0.0, s.Time);
    }

    [Fact]
    public void InvalidStepThrows()
    {
        using var sim = Simulation.Create(VehicleType.FixedWing);
        Assert.Throws<SimulationException>(() => sim.Step(0));
        Assert.Throws<SimulationException>(() => sim.Step(double.NaN));
    }

    [Fact]
    public void UseAfterDisposeThrows()
    {
        var sim = Simulation.Create(VehicleType.FixedWing);
        sim.Dispose();
        Assert.Throws<ObjectDisposedException>(() => sim.Step(0.01));
    }
}
