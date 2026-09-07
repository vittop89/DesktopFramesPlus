using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Desktop_Frames.Localization;

namespace Desktop_Frames
{
    /// <summary>
    /// Picks a colour that is not one of the thirteen.
    ///
    /// The result is a hex string, and that is deliberate: everywhere a frame colour is
    /// stored it is stored as a name, and a name is a string. Handing back "#3A6EA5"
    /// rather than inventing a second kind of setting means the frame file, the global
    /// option and every place that reads a colour keep working untouched - the only
    /// thing that had to learn anything is the one function that turns a name into a
    /// colour.
    /// </summary>
    public static class CustomColorPicker
    {
        /// <summary>The value the "pick a colour" entry carries, which is never saved.</summary>
        private const string PickTag = "__pick__";

        /// <summary>
        /// Adds "pick a colour" to a dropdown of colour names, and makes room for one
        /// that was picked before.
        ///
        /// The entry is a doorway, not a value: choosing it opens the picker and is then
        /// replaced by whatever came back, because saving the doorway as a colour would
        /// leave the frame with no colour at all. Cancelling puts the previous choice
        /// back, so opening the picker and changing your mind costs nothing.
        ///
        /// Shared by the frame's settings and the program's, because a colour is a
        /// colour and two implementations would drift.
        /// </summary>
        public static void AttachTo(ComboBox combo, string current = null)
        {
            if (!string.IsNullOrWhiteSpace(current) && current.TrimStart().StartsWith("#")
                && Find(combo, current) == null)
            {
                ComboBoxItem restored = MakeItem(current);
                combo.Items.Add(restored);
                combo.SelectedItem = restored;
            }

            combo.Items.Add(new ComboBoxItem { Content = Strings.LblCustomColorPick, Tag = PickTag });

            object previous = combo.SelectedItem;

            combo.SelectionChanged += (s, e) =>
            {
                if ((combo.SelectedItem as ComboBoxItem)?.Tag as string != PickTag)
                {
                    previous = combo.SelectedItem;
                    return;
                }

                string from = (previous as ComboBoxItem)?.Tag as string ?? "Gray";
                string picked = Show(Window.GetWindow(combo), from);

                if (string.IsNullOrEmpty(picked))
                {
                    combo.SelectedItem = previous;
                    return;
                }

                ComboBoxItem item = Find(combo, picked);

                if (item == null)
                {
                    item = MakeItem(picked);

                    // Before the doorway, so it stays last where somebody expects it.
                    combo.Items.Insert(combo.Items.Count - 1, item);
                }

                combo.SelectedItem = item;
                previous = item;
            };
        }

        /// <summary>The entry for a colour already in the list, or null.</summary>
        public static ComboBoxItem Find(ComboBox combo, string value)
        {
            foreach (object entry in combo.Items)
                if (entry is ComboBoxItem item
                    && string.Equals(item.Tag as string, value, StringComparison.OrdinalIgnoreCase))
                    return item;

            return null;
        }

