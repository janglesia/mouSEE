using System.ComponentModel;
using System.Runtime.InteropServices;

namespace app.Input;

/// <summary>Something that can perform a left-click. Lets the dwell/blink triggers (and tests) use a fake.</summary>
public interface IClickInjector
{
    /// <summary>Left-clicks at the current cursor position. True if Windows accepted both the press and the release.</summary>
    bool LeftClick();

    /// <summary>Right-clicks at the current cursor position. True if Windows accepted both the press and the release.</summary>
    bool RightClick();

    /// <summary>Double-clicks (left) at the current cursor position. True if Windows accepted all four events.</summary>
    bool DoubleClick();
}

/// <summary>Performs a real Windows left-click with the Win32 SendInput API, at wherever the cursor is now.</summary>
public sealed class MouseClicker : IClickInjector
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

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

    public bool LeftClick() => Send("left click", MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

    public bool RightClick() => Send("right click", MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);

    // Windows has no "double-click event": it turns two left clicks in the same place, within the
    // system double-click time (GetDoubleClickTime, 500 ms by default), into WM_LBUTTONDBLCLK.
    // Sending all four events in one SendInput call keeps them ordered, back to back, and at one spot.
    public bool DoubleClick() => Send("double click",
        MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);

    /// <summary>Sends the button events in order in one call. True only if Windows accepted every one.</summary>
    private static bool Send(string name, params uint[] flags)
    {
        var inputs = new INPUT[flags.Length];
        for (int i = 0; i < flags.Length; i++)
            inputs[i] = new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = flags[i] } };

        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            System.Diagnostics.Debug.WriteLine(
                $"[click] {name}: SendInput sent {sent}/{inputs.Length}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        return sent == inputs.Length;
    }
}
