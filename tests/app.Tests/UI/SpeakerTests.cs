namespace app.Tests;

public class SpeakerTests
{
    // closing a window disposes its speaker, often while something is being said. that crashed the app
    [Fact]
    public async Task Saying_after_dispose_is_a_cancel_not_a_crash()
    {
        var speaker = new Speaker();
        speaker.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => speaker.SayAsync("hello", CancellationToken.None));
        speaker.SayNow("hello");
        speaker.Stop();
        speaker.Dispose();
    }
}
