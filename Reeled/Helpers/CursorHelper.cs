using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;

namespace Reeled.Helpers;

[ComImport]
[Guid("ac6f5065-90c4-46ce-beb7-05e138e54117")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInputCursorStaticsInterop
{
    void GetIids();
    void GetRuntimeClassName();
    void GetTrustLevel();
    [PreserveSig]
    int CreateFromHCursor(IntPtr hcursor, out IntPtr inputCursor);
}

public static class CursorHelper
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateCursor(IntPtr hInst, int xHotSpot, int yHotSpot, int nWidth, int nHeight, byte[] pvANDPlane, byte[] pvXORPlane);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyCursor(IntPtr hCursor);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CopyIcon(IntPtr hIcon);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    public const uint OCR_NORMAL = 32512;
    public const uint OCR_IBEAM = 32513;
    public const uint OCR_HAND = 32649;
    public const uint SPI_SETCURSORS = 0x0057;

    private static readonly byte[] BlankAndMask = new byte[128];
    private static readonly byte[] BlankXorMask = new byte[128];
    private static IntPtr _hBlankCursor = IntPtr.Zero;
    private static InputCursor? _blankInputCursor;
    private static bool _initAttempted;
    private static bool _isGlobalHidden;
    private static readonly object _syncLock = new();

    static CursorHelper()
    {
        Array.Fill(BlankAndMask, (byte)0xFF);
        Array.Fill(BlankXorMask, (byte)0x00);
    }

    public static IntPtr GetBlankHCursor()
    {
        if (_hBlankCursor == IntPtr.Zero)
        {
            try
            {
                _hBlankCursor = CreateCursor(IntPtr.Zero, 0, 0, 32, 32, BlankAndMask, BlankXorMask);
            }
            catch { }
        }
        return _hBlankCursor;
    }

    public static InputCursor? GetBlankInputCursor()
    {
        if (_blankInputCursor != null) return _blankInputCursor;
        if (_initAttempted) return null;
        _initAttempted = true;

        try
        {
            IntPtr hCursor = GetBlankHCursor();
            if (hCursor != IntPtr.Zero)
            {
                var factory = InputCursor.As<IInputCursorStaticsInterop>();
                int hr = factory.CreateFromHCursor(hCursor, out IntPtr pCursor);
                if (hr == 0 && pCursor != IntPtr.Zero)
                {
                    _blankInputCursor = InputCursor.FromAbi(pCursor);
                }
            }
        }
        catch { }

        return _blankInputCursor;
    }

    public static void SetElementCursor(UIElement? element, InputCursor? cursor)
    {
        if (element == null) return;
        try
        {
            typeof(UIElement).InvokeMember(
                "ProtectedCursor",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.SetProperty,
                null,
                element,
                new object?[] { cursor });
        }
        catch { }
    }

    public static void HideGlobalCursor()
    {
        lock (_syncLock)
        {
            if (_isGlobalHidden) return;
            _isGlobalHidden = true;
        }
    }

    public static void RestoreGlobalCursor()
    {
        lock (_syncLock)
        {
            if (!_isGlobalHidden) return;
            _isGlobalHidden = false;
            try
            {
                SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, 0);
            }
            catch { }
        }
    }
}
