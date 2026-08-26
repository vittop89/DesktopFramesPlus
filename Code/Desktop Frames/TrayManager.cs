using Desktop_Frames.Localization;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using WinFormsMouseEventArgs = System.Windows.Forms.MouseEventArgs;


namespace Desktop_Frames
{
    public class TrayManager : IDisposable
    {
        private NotifyIcon _trayIcon;
      
        private bool _disposed;
        public static bool IsStartWithWindows { get; private set; }

        private const string RUN_KEY_PATH = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string APP_NAME = "Desktop Frames +"; // --- FIX: Ensures new registry entries use the correct name ---

        // --- NEW: Logon task ---
        // The same name, so the two never coexist unnoticed. The Run key is kept as
        // the fallback and for reading older installations, but a task is what the
        // checkbox creates now: see SetStartupEntry for why.
        private const string TASK_NAME = "Desktop Frames +";

        private static readonly List<HiddenFrame> HiddenFrames = new List<HiddenFrame>();
    
        private ToolStripMenuItem _showHiddenFramesItem;

        private ToolStripMenuItem _profilesMenuItem;
        public static TrayManager Instance { get; private set; } // Singleton instance

        private bool _areFramesTempHidden = false;
    
        private List<NonActivatingWindow> _tempHiddenFrames = new List<NonActivatingWindow>();

        private bool Showintray = SettingsManager.ShowInTray;

        private const int WM_NCLBUTTONDOWN = 0xA1;

        private const int HT_CAPTION = 0x2;

        private ToolStripMenuItem _automationMenuItem; //
        private ToolStripMenuItem _autoOrganizeMenuItem; // NEW

        private class HiddenFrame
        {
            public string Title { get; set; }
            public NonActivatingWindow Window { get; set; }
        }

        public void UpdateAutomationMenuCheck(bool isChecked)
        {
            if (_automationMenuItem != null)
            {
                // This prevents infinite loops by checking the value first
                if (_automationMenuItem.Checked != isChecked)
                {
                    _automationMenuItem.Checked = isChecked;
                }
            }
        }

        public void UpdateAutoOrganizeMenuCheck(bool isChecked)
        {
            if (_autoOrganizeMenuItem != null)
            {
                if (_autoOrganizeMenuItem.Checked != isChecked)
                {
                    _autoOrganizeMenuItem.Checked = isChecked;
                }
            }
        }

        public TrayManager()
        {
            // 1. AUTO-MIGRATION: Check if we need to move from Shortcut to Registry
            PerformStartupMigration();
           
            // 2. Check status using the NEW logic (Task check + Registry check + Shortcut fallback)
            IsStartWithWindows = CheckIfStartWithWindowsEnabled();

            // 2b. Move an installation that still starts from the Run key onto a task
            UpgradeRunKeyToLogonTask();

            // 3. Start Remote Info System (Runs 25s later)
            RemoteInfoManager.Initialize();

            Instance = this; // Set singleton instance
        }

        private void OnTrayIconDoubleClick(object sender, EventArgs e)
        {
			// 1. If the frame are officially hidden (either by timer or tray), wake them up!
			if (Framemanager._areFramesAutoHidden)
            {
                Framemanager.WakeUpFrames();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Tray Double-Click: Woke up frames.");
            }
            // 2. Otherwise, they are visible, so force them into the official hidden state.
            else
            {
                Framemanager.ForceHideFrames();
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, "Tray Double-Click: Forced frames to hide.");
            }

            UpdateTrayIcon();
        }


