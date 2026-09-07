using System.Windows;
using System.Windows.Media;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// The site in a window of its own.
    ///
    /// Used only when the frame cannot carry the page itself - a frame built with
    /// AllowsTransparency is drawn into a bitmap that no native child window appears in,
    /// and a browser is a native child window. A web page frame is created opaque for
    /// exactly that reason, so this is the fallback for the frames that were not.
    /// </summary>
    public class WebPageWindow : Window
    {
        /// <summary>Where the window was when it closed, for the frame to remember.</summary>
        public WebPageSettings.Rect? Placement { get; private set; }

        public WebPageWindow(WebPageSite site, WebPageSettings.Rect? bounds, bool onTop)
        {
            Title = site.Name;
            Width = bounds?.Width ?? 980;
            Height = bounds?.Height ?? 740;
            MinWidth = 360;
            MinHeight = 300;
            Topmost = onTop;
            ShowInTaskbar = true;
            Background = Brushes.White;
            Content = new WebPageBrowser(site);

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

            Closing += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                    Placement = new WebPageSettings.Rect(Left, Top, Width, Height);

                (Content as WebPageBrowser)?.Stop();
            };
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
