using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// What one frame remembers: which site it opens, which browser profile it signs in
    /// with, and where its window was left.
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
        private const string ProfileKey = "WebPageProfile";

        private static readonly Regex PlainName = new Regex(@"^[\p{L}\p{Nd}-]{1,80}$", RegexOptions.CultureInvariant);

        /// <summary>Empty until somebody chooses, which is why the frame asks before it opens anything.</summary>
        public string Address { get; set; } = string.Empty;

        public bool AlwaysOnTop { get; set; }

        /// <summary>Where the window was last left, or null the first time.</summary>
        public Rect? Bounds { get; set; }

        /// <summary>
        /// This frame's browser profile - its cookies and its signed-in session - as a
        /// folder name.
        ///
        /// One per frame, so that two frames on one site can be signed in to two
        /// accounts: a personal Keep beside a school one. A profile per site made that
        /// impossible, since both frames shared one set of cookies and one account.
        ///
        /// A frame set up before this existed has none recorded, and keeps the site's
        /// shared folder - which is where its session already is.
        /// </summary>
        public string Profile { get; set; } = string.Empty;

        public WebPageSite Site => Resolve(Address);

        /// <summary>
        /// The profile, if it is a plain folder name, or the site's folder if not.
        ///
        /// The value is read from a file anyone can edit, and a name such as "..\.."
        /// would otherwise decide where a browser writes cookies and a session.
        /// </summary>
        public string ProfileFolder => PlainName.IsMatch(Profile) ? Profile : Site.Slug;

        /// <summary>
        /// Gives the frame a profile of its own when it has none yet, or when it has been
        /// pointed at another site, whose session its old profile does not hold.
        /// </summary>
        public void EnsureProfile()
        {
            string slug = Site.Slug;

            if (Profile == slug || Profile.StartsWith(slug + "-", StringComparison.Ordinal)) return;

            Profile = slug + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

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

            if (from.TryGetValue(ProfileKey, out object? profile))
                settings.Profile = profile?.ToString() ?? string.Empty;

            // Set up when profiles were per site: its session is in the site's folder.
            if (settings.Profile.Length == 0 && settings.IsConfigured)
                settings.Profile = settings.Site.Slug;

            return settings;
        }

        public void WriteTo(IDictionary<string, object> into)
        {
            into[AddressKey] = Address;
            into[OnTopKey] = AlwaysOnTop;
            into[ProfileKey] = Profile;

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
