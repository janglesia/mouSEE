using System.Runtime.InteropServices;
using System.Windows;

namespace app.Input;

/// <summary>
/// Moves the actual Windows cursor using the Win32 SetCursorPos API.
/// </summary>
public sealed class CursorController
{
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>
    /// Moves the cursor to normalized coordinates (0-1), where (0,0) is top-left and (1,1) is bottom-right.
    /// </summary>
    /// <param name="x">Normalized X coordinate (0 to 1).</param>
    /// <param name="y">Normalized Y coordinate (0 to 1).</param>
    /// <returns>True if the cursor was moved successfully, false otherwise.</returns>
    public bool MoveToNormalized(double x, double y)
    {
        // Clamp to [0, 1]
        x = Math.Clamp(x, 0.0, 1.0);
        y = Math.Clamp(y, 0.0, 1.0);

        // Convert to screen pixels
        (int screenX, int screenY) = ConvertToScreenCoordinates(x, y);

        return SetCursorPos(screenX, screenY);
    }

    /// <summary>
    /// Converts normalized coordinates (0-1) to screen pixel coordinates.
    /// </summary>
    /// <param name="x">Normalized X coordinate (0 to 1).</param>
    /// <param name="y">Normalized Y coordinate (0 to 1).</param>
    /// <returns>Screen pixel coordinates (x, y).</returns>
    public static (int X, int Y) ConvertToScreenCoordinates(double x, double y)
    {
        int screenX = (int)(x * SystemParameters.PrimaryScreenWidth);
        int screenY = (int)(y * SystemParameters.PrimaryScreenHeight);
        return (screenX, screenY);
    }
}
