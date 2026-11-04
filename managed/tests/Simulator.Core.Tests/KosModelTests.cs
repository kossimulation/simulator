using System.Runtime.InteropServices;
using Simulator.Core.Kos;
using Xunit;

namespace Simulator.Core.Tests;

public class KosModelTests
{
    private static string FakeModelPath()
    {
        var file = OperatingSystem.IsWindows() ? "kos_fake_model.dll"
            : OperatingSystem.IsMacOS() ? "libkos_fake_model.dylib"
            : "libkos_fake_model.so";
        return Path.Combine(AppContext.BaseDirectory, file);
    }

    [Fact]
    public void DiscoversPortsFromDescriptors()
    {
        using var m = KosModel.Load(FakeModelPath());
        Assert.Equal(["thrust", "mass", "gains"], m.Inputs.Select(p => p.Name));
        Assert.Equal(["velocity", "position", "sum_gains"], m.Outputs.Select(p => p.Name));
        Assert.Equal(3, m.Port("gains").Width);
        Assert.All(m.Ports, p => Assert.True(p.IsDouble));
        Assert.Equal(0.01, m.StepSize, 12);
    }

    [Fact]
    public void StepsModelThroughPortAddresses()
    {
        using var m = KosModel.Load(FakeModelPath());
        m["mass"] = 2.0;
        m["thrust"] = 4.0;
        m.Write("gains", [1.0, 2.0, 3.5]);
        for (var i = 0; i < 100; i++) m.Step();

        Assert.Equal(1.0, m.Time, 9);
        Assert.Equal(2.0, m["velocity"], 9);  // a = 2 m/s^2 for 1 s
        Assert.Equal(6.5, m["sum_gains"], 12);
        Assert.Equal([1.0, 2.0, 3.5], m.Read("gains"));
    }

    [Fact]
    public void RejectsBadPortAccess()
    {
        using var m = KosModel.Load(FakeModelPath());
        Assert.Throws<KeyNotFoundException>(() => m["nope"]);
        Assert.Throws<InvalidOperationException>(() => m["velocity"] = 1.0);
        Assert.Throws<ArgumentException>(() => m.Write("gains", [1.0]));
    }

    [Fact]
    public void InstancesAreIndependentAndDisposeIsFinal()
    {
        var a = KosModel.Load(FakeModelPath());
        using var b = KosModel.Load(FakeModelPath());
        a["thrust"] = 1.0;
        b["thrust"] = 3.0;
        a.Step();
        b.Step();
        Assert.True(b["velocity"] > a["velocity"]);
        a.Dispose();
        Assert.Throws<ObjectDisposedException>(() => a.Step());
        b.Step();
    }

    [Fact]
    public void ReadsPeMachine()
    {
        var path = Path.GetTempFileName();
        try
        {
            var image = new byte[0x100];
            image[0] = (byte)'M'; image[1] = (byte)'Z';
            BitConverter.GetBytes(0x80).CopyTo(image, 0x3C);
            "PE\0\0"u8.ToArray().CopyTo(image, 0x80);
            BitConverter.GetBytes((ushort)0x014C).CopyTo(image, 0x84);
            File.WriteAllBytes(path, image);
            Assert.Equal(Architecture.X86, KosModel.PeMachine(path));

            File.WriteAllBytes(path, [0x7F, (byte)'E', (byte)'L', (byte)'F']);
            Assert.Null(KosModel.PeMachine(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
