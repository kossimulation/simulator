using System.Globalization;
using Simulator.Core;

namespace Simulator.Cli;

// Runs a scripted flight and writes telemetry to CSV.
//
//   simulator-cli --vehicle helicopter --speed 0 --altitude 100 --duration 30
//   simulator-cli --vehicle fixed-wing --speed 50 --altitude 1000 --pitch-step 0.1 --step-at 5
internal static class Program
{
    private sealed record Options(
        VehicleType Vehicle = VehicleType.FixedWing,
        double Speed = 50.0,
        double Altitude = 1000.0,
        double Duration = 30.0,
        double Dt = 0.01,
        double PitchStep = 0.0,
        double RollStep = 0.0,
        double ThrottleStep = 0.0,
        double StepAt = 5.0,
        string Output = "telemetry.csv");

    private static int Main(string[] args)
    {
        Options options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            PrintUsage();
            return 2;
        }

        try
        {
            Run(options);
            return 0;
        }
        catch (SimulationException e)
        {
            Console.Error.WriteLine($"Simulation error: {e.Message}");
            return 1;
        }
    }

    private static void Run(Options o)
    {
        using var sim = Simulation.Create(o.Vehicle);
        var trim = sim.Trim(o.Speed, o.Altitude);
        Console.WriteLine($"{o.Vehicle} trimmed at {o.Speed} m/s, {o.Altitude} m: " +
                          $"throttle={trim.Throttle:F3} pitch={trim.Pitch:F3} roll={trim.Roll:F3} yaw={trim.Yaw:F3}");

        var stepped = trim with
        {
            Throttle = trim.Throttle + o.ThrottleStep,
            Pitch = trim.Pitch + o.PitchStep,
            Roll = trim.Roll + o.RollStep,
        };

        using var writer = new StreamWriter(o.Output);
        writer.WriteLine("time,north,east,altitude,vel_north,vel_east,vel_down,roll_deg,pitch_deg,yaw_deg," +
                         "p,q,r,airspeed,alpha_deg,beta_deg,on_ground,throttle,pitch_in,roll_in,yaw_in");

        var steps = (int)Math.Round(o.Duration / o.Dt);
        var applied = false;
        for (var i = 0; i <= steps; i++)
        {
            var s = sim.State;
            if (!applied && s.Time >= o.StepAt - 1e-9)
            {
                sim.Controls = stepped;
                applied = true;
            }
            WriteRow(writer, s, sim.Controls);
            if (i < steps) sim.Step(o.Dt);
        }

        var end = sim.State;
        Console.WriteLine($"t={end.Time:F2} s  alt={end.Altitude:F1} m  airspeed={end.Airspeed:F1} m/s  " +
                          $"roll={Deg(end.Roll):F1} pitch={Deg(end.Pitch):F1} yaw={Deg(end.Yaw):F1} deg");
        Console.WriteLine($"Telemetry written to {Path.GetFullPath(o.Output)}");
    }

    private static void WriteRow(TextWriter w, SimState s, Controls c)
    {
        var inv = CultureInfo.InvariantCulture;
        double[] values =
        [
            s.Time, s.PosNorth, s.PosEast, s.Altitude, s.VelNorth, s.VelEast, s.VelDown,
            Deg(s.Roll), Deg(s.Pitch), Deg(s.Yaw), s.P, s.Q, s.R,
            s.Airspeed, Deg(s.Alpha), Deg(s.Beta), s.OnGround ? 1 : 0,
            c.Throttle, c.Pitch, c.Roll, c.Yaw,
        ];
        w.WriteLine(string.Join(',', values.Select(v => v.ToString("G6", inv))));
    }

    private static double Deg(double rad) => rad * 180.0 / Math.PI;

    private static Options Parse(string[] args)
    {
        var o = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            var key = args[i];
            if (key is "-h" or "--help")
            {
                PrintUsage();
                Environment.Exit(0);
            }
            if (i + 1 >= args.Length) throw new ArgumentException($"Missing value for {key}.");
            var value = args[++i];
            o = key switch
            {
                "--vehicle" => o with { Vehicle = ParseVehicle(value) },
                "--speed" => o with { Speed = Num(key, value) },
                "--altitude" => o with { Altitude = Num(key, value) },
                "--duration" => o with { Duration = Num(key, value) },
                "--dt" => o with { Dt = Num(key, value) },
                "--pitch-step" => o with { PitchStep = Num(key, value) },
                "--roll-step" => o with { RollStep = Num(key, value) },
                "--throttle-step" => o with { ThrottleStep = Num(key, value) },
                "--step-at" => o with { StepAt = Num(key, value) },
                "--out" => o with { Output = value },
                _ => throw new ArgumentException($"Unknown option {key}."),
            };
        }
        if (o.Dt <= 0 || o.Duration <= 0) throw new ArgumentException("--dt and --duration must be positive.");
        return o;
    }

    private static VehicleType ParseVehicle(string value) => value.ToLowerInvariant() switch
    {
        "fixed-wing" or "aircraft" or "plane" => VehicleType.FixedWing,
        "helicopter" or "heli" => VehicleType.Helicopter,
        _ => throw new ArgumentException($"Unknown vehicle '{value}'. Use fixed-wing or helicopter."),
    };

    private static double Num(string key, string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new ArgumentException($"Invalid number for {key}: '{value}'.");

    private static void PrintUsage() => Console.WriteLine(
        """
        Usage: simulator-cli [options]
          --vehicle fixed-wing|helicopter   (default fixed-wing)
          --speed <m/s>                     trim true airspeed, 0 = hover (default 50)
          --altitude <m>                    trim altitude (default 1000)
          --duration <s>                    (default 30)
          --dt <s>                          output/step interval (default 0.01)
          --pitch-step <-1..1>              pitch input step added to trim
          --roll-step <-1..1>               roll input step added to trim
          --throttle-step <-1..1>           throttle/collective step added to trim
          --step-at <s>                     time of the step (default 5)
          --out <file.csv>                  (default telemetry.csv)
        """);
}
