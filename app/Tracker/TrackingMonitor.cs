using System.Diagnostics;

namespace app.Tracker;

public enum TrackingState
{
    Starting,       // no frames yet
    Tracking,       // all good, the cursor can move
    Uncertain,      // bad frames for less than LostAfterMs (a blink, a glitch). hold the cursor, don't tell the user
    Lost,           // bad for LostAfterMs or longer, tell the user why
    Recovering,     // good frames again, waiting for RecoverFrames in a row before trusting them
    NotResponding,  // frames stopped arriving
    Stopped,        // tracker process exited
    Paused,         // paused by the user. frames still arrive, the cursor stays put
}

/// <summary>
/// Turns the tracker's per-frame status (tracking_status.py) into a state that's stable enough to
/// show the user and to decide whether the cursor may move. Doesn't touch any UI.
/// Call OnFrame for every frame message and CheckTimeout regularly (frames stopping can't be noticed otherwise).
/// </summary>
public sealed class TrackingMonitor
{
    // LostAfterMs, RecoverFrames and NotRespondingAfterMs come from here
    private AdvancedSettings _timings;

    public TrackingState State { get; private set; } = TrackingState.Starting;

    /// <summary>Why tracking is lost: a tracking_status.py reason (no_face, head_turned...) or eyes_closed.</summary>
    public string? Reason { get; private set; }

    public bool CanMoveCursor => State == TrackingState.Tracking;

    /// <summary>Fires when State or Reason changes.</summary>
    public event Action? Changed;

    private readonly Func<long> _nowMs;
    private long _badSinceMs = -1, _lastFrameMs = -1;
    private int _goodStreak;

    // nowMs is only for tests, normally a stopwatch
    public TrackingMonitor(AdvancedSettings? timings = null, bool paused = false, Func<long>? nowMs = null)
    {
        var clock = Stopwatch.StartNew();
        _nowMs = nowMs ?? (() => clock.ElapsedMilliseconds);
        _timings = timings ?? new AdvancedSettings();
        if (paused) State = TrackingState.Paused;
    }

    /// <summary>New timings, used from the next frame.</summary>
    public void Configure(AdvancedSettings timings) => _timings = timings;

    public void SetPaused(bool paused)
    {
        if (State == TrackingState.Stopped) return;
        if (paused)
        {
            Set(TrackingState.Paused, null);
        }
        else if (State == TrackingState.Paused)
        {
            // frames were ignored while paused, so wait for RecoverFrames good ones again
            _goodStreak = 0;
            _badSinceMs = -1;
            Set(_lastFrameMs < 0 ? TrackingState.Starting : TrackingState.Recovering, null);
        }
    }

    public void OnFrame(Tracking? tracking)
    {
        if (State == TrackingState.Stopped) return;
        long now = _nowMs();
        _lastFrameMs = now;
        if (State == TrackingState.Paused) return;

        // an older tracker without the field: assume it's fine
        bool good = tracking is null || tracking.State == "ok";
        if (good)
        {
            _badSinceMs = -1;
            _goodStreak++;
            switch (State)
            {
                case TrackingState.Uncertain:
                    // never told the user anything, so just carry on
                    Set(TrackingState.Tracking, null);
                    break;
                case TrackingState.Starting or TrackingState.Lost or TrackingState.NotResponding or TrackingState.Recovering:
                    bool recovered = _goodStreak >= _timings.RecoverFrames;
                    Set(recovered ? TrackingState.Tracking : TrackingState.Recovering, recovered ? null : Reason);
                    break;
            }
            return;
        }

        string reason = tracking!.Reason ?? tracking.State;
        _goodStreak = 0;
        if (_badSinceMs < 0) _badSinceMs = now;

        switch (State)
        {
            case TrackingState.Tracking:
                Set(TrackingState.Uncertain, reason);
                break;
            case TrackingState.Uncertain:
                if (now - _badSinceMs >= _timings.LostAfterMs) Set(TrackingState.Lost, reason);
                else Reason = reason; // not shown yet, no need to fire Changed
                break;
            default:
                // not tracking at the moment anyway (starting, recovering, lost...), no reason to wait
                Set(TrackingState.Lost, reason);
                break;
        }
    }

    public void CheckTimeout()
    {
        // the tracker takes a few seconds to load MediaPipe, so only after the first frame
        if (_lastFrameMs < 0 || State is TrackingState.NotResponding or TrackingState.Stopped or TrackingState.Paused) return;
        if (_nowMs() - _lastFrameMs >= _timings.NotRespondingAfterMs)
        {
            _goodStreak = 0;
            Set(TrackingState.NotResponding, null);
        }
    }

    public void OnTrackerStopped(string reason) => Set(TrackingState.Stopped, reason);

    private void Set(TrackingState state, string? reason)
    {
        if (state == State && reason == Reason) return;
        State = state;
        Reason = reason;
        Changed?.Invoke();
    }

    /// <summary>What to tell the user: a short headline and what to do about it.</summary>
    public static (string Headline, string Hint) Describe(TrackingState state, string? reason) => state switch
    {
        TrackingState.Starting => ("Starting camera...", "This can take a few seconds"),
        TrackingState.Tracking or TrackingState.Uncertain => ("Tracking", ""),
        TrackingState.Recovering => ("Face found", "Hold still for a moment"),
        TrackingState.NotResponding => ("Tracker not responding", "Check the camera is connected, or restart mouSEE"),
        TrackingState.Stopped => ("Tracker stopped", "Restart mouSEE"),
        TrackingState.Paused => ("Paused", "Press Ctrl+Alt+P to continue"),
        _ => reason switch
        {
            "no_face" => ("Can't see your face", "Sit in front of the camera"),
            "face_partial" => ("Face is cut off", "Move to the middle of the camera's view"),
            "head_turned" => ("Head turned too far", "Face the screen"),
            "eyes_not_found" => ("Can't find your eyes", "Check the lighting and that nothing is in front of your eyes"),
            "too_far" => ("Too far from the camera", "Move closer"),
            "eyes_covered" => ("Eyes are covered", "Make sure nothing is in front of your eyes"),
            "eyes_closed" => ("Eyes closed", "Open your eyes to continue"),
            _ => ("Tracking lost", reason ?? ""),
        },
    };
}
