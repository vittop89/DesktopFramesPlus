using System;
using System.Collections.Generic;
using System.Globalization;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// What one frame remembers: which site it opens, and where its window was left.
    ///
    /// Values travel to disk exactly as written here and never through the translations,
    /// so a frame set up in Italian still opens the same site after the program is
    /// switched to English.
    /// </summary>
    public class WebPageSettings
    {
        private const string AddressKey = "WebPageAddress";
        private const string OnTopKey = "WebPageOnTop";
        private const string BoundsKey = "WebPageBounds";

        /// <summary>Empty until somebody chooses, which is why the frame asks before it opens anything.</summary>
        public string Address { get; set; } = string.Empty;

        public bool AlwaysOnTop { get; set; }

        /// <summary>Where the window was last left, or null the first time.</summary>
        public Rect? Bounds { get; set; }

        public WebPageSite Site => Resolve(Address);

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Address);

        public static WebPageSettings Read(IDictionary<string, object>? from)
        {
            var settings = new WebPageSettings();
            if (from == null) return settings;

            if (from.TryGetValue(AddressKey, out object? address))
                settings.Address = address?.ToString() ?? string.Empty;

            if (from.TryGetValue(OnTopKey, out object? onTop)
                && bool.TryParse(onTop?.ToString(), out bool parsedOnTop))
                settings.AlwaysOnTop = parsedOnTop;

            if (from.TryGetValue(BoundsKey, out object? bounds))
                settings.Bounds = ParseBounds(bounds?.ToString());

            return settings;
        }

        public void WriteTo(IDictionary<string, object> into)
        {
            into[AddressKey] = Address;
            into[OnTopKey] = AlwaysOnTop;

            if (Bounds is Rect where)
                into[BoundsKey] = string.Format(CultureInfo.InvariantCulture, "{0};{1};{2};{3}",
                    Math.Round(where.Left), Math.Round(where.Top),
                    Math.Round(where.Width), Math.Round(where.Height));
        }

        /// <summary>The preset matching this address, or one built for it.</summary>
        public static WebPageSite Resolve(string address)
        {
            foreach (WebPageSite preset in WebPageSite.Presets)
                if (string.Equals(preset.Address, address, StringComparison.OrdinalIgnoreCase))
                    return preset;

            return WebPageSite.ForAddress(address);
        }

        /// <summary>
        /// Read back defensively: the file can be edited by hand, and a frame that
        /// refuses to open because four numbers went missing would be worse than one
        /// that opens where the system puts it.
        /// </summary>
        private static Rect? ParseBounds(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            string[] parts = text.Split(';');
            if (parts.Length != 4) return null;

            var numbers = new double[4];

            for (int i = 0; i < 4; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                    return null;

            if (numbers[2] < 200 || numbers[3] < 200) return null;

            return new Rect(numbers[0], numbers[1], numbers[2], numbers[3]);
        }

        /// <summary>Window placement, kept as plain numbers rather than a WPF type.</summary>
        public readonly struct Rect
        {
            public Rect(double left, double top, double width, double height)
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            public double Left { get; }
            public double Top { get; }
            public double Width { get; }
            public double Height { get; }
        }
    }
}
