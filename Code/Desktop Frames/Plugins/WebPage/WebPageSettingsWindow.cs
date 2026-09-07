using System;
using System.Windows;
using System.Windows.Controls;
using Desktop_Frames.Localization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// Which site the frame opens, and whether its window stays in front.
    ///
    /// Returns true when something was changed and saved, so the caller knows whether to
    /// write anything down.
    /// </summary>
    public static class WebPageSettingsWindow
    {
        public static bool Show(Window? owner, WebPageSettings settings)
        {
            var window = new Window
            {
                Title = Strings.WebPageSettingsTitle,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(20) };

            var known = new ComboBox { Height = 26, DisplayMemberPath = nameof(WebPageSite.Name) };
            foreach (WebPageSite preset in WebPageSite.Presets) known.Items.Add(preset);

            layout.Children.Add(Labelled(Strings.WebPageSiteLabel, known));

            var address = new TextBox { Text = settings.Address, Height = 26 };
            layout.Children.Add(Labelled(Strings.WebPageAddressLabel, address));

            // Choosing a name fills the address; typing an address leaves the name alone.
            // The address is the setting - the list is a shortcut to it, not a second
            // source of truth that could disagree with what is written below it.
            known.SelectionChanged += (s, e) =>
            {
                if (known.SelectedItem is WebPageSite chosen) address.Text = chosen.Address;
            };

            var onTop = new CheckBox
            {
                Content = Strings.WebPageOnTop,
                IsChecked = settings.AlwaysOnTop,
                Margin = new Thickness(0, 4, 0, 10)
            };
            layout.Children.Add(onTop);

            var note = new TextBlock
            {
                Text = Strings.WebPageConfinedNote,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Thickness(0, 0, 0, 10)
            };
            layout.Children.Add(note);

            var error = new TextBlock
            {
                Foreground = System.Windows.Media.Brushes.Firebrick,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            layout.Children.Add(error);

            bool saved = false;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button { Content = Strings.BtnCancel, Width = 100, Height = 30, IsCancel = true };
            cancel.Click += (s, e) => window.Close();

            var save = new Button { Content = Strings.BtnSave, Width = 100, Height = 30, IsDefault = true };

            save.Click += (s, e) =>
            {
                string typed = address.Text.Trim();

                // Only web addresses, and only absolute ones. A frame that accepted
                // file:// would be a frame that opens local files on a click.
                if (!Uri.TryCreate(typed, UriKind.Absolute, out Uri? parsed)
                    || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
                {
                    error.Text = Strings.WebPageBadAddress;
                    error.Visibility = Visibility.Visible;
                    return;
                }

                settings.Address = parsed.AbsoluteUri;
                settings.AlwaysOnTop = onTop.IsChecked == true;

                saved = true;
                window.Close();
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(save);
            layout.Children.Add(buttons);

            window.Content = layout;
            window.ShowDialog();

            return saved;
        }

        private static StackPanel Labelled(string caption, FrameworkElement field)
        {
            var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            block.Children.Add(new TextBlock
            {
                Text = caption,
                Margin = new Thickness(0, 0, 0, 4),
                Opacity = 0.8
            });

            block.Children.Add(field);
            return block;
        }
    }
}
