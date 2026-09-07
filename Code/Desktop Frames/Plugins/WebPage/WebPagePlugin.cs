using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// A frame showing a site - Google Keep, Todoist, a webmail, whatever address is
    /// put in the settings.
    ///
    /// The page lives in the frame when the frame can carry it. That is not always: a
    /// frame built with AllowsTransparency is drawn into a bitmap, and a browser is a
    /// native child window that never reaches that bitmap. Web page frames are created
    /// opaque so they can, and this plugin looks at the window it was given rather than
    /// assuming - so a frame made before that rule existed still works, with the page in
    /// a window of its own.
    ///
    /// Nothing here reads or writes the site's content. The page and the session are the
    /// site's own; this plugin only decides where the browser is allowed to go.
    /// </summary>
    public class WebPagePlugin : IFramePlugin
    {
        public string PluginId => "WebPage";

        public string DisplayName => "Web page";

        public int DevelopmentState => 3;

        private WebPageSettings _settings = new WebPageSettings();

        private Grid? _root;
        private StackPanel? _launcher;
        private TextBlock? _caption;
        private TextBlock? _hint;
        private Button? _open;

        /// <summary>The page inside the frame, when the frame can hold it.</summary>
        private WebPageBrowser? _inside;

        /// <summary>The page in a window, when it cannot.</summary>
        private WebPageWindow? _window;

        /// <summary>
        /// True once the host window has been found to be opaque, which is the only
        /// condition under which a browser can be seen inside it.
        /// </summary>
        private bool _canHostInside;

        public FrameworkElement CreateVisualElement()
        {
            _root = new Grid();

            _launcher = new StackPanel
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
                Margin = new Thickness(0, 0, 0, 8)
            };

            // Said only when there is nothing to click. A frame offering a dead button
            // teaches nothing; one that says where the setting lives costs a line.
            _hint = new TextBlock
            {
                Text = Strings.WebPageConfigureHint,
                FontSize = 11,
                Foreground = Brushes.White,
                Opacity = 0.7,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center
            };

            _open = new Button
            {
                Height = 30,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };

            _open.Click += (s, e) => OpenWindow();

            _launcher.Children.Add(_caption);
            _launcher.Children.Add(_hint);
            _launcher.Children.Add(_open);

            _root.Children.Add(_launcher);

            return _root;
        }

        public void Initialize(FrameworkElement visual, Dictionary<string, object> settings)
        {
            _settings = WebPageSettings.Read(settings);

            // Asked of the window rather than assumed of the program. The answer decides
            // whether this frame shows a page or a button, and getting it wrong in the
            // hopeful direction means a frame that looks empty for no visible reason.
            visual.Loaded += (s, e) => Attach(visual);

            Describe();
        }

        private void Attach(FrameworkElement visual)
        {
            Window? host = Window.GetWindow(visual);
            if (host == null) return;

            _canHostInside = !host.AllowsTransparency;

            // A frame refuses the keyboard by default, which is right for a frame full of
            // icons and wrong for one holding a page somebody types into. The program
            // already has the switch - the note frames use it to be edited.
            if (_canHostInside && host is NonActivatingWindow frame)
                frame.EnableFocusPrevention(false);

            ShowPage();
            Describe();
        }

        /// <summary>Puts the page in the frame, when the frame can hold one.</summary>
        private void ShowPage()
        {
            if (!_canHostInside || !_settings.IsConfigured || _root == null) return;
            if (_inside != null) return;

            _inside = new WebPageBrowser(_settings.Site);
            _root.Children.Insert(0, _inside);
        }

        private void HidePage()
        {
            if (_inside == null || _root == null) return;

            _inside.Stop();
            _root.Children.Remove(_inside);
            _inside = null;
        }

        /// <summary>
        /// Puts the frame in the shape of what is true right now.
        ///
        /// When the page is inside, the frame is the page and the launcher goes away.
        /// Otherwise the button names the site: "Open" alone is a question - open what? -
        /// and the frame is the only thing that knows the answer.
        /// </summary>
        private void Describe()
        {
            if (_caption == null || _open == null || _hint == null || _launcher == null) return;

            if (_inside != null)
            {
                _launcher.Visibility = Visibility.Collapsed;
                return;
            }

            _launcher.Visibility = Visibility.Visible;

            if (!_settings.IsConfigured)
            {
                _caption.Text = Strings.WebPageNotConfigured;
                _hint.Visibility = Visibility.Visible;
                _open.Visibility = Visibility.Collapsed;
                return;
            }

            string site = _settings.Site.Name;
            bool open = _window != null;

            _caption.Text = open ? site + " — " + Strings.WebPageOpenedNow : site;
            _hint.Visibility = Visibility.Collapsed;
            _open.Visibility = Visibility.Visible;
            _open.Content = Strings.Get(open ? "WebPageShowNamed" : "WebPageOpenNamed", site);
        }

        private void OpenWindow()
        {
            if (!_settings.IsConfigured) return;

            if (_window != null)
            {
                if (_window.WindowState == WindowState.Minimized)
                    _window.WindowState = WindowState.Normal;

                _window.Activate();
                return;
            }

            var window = new WebPageWindow(_settings.Site, _settings.Bounds, _settings.AlwaysOnTop);
            _window = window;

            window.Closed += (s, e) =>
            {
                if (window.Placement is WebPageSettings.Rect where) _settings.Bounds = where;

                _window = null;
                Describe();
            };

            window.Show();
            Describe();
        }

        public void ShowSettingsWindow(Window ownerWindow, dynamic frameData)
        {
            if (!WebPageSettingsWindow.Show(ownerWindow, _settings)) return;

            Persist(frameData);

            // The site changed, so whatever is on screen is the previous one.
            HidePage();
            _window?.Close();

            ShowPage();
            Describe();
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
            // Left running on purpose. A rolled-up frame is still a page somebody may be
            // halfway through writing on, and tearing down the browser to save a frame
            // that is about to be rolled back down would lose it.
        }

        public void Resume()
        {
        }

        public void Cleanup()
        {
            HidePage();

            _window?.Close();
            _window = null;
        }
    }
}
