using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// The site itself, in a window of its own.
    ///
    /// Not inside the frame, and not by choice: a frame is a layered window - it is
    /// created with AllowsTransparency, which makes WPF draw it into a bitmap that no
    /// native child window can appear in - and it is a NonActivatingWindow, which
    /// refuses keyboard focus. A browser needs both. So the frame becomes the way in,
    /// and the page lives here, in an ordinary window that can be typed into.
    /// </summary>
    public class WebPageWindow : Window
    {
        private readonly WebPageSite _site;
        private readonly WebView2 _view = new WebView2();
        private readonly TextBlock _message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40),
            TextAlignment = TextAlignment.Center
        };

        /// <summary>Where the window was when it closed, for the frame to remember.</summary>
        public WebPageSettings.Rect? Placement { get; private set; }

        public WebPageWindow(WebPageSite site, WebPageSettings.Rect? bounds, bool onTop)
        {
            _site = site;

            Title = site.Name;
            Width = bounds?.Width ?? 980;
            Height = bounds?.Height ?? 740;
            MinWidth = 360;
            MinHeight = 300;
            Topmost = onTop;
            ShowInTaskbar = true;
            Background = Brushes.White;

            if (bounds is WebPageSettings.Rect where && OnAScreen(where))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = where.Left;
                Top = where.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var layout = new Grid();
            layout.Children.Add(_message);
            layout.Children.Add(_view);
            Content = layout;

            _message.Visibility = Visibility.Collapsed;

            Loaded += async (s, e) => await StartAsync();
            Closing += (s, e) => Remember();
        }

        /// <summary>
        /// Brings up the browser and points it at the site.
        ///
        /// Everything that can go wrong here is somebody else's machine rather than a
        /// mistake: the runtime may be missing, the folder may be unwritable, the
        /// network may be down. Each ends with a sentence in the window instead of an
        /// exception nobody sees.
        /// </summary>
        private async System.Threading.Tasks.Task StartAsync()
        {
            try
            {
                Directory.CreateDirectory(ProfileFolder);

                CoreWebView2Environment environment =
                    await CoreWebView2Environment.CreateAsync(null, ProfileFolder);

                await _view.EnsureCoreWebView2Async(environment);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"WebPage: the browser component would not start: {ex.Message}");

                Show(Strings.WebPageNoRuntime);
                return;
            }

            CoreWebView2 core = _view.CoreWebView2;

            // Nothing of this program is exposed to the page. It is somebody else's
            // site: it gets a browser, not a way into the application.
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;

            core.NavigationStarting += (s, e) => Guard(e.Uri, () => e.Cancel = true);

            // A link that wants its own window is a link leaving this one. It goes to the
            // real browser, where there is an address bar to read.
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;
                OpenOutside(e.Uri);
            };

            core.ProcessFailed += (s, e) =>
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"WebPage: the browser process failed ({e.ProcessFailedKind}).");

                Show(Strings.WebPageCrashed);
            };

            core.Navigate(_site.Address);
        }

        /// <summary>
        /// Keeps the window on the site it was opened for.
        ///
        /// Anything else is handed to the real browser and refused here. Not because
        /// other sites are dangerous in themselves, but because a window with no address
        /// bar is a bad place to end up somewhere unexpected - nobody can see where they
        /// are, which is exactly the condition a convincing sign-in page needs.
        /// </summary>
        private void Guard(string uri, Action cancel)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? address))
            {
                cancel();
                return;
            }

            if (_site.Allows(address)) return;

            cancel();
            OpenOutside(uri);
        }

        private void OpenOutside(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? address)
                || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    "WebPage: refused to follow a link that is not a web address.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"WebPage: could not open a link in the browser: {ex.Message}");
            }
        }

        /// <summary>The browser profile for this site: its cookies, its storage, its session.</summary>
        private string ProfileFolder =>
            Path.Combine(ProfileManager.CurrentProfileDir, "WebPage", _site.Slug);

        private void Show(string text)
        {
            _view.Visibility = Visibility.Collapsed;
            _message.Text = text;
            _message.Visibility = Visibility.Visible;
        }

        private void Remember()
        {
            if (WindowState != WindowState.Normal) return;

            Placement = new WebPageSettings.Rect(Left, Top, Width, Height);
        }

        /// <summary>
        /// Whether remembered coordinates still land on a screen.
        ///
        /// A laptop that was docked yesterday has fewer screens today, and a window
        /// restored onto one that is gone is a window nobody can reach.
        /// </summary>
        private static bool OnAScreen(WebPageSettings.Rect where)
        {
            foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
            {
                System.Drawing.Rectangle area = screen.WorkingArea;

                if (where.Left < area.Right && where.Left + where.Width > area.Left &&
                    where.Top < area.Bottom && where.Top + where.Height > area.Top)
                    return true;
            }

            return false;
        }
    }
}
