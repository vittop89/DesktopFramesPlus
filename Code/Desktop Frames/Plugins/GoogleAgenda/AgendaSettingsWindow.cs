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
    /// beyond the session and the settings: the plugin draws a frame and refreshes
    /// it, this draws a form. Keeping them together is how the other plugins grew
    /// past a thousand lines.
    ///
    /// It reads the state from the same <see cref="AgendaSession"/> the frame reads,
    /// and redraws when it changes, so the window and the frame behind it can never
    /// disagree about whether somebody is signed in.
    /// </summary>
    public static class AgendaSettingsWindow
    {
        public static void Show(Window? ownerWindow, AgendaSession session,
                                Dictionary<string, object>? settings)
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

            var status = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(90, 90, 90)),
                Margin = new Thickness(0, 0, 0, 16)
            };
            layout.Children.Add(status);

            var account = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 16)
            };

            var signIn = new Button { Width = 170, Height = 30, Margin = new Thickness(0, 0, 8, 0) };
            signIn.Click += (s, e) => _ = session.SignInAsync();

            var signOut = new Button { Content = Strings.AgendaSignOut, Width = 110, Height = 30 };
            signOut.Click += (s, e) => _ = session.SignOutAsync();

            account.Children.Add(signIn);
            account.Children.Add(signOut);
            layout.Children.Add(account);

            // One place decides what the window says, called on every change, so the
            // form cannot drift from the session it is showing.
            void Draw()
            {
                switch (session.State)
                {
                    case AgendaState.NotConfigured:
                        status.Text = Strings.AgendaNotConfigured;
                        break;
                    case AgendaState.SignedOut:
                        status.Text = Strings.AgendaSignedOut;
                        break;
                    case AgendaState.SigningIn:
                        status.Text = Strings.AgendaSigningIn;
                        break;
                    case AgendaState.SignedIn:
                        status.Text = Strings.AgendaSignedIn;
                        break;
                    case AgendaState.Failed:
                        status.Text = session.LastError ?? Strings.AgendaFailed;
                        break;
                }

                signIn.Content = session.State == AgendaState.SignedIn
                    ? Strings.AgendaSignInAgain
                    : Strings.AgendaSignIn;

                signIn.IsEnabled = session.State != AgendaState.SigningIn
                                && session.State != AgendaState.NotConfigured;

                signOut.IsEnabled = session.State == AgendaState.SignedIn;
            }

            session.Changed += Draw;
            // Leaving the handler attached would keep this window alive for as long as
            // the frame, and every reopening would add another one.
            window.Closed += (s, e) => session.Changed -= Draw;

            Draw();

            var close = new Button
            {
                Content = Strings.BtnClose,
                Width = 100,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsCancel = true
            };
            close.Click += (s, e) => window.Close();
            layout.Children.Add(close);

            window.Content = layout;
            window.ShowDialog();
        }
    }
}
