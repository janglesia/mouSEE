using System.ComponentModel;
using System.Runtime.InteropServices;

namespace app.Input;

// injects vertical wheel input at OS level, matching normal page scrolling
public interface IScrollInjector
{
    bool Scroll(int wheelDelta);
}

// sends vertical mouse-wheel input through the Windows SendInput API
// positive wheelDelta scrolls upwards. Negative wheelDelta scrolls downwards.
public sealed class MouseScroller : IScrollInjector
{
    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;

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
        public MOUSEINPUT mouseInput;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    public bool Scroll(int wheelDelta)
    {
        var input = new INPUT
        {
            type = INPUT_MOUSE,
            mouseInput = new MOUSEINPUT
            {
                mouseData = unchecked((uint)wheelDelta),
                dwFlags = MOUSEEVENTF_WHEEL,
            },
        };

        uint sent = SendInput(1, [input], Marshal.SizeOf<INPUT>());
        if(sent != 1)
        {
            System.Diagnostics.Debug.WriteLine($"[scroll] SendInput failed: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        return sent == 1;
    }
}