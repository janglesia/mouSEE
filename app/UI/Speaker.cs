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
        if (_synth is null)
        {
            // about how long it takes to say, so steps don't start before someone could have read it
            await Task.Delay(400 + 60 * text.Length, ct);
            return;
        }

        _synth.SpeakAsyncCancelAll();
        var done = new TaskCompletionSource();
        var prompt = new Prompt(text);
        void OnCompleted(object? s, SpeakCompletedEventArgs e)
        {
            if (e.Prompt == prompt) done.TrySetResult();
        }
        _synth.SpeakCompleted += OnCompleted;
        try
        {
            _synth.SpeakAsync(prompt);
            using (ct.Register(() => { _synth.SpeakAsyncCancel(prompt); done.TrySetCanceled(); }))
                await done.Task;
        }
        finally
        {
            _synth.SpeakCompleted -= OnCompleted;
        }
    }

    /// <summary>Says it without waiting, for hints during a step.</summary>
    public void SayNow(string text)
    {
        if (_synth is null) return;
        _synth.SpeakAsyncCancelAll();
        _synth.SpeakAsync(text);
    }

    /// <summary>The sound at the end of a step, so people with their eyes closed know it's over.</summary>
    public static void Ding() => SystemSounds.Asterisk.Play();

    public void Stop() => _synth?.SpeakAsyncCancelAll();

    public void Dispose()
    {
        Stop();
        _synth?.Dispose();
    }
}