        /// <summary>An entry showing the colour itself, since "#3A6EA5" tells nobody much.</summary>
        public static ComboBoxItem MakeItem(string value)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(2),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Background = new SolidColorBrush(Utility.GetColorFromName(value)),
                VerticalAlignment = VerticalAlignment.Center
            };

            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(swatch);
            row.Children.Add(new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center });

            return new ComboBoxItem { Content = row, Tag = value };
        }

        /// <summary>
        /// Shows the picker over <paramref name="owner"/>, starting from
        /// <paramref name="current"/>. Returns the chosen colour as "#RRGGBB", or null
        /// when nothing was chosen.
        /// </summary>
        public static string Show(Window owner, string current)
        {
            Color start = Utility.GetColorFromName(current);
            if (start.A == 0) start = Colors.SteelBlue;

            Color chosen = start;
            double shade = 0;

            var window = new Window
            {
                Title = Strings.LblCustomColorPick,
                Width = 380,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null
                    ? WindowStartupLocation.CenterOwner
                    : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };

            var layout = new StackPanel { Margin = new Thickness(18) };

            var preview = new Border
            {
                Height = 54,
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = Brushes.Gray,
                Margin = new Thickness(0, 0, 0, 12)
            };

            var readout = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12),
                FontFamily = new FontFamily("Consolas")
            };

            // The thirteen the program already offers, as a starting point. Most of the
            // time somebody wants one of these but lighter, and asking them to find it
            // again in a full colour wheel would be a step backwards.
            var swatches = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };

            var slider = new Slider
            {
                Minimum = -80,
                Maximum = 80,
                Value = 0,
                TickFrequency = 10,
                IsSnapToTickEnabled = false,
                Margin = new Thickness(0, 0, 0, 4)
            };

            void Refresh()
            {
                Color shown = Shade(chosen, shade);
                preview.Background = new SolidColorBrush(shown);
                readout.Text = ToHex(shown);
            }

            foreach (string name in Utility.NamedColors)
            {
                Color swatchColour = Utility.GetColorFromName(name);

                var swatch = new Border
                {
                    Width = 26,
                    Height = 26,
                    Margin = new Thickness(0, 0, 5, 5),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                    BorderBrush = Brushes.Gray,
                    Background = new SolidColorBrush(swatchColour),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = name
                };

                swatch.MouseLeftButtonUp += (s, e) =>
                {
                    chosen = swatchColour;
                    slider.Value = 0;
                    shade = 0;
                    Refresh();
                };

                swatches.Children.Add(swatch);
            }

            slider.ValueChanged += (s, e) =>
            {
                shade = slider.Value;
                Refresh();
            };

            var wheel = new Button
            {
                Content = Strings.LblCustomColorWheel,
                Height = 28,
                Margin = new Thickness(0, 0, 0, 12)
            };

            wheel.Click += (s, e) =>
            {
                // The system's own picker, which has the wheel, the eyedropper and the
                // saved custom colours somebody already has. Rewriting that would be a
                // worse version of a dialog everyone already knows.
                using var dialog = new System.Windows.Forms.ColorDialog
                {
                    FullOpen = true,
                    Color = System.Drawing.Color.FromArgb(chosen.R, chosen.G, chosen.B)
                };

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                chosen = Color.FromRgb(dialog.Color.R, dialog.Color.G, dialog.Color.B);
                slider.Value = 0;
                shade = 0;
                Refresh();
            };

            layout.Children.Add(preview);
            layout.Children.Add(readout);
            layout.Children.Add(swatches);
            layout.Children.Add(wheel);
            layout.Children.Add(new TextBlock { Text = Strings.LblCustomColorShade, Opacity = 0.8 });
            layout.Children.Add(slider);

            string result = null;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var cancel = new Button { Content = Strings.BtnCancel, Width = 96, Height = 28, IsCancel = true };
            cancel.Click += (s, e) => window.Close();

            var ok = new Button
            {
                Content = Strings.BtnSave,
                Width = 96,
                Height = 28,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = true
            };

            ok.Click += (s, e) =>
            {
                result = ToHex(Shade(chosen, shade));
                window.Close();
            };

            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            layout.Children.Add(buttons);

            Refresh();

            window.Content = layout;
            window.ShowDialog();

            return result;
        }

        /// <summary>
        /// Moves a colour towards white or towards black.
        ///
        /// Done on the channels rather than by converting to a hue-lightness model,
        /// because the effect wanted here is "the same colour, lighter" and mixing
        /// towards white is exactly that. A proper lightness conversion would also shift
        /// saturation, which reads as a different colour rather than a paler one.
        /// </summary>
        private static Color Shade(Color colour, double amount)
        {
            if (Math.Abs(amount) < 0.5) return colour;

            double factor = Math.Abs(amount) / 100.0;

            byte Mix(byte channel, byte towards) =>
                (byte)Math.Round(channel + (towards - channel) * factor);

            return amount > 0
                ? Color.FromRgb(Mix(colour.R, 255), Mix(colour.G, 255), Mix(colour.B, 255))
                : Color.FromRgb(Mix(colour.R, 0), Mix(colour.G, 0), Mix(colour.B, 0));
        }

        private static string ToHex(Color colour) =>
            string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}",
                colour.R, colour.G, colour.B);
    }
}
