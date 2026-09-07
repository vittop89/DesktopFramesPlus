using Desktop_Frames.Localization;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The plugin's own settings window.
    ///
    /// Separate from the plugin because the two have nothing to say to each other
    /// beyond the settings dictionary: the plugin draws a frame and refreshes it,
    /// this draws a form. Keeping them in one file is how the other plugins grew
    /// past a thousand lines.
    /// </summary>
    public static class AgendaSettingsWindow
    {
        public static void Show(Window? ownerWindow, dynamic frameData, Dictionary<string, object>? settings)
        {
            var window = new Window
            {
                Title = Strings.AgendaSettingsTitle,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = ownerWindow != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = ownerWindow,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(20) };

            layout.Children.Add(new TextBlock
            {
                Text = Strings.AgendaSettingsTitle,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 12)
            });

            layout.Children.Add(new TextBlock
            {
                Text = AgendaCredentials.AreAvailable()
                    ? Strings.AgendaSignedOut
                    : Strings.AgendaNotConfigured,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var close = new Button
            {
                Content = Strings.BtnClose,
                Width = 100,
                Height = 30,
                IsCancel = true
            };
            close.Click += (s, e) => window.Close();

            buttons.Children.Add(close);
            layout.Children.Add(buttons);

            window.Content = layout;
            window.ShowDialog();
        }
    }
}
