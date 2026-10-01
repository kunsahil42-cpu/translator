using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GamingLiveTranslator.Utilities;

/// <summary>
/// Win32 window interop helpers for configuring always-on-top HUD overlays,
/// preventing focus stealing (WS_EX_NOACTIVATE), and enabling true OS-level click-through (WS_EX_TRANSPARENT).
/// </summary>
public static class WindowInteropHelpers
{
    public const int GWL_EXSTYLE = -20;

    // Extended Window Styles
    public const int WS_EX_TRANSPARENT = 0x00000020; // Mouse clicks pass through window to underlying apps
    public const int WS_EX_TOOLWINDOW = 0x00000080;  // Prevents window from appearing in Alt-Tab switch dialog
    public const int WS_EX_NOACTIVATE = 0x08000000;  // Prevents window from stealing foreground focus on display

    #region Win32 P/Invoke

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        if (IntPtr.Size == 8)
            return GetWindowLongPtr64(hWnd, nIndex);
        else
            return new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
        else
            return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    #endregion

    /// <summary>
    /// Applies baseline HUD overlay styles: WS_EX_NOACTIVATE (focus protection)
    /// and WS_EX_TOOLWINDOW (hides from Alt-Tab switcher).
    /// </summary>
    public static bool InitializeOverlayStyles(IntPtr hwnd, bool clickThrough)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            var currentExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            // Always ensure NOACTIVATE and TOOLWINDOW are enabled
            long newExStyle = currentExStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;

            if (clickThrough)
            {
                newExStyle |= WS_EX_TRANSPARENT;
            }
            else
            {
                newExStyle &= ~WS_EX_TRANSPARENT;
            }

            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newExStyle));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Failed to initialize overlay Win32 extended window styles.", ex);
            return false;
        }
    }

    /// <summary>
    /// Dynamically toggles OS-level click-through (WS_EX_TRANSPARENT).
    /// </summary>
    public static bool SetClickThrough(IntPtr hwnd, bool clickThrough)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        try
        {
            var currentExStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();

            long newExStyle;
            if (clickThrough)
            {
                newExStyle = currentExStyle | WS_EX_TRANSPARENT;
            }
            else
            {
                newExStyle = currentExStyle & ~WS_EX_TRANSPARENT;
            }

            SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(newExStyle));
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to toggle click-through (clickThrough={clickThrough}).", ex);
            return false;
        }
    }

    /// <summary>
    /// Gets the HWND for a WPF Window safely. Returns IntPtr.Zero if uninitialized.
    /// </summary>
    public static IntPtr GetHwnd(Window window)
    {
        if (window == null)
            return IntPtr.Zero;

        var helper = new WindowInteropHelper(window);
        return helper.Handle;
    }
}
