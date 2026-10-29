using System.Text.Json;

namespace app.Tests;

public class TrackerEyeFieldsTests
{
    [Fact]
    public void Eye_check_fields_are_read_from_a_frame()
    {
        // the per-eye part of a frame as eye_extractor.py sends it (trimmed)
        string json = """
            {"type":"frame","t":1,"capture_fps":30,"faces":[{"box":[0,0,1,1],"landmarks":[],"irises":{"left":[0,0],"right":[0,0]},
             "pose":{"yaw":2,"pitch":-9,"roll":0},"blendshapes":{},
             "eyes":{"left":{"offset":{"h":0.1,"v":0},"closed":true,"openness":0.2,"blink":0.31,"contrast":0.42,"iris_ratio":0.93,"covered":false,"width_px":58.5},
                     "gaze":{"raw":{"h":null,"v":null},"h":null,"v":null,"direction":"closed","binocular_error":{"h":null,"v":null},"eyes_used":0}}}]}
            """;
        var msg = JsonSerializer.Deserialize<TrackerMessage>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var frame = FrameSample.From(msg, 1500);

        Assert.True(frame.FaceFound);
        Assert.Equal(-9, frame.Pitch);
        Assert.Equal(new EyeSample(0.93, 0.42, 0.31, 0.2, 58.5, true, false, 0.1, 0), frame.Left);
        Assert.Null(frame.Right); // dropped eyes are missing from the message
        Assert.Equal(30, frame.CaptureFps);
    }

    [Fact]
    public void No_face_is_a_frame_without_a_face()
    {
        var msg = new TrackerMessage("frame", 1, [], null, 0, 0);
        Assert.False(FrameSample.From(msg, 0).FaceFound);
    }
}
