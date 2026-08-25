using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Desktop_Frames
{
    public static class DesktopIconManager
    {
        #region P/Invoke Definitions
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        #endregion
        private static bool _originalDesktopState = true;
        private static bool _isInitialized = false;
        private static bool _isCurrentlyHidden = false;
        private static bool _isShuttingDown = false; // --- NEW: Shutdown Lock ---
        private static Window _dotWindow;

        // --- NEW: Desktop Readiness ---
        // Explorer owns the desktop this class shows and hides, so none of it can
        // be found until Explorer has built it. That is the normal state for the
        // first seconds of a logon, and again for a moment every time Explorer
        // restarts.
        private static bool _exitHandlersAttached = false;
        private static volatile bool _waitingForDesktop = false;
        private static bool? _pendingVisible = null;
        private const int DESKTOP_POLL_MS = 250;
        private const int DESKTOP_WAIT_TIMEOUT_MS = 120000;

        /// <summary>
        /// Records the state of the desktop before the program starts changing it.
        ///
        /// The reading needs Explorer's desktop, and it is not there yet when the
        /// program starts early in the logon or while Explorer is restarting. The
        /// previous version read nothing in that case and marked itself initialized
        /// anyway, so the state it restored on exit was a default nobody had looked
        /// at: a user who keeps the desktop icons hidden got them back, or not,
        /// depending on timing.
        ///
        /// It now waits for the desktop, in the background. Waiting here on the
        /// caller's thread would hold the interface — this runs while the frames are
        /// being built — and the tray icon would appear late for the same reason.
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            AttachExitHandlers();

            // Store the user's actual desktop state before we mess with it
            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                WaitForDesktopInBackground();
                return;
            }

            _originalDesktopState = IsWindowVisible(listView);
            _isInitialized = true;
        }

        /// <summary>
        /// The exit traps, attached once. They are separate from the rest of the
        /// initialization because that part can now be postponed, and a program
        /// that exits while still waiting for the desktop must restore it too.
        /// </summary>
        private static void AttachExitHandlers()
        {
            if (_exitHandlersAttached) return;
            _exitHandlersAttached = true;

            // Safety net: Ensure we restore icons if the program crashes
            AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreOriginalState();

            // --- BUG FIX: Graceful Shutdown Trap ---
            // Catch normal WPF application shutdown (like quitting from the tray icon)
            if (Application.Current != null)
            {
                Application.Current.Exit += (s, e) => RestoreOriginalState();
            }
        }

        /// <summary>
        /// Watches for Explorer's desktop and finishes the initialization the moment
        /// it appears, then applies the visibility that was asked for in the meantime.
        ///
        /// It gives up after two minutes. Past that the desktop is not coming — the
        /// shell is something other than Explorer, or it failed to start — and a
        /// thread polling for the rest of the session would outlast its own purpose.
        /// </summary>
        private static void WaitForDesktopInBackground()
        {
            if (_waitingForDesktop) return;
            _waitingForDesktop = true;

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    for (int waited = 0; waited < DESKTOP_WAIT_TIMEOUT_MS; waited += DESKTOP_POLL_MS)
                    {
                        if (_isShuttingDown) return;

                        IntPtr listView = GetDesktopListView();
                        if (listView != IntPtr.Zero)
                        {
                            _originalDesktopState = IsWindowVisible(listView);
                            _isInitialized = true;

                            // Whatever was requested while the desktop was missing. The
                            // dot window is created through the dispatcher further down,
                            // so this is safe to call from here.
                            bool? pending = _pendingVisible;
                            if (pending.HasValue) SetDesktopIconsVisible(pending.Value);
                            return;
                        }

                        System.Threading.Thread.Sleep(DESKTOP_POLL_MS);
                    }
                }
                catch (Exception)
                {
                    // A desktop that never arrives must not take the program with it.
                }
                finally
                {
                    _waitingForDesktop = false;
                }
            });
        }

        private static IntPtr GetDesktopListView()
        {
            IntPtr progman = FindWindow("Progman", null);
            IntPtr shelldll = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

            if (shelldll == IntPtr.Zero)
            {
                IntPtr workerW = IntPtr.Zero;
                do
                {
                    workerW = FindWindowEx(IntPtr.Zero, workerW, "WorkerW", null);
                    shelldll = FindWindowEx(workerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                } while (shelldll == IntPtr.Zero && workerW != IntPtr.Zero);
            }

            if (shelldll != IntPtr.Zero)
            {
                return FindWindowEx(shelldll, IntPtr.Zero, "SysListView32", null);
            }

            return IntPtr.Zero;
        }

        public static void SetDesktopIconsVisible(bool visible)
        {
            // --- BUG FIX: Prevent late-firing hide commands from executing during app teardown ---
            if (_isShuttingDown) return;

            // Remembered so the wait below can honour it once the desktop exists.
            // Without this the setting was simply dropped when the program started
            // before Explorer, and the icons stayed as they were until something
            // else asked again.
            _pendingVisible = visible;

            if (!_isInitialized) Initialize();

            IntPtr listView = GetDesktopListView();
            if (listView == IntPtr.Zero)
            {
                WaitForDesktopInBackground();
                return;
            }

            ShowWindow(listView, visible ? SW_SHOW : SW_HIDE);
            _isCurrentlyHidden = !visible;
            UpdateDotVisibility();
        }

        public static void ToggleDesktopIcons()
        {
            SetDesktopIconsVisible(_isCurrentlyHidden); // Flip the state
        }

        public static void RestoreOriginalState()
        {
            _isShuttingDown = true; // Lock the visibility engine immediately,
                                    // and stop the wait above if one is running

            if (_isInitialized)
            {
                IntPtr listView = GetDesktopListView();
                if (listView != IntPtr.Zero)
                {
                    // --- BUG FIX: State Pollution Override ---
                    // If a previous crash left the desktop hidden, _originalDesktopState was recorded wrong.
                    // If the user's settings dictate icons should be visible while running, we MUST guarantee they are visible on exit!
                    bool finalState = _originalDesktopState;
                    if (!SettingsManager.HideDesktopElementsOnStart)
                    {
                        finalState = true;
                    }

                    ShowWindow(listView, finalState ? SW_SHOW : SW_HIDE);
                }
                HideDot();
            }
        }

        #region Floating Dot Logic

        public static void UpdateDotVisibility()
        {
            bool shouldShowDot = _isCurrentlyHidden && SettingsManager.ShowDesktopDot;

            if (shouldShowDot)
            {
                ShowDot();
            }
            else
            {
                HideDot();
            }
        }

        private static void ShowDot()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_dotWindow == null)
                {
                    Color accent = Utility.GetColorFromName(SettingsManager.SelectedColor);

                    _dotWindow = new Window
                    {
                        Width = 24,
                        Height = 24,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        ShowInTaskbar = false,
                        Topmost = false,
                        ResizeMode = ResizeMode.NoResize,
                        ShowActivated = false
                    };

                    Border dot = new Border
                    {
                        Background = new SolidColorBrush(accent),
                        CornerRadius = new CornerRadius(12), // Makes it a perfect circle (half of 24)
                        Opacity = 0.5,
                        Cursor = Cursors.Hand
                    };

                    dot.MouseEnter += (s, e) => dot.Opacity = 1.0;
                    dot.MouseLeave += (s, e) => dot.Opacity = 0.5;
                    dot.MouseLeftButtonUp += (s, e) => ToggleDesktopIcons();

                    _dotWindow.Content = dot;

                    // Make it a non-activating window so clicking it doesn't steal focus from frames
                    _dotWindow.SourceInitialized += (s, e) =>
                    {
                        var hwnd = new WindowInteropHelper(_dotWindow).Handle;
                        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE);
                    };
                }

                // Simplified placement: Bottom center, roughly 50px above the bottom edge
                _dotWindow.Left = (SystemParameters.PrimaryScreenWidth - _dotWindow.Width) / 2;
                _dotWindow.Top = SystemParameters.PrimaryScreenHeight - 65; // ~24px dot + 40px taskbar

                if (!_dotWindow.IsVisible)
                {
                    _dotWindow.Show();
                }

                // Keep color updated if they changed it in options
                if (_dotWindow.Content is Border b)
                {
                    b.Background = new SolidColorBrush(Utility.GetColorFromName(SettingsManager.SelectedColor));
                }
            });
        }

        private static void HideDot()
        {
            // --- BUG FIX: Safety check for early startup or late shutdown ---
            if (Application.Current == null || Application.Current.Dispatcher == null) return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_dotWindow != null && _dotWindow.IsVisible)
                {
                    _dotWindow.Hide();
                }
            });
        }

        #endregion
    }
}