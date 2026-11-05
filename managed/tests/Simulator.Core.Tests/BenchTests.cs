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
