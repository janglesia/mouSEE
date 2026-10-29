using System.Diagnostics;

namespace app.LookToAnswer;

/// <summary>
/// "I'm done looking": both eyes closed for HoldMs. For screens where you look around first (the fine-tune's preview,
/// the accuracy test's results) and answer by looking left or right after: looking at a dot near the edge looked like
/// an answer, so there the answer only counts after this. A blink is ~0.3 s, so it can't happen by accident.
/// Feed it every frame message with OnFrame.
/// </summary>
public sealed class ClosedEyesGate
{
    public const long HoldMs = 1500;
    // an eye that reads open for a frame or two in the middle doesn't start the count over
    public const long DropoutMs = 250;

    private readonly Func<long> _nowMs;
    private bool _closing, _done;
    private long _since, _lastClosed;

    /// <summary>0-1, how long the eyes have been closed so far.</summary>
    public double Progress { get; private set; }

    /// <summary>Fires once, when the eyes have been closed for HoldMs. Reset to wait again.</summary>
    public event Action? Done;

    /// <summary>Progress changed, for redrawing.</summary>
    public event Action? Changed;

    // nowMs is only for tests, normally a stopwatch
    public ClosedEyesGate(Func<long>? nowMs = null)
    {
        var clock = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
    }

    public void Reset()
    {
        _closing = _done = false;
        Set(0);
    }

    public void OnFrame(TrackerMessage msg)
    {
        if (msg.Type != "frame" || _done) return;
        long now = _nowMs();
        var eyes = msg.Faces is { Count: > 0 } faces ? faces[0].Eyes : null;
        bool closed = eyes is { Left: { Closed: true, Covered: false }, Right: { Closed: true, Covered: false } };

        if (!closed)
        {
            if (_closing && now - _lastClosed <= DropoutMs) return;
            _closing = false;
            Set(0);
            return;
        }

        if (!_closing)
        {
            _closing = true;
            _since = now;
        }
        _lastClosed = now;
        Set(Math.Min(1, (now - _since) / (double)HoldMs));
        if (Progress < 1) return;
        _done = true;
        Done?.Invoke();
    }

    private void Set(double progress)
    {
        if (Math.Abs(progress - Progress) < 0.005 && progress is > 0 and < 1) return;
        Progress = progress;
        Changed?.Invoke();
    }
}
