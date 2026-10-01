using System.Runtime.InteropServices;
using GamingLiveTranslator.Utilities;

namespace GamingLiveTranslator.Services.Hotkeys;

public enum HotkeyMode
{
    PushToTalk, // Hold to speak: down = start, up = stop
    Toggle      // Press to start, press again to stop
}

/// <summary>
/// Global Hotkey and Push-to-Talk service using periodic GetAsyncKeyState polling.
/// DOES NOT install low-level system hooks (WH_KEYBOARD_LL / SetWindowsHookEx)
/// to maintain 100% compliance with multiplayer anti-cheat software (EAC, BattlEye, Vanguard).
/// </summary>
public class GlobalHotkeyService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly object _lock = new();
    private Timer? _pollingTimer;
    private bool _isKeyDown;
    private bool _isToggleActive;
    private bool _isDisposed;

    private int _virtualKey = 0x78; // Default: VK_F9
    private string _keyDisplayName = "F9";
    private HotkeyMode _mode = HotkeyMode.PushToTalk;

    // Key recording state
    private bool _isRecording;
    private Action<int, string>? _onKeyRecorded;
    private Action? _onRecordingCancelled;
    private readonly HashSet<int> _keysDownAtRecordingStart = new();

    public int VirtualKey
    {
        get { lock (_lock) return _virtualKey; }
        set
        {
            lock (_lock)
            {
                _virtualKey = value;
                _keyDisplayName = GetKeyName(value);
                _isKeyDown = false;
                _isToggleActive = false;
            }
        }
    }

    public string KeyDisplayName
    {
        get { lock (_lock) return _keyDisplayName; }
    }

    public HotkeyMode Mode
    {
        get { lock (_lock) return _mode; }
        set
        {
            lock (_lock)
            {
                _mode = value;
                _isToggleActive = false;
            }
        }
    }

    public bool IsRunning { get; private set; }
    public bool IsRecordingNewKey => _isRecording;

    /// <summary>
    /// Fired when the hotkey is pressed down (Push-to-Talk) or activated (Toggle).
    /// </summary>
    public event Action? HotkeyPressed;

    /// <summary>
    /// Fired when the hotkey is released (Push-to-Talk) or deactivated (Toggle).
    /// </summary>
    public event Action? HotkeyReleased;

    /// <summary>
    /// Fired whenever toggle state changes in Toggle mode (passes true when activated, false when deactivated).
    /// </summary>
    public event Action<bool>? HotkeyToggled;

    public GlobalHotkeyService(int defaultKey = 0x78, HotkeyMode defaultMode = HotkeyMode.PushToTalk)
    {
        _virtualKey = defaultKey;
        _keyDisplayName = GetKeyName(defaultKey);
        _mode = defaultMode;
    }

    public void Start(int pollingIntervalMs = 35)
    {
        lock (_lock)
        {
            if (IsRunning)
                return;

            // Clear any stale state on start
            _isKeyDown = false;
            _isToggleActive = false;
            _pollingTimer = new Timer(PollKeyStates, null, 0, Math.Clamp(pollingIntervalMs, 20, 100));
            IsRunning = true;
            Logger.Info($"GlobalHotkeyService started polling for [{_keyDisplayName}] (VK: 0x{_virtualKey:X2}) in {_mode} mode at {pollingIntervalMs}ms interval.");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning)
                return;

            _pollingTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _pollingTimer?.Dispose();
            _pollingTimer = null;
            IsRunning = false;

            // If held down when stopped, fire release to guarantee pipeline cleanup
            if (_isKeyDown)
            {
                _isKeyDown = false;
                HotkeyReleased?.Invoke();
            }

            if (_isToggleActive)
            {
                _isToggleActive = false;
                HotkeyToggled?.Invoke(false);
                HotkeyReleased?.Invoke();
            }

            Logger.Info("GlobalHotkeyService stopped.");
        }
    }

    public void Configure(int virtualKey, HotkeyMode mode)
    {
        lock (_lock)
        {
            _virtualKey = virtualKey;
            _keyDisplayName = GetKeyName(virtualKey);
            _mode = mode;
            _isKeyDown = false;
            _isToggleActive = false;
            Logger.Info($"GlobalHotkeyService reconfigured: Key={_keyDisplayName} (0x{virtualKey:X2}), Mode={mode}");
        }
    }

    /// <summary>
    /// Puts the service into interactive key recording mode.
    /// The next valid key or mouse button pressed is recorded.
    /// Pressing Escape cancels recording.
    /// </summary>
    public void StartRecordingNewKey(Action<int, string> onKeyRecorded, Action? onCancelled = null)
    {
        lock (_lock)
        {
            _onKeyRecorded = onKeyRecorded;
            _onRecordingCancelled = onCancelled;
            _keysDownAtRecordingStart.Clear();

            // Snapshot keys currently held down at start of recording to prevent instant trigger
            for (int vKey = 1; vKey <= 254; vKey++)
            {
                if (IsKeyPhysicallyDown(vKey))
                {
                    _keysDownAtRecordingStart.Add(vKey);
                }
            }

            _isRecording = true;
            Logger.Info("Started recording new global hotkey...");
        }
    }

    public void CancelRecording()
    {
        lock (_lock)
        {
            if (!_isRecording)
                return;

            _isRecording = false;
            var cancelCallback = _onRecordingCancelled;
            _onKeyRecorded = null;
            _onRecordingCancelled = null;
            _keysDownAtRecordingStart.Clear();
            cancelCallback?.Invoke();
            Logger.Info("Hotkey recording cancelled by user.");
        }
    }

    private void PollKeyStates(object? state)
    {
        try
        {
            if (_isRecording)
            {
                PollForNewKeyRecord();
                return;
            }

            int targetKey;
            HotkeyMode currentMode;
            lock (_lock)
            {
                targetKey = _virtualKey;
                currentMode = _mode;
            }

            if (targetKey <= 0)
                return;

            bool isCurrentlyDown = IsKeyPhysicallyDown(targetKey);

            if (currentMode == HotkeyMode.PushToTalk)
            {
                if (isCurrentlyDown && !_isKeyDown)
                {
                    // Transition: Up -> Down (Key Pressed)
                    _isKeyDown = true;
                    HotkeyPressed?.Invoke();
                }
                else if (!isCurrentlyDown && _isKeyDown)
                {
                    // Transition: Down -> Up (Key Released)
                    _isKeyDown = false;
                    HotkeyReleased?.Invoke();
                }
            }
            else // Toggle Mode
            {
                if (isCurrentlyDown && !_isKeyDown)
                {
                    // Transition: Up -> Down (Toggle Pressed)
                    _isKeyDown = true;
                    _isToggleActive = !_isToggleActive;

                    if (_isToggleActive)
                    {
                        HotkeyPressed?.Invoke();
                    }
                    else
                    {
                        HotkeyReleased?.Invoke();
                    }

                    HotkeyToggled?.Invoke(_isToggleActive);
                }
                else if (!isCurrentlyDown && _isKeyDown)
                {
                    // Physical key released, ready for next toggle
                    _isKeyDown = false;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Unexpected exception in hotkey polling loop: {ex.Message}");
        }
    }

    private void PollForNewKeyRecord()
    {
        // Scan standard keyboard and mouse virtual keys
        for (int vKey = 1; vKey <= 254; vKey++)
        {
            // Skip invalid or forbidden keys
            if (vKey == 0x5B || vKey == 0x5C || vKey == 0x5D || // Windows keys
                vKey == 0xF0 || vKey == 0xF1 || // OEM specific
                vKey == 0xFF)
            {
                continue;
            }

            bool isDown = IsKeyPhysicallyDown(vKey);
            if (!isDown)
            {
                _keysDownAtRecordingStart.Remove(vKey);
                continue;
            }

            // Key is down. If it was already down when recording started, wait until released
            if (_keysDownAtRecordingStart.Contains(vKey))
                continue;

            // Handle Escape key as Cancel
            if (vKey == 0x1B) // VK_ESCAPE
            {
                CancelRecording();
                return;
            }

            // Ignore primary left-click during recording if it's the click activating the UI
            // But allow side mouse buttons (XBUTTON1, XBUTTON2) and middle button (MBUTTON)
            if (vKey == 0x01) // VK_LBUTTON
            {
                continue;
            }

            // Valid key recorded!
            var keyName = GetKeyName(vKey);
            Action<int, string>? callback;

            lock (_lock)
            {
                _isRecording = false;
                callback = _onKeyRecorded;
                _onKeyRecorded = null;
                _onRecordingCancelled = null;
                _keysDownAtRecordingStart.Clear();
                _virtualKey = vKey;
                _keyDisplayName = keyName;
            }

            Logger.Info($"Recorded new hotkey: {keyName} (0x{vKey:X2})");
            callback?.Invoke(vKey, keyName);
            return;
        }
    }

    /// <summary>
    /// Checks the most-significant bit (MSB 0x8000) of GetAsyncKeyState to determine
    /// if the key is currently physically held down.
    /// </summary>
    private static bool IsKeyPhysicallyDown(int vKey)
    {
        return (GetAsyncKeyState(vKey) & 0x8000) != 0;
    }

    /// <summary>
    /// Translates Win32 Virtual-Key codes into friendly, gamer-readable names.
    /// </summary>
    public static string GetKeyName(int vKey)
    {
        return vKey switch
        {
            // Mouse buttons
            0x01 => "Left Mouse",
            0x02 => "Right Mouse",
            0x04 => "Middle Mouse",
            0x05 => "Mouse 4 (Thumb Back)",
            0x06 => "Mouse 5 (Thumb Forward)",

            // Common control / modifiers
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left Arrow",
            0x26 => "Up Arrow",
            0x27 => "Right Arrow",
            0x28 => "Down Arrow",
            0x2D => "Insert",
            0x2E => "Delete",

            // Alphanumeric keys
            >= 0x30 and <= 0x39 => ((char)vKey).ToString(),
            >= 0x41 and <= 0x5A => ((char)vKey).ToString(),

            // Numpad
            0x60 => "Num 0",
            0x61 => "Num 1",
            0x62 => "Num 2",
            0x63 => "Num 3",
            0x64 => "Num 4",
            0x65 => "Num 5",
            0x66 => "Num 6",
            0x67 => "Num 7",
            0x68 => "Num 8",
            0x69 => "Num 9",
            0x6A => "Num *",
            0x6B => "Num +",
            0x6D => "Num -",
            0x6E => "Num .",
            0x6F => "Num /",

            // Function keys
            0x70 => "F1",
            0x71 => "F2",
            0x72 => "F3",
            0x73 => "F4",
            0x74 => "F5",
            0x75 => "F6",
            0x76 => "F7",
            0x77 => "F8",
            0x78 => "F9",
            0x79 => "F10",
            0x7A => "F11",
            0x7B => "F12",
            0x7C => "F13",
            0x7D => "F14",
            0x7E => "F15",
            0x7F => "F16",
            0x80 => "F17",
            0x81 => "F18",
            0x82 => "F19",
            0x83 => "F20",
            0x84 => "F21",
            0x85 => "F22",
            0x86 => "F23",
            0x87 => "F24",

            // Specific side modifiers
            0xA0 => "Left Shift",
            0xA1 => "Right Shift",
            0xA2 => "Left Ctrl",
            0xA3 => "Right Ctrl",
            0xA4 => "Left Alt",
            0xA5 => "Right Alt",

            // OEM keys
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "` ~ (Tilde)",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",

            _ => $"Key 0x{vKey:X2}"
        };
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
        }
        GC.SuppressFinalize(this);
    }
}
