using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// A frame that opens a site in a window of its own - Google Keep, Todoist, a
    /// webmail, whatever somebody puts in the address.
    ///
    /// The frame is the way in, not the container. That is not a simplification: a frame
    /// is created with AllowsTransparency and as a NonActivatingWindow, and a browser
    /// needs both a surface a native window can draw on and the keyboard focus that
    /// window refuses. So the frame holds a button and the site gets a real window.
    ///
    /// Nothing here reads or writes the site's content. The page is the site's own, the
    /// session is the site's own, and this plugin only decides where it is allowed to
    /// go.
    /// </summary>
    public class WebPagePlugin : IFramePlugin
    {
        public string PluginId => "WebPage";

        public string DisplayName => "Web page";

        // In development, like the agenda: it is new, and the setting that gates these
        // is the honest place to say so.
        public int DevelopmentState => 3;

        private WebPageSettings _settings = new WebPageSettings();

        private StackPanel? _panel;
        private TextBlock? _caption;
        private Button? _open;

        /// <summary>
        /// The window while it is open, so a second click raises the one that exists
        /// instead of starting a second browser on the same profile - which Chromium
        /// will not share, and which would fail confusingly.
        /// </summary>
        private WebPageWindow? _window;

        public FrameworkElement CreateVisualElement()
        {
            _panel = new StackPanel
            {
                Margin = new Thickness(12),
                VerticalAlignment = VerticalAlignment.Center
            };

            _caption = new TextBlock
            {
                FontSize = 13,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            _open = new Button
            {
                Content = Strings.WebPageOpen,
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _open.Click += (s, e) => Open();

            _panel.Children.Add(_caption);
            _panel.Children.Add(_open);

            return _panel;
        }

        public void Initialize(FrameworkElement visual, Dictionary<string, object> settings)
        {
            _settings = WebPageSettings.Read(settings);
            Describe();
        }

        private void Describe()
        {
            if (_caption == null || _open == null) return;

            _caption.Text = _settings.IsConfigured ? _settings.Site.Name : Strings.WebPageNotConfigured;
            _open.IsEnabled = _settings.IsConfigured;
        }

        private void Open()
        {
            if (!_settings.IsConfigured) return;

            if (_window != null)
            {
                // Already there, possibly behind something or minimised.
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;

                _window.Activate();
                return;
            }

            var window = new WebPageWindow(_settings.Site, _settings.Bounds, _settings.AlwaysOnTop);
            _window = window;

            window.Closed += (s, e) =>
            {
                // Where it was left is worth keeping, but only in memory: writing it
                // needs the frame's data, which arrives with the settings window. It
                // reaches disk the next time anything else is saved.
                if (window.Placement is WebPageSettings.Rect where) _settings.Bounds = where;

                _window = null;
            };

            window.Show();
        }

        public void ShowSettingsWindow(Window ownerWindow, dynamic frameData)
        {
            if (!WebPageSettingsWindow.Show(ownerWindow, _settings)) return;

            Describe();
            Persist(frameData);

            // A site change makes the open window the wrong one. Closing it is clearer
            // than leaving yesterday's page in front of today's setting.
            _window?.Close();
        }

        private void Persist(dynamic frameData)
        {
            try
            {
                var stored = new Dictionary<string, object>();
                _settings.WriteTo(stored);

                if (frameData is Newtonsoft.Json.Linq.JObject asJson)
                    asJson["PluginSettings"] = Newtonsoft.Json.Linq.JObject.FromObject(stored);
                else
                    ((IDictionary<string, object>)frameData)["PluginSettings"] = stored;

                FrameDataManager.SaveFrameData();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.Settings,
                    $"WebPage: could not save the frame settings: {ex.Message}");
            }
        }

        public void Pause()
        {
            // Nothing to pause. The frame holds a button; the window, if it is open, is
            // the person's own window and closing it because a frame scrolled out of
            // sight would lose whatever they were typing.
        }

        public void Resume()
        {
        }

        public void Cleanup()
        {
            _window?.Close();
            _window = null;
        }
    }
}
