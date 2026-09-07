using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// The browser, and the rules about where it may go.
    ///
    /// Kept apart from both the things that host it - a frame that can carry it, and a
    /// window for the frames that cannot - because the rules are the same either way and
    /// were worth writing once. What differs between the two is only where the rectangle
    /// sits.
    /// </summary>
    public class WebPageBrowser : Grid
    {
        private readonly WebPageSite _site;
        private readonly WebView2 _view = new WebView2();
        private readonly TextBlock _message = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            TextAlignment = TextAlignment.Center
        };

        public WebPageBrowser(WebPageSite site)
        {
            _site = site;

            _message.Visibility = Visibility.Collapsed;

            Children.Add(_message);
            Children.Add(_view);

            Loaded += async (s, e) => await StartAsync();
        }

        /// <summary>
        /// Brings up the browser and points it at the site.
        ///
        /// Everything that can go wrong here is somebody else's machine rather than a
        /// mistake: the runtime may be missing, the folder may be unwritable, the network
        /// may be down. Each ends as a sentence on screen instead of an exception nobody
        /// sees.
        /// </summary>
        private async System.Threading.Tasks.Task StartAsync()
        {
            if (_view.CoreWebView2 != null) return;

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

                Say(Strings.WebPageNoRuntime);
                return;
            }

            CoreWebView2 core = _view.CoreWebView2;

            // Nothing of this program is exposed to the page. It is somebody else's site:
            // it gets a browser, not a way into the application.
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;

            core.NavigationStarting += (s, e) => Guard(e.Uri, () => e.Cancel = true);

            // A popup belonging to the site stays here. Sending every one of them to the
            // real browser is what carried a finished sign-in out of the frame: the flow
            // opens a window of its own, and handing that away hands away the session.
            core.NewWindowRequested += (s, e) =>
            {
                e.Handled = true;

                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out Uri? wanted) && _site.Allows(wanted))
                {
                    core.Navigate(wanted.AbsoluteUri);
                    return;
                }

                OpenOutside(e.Uri);
            };

            core.ProcessFailed += (s, e) =>
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"WebPage: the browser process failed ({e.ProcessFailedKind}).");

                Say(Strings.WebPageCrashed);
            };

            core.Navigate(_site.Address);
        }

        /// <summary>
        /// Keeps the browser on the site it was opened for.
        ///
        /// Anything else is handed to the real browser and refused here. Not because
        /// other sites are dangerous in themselves, but because a page with no address
        /// bar is a bad place to end up somewhere unexpected - nobody can see where they
        /// are, which is the condition a convincing sign-in page needs.
        /// </summary>
        private void Guard(string uri, Action cancel)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? address))
            {
                cancel();
                return;
            }

            if (_site.Allows(address)) return;

            // The host, never the address. A sign-in redirect carries the authorisation
            // code in its query string, and a log file is not the place for one.
            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                $"WebPage: {address.Host} is not part of {_site.Name}, opening it in the browser.");

            cancel();
            OpenOutside(uri);
        }

        private static void OpenOutside(string uri)
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

        private void Say(string text)
        {
            _view.Visibility = Visibility.Collapsed;
            _message.Text = text;
            _message.Visibility = Visibility.Visible;
        }

        /// <summary>Lets go of the browser process. A frame that closed should not keep one.</summary>
        public void Stop()
        {
            try
            {
                _view.Dispose();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"WebPage: the browser did not shut down cleanly: {ex.Message}");
            }
        }
    }
}
