using System.Globalization;
using Simulator.Core;
using Simulator.Core.Kos;

namespace Simulator.Cli;

// simulator-cli kos --dll AK13Dynamics.dll --list
// simulator-cli kos --dll DynamicsGorEl.dll --set Initial_Height=100 --set Initial_Airspeed=29 \
//                   --duration 20 --outputs roll_deg,pitch_deg,AltitudeMSL_m --out gorel.csv
internal static class KosCommand
{
    public static int Run(string[] args)
    {
        string? dll = null, output = "kos.csv";
        var list = false;
        var duration = 10.0;
        var sets = new List<(string Name, double Value)>();
        string[]? outputs = null;

        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "-h" or "--help") { PrintUsage(); return 0; }
            if (key == "--list") { list = true; continue; }
            if (i + 1 >= args.Length) return Fail($"Missing value for {key}.");
            var value = args[++i];
            switch (key)
            {
                case "--dll": dll = value; break;
                case "--out": output = value; break;
                case "--duration":
                    if (!TryNum(value, out duration) || duration <= 0) return Fail("--duration must be a positive number.");
                    break;
                case "--outputs": outputs = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries); break;
                case "--set":
                    var eq = value.IndexOf('=');
                    if (eq <= 0 || !TryNum(value[(eq + 1)..], out var v)) return Fail($"--set expects name=value, got '{value}'.");
                    sets.Add((value[..eq], v));
                    break;
                default: return Fail($"Unknown option {key}.");
            }
        }
        if (dll is null) return Fail("--dll is required.");

        try
        {
            using var model = KosModel.Load(dll);
            if (list)
            {
                PrintPorts(model);
                return 0;
            }
            return Simulate(model, sets, outputs, duration, output!);
        }
        catch (Exception e) when (e is SimulationException or FileNotFoundException or KeyNotFoundException
                                    or InvalidOperationException or NotSupportedException or BadImageFormatException)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static void PrintPorts(KosModel m)
    {
        Console.WriteLine($"{Path.GetFileName(m.Path)}: {m.Inputs.Count} inputs, {m.Outputs.Count} outputs, step {m.StepSize} s");
        foreach (var group in new[] { ("Inputs", m.Inputs), ("Outputs", m.Outputs) })
        {
            Console.WriteLine($"\n{group.Item1}:");
            foreach (var p in group.Item2)
            {
                var extra = (p.Width != 1 ? $"[{p.Width}]" : "") + (p.IsDouble ? "" : $" (VARTYPE {p.VarType})");
                Console.WriteLine($"  {p.Index,4}  {p.Name}{extra}");
            }
        }
    }

    private static int Simulate(KosModel m, List<(string Name, double Value)> sets, string[]? outputs,
                                double duration, string path)
    {
        foreach (var (name, value) in sets) m[name] = value;

        var columns = (outputs ?? m.Outputs.Where(p => p.IsDouble).Select(p => p.Name).ToArray())
            .SelectMany(n => m.Port(n).Width == 1
                ? [(Name: n, Element: 0, Header: n)]
                : Enumerable.Range(0, m.Port(n).Width).Select(k => (Name: n, Element: k, Header: $"{n}[{k}]")))
            .ToArray();

        var inv = CultureInfo.InvariantCulture;
        using var w = new StreamWriter(path);
        w.WriteLine("time," + string.Join(',', columns.Select(c => c.Header)));

        m.Start();
        var steps = (int)Math.Round(duration / m.StepSize);
        for (var i = 0; i < steps; i++)
        {
            m.Step();
            var cache = new Dictionary<string, double[]>();
            var row = columns.Select(c =>
            {
                if (!cache.TryGetValue(c.Name, out var v)) cache[c.Name] = v = m.Read(c.Name);
                return v[c.Element].ToString("G9", inv);
            });
            w.WriteLine(m.Time.ToString("G9", inv) + "," + string.Join(',', row));
        }

        Console.WriteLine($"{Path.GetFileName(m.Path)}: {steps} steps of {m.StepSize} s, t = {m.Time:F3} s");
        Console.WriteLine($"Telemetry written to {Path.GetFullPath(path)}");
        return 0;
    }

    private static bool TryNum(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        PrintUsage();
        return 2;
    }

    private static void PrintUsage() => Console.WriteLine(
        """
        Usage: simulator-cli kos --dll <model.dll> [options]
          Runs a model DLL built with the kos_simulation Simulink target
          (AK13Dynamics.dll, DynamicsGorEl.dll, ...).
          --list                   print ports and exit
          --set name=value         set an input before start (repeatable)
          --duration <s>           simulated time (default 10)
          --outputs a,b,c          output ports to log (default: all)
          --out <file.csv>         (default kos.csv)
          x86 DLLs need a 32-bit host: dotnet publish managed/src/Simulator.Cli -c Release -r win-x86 --self-contained
        """);
}