        /// <summary>
        /// Handles single click on tray icon - checks for special key combination CTRL+ALT+SHIFT
        /// </summary>
        private void OnTrayIconClick(object sender, System.Windows.Forms.MouseEventArgs e)
        {
            try
            {
                // Only handle left click
                if (e.Button != MouseButtons.Left) return;

                // Check if CTRL+ALT+SHIFT are all pressed
                bool isCtrlPressed = (System.Windows.Forms.Control.ModifierKeys & Keys.Control) == Keys.Control;
                bool isAltPressed = (System.Windows.Forms.Control.ModifierKeys & Keys.Alt) == Keys.Alt;
                bool isShiftPressed = (System.Windows.Forms.Control.ModifierKeys & Keys.Shift) == Keys.Shift;

                if (isCtrlPressed && isAltPressed && isShiftPressed)
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "TrayIcon: CTRL+ALT+SHIFT+Click detected - Exporting registry values");

                    // Execute the registry export function
                    bool success = RegistryHelper.ExportProgramManagementValues();

                    if (success)
                    {
                        // Show notification that export was successful
                        _trayIcon.BalloonTipTitle = Strings.TrayExportDoneTitle;
                        _trayIcon.BalloonTipText = Strings.TrayExportDoneText;
                        _trayIcon.BalloonTipIcon = ToolTipIcon.Info;
                        _trayIcon.ShowBalloonTip(3000); // Show for 3 seconds

                        LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                            "TrayIcon: Registry values export completed successfully");
                    }
                    else
                    {
                        // Show error notification
                        _trayIcon.BalloonTipTitle = Strings.TrayExportFailedTitle;
                        _trayIcon.BalloonTipText = Strings.TrayExportFailedText;
                        _trayIcon.BalloonTipIcon = ToolTipIcon.Error;
                        _trayIcon.ShowBalloonTip(3000);

                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                            "TrayIcon: Registry values export failed");
                    }
                }
                else
                {
                    // Log debug info about key states (only if at least one modifier is pressed)
                    if (isCtrlPressed || isAltPressed || isShiftPressed)
                    {
                        LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                            $"TrayIcon: Single click with modifiers - Ctrl:{isCtrlPressed}, Alt:{isAltPressed}, Shift:{isShiftPressed}");
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"TrayIcon: Error in click handler: {ex.Message}");
            }
        }

        private string GetFocusFrameHotkeyString()
        {
            try
            {
                string mod = SettingsManager.FocusFrameModifier ?? "";
                int key = SettingsManager.FocusFrameKey;

                if (string.IsNullOrWhiteSpace(mod) && key == 0) return "Not Set";

                List<string> parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(mod))
                {
                    // Clean up the string to match standard UI format
                    string formattedMod = mod.Replace("Control", "Ctrl").Replace(", ", "+");
                    parts.Add(formattedMod);
                }

                if (key != 0)
                {
                    // FIX: Use System.Windows.Forms.Keys because it perfectly maps to Win32 Virtual Key codes
                    string keyStr = ((System.Windows.Forms.Keys)key).ToString();

                    // Clean up default enum names (converts "D1" to "1")
                    if (keyStr.StartsWith("D") && keyStr.Length == 2 && char.IsDigit(keyStr[1]))
                        keyStr = keyStr.Substring(1);

                    parts.Add(keyStr);
                }

                return string.Join("+", parts);
            }
            catch
            {
                return "Ctrl+Alt+Z"; // Safe fallback
            }
        }

        public void InitializeTray()
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;

            // Dispose old icon if re-initializing to prevent ghosting
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }

            _trayIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(exePath),
                Visible = true,
                Text = $"Desktop Frames ({ProfileManager.CurrentProfileName})"
            };

            _trayIcon.DoubleClick += OnTrayIconDoubleClick;
            _trayIcon.MouseClick += OnTrayIconClick;

            // Explicitly detach and clear any existing context menu to prevent duplication
            if (_trayIcon.ContextMenuStrip != null)
            {
                var oldMenu = _trayIcon.ContextMenuStrip;
                _trayIcon.ContextMenuStrip = null;
                oldMenu.Dispose();
            }

            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add(Strings.MenuAbout, null, (s, e) => AboutFormManager.ShowAboutForm());
            trayMenu.Items.Add(Strings.MenuOptions, null, (s, e) => OptionsFormManager.ShowOptionsForm());
            trayMenu.Items.Add(new ToolStripSeparator());

            // Profiles Submenu
            _profilesMenuItem = new ToolStripMenuItem(Strings.TrayProfiles);
            trayMenu.Items.Add(_profilesMenuItem);

            // Standalone Automation Toggle with explicit Save
            _automationMenuItem = new ToolStripMenuItem(Strings.LblEnableProfileAutomation) { CheckOnClick = true };
            _automationMenuItem.Checked = SettingsManager.EnableProfileAutomation;
            _automationMenuItem.Click += (s, e) => {
                SettingsManager.EnableProfileAutomation = _automationMenuItem.Checked;
                try { SettingsManager.SaveSettings(); } catch { }
                if (SettingsManager.EnableProfileAutomation) AutomationManager.Start();
            };
            trayMenu.Items.Add(_automationMenuItem);

            trayMenu.Items.Add(new ToolStripSeparator());

            // --- SMART DESKTOP OPTIONS ---
            trayMenu.Items.Add(Strings.BtnSmartDesktopRules, null, (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    new AutoOrganizeForm().ShowDialog();
                }));
            });

            _autoOrganizeMenuItem = new ToolStripMenuItem(Strings.OptAutoOrganize) { CheckOnClick = true };
            _autoOrganizeMenuItem.Checked = SettingsManager.EnableAutoOrganize;
            _autoOrganizeMenuItem.Click += (s, e) =>
            {
                SettingsManager.EnableAutoOrganize = _autoOrganizeMenuItem.Checked;
                try { SettingsManager.SaveSettings(); } catch { }

                if (SettingsManager.EnableAutoOrganize)
                    AutoOrganizeManager.Start();
                else
                    AutoOrganizeManager.Stop();
            };
            trayMenu.Items.Add(_autoOrganizeMenuItem);

            trayMenu.Items.Add(new ToolStripSeparator());
            // --- END SMART DESKTOP OPTIONS ---

            trayMenu.Items.Add(Strings.TrayReloadAllFrames, null, async (s, e) => { await reloadallFrames(); });

            trayMenu.Items.Add(new ToolStripSeparator());

            _showHiddenFramesItem = new ToolStripMenuItem(Strings.TrayShowHiddenFrames) { Enabled = false };
            trayMenu.Items.Add(_showHiddenFramesItem);

            string focusHotkeyStr = GetFocusFrameHotkeyString();
            trayMenu.Items.Add(Strings.Get("TrayFocusFrame", focusHotkeyStr), null, (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    FrameFocusFormManager focusManager = new FrameFocusFormManager();
                    focusManager.ShowDialog();
                }));
            });

            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(Strings.MenuExit, null, (s, e) => System.Windows.Application.Current.Shutdown());

            _trayIcon.ContextMenuStrip = trayMenu;

            UpdateProfilesMenu();
            UpdateHiddenFramesMenu();
            UpdateTrayIcon();
        }


        public static async Task reloadallFrames()
        {
            var waitWindow = new System.Windows.Window
            {
                Title = "Desktop Frames +",
                Width = 300,
                Height = 150,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
                WindowStyle = System.Windows.WindowStyle.None,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250)),
                AllowsTransparency = true,
                Topmost = true
            };

            var mainBorder = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                CornerRadius = new System.Windows.CornerRadius(8),
                Margin = new System.Windows.Thickness(8),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    Direction = 270,
                    ShadowDepth = 4,
                    Opacity = 0.15,
                    BlurRadius = 8
                }
            };

            var waitStack = new System.Windows.Controls.StackPanel
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Orientation = System.Windows.Controls.Orientation.Vertical
            };

            var titleText = new System.Windows.Controls.TextBlock
            {
                Text = "Desktop Frames +",
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 16,
                FontWeight = System.Windows.FontWeights.Medium,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 33, 36)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 0, 10)
            };
            waitStack.Children.Add(titleText);

            var logoImage = new System.Windows.Controls.Image
            {
                Width = 32,
                Height = 32,
                Margin = new System.Windows.Thickness(0, 0, 0, 10),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };

            // FIX 1: Properly dispose the extracted GDI Icon to prevent memory leaks
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceStream = assembly.GetManifestResourceStream("Desktop_Frames.Resources.logo1.png");
                if (resourceStream != null)
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.StreamSource = resourceStream;
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; // Important for stream closing
                    bitmap.EndInit();
                    bitmap.Freeze(); // Make it efficient
                    logoImage.Source = bitmap;
                    resourceStream.Dispose(); // Close stream
                }
                else
                {
                    string exePath = Assembly.GetEntryAssembly().Location;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null)
                            logoImage.Source = icon.ToImageSource();
                    }
                }
            }
            catch
            {
                // Fallback
                try
                {
                    string exePath = Assembly.GetEntryAssembly().Location;
                    using (var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath))
                    {
                        if (icon != null) logoImage.Source = icon.ToImageSource();
                    }
                }
                catch { }
            }
            waitStack.Children.Add(logoImage);

            var waitText = new System.Windows.Controls.TextBlock
            {
                Text = Strings.TrayReloadingFrames,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(95, 99, 104)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center
            };
            waitStack.Children.Add(waitText);

            mainBorder.Child = waitStack;
            waitWindow.Content = mainBorder;
            waitWindow.Show();

            try
            {
                await Task.Run(async () =>
                {
                    // Allow UI to render the wait window
                    await Task.Delay(100);

                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // 1. Close all windows
                        var windows = System.Windows.Application.Current.Windows.OfType<NonActivatingWindow>().ToList();
                        foreach (var frame in windows)
                        {
                            frame.Close();
                        }

                        // 2. Reload Logic (This calls Framemanager)
                        Framemanager.ReloadFrames();

                        // FIX 2: Force Garbage Collection
                        // Since we just closed heavy WPF windows, we force a collection to release 
                        // the memory immediately before loading new ones.
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    });
                });
            }
            catch (Exception ex)
            {
                MessageBoxesManager.ShowOKOnlyMessageBoxFormStatic(Strings.Get("MsgReloadFramesFailed", ex.Message), Strings.DlgError);
            }
            finally
            {
                waitWindow.Close();
                // Ensure the wait window itself is collected
                waitWindow = null;
                GC.Collect();
            }
        }


        public static void AddHiddenFrame(NonActivatingWindow frame)
        {
            if (frame == null || string.IsNullOrEmpty(frame.Title)) return;
			frame.Dispatcher.Invoke(() =>
            {
				frame.Visibility = Visibility.Hidden;
            });

            if (!HiddenFrames.Any(f => f.Title == frame.Title))
            {
                HiddenFrames.Add(new HiddenFrame { Title = frame.Title, Window = frame });
				frame.Visibility = System.Windows.Visibility.Hidden;
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Added frame '{frame.Title}' to hidden list");
                Instance?.UpdateHiddenFramesMenu();
                Instance?.UpdateTrayIcon();
            }
        }

        public static void ShowHiddenFrame(string title)
        {
            // 1. Find the target frame data and update its visibility state immediately
            var FrameData = Framemanager.GetFrameData().FirstOrDefault(f => f.Title == title);
            if (FrameData != null)
            {
                Framemanager.UpdateFrameProperty(FrameData, "IsHidden", "false", $"Unhiding frame '{title}'");
            }

            // 2. Clean up the Tray menu's internal tracking list
            var HiddenFrame = HiddenFrames.FirstOrDefault(f => f.Title == title);
            if (HiddenFrame != null)
            {
                HiddenFrames.Remove(HiddenFrame);
            }

            // 3. Surgically rebuild ONLY this frame (Bypasses all "Zombie Window" bugs)
            if (FrameData != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Passes the specific frame, and 'null' for the target since this isn't a move operation
                    Framemanager.ReloadSpecificFrames(FrameData, null);
                }), System.Windows.Threading.DispatcherPriority.Background);
            }

            LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI, $"Showed frame '{title}' via specific reload");
            Instance?.UpdateHiddenFramesMenu();
            Instance?.UpdateTrayIcon();
        }
        public void UpdateHiddenFramesMenu()
        {
            if (_showHiddenFramesItem == null) return;

            _showHiddenFramesItem.DropDownItems.Clear();
            _showHiddenFramesItem.Enabled = HiddenFrames.Count > 0;

            foreach (var frame in HiddenFrames)
            {
                var menuItem = new ToolStripMenuItem(frame.Title);
                menuItem.Click += (s, e) => ShowHiddenFrame(frame.Title);
                _showHiddenFramesItem.DropDownItems.Add(menuItem);
            }
        }


        public void UpdateProfilesMenu()
        {
            if (_profilesMenuItem == null) return;

            _profilesMenuItem.DropDownItems.Clear();
            string currentProfile = ProfileManager.CurrentProfileName;

            // Get sorted list of profiles
            var profiles = ProfileManager.GetProfiles();

            // 1. List Existing Profiles
            foreach (var profile in profiles)
            {
                // Format: "Default [0]" or "Work [1]"
                string label = $"{profile.Name} [{profile.Id}]";
                var item = new ToolStripMenuItem(label);

                if (string.Equals(profile.Name, currentProfile, StringComparison.OrdinalIgnoreCase))
                {
                    item.Checked = true;
                    item.Enabled = false; // Disable clicking the active one
                }
                else
                {
                    item.Click += (s, e) =>
                    {
                        ProfileManager.SwitchToProfile(profile.Name);
                        // Update the 'Home' profile so automation reverts to this manual choice later
                        ProfileManager.SetManualBaseProfile(profile.Name);
                        _trayIcon.Text = $"Desktop Frames ({profile.Name})";
                        UpdateProfilesMenu();
                    };
                }
                _profilesMenuItem.DropDownItems.Add(item);
            }

            _profilesMenuItem.DropDownItems.Add(new ToolStripSeparator());

            // 2. Quick Action: Create New Profile (Keep this for speed)
            var createItem = new ToolStripMenuItem(Strings.TrayCreateNewProfile);
            createItem.Click += (s, e) =>
            {
                string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter name for new profile:", "New Profile");

                if (!string.IsNullOrWhiteSpace(newName))
                {
                    if (ProfileManager.CreateProfile(newName))
                    {
                        UpdateProfilesMenu();
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.Get("MsgProfileCreated", newName), Strings.DlgSuccess);
                    }
                    else
                    {
                        MessageBoxesManager.ShowOKOnlyMessageBoxForm(Strings.MsgCreateProfileFailed, Strings.DlgError);
                    }
                }
            };
            _profilesMenuItem.DropDownItems.Add(createItem);

            // 3. Full UI: Manage Profiles (The new form)
            var manageItem = new ToolStripMenuItem(Strings.TrayManageProfiles);
            manageItem.Click += (s, e) =>
            {
                // Open the new Manager Window
                var form = new ProfileManagerForm();
                form.ShowDialog();

                // Refresh menu immediately after closing the manager
                // This ensures renames/reorders/deletes are reflected in the tray instantly
                UpdateProfilesMenu();
            };
            _profilesMenuItem.DropDownItems.Add(manageItem);
        }


        // --- NEW METHODS START ---

        // 1. The Public Toggle Method (Called by Options Form)
      
        public void ToggleStartWithWindows(bool enable)
        {
            try
            {
                // A. Update the startup entry (a logon task, or the Run key if the
                //    task cannot be created)
                SetStartupEntry(enable);

                // ====================================================================
                // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
                // Retained to safely scrub older installations of trademarked terms.
                // ====================================================================
                // B. AGGRESSIVE CLEANUP: Clean any lingering shortcuts to enforce registry-only startup.
                string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
                string legacyFencesShortcut = Path.Combine(startupPath, "Desktop Fences.lnk");
                string legacyFramesShortcut = Path.Combine(startupPath, "Desktop Frames +.lnk");

                if (File.Exists(legacyFencesShortcut))
                {
                    File.Delete(legacyFencesShortcut);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, "TrayManager: Legacy 'Frames' shortcut removed.");
                }
                if (File.Exists(legacyFramesShortcut))
                {
                    File.Delete(legacyFramesShortcut);
                    LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.General, "TrayManager: Legacy 'Frames' shortcut removed.");
                }

                IsStartWithWindows = enable;
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings, $"Start with Windows set to: {enable}");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"Failed to toggle Start with Windows: {ex.Message}");
                throw;
            }
        }
        // 2. Migration Logic: Runs once on startup
        private void PerformStartupMigration()
        {
            // If we already flagged this as done in RegistryHelper, stop here.
            if (RegistryHelper.IsStartupMigrated()) return;

            // ====================================================================
            // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
            // Retained to safely scrub older installations of trademarked terms.
            // ====================================================================
            string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string shortcutPath = Path.Combine(startupPath, "Desktop Fences.lnk");

            // If the old shortcut exists, it means the user WANTED start-up enabled.
            // We must transfer that intent to the Registry.
            if (File.Exists(shortcutPath))
            {
                try
                {
                    SetStartupEntry(true); // Create the logon task, or the Run key
                    File.Delete(shortcutPath); // Delete Old Shortcut
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, "TrayManager: Migrated startup from Shortcut to a logon task.");
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General, $"TrayManager: Migration Error: {ex.Message}");
                }
            }

            // Mark as migrated so we don't run this logic again
            RegistryHelper.SetStartupMigrated();
        }

        // 3. Helper to write/delete the startup entry
        //
        // A task that runs at logon, rather than a value under Run.
        //
        // The Run key is read by Explorer, once the shell is up, and Explorer then
        // holds those entries back by another ten seconds by default. For a program
        // whose windows are meant to look like part of the desktop, arriving a
        // quarter of a minute after the desktop is the one thing it should not do.
        // The key also has no order: which of its entries goes first is not
        // something that can be written anywhere.
        //
        // A logon task is started by the Task Scheduler service instead, alongside
        // the shell rather than behind it, and it takes no delay unless one is asked
        // for. It needs no elevation, so enabling the setting still prompts for
        // nothing.
        //
        // If the task cannot be created - a policy that forbids it, the service
        // disabled - the Run key is written instead. Late is better than never, and
        // a checkbox that silently does nothing is worse than either.
        private void SetStartupEntry(bool enable)
        {
            if (!enable)
            {
                DeleteLogonTask();
                SetRegistryStartup(false);
                return;
            }

            if (CreateLogonTask())
            {
                // Exactly one of the two may survive. Both, and the program starts
                // twice at every logon, with the single-instance mutex quietly
                // killing the second copy.
                SetRegistryStartup(false);
                return;
            }

            LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                "TrayManager: Could not create the logon task, using the Run key instead.");
            SetRegistryStartup(true);
        }

        /// <summary>
        /// Moves an installation that still starts from the Run key onto a logon task.
        ///
        /// Creating the task happens when the setting is switched on, and somebody who
        /// already had it on never switches it on again: the box is drawn ticked
        /// because the Run key is there, saving changes nothing, and the program would
        /// go on starting the slow way for ever. The one case the setting cannot
        /// cover is the one every existing installation is in.
        ///
        /// So it happens here instead, once, on its own. If the task cannot be
        /// created the Run key is left exactly where it is, and the program starts as
        /// it did yesterday.
        /// </summary>
        private void UpgradeRunKeyToLogonTask()
        {
            if (!IsStartWithWindows) return;
            if (LogonTaskExists()) return;
            if (!RunKeyEntryExists()) return;

            if (!CreateLogonTask()) return;

            SetRegistryStartup(false);
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.Settings,
                "TrayManager: Startup moved from the Run key to a logon task.");
        }

        private static bool RunKeyEntryExists()
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, false))
            {
                return key != null && key.GetValue(APP_NAME) != null;
            }
        }

        /// <summary>
        /// Registers the task from an XML description.
        ///
        /// The XML is not decoration: the command line form of schtasks cannot say
        /// any of what matters here, and its defaults are wrong for a program that
        /// stays open. It would refuse to start on battery, stop the program when
        /// the machine went onto battery, and terminate it after three days.
        /// </summary>
        private bool CreateLogonTask()
        {
            string xmlPath = null;
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string workingDir = Path.GetDirectoryName(exePath) ?? string.Empty;
                string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

                xmlPath = Path.Combine(Path.GetTempPath(), "DesktopFramesPlus_LogonTask.xml");

                // UTF-16, because schtasks rejects the file otherwise.
                File.WriteAllText(xmlPath, BuildLogonTaskXml(exePath, workingDir, user),
                                  System.Text.Encoding.Unicode);

                return RunSchTasks($"/Create /TN \"{TASK_NAME}\" /XML \"{xmlPath}\" /F", logFailure: true);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.General,
                    $"TrayManager: Logon task creation failed: {ex.Message}");
                return false;
            }
            finally
            {
                try { if (xmlPath != null && File.Exists(xmlPath)) File.Delete(xmlPath); }
                catch (Exception) { /* a leftover temporary file is not worth a failure */ }
            }
        }

        private void DeleteLogonTask()
        {
            // Nothing to report when there was no task: turning the setting off
            // twice, or off before it was ever on, is not a problem.
            RunSchTasks($"/Delete /TN \"{TASK_NAME}\" /F", logFailure: false);
        }

        private static bool LogonTaskExists()
        {
            // "No task by that name" is the answer to a question, not a fault, and it
            // is the answer every time the setting is simply off. Logging it put a
            // warning in the log at every single start.
            return RunSchTasks($"/Query /TN \"{TASK_NAME}\"", logFailure: false);
        }

        /// <summary>
        /// Runs schtasks out of sight and reports whether it succeeded.
        ///
        /// Only the caller knows whether a failure means anything: creating the task
        /// and failing changes what the program does and has to be written down,
        /// while asking whether a task exists and hearing "no" is an ordinary answer.
        /// </summary>
        private static bool RunSchTasks(string arguments, bool logFailure)
        {
            try
            {
                var info = new ProcessStartInfo("schtasks.exe", arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (Process process = Process.Start(info))
                {
                    if (process == null) return false;

                    // Only the error stream is read, and to the end: redirecting both
                    // and reading them one after the other is how a child process ends
                    // up waiting on a full pipe forever.
                    string error = process.StandardError.ReadToEnd();

                    if (!process.WaitForExit(15000))
                    {
                        try { process.Kill(); } catch (Exception) { }
                        return false;
                    }

                    // Warn, not Debug: this is the reason the program will fall back
                    // to the Run key, and a fallback nobody can see is the silent
                    // failure this whole path exists to avoid.
                    if (logFailure && process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
                    {
                        LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                            $"TrayManager: schtasks said: {error.Trim()}");
                    }

                    return process.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                // Always worth a line: schtasks itself failing to run is never normal.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"TrayManager: schtasks could not be run: {ex.Message}");
                return false;
            }
        }

        private static string BuildLogonTaskXml(string exePath, string workingDir, string user)
        {
            string Esc(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;

            return
// Schema 1.2, and only elements that have been in it since Vista. The two
// Windows 8 additions that a task exported from the Task Scheduler window
// carries - UseUnifiedSchedulingEngine and DisallowStartOnRemoteAppSession -
// belong to a later version of the schema and are rejected here. Neither
// changes anything: the unified engine is what modern Windows uses anyway.
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo>\r\n" +
"    <Description>Starts Desktop Frames + when signing in, without the delay Explorer applies to the Run key.</Description>\r\n" +
"  </RegistrationInfo>\r\n" +
"  <Triggers>\r\n" +
"    <LogonTrigger>\r\n" +
"      <Enabled>true</Enabled>\r\n" +
$"      <UserId>{Esc(user)}</UserId>\r\n" +
"    </LogonTrigger>\r\n" +
"  </Triggers>\r\n" +
"  <Principals>\r\n" +
"    <Principal id=\"Author\">\r\n" +
$"      <UserId>{Esc(user)}</UserId>\r\n" +
"      <LogonType>InteractiveToken</LogonType>\r\n" +
"      <RunLevel>LeastPrivilege</RunLevel>\r\n" +
"    </Principal>\r\n" +
"  </Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <AllowHardTerminate>false</AllowHardTerminate>\r\n" +
"    <StartWhenAvailable>false</StartWhenAvailable>\r\n" +
"    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\r\n" +
"    <IdleSettings>\r\n" +
"      <StopOnIdleEnd>false</StopOnIdleEnd>\r\n" +
"      <RestartOnIdle>false</RestartOnIdle>\r\n" +
"    </IdleSettings>\r\n" +
"    <AllowStartOnDemand>true</AllowStartOnDemand>\r\n" +
"    <Enabled>true</Enabled>\r\n" +
"    <Hidden>false</Hidden>\r\n" +
"    <RunOnlyIfIdle>false</RunOnlyIfIdle>\r\n" +
"    <WakeToRun>false</WakeToRun>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>6</Priority>\r\n" +
"  </Settings>\r\n" +
"  <Actions Context=\"Author\">\r\n" +
"    <Exec>\r\n" +
$"      <Command>{Esc(exePath)}</Command>\r\n" +
$"      <WorkingDirectory>{Esc(workingDir)}</WorkingDirectory>\r\n" +
"    </Exec>\r\n" +
"  </Actions>\r\n" +
"</Task>\r\n";
        }

        // 4. Helper to write/delete the Registry Key
        private void SetRegistryStartup(bool enable)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RUN_KEY_PATH, true))
            {
                if (key == null) return;

                if (enable)
                {
                    // We wrap the path in quotes to be safe against spaces in path
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    key.SetValue(APP_NAME, $"\"{exePath}\"");
                }
                else
                {
                    // If disabling, remove the value
                    key.DeleteValue(APP_NAME, false);
                }
            }
        }

        // 5. Status Checker (Replaces IsInStartupFolder)
        private bool CheckIfStartWithWindowsEnabled()
        {
            // The task first, since that is what the setting creates now. The Run key
            // is still read after it: an installation that predates the task, or one
            // where the task could not be created, must show the checkbox ticked.
            if (LogonTaskExists()) return true;

            // Then, check if the Registry Key exists
            if (RunKeyEntryExists()) return true;

            // ====================================================================
            // [LEGACY "FENCES" MIGRATION - DO NOT REMOVE]
            // Retained to safely scrub older installations of trademarked terms.
            // ====================================================================
            // Fallback: Check if the old shortcut exists (in case migration hasn't run yet)
            string startupPath = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return File.Exists(Path.Combine(startupPath, "Desktop Fences.lnk"));
        }
        // --- NEW METHODS END ---


        public void Dispose()
        {
            if (_disposed) return;
            _trayIcon?.Dispose();
            _disposed = true;
        }

        private Icon GenerateIconWithNumber(int count)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            using (var baseIcon = Icon.ExtractAssociatedIcon(exePath))

            using (var bitmap = baseIcon.ToBitmap())
            using (var graphics = Graphics.FromImage(bitmap))
            {
                int circleDiameter = 24;
                int circleX = -4;
                int circleY = -1;

                var circleBrush = new SolidBrush(Color.FromArgb(230, 255, 153, 53));
                graphics.FillEllipse(circleBrush, circleX, circleY, circleDiameter, circleDiameter);

                var font = new Font("Calibri", 26, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
                var textBrush = new SolidBrush(Color.Navy);

                string text = count.ToString();
                var textSize = graphics.MeasureString(text, font);
                float textX = circleX + (circleDiameter - textSize.Width) / 2;
                float textY = circleY + (circleDiameter - textSize.Height) / 2;

                graphics.DrawString(text, font, textBrush, textX, textY);

                return Icon.FromHandle(bitmap.GetHicon());
            }
        }

        public void UpdateTrayIcon()
        {
            if (Showintray == true)
            {
                // FIX: Update the tooltip text to match the current profile
                _trayIcon.Text = $"Desktop Frames + ({ProfileManager.CurrentProfileName})";

                if (HiddenFrames.Count > 0)
                {
                    _trayIcon.Icon = GenerateIconWithNumber(HiddenFrames.Count + _tempHiddenFrames.Count);
                }
                else
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    _trayIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
                }
                _trayIcon.Visible = true;
            }
            else
            {
                _trayIcon.Visible = false; // Properly hide the icon
            }
        }

		/// <summary>
		/// Clears all references to hidden frames. 
		/// Call this when switching profiles or reloading frames to prevent "Zombie" windows.
		/// </summary>
		// Add inside TrayManager class
		public void ClearHiddenFrames()
        {
            HiddenFrames.Clear();
            _tempHiddenFrames.Clear();
            _areFramesTempHidden = false;
            UpdateHiddenFramesMenu();
            UpdateTrayIcon();
        }



    }
}