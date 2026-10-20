using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace app.Calibration.GazeGrid;

// The grid screen's run: the 20 points, then the head step (leaning and tilting, guided). See GridWindow.xaml.cs.
public partial class GridWindow
{
    // =====================================================================
    // The sequence
    // =====================================================================

    private async void StartSequence()
    {
        if (_phase != Phase.Intro) return; // Space on a focused button can fire twice
        await RunSequenceAsync();
    }

    private async Task RunSequenceAsync()
    {
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _sessionStarted = true;
        _sessionEnded = false;
        _dwellMs = _settings.Current.Calibration.TargetDwellMs; // same for every target in a run
        _backendCompletedTargets = null;
        _backendTotalTargets = null;
        _backendStatus = null;
        _calibrationRatios.Clear();
        _allTotal = 0;
        _allReady = 0;
        _sessionClock = Stopwatch.StartNew();
        RefreshBackendText();

        ShowPhase(Phase.Running);
        CalibrationStarted?.Invoke();

        try
        {
            _restHead.Clear();
            _recordingRest = true;
            for (int i = 0; i < Targets.Length; i++)
            {
                var (x, y) = Targets[i];
                ProgressText.Text = $"Point {i + 1} of {Targets.Length}";
                _calibrationRatios.Add(await RunTargetAsync(new CalibrationTarget(i, x, y), snap: i == 0, ct));
            }
            _recordingRest = false;
            _restReference = _restHead.Count == 0 ? null
                : (Stats.Percentile(_restHead.Select(h => h.FaceX).ToList(), 50), Stats.Percentile(_restHead.Select(h => h.Pitch).ToList(), 50));
            // then the head step: dots while leaning left and right and with the head tilted up and down. The eyes are on a
            // known dot while the head is somewhere else, which shows the fit what leaning and tilting do (GazeFit.FitHead).
            // each new position is said and shown in the middle first, it's a different kind of step and easy to miss otherwise
            LeanText.Visibility = Visibility.Visible;
            try
            {
                string? pose = null;
                int index = HeadStepFirstIndex;
                foreach (var (p, x, y) in HeadPose.Step)
                {
                    if (p != pose)
                    {
                        string say = HeadPose.Say(p);
                        if (pose is null) say = "Last step: your head. " + say;
                        pose = p;
                        LeanText.Text = HeadPose.Say(p);
                        ProgressText.Text = "Last step: move your head and look at each dot";
                        // guide toward the new position right away, not the last one while the instruction is read out
                        StartGuiding(p);
                        await _speaker.SayAsync(say, ct);
                        await WaitForPoseAsync(p, ct);
                    }
                    await RunTargetAsync(new CalibrationTarget(index++, x, y, Pose: p), snap: false, ct);
                }
            }
            finally
            {
                LeanText.Visibility = PoseHintText.Visibility = Visibility.Collapsed;
                _guidingPose = null;
            }
            Speaker.Ding();
            _ = _speaker.SayAsync("Done. Sit normally again.", CancellationToken.None);

            CalibrationCompleted?.Invoke(); // the backend can fit the mapping now
        }
        catch (OperationCanceledException)
        {
            return; // window is closing; don't touch the UI
        }

        _sessionEnded = true;
        _sessionClock.Stop();
        ShowPhase(Phase.Result);
        RefreshResult();
    }

