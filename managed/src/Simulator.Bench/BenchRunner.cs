using System.Diagnostics;

namespace Simulator.Bench;

/// <summary>Snapshot sent to the browser: last frame plus runner state.</summary>
public sealed record BenchSnapshot(
    BenchFrame Frame, BenchControls Controls, bool Paused, double Speed, double RealTimeFactor,
    string? Error, long Revision);

/// <summary>
/// Steps a model on a background thread, locked to wall-clock time (times Speed).
/// If the model cannot keep up, simulated time slows down instead of piling up steps.
/// All model access goes through one lock, so commands from the web side are safe.
/// </summary>
public sealed class BenchRunner : IDisposable
{
    private const int MaxStepsPerTick = 200;

    private readonly IBenchModel _model;
    private readonly object _gate = new();
    private readonly Thread _thread;
    private volatile bool _running = true;

    private BenchControls _controls;
    private BenchFrame _frame;
    private bool _paused;
    private double _speed = 1.0;
    private string? _error;
    private double _rtf;
    private long _revision;  // bumped when controls change server-side (reset, action)

    public BenchRunner(IBenchModel model, bool startPaused = false)
    {
        _model = model;
        _controls = model.Reset().Clamped();
        _frame = model.Read();
        _paused = startPaused;
        _thread = new Thread(Loop) { IsBackground = true, Name = "bench-model" };
        _thread.Start();
    }

    public IBenchModel Model => _model;

    public BenchSnapshot Snapshot()
    {
        lock (_gate) return new BenchSnapshot(_frame, _controls, _paused, _speed, _rtf, _error, _revision);
    }

    public void SetControls(BenchControls c)
    {
        lock (_gate) _controls = c.Clamped();
    }

    public void SetPaused(bool paused)
    {
        lock (_gate) _paused = paused;
    }

    public void SetSpeed(double speed)
    {
        lock (_gate) _speed = Math.Clamp(speed, 0.05, 20);
    }

    public void Reset()
    {
        lock (_gate)
        {
            try
            {
                _controls = _model.Reset().Clamped();
                _frame = _model.Read();
                _error = null;
            }
            catch (Exception e)
            {
                Fail(e);
            }
            _revision++;
        }
    }

    public void RunAction(string id)
    {
        lock (_gate)
        {
            var c = _model.RunAction(id);
            if (c is not null)
            {
                _controls = c.Clamped();
                _frame = _model.Read();
                _error = null;
                _revision++;
            }
        }
    }

    /// <summary>Advances synchronously (paused runner or tests).</summary>
    public void StepOnce(int steps = 1)
    {
        lock (_gate)
        {
            for (var i = 0; i < steps && _error is null; i++) StepLocked();
        }
    }

    private void Loop()
    {
        var clock = Stopwatch.StartNew();
        var last = clock.Elapsed.TotalSeconds;
        var debt = 0.0;  // simulated seconds owed to wall clock
        var rtfWall = 0.0;
        var rtfSim = 0.0;

        while (_running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var wall = now - last;
            last = now;

            var done = 0.0;
            lock (_gate)
            {
                if (!_paused && _error is null)
                {
                    var dt = _model.StepSize;
                    debt += wall * _speed;
                    var steps = 0;
                    while (debt >= dt && steps < MaxStepsPerTick && _error is null)
                    {
                        StepLocked();
                        debt -= dt;
                        done += dt;
                        steps++;
                    }
                    if (steps == MaxStepsPerTick) debt = 0;  // fell behind: drop, do not spiral
                }
                else
                {
                    debt = 0;
                }

                rtfWall += wall;
                rtfSim += done;
                if (rtfWall >= 0.5)
                {
                    _rtf = rtfSim / rtfWall;
                    rtfWall = rtfSim = 0;
                }
            }
            Thread.Sleep(1);
        }
    }

    private void StepLocked()
    {
        try
        {
            _model.Step(_controls);
            _frame = _model.Read();
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private void Fail(Exception e)
    {
        _error = e.Message;
        _paused = true;
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join();
        lock (_gate) _model.Dispose();
    }
}
