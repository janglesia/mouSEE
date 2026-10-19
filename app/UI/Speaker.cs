using System.Diagnostics;
using System.Media;
using System.Speech.Synthesis;

namespace app.UI;

// Speaks instructions. The eye check needs this: with an eye closed, a hand over it or the head tilted
// back, people can't read the screen (in the recordings the tester fell a whole step behind).
// Without a voice installed it stays quiet but still waits about as long as saying it would take.
public sealed class Speaker : IDisposable
{
    private readonly SpeechSynthesizer? _synth;
    // a window disposes its speaker when it closes, often in the middle of something being said. after that the
    // voice can't be touched at all (it throws ObjectDisposedException, which crashed the app: "Error 127" from make)
    private bool _disposed;
    // what's being said right now, so Dispose can end the wait (a disposed voice never says it's done)
    private TaskCompletionSource? _saying;

    public Speaker()
    {
        try
        {
            _synth = new SpeechSynthesizer();
            _synth.SetOutputToDefaultAudioDevice();
            _synth.Rate = 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[speech] no voice: {ex.Message}");
            _synth?.Dispose();
            _synth = null;
        }
    }

    /// <summary>Says it and returns once it's been said. Stops what was being said before.</summary>
    public async Task SayAsync(string text, CancellationToken ct)
    {
        // the window is closing, stop what it's doing like a cancel would
        if (_disposed) throw new OperationCanceledException(ct);
        if (_synth is null)
        {
            // about how long it takes to say, so steps don't start before someone could have read it
            await Task.Delay(400 + 60 * text.Length, ct);
            return;
        }

        _synth.SpeakAsyncCancelAll();
        var done = new TaskCompletionSource();
        _saying = done;
        var prompt = new Prompt(text);
        void OnCompleted(object? s, SpeakCompletedEventArgs e)
        {
            if (e.Prompt == prompt) done.TrySetResult();
        }
        _synth.SpeakCompleted += OnCompleted;
        try
        {
            _synth.SpeakAsync(prompt);
            using (ct.Register(() =>
                   {
                       if (!_disposed) _synth.SpeakAsyncCancel(prompt);
                       done.TrySetCanceled();
                   }))
                await done.Task;
            // disposed while it was being said: the window closed, same as a cancel
            if (_disposed) throw new OperationCanceledException(ct);
        }
        finally
        {
            if (!_disposed) _synth.SpeakCompleted -= OnCompleted;
            if (_saying == done) _saying = null;
        }
    }

    /// <summary>Says it without waiting, for hints during a step.</summary>
    public void SayNow(string text)
    {
        if (_synth is null || _disposed) return;
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    /// <summary>The sound at the end of a step, so people with their eyes closed know it's over.</summary>
    public static void Ding() => SystemSounds.Asterisk.Play();

    public void Stop()
    {
        if (!_disposed) _synth?.SpeakAsyncCancelAll();
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
        _saying?.TrySetCanceled();
        _synth?.Dispose();
    }
}