    // shows "lean a little more / a little less / good" live until the head has been where the pose wants it for a moment,
    // and keeps showing it during the pose's dots. Without a reference (no face during the grid points) or if it takes
    // too long, it goes on anyway: the fit leaves out what didn't move enough
    // switches the live hint to this pose: no hint until the next frame says how far off the head is
    private void StartGuiding(string pose)
    {
        _guidingPose = pose;
        _poseInRange = false;
        PoseHintText.Text = "";
        PoseHintText.Visibility = _restReference is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task WaitForPoseAsync(string pose, CancellationToken ct)
    {
        if (_guidingPose != pose) StartGuiding(pose);
        if (_restReference is null)
        {
            await Task.Delay(HeadMoveSettleMs, ct);
            return;
        }

        var clock = Stopwatch.StartNew();
        long inSince = -1;
        bool hinted = false;
        while (clock.ElapsedMilliseconds < PoseTimeoutMs)
        {
            long now = clock.ElapsedMilliseconds;
            if (_poseInRange)
            {
                if (inSince < 0) inSince = now;
                if (now - inSince >= PoseHoldMs)
                {
                    _speaker.SayNow("Good, hold it there.");
                    return;
                }
            }
            else
            {
                inSince = -1;
                // say what to do once, for when the screen is hard to read with the head tilted
                if (!hinted && now >= PoseHintAfterMs)
                {
                    _speaker.SayNow(HeadPose.Spoken(PoseHintText.Text));
                    hinted = true;
                }
            }
            await Task.Delay(50, ct);
        }
    }

    // Moves to the target, lets the eye settle, then fills the ring. Returns the share of capture frames with both eyes tracked.
    private async Task<double> RunTargetAsync(CalibrationTarget target, bool snap, CancellationToken ct, int? dwellMs = null)
    {
        SetRingProgress(0);

        await MoveTargetAsync(target.NormalizedX, target.NormalizedY, snap, ct);
        TargetPresented?.Invoke(target);

        await SettleAsync(ct);
        TargetCaptureStarted?.Invoke(target);

        double ratio = await DwellAsync(ct, dwellMs ?? _dwellMs);
        TargetAccepted?.Invoke(target);
        return ratio;
    }

    private async Task MoveTargetAsync(double x, double y, bool snap, CancellationToken ct)
    {
        double left = x * Root.ActualWidth - TargetVisual.Width / 2;
        double top = y * Root.ActualHeight - TargetVisual.Height / 2;

        if (snap)
        {
            // a finished glide from a previous run keeps overriding the local value until cleared
            TargetVisual.BeginAnimation(Canvas.LeftProperty, null);
            TargetVisual.BeginAnimation(Canvas.TopProperty, null);
            Canvas.SetLeft(TargetVisual, left);
            Canvas.SetTop(TargetVisual, top);
            return;
        }

        double fromLeft = Canvas.GetLeft(TargetVisual);
        double fromTop = Canvas.GetTop(TargetVisual);

        // set the final value first, then animate toward it
        Canvas.SetLeft(TargetVisual, left);
        Canvas.SetTop(TargetVisual, top);

        var duration = TimeSpan.FromMilliseconds(GlideMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        TargetVisual.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(fromLeft, left, duration) { EasingFunction = ease });
        TargetVisual.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(fromTop, top, duration) { EasingFunction = ease });

        await Task.Delay(GlideMs, ct);
    }

    private async Task SettleAsync(CancellationToken ct)
    {
        // big logo shrinking onto the point draws the eye to the center
        var shrink = new DoubleAnimation(StartScale, 1.0, TimeSpan.FromMilliseconds(SettleMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        TargetScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        TargetScale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);

        await Task.Delay(SettleMs, ct);
    }

    private async Task<double> DwellAsync(CancellationToken ct, int dwellMs)
    {
        _dwellTotal = 0;
        _dwellReady = 0;
        _capturing = true;
        try
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < dwellMs)
            {
                SetRingProgress(sw.ElapsedMilliseconds / (double)dwellMs);
                await Task.Delay(16, ct);
            }
            SetRingProgress(1);
        }
        finally
        {
            _capturing = false;
        }

        _allTotal += _dwellTotal;
        _allReady += _dwellReady;
        return _dwellTotal > 0 ? _dwellReady / (double)_dwellTotal : 0;
    }

    // Ellipse dashes are measured in multiples of the stroke thickness, so the visible arc is
    // progress * (circumference / thickness) long, followed by a gap bigger than the remainder.
    private void SetRingProgress(double progress)
    {
        double t = CaptureRing.StrokeThickness;
        double length = Math.PI * (CaptureRing.Width - t) / t;
        CaptureRing.StrokeDashArray = new DoubleCollection { Math.Clamp(progress, 0, 1) * length, length + 1 };
    }
}
