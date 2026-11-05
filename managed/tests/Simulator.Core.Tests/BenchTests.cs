using Simulator.Bench;
using Xunit;

namespace Simulator.Core.Tests;

public class BenchTests
{
    private static string FakeModelPath() => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "kos_fake_model.dll"
        : OperatingSystem.IsMacOS() ? "libkos_fake_model.dylib" : "libkos_fake_model.so");

    private const string FakeProfile = """
        {
          // comments and trailing commas are allowed
          "name": "fake", "kind": "fixed-wing",
          "inputs": { "mass": 2 },
          "roll": { "port": "sum_gains" }, "pitch": { "port": "sum_gains" }, "yaw": { "port": "sum_gains" },
          "position": { "mode": "absolute", "north": "position", "east": "sum_gains", "up": "velocity" },
          "airspeed": { "port": "velocity", "scale": 2 },
          "controls": { "throttle": { "port": "thrust", "min": 0, "max": 10, "initial": 0.4 } },
          "actions": [
            { "id": "p", "label": "pulse", "set": { "thrust": 100 }, "holdSteps": 1 },
          ],
        }
        """;

    private static KosProfile Profile() =>
        System.Text.Json.JsonSerializer.Deserialize<KosProfile>(FakeProfile, KosProfile.JsonOptions)!;

    [Fact]
    public void KosBenchModelMapsControlsAndPose()
    {
        using var m = new KosBenchModel(FakeModelPath(), Profile());
        var c = m.Reset();
        Assert.Equal(0.4, c.Throttle);

        for (var i = 0; i < 100; i++) m.Step(c);  // thrust 4 N, mass 2 kg, 1 s
        var f = m.Read();
        Assert.Equal(1.0, f.Time, 9);
        Assert.Equal(2.0, f.Up, 9);          // "up" bound to velocity
        Assert.Equal(4.0, f.Airspeed, 9);    // scale 2
        Assert.Equal(2.0, f.Channels["velocity"], 9);

        m.Reset();
        Assert.Equal(0.0, m.Read().Time);
    }

    [Fact]
    public void ActionsLatchOrPulse()
    {
        using var m = new KosBenchModel(FakeModelPath(), Profile());
        var c = m.Reset() with { Throttle = 0 };
        m.RunAction("p");                     // thrust 100 N for one step, then back to 0 by the action
        m.Step(c);
        m.Step(c);
        Assert.Equal(100.0 / 2 * 0.01, m.Read().Channels["velocity"], 9);
    }

    [Fact]
    public void ProfileWithUnknownPortFailsUpFront()
    {
        var bad = Profile() with { Roll = new KosChannel("no_such_port") };
        var e = Assert.Throws<InvalidDataException>(() => new KosBenchModel(FakeModelPath(), bad));
        Assert.Contains("no_such_port", e.Message);
    }

    [Fact]
    public void RunnerAdvancesInRealTimeAndHandlesCommands()
    {
        using var runner = new BenchRunner(new SimcoreBenchModel(VehicleType.Helicopter, 0, 100));
        Thread.Sleep(600);
        var s = runner.Snapshot();
        Assert.InRange(s.Frame.Time, 0.1, 3.0);  // wall-clock bound; loose for slow CI runners
        Assert.Equal(100.0, s.Frame.Up, 0.05);  // trimmed hover

        runner.SetPaused(true);
        var t = runner.Snapshot().Frame.Time;
        Thread.Sleep(200);
        Assert.Equal(t, runner.Snapshot().Frame.Time);
        runner.StepOnce(10);
        Assert.Equal(t + 0.1, runner.Snapshot().Frame.Time, 6);

        var rev = runner.Snapshot().Revision;
        runner.Reset();
        Assert.Equal(0.0, runner.Snapshot().Frame.Time);
        Assert.Equal(rev + 1, runner.Snapshot().Revision);

        runner.RunAction("ground");
        Assert.True(runner.Snapshot().Frame.OnGround);
    }
}

public class BenchEngineTests
{
    private static string Fake() => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "kos_fake_model.dll"
        : OperatingSystem.IsMacOS() ? "libkos_fake_model.dylib" : "libkos_fake_model.so");

    // The fake DLL plays both roles: the "engine" turns lever into velocity (= thrust source),
    // which a link feeds into the dynamics' thrust input.
    private const string Profile = """
        {
          "name": "fake+engine", "kind": "fixed-wing",
          "inputs": { "mass": 2, "engine:mass": 1 },
          "roll": { "port": "sum_gains" }, "pitch": { "port": "sum_gains" }, "yaw": { "port": "sum_gains" },
          "position": { "mode": "absolute", "north": "position", "east": "sum_gains", "up": "sum_gains" },
          "controls": { "throttle": { "port": "engine:thrust", "min": 0, "max": 10, "initial": 0.1 } },
          "engine": { "dll": "unused", "links": [ { "from": "engine:velocity", "to": "thrust", "scale": 2 } ] }
        }
        """;

    [Fact]
    public void EngineIsCoSimulatedThroughLinks()
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<KosProfile>(Profile, KosProfile.JsonOptions)!;
        using var m = new KosBenchModel(Fake(), p, Fake());
        var c = m.Reset();               // engine thrust = 1 N on 1 kg -> engine velocity = t
        for (var i = 0; i < 100; i++) m.Step(c);
        var f = m.Read();
        Assert.Equal(1.0, f.Channels["engine:velocity"], 9);
        // Dynamics thrust = 2 * engine velocity, integrated on 2 kg: v = sum(2 * 0.01k) / 2 * 0.01
        Assert.Equal(0.505, f.Channels["velocity"], 9);
    }

    [Fact]
    public void EnginePortWithoutEngineIsRejected()
    {
        var p = System.Text.Json.JsonSerializer.Deserialize<KosProfile>(Profile, KosProfile.JsonOptions)! with { Engine = null };
        var e = Assert.Throws<InvalidDataException>(() => new KosBenchModel(Fake(), p));
        Assert.Contains("engine:thrust", e.Message);
    }

    [Fact]
    public unsafe void DecodesCp1251PortNames()
    {
        // "неповрежден" as written by patch_ports.py (CP1251), plus ASCII and UTF-8.
        byte[] cp1251 = [0xED, 0xE5, 0xEF, 0xEE, 0xE2, 0xF0, 0xE5, 0xE6, 0xE4, 0xE5, 0xED, 0];
        fixed (byte* p = cp1251) Assert.Equal("неповрежден", Simulator.Core.Kos.KosModel.DecodeName((IntPtr)p));
        byte[] ascii = "P1\0"u8.ToArray();
        fixed (byte* p = ascii) Assert.Equal("P1", Simulator.Core.Kos.KosModel.DecodeName((IntPtr)p));
        byte[] utf8 = [.. System.Text.Encoding.UTF8.GetBytes("кут"), 0];
        fixed (byte* p = utf8) Assert.Equal("кут", Simulator.Core.Kos.KosModel.DecodeName((IntPtr)p));
    }
}
