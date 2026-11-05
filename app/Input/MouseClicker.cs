using System.ComponentModel;
using System.Runtime.InteropServices;

namespace app.Input;

/// <summary>Something that can perform a left-click. Lets the dwell/blink triggers (and tests) use a fake.</summary>
public interface IClickInjector
{
    /// <summary>Left-clicks at the current cursor position. True if Windows accepted both the press and the release.</summary>
    bool LeftClick();
}

/// <summary>Performs a real Windows left-click with the Win32 SendInput API, at wherever the cursor is now.</summary>
public sealed class MouseClicker : IClickInjector
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public bool LeftClick()
    {
        var inputs = new[]
        {
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTDOWN } },
            new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = MOUSEEVENTF_LEFTUP } },
        };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            System.Diagnostics.Debug.WriteLine(
                $"[click] SendInput sent {sent}/2: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return sent == inputs.Length;
    }
}