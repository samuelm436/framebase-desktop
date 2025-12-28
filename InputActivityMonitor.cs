using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace framebase_app
{
    public class InputActivityMonitor
    {
        private bool _isMonitoring = false;
        private DispatcherTimer? _activityCheckTimer;
        private DateTime _lastInputActivity = DateTime.MinValue; // no activity until first input
        private bool _wasActiveLastCheck = false; // start as Inaktiv until first input
#pragma warning disable CS0414 // Field is assigned but never used - actually used in line 116
        private bool _hasEverSeenActivity = false; // becomes true after first input
#pragma warning restore CS0414
        private bool _wasRecordingLastCheck = false; // track recording state changes
        
        // check interval and threshold
        private readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(1500); // 1.5s
        private readonly int ChecksBeforeUpload = 10; // 10 * 1.5s = 15s
        private int _inactivityCount = 0;
        private bool _inactivityUploadTriggered = false; // ensure single trigger per inactivity streak

        // Event für Aktivitäts-Status-Änderungen (raised when transition happens)
        public event Action<bool>? ActivityStatusChanged;
        
        // Event für Recording-Status (game in foreground, regardless of input)
        public event Action<bool>? RecordingStatusChanged;
        
        // Event für Upload-Trigger bei Inaktivität
        public event Action? InactivityUploadTriggered;

        // Windows API für Fenster-Detection
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        // Anti-Cheat-kompatible Tastatur-Erkennung
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);


        // XInput für Controller (Anti-Cheat-sicher)
        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern int XInputGetState(int dwUserIndex, ref XINPUT_STATE pState);

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

    // Bekannte Spiele für Detection
    private static readonly string[] GAME_PROCESSES = { "cs2", "FortniteClient-Win64-Shipping", "ForzaHorizon5", "VALORANT-Win64-Shipping", "Cyberpunk2077" };
    private readonly object _targetLock = new();
    private readonly HashSet<string> _targetProcesses = new(StringComparer.OrdinalIgnoreCase);

        // Controller-Status für Änderungserkennung
    private XINPUT_STATE[] _lastControllerStates = new XINPUT_STATE[4];

        public InputActivityMonitor()
        {
            ResetTargetProcessesToDefaults();
        }

        private void ResetTargetProcessesToDefaults()
        {
            lock (_targetLock)
            {
                _targetProcesses.Clear();
                foreach (var proc in GAME_PROCESSES)
                {
                    var normalized = NormalizeProcessName(proc);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        _targetProcesses.Add(normalized);
                    }
                }
            }
        }

        public void SetTargetProcesses(IEnumerable<string>? processes, bool includeDefaults = true)
        {
            lock (_targetLock)
            {
                if (includeDefaults)
                {
                    ResetTargetProcessesToDefaults();
                }
                else
                {
                    _targetProcesses.Clear();
                }

                if (processes == null)
                {
                    return;
                }

                foreach (var process in processes)
                {
                    var normalized = NormalizeProcessName(process);
                    if (!string.IsNullOrEmpty(normalized))
                    {
                        _targetProcesses.Add(normalized);
                    }
                }
            }
        }

        private static string NormalizeProcessName(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return string.Empty;
            }

            var trimmed = processName.Trim();
            var withoutExtension = Path.GetFileNameWithoutExtension(trimmed);
            return withoutExtension.ToLowerInvariant();
        }

        public void StartMonitoring()
        {
            if (_isMonitoring)
            {
                return;
            }

            // Timer for CheckInterval
            _activityCheckTimer = new DispatcherTimer
            {
                Interval = CheckInterval
            };
            _activityCheckTimer.Tick += CheckActivity;
            _activityCheckTimer.Start();

            _isMonitoring = true;
            
            // Immediately check game status on start
            bool gameInForeground = IsGameWindowInForeground();
            _wasRecordingLastCheck = gameInForeground;
            RecordingStatusChanged?.Invoke(gameInForeground);
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            _activityCheckTimer?.Stop();
            _activityCheckTimer = null;

            _isMonitoring = false;
        }

        private void CheckActivity(object? sender, EventArgs e)
        {
            bool hasActivity = false;
            bool gameInForeground = IsGameWindowInForeground();

            // Notify recording state changes (game in foreground = recording)
            if (gameInForeground != _wasRecordingLastCheck)
            {
                _wasRecordingLastCheck = gameInForeground;
                RecordingStatusChanged?.Invoke(gameInForeground);
            }

            // Only consider input when a known game window is in foreground
            if (gameInForeground)
            {
                hasActivity = CheckKeyboardInput() || CheckControllerInput();
            }
            else
            {
                // Reset activity tracking when no game is in foreground
                _hasEverSeenActivity = false;
                _inactivityCount = ChecksBeforeUpload; // Force inactive immediately
            }

            if (hasActivity)
            {
                _inactivityCount = 0;
                _lastInputActivity = DateTime.Now;
                _inactivityUploadTriggered = false; // rearm trigger when activity returns
                _hasEverSeenActivity = true;
            }

            // Display state: Inaktiv at start, then Aktiv until 15s without input
            bool displayActive = _hasEverSeenActivity && (_inactivityCount < ChecksBeforeUpload);
            if (displayActive != _wasActiveLastCheck)
            {
                _wasActiveLastCheck = displayActive;
                ActivityStatusChanged?.Invoke(displayActive);
            }

            // Upload trigger after 10 consecutive inactive checks (15s)
            if (!hasActivity)
            {
                _inactivityCount = Math.Min(_inactivityCount + 1, ChecksBeforeUpload);
                if (_inactivityCount == ChecksBeforeUpload && !_inactivityUploadTriggered)
                {
                    _inactivityUploadTriggered = true;
                    InactivityUploadTriggered?.Invoke();
                }
            }
        }

        private bool CheckKeyboardInput()
        {
            try
            {
                int[] gameKeys = {
                    0x57, 0x41, 0x53, 0x44,
                    0x20, 0x10, 0x11, 0x12,
                    0x51, 0x45, 0x52, 0x54,
                    0x46, 0x47, 0x43, 0x56,
                    0x09, 0x1B, 0x0D,
                    0x31, 0x32, 0x33, 0x34, 0x35
                };

                foreach (int key in gameKeys)
                {
                    if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    {
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckControllerInput()
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    XINPUT_STATE currentState = new XINPUT_STATE();

                    if (XInputGetState(i, ref currentState) == 0)
                    {
                        if (HasControllerStateChanged(_lastControllerStates[i], currentState))
                        {
                            _lastControllerStates[i] = currentState;
                            return true;
                        }
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

    // Mouse input intentionally not considered to avoid menus being treated as active

        private bool HasControllerStateChanged(XINPUT_STATE old, XINPUT_STATE current)
        {
            if (old.Gamepad.wButtons != current.Gamepad.wButtons)
                return true;

            if (Math.Abs(old.Gamepad.bLeftTrigger - current.Gamepad.bLeftTrigger) > 50 ||
                Math.Abs(old.Gamepad.bRightTrigger - current.Gamepad.bRightTrigger) > 50)
                return true;

            const int threshold = 5000;
            if (Math.Abs(old.Gamepad.sThumbLX - current.Gamepad.sThumbLX) > threshold ||
                Math.Abs(old.Gamepad.sThumbLY - current.Gamepad.sThumbLY) > threshold ||
                Math.Abs(old.Gamepad.sThumbRX - current.Gamepad.sThumbRX) > threshold ||
                Math.Abs(old.Gamepad.sThumbRY - current.Gamepad.sThumbRY) > threshold)
                return true;

            return false;
        }

        private bool IsGameWindowInForeground()
        {
            try
            {
                // Get the foreground window
                IntPtr foregroundWindow = GetForegroundWindow();
                if (foregroundWindow == IntPtr.Zero)
                    return false;

                // Get the process ID of the foreground window
                uint foregroundProcessId;
                GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);

                // Get the process name of the foreground window
                using var foregroundProcess = Process.GetProcessById((int)foregroundProcessId);
                string foregroundProcessName = foregroundProcess.ProcessName;

                // Check if foreground process is one of our game processes
                var normalizedForeground = NormalizeProcessName(foregroundProcessName);
                bool isGameProcess;
                lock (_targetLock)
                {
                    isGameProcess = _targetProcesses.Contains(normalizedForeground);
                }

                return isGameProcess;
            }
            catch
            {
                return false;
            }
        }

        public bool ShouldCollectData()
        {
            // Always collect when game is focused - data collection never stops
            return IsGameWindowInForeground();
        }

        public bool ShouldWriteToCsv() => ShouldCollectData();

        public bool IsUserActiveInLastSeconds(int seconds)
        {
            return (DateTime.Now - _lastInputActivity).TotalSeconds < seconds;
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
