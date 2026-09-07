using System;
using System.Collections.Generic;

namespace Desktop_Frames.Plugins.WebPage
{
    /// <summary>
    /// A site the frame can open, and the hosts its window is allowed to reach.
    ///
    /// The second half is the point. A window that follows every link becomes a browser
    /// with no address bar and no way out - which is both a poor browser and a good
    /// place to be phished, since somebody watching a page inside "their notes frame"
    /// has no way to see they have been sent elsewhere. Anything off the list opens in
    /// the real browser instead, where the address is visible.
    /// </summary>
    public class WebPageSite
    {
        public string Name { get; set; } = string.Empty;

        public string Address { get; set; } = string.Empty;

        /// <summary>
        /// Host suffixes the window may navigate to. A leading dot is not required:
        /// "google.com" also allows "keep.google.com", but never "notgoogle.com".
        /// </summary>
        public IReadOnlyList<string> Hosts { get; set; } = new List<string>();

        /// <summary>
        /// A folder name for this site's browser profile - cookies, storage, the signed-in
        /// session. Kept apart per site so signing out of one does not sign out of another,
        /// and so one site cannot read another's storage.
        /// </summary>
        public string Slug { get; set; } = string.Empty;

        /// <summary>
        /// The sites offered by name. Anything else is reached by typing an address, which
        /// then only trusts its own host.
        /// </summary>
        public static IReadOnlyList<WebPageSite> Presets { get; } = new List<WebPageSite>
        {
            new WebPageSite
            {
                Name = "Google Keep",
                Address = "https://keep.google.com/",
                Slug = "keep",

                // Signing in leaves keep.google.com for Google's account pages and comes
                // back, so those have to be allowed or the sign-in dead-ends.
                Hosts = new List<string> { "keep.google.com", "accounts.google.com", "accounts.youtube.com" }
            },
            new WebPageSite
            {
                Name = "Google Tasks",
                Address = "https://tasks.google.com/",
                Slug = "tasks",
                Hosts = new List<string> { "tasks.google.com", "accounts.google.com" }
            },
            new WebPageSite
            {
                Name = "Todoist",
                Address = "https://app.todoist.com/",
                Slug = "todoist",
                Hosts = new List<string> { "todoist.com" }
            }
        };

        /// <summary>
        /// A site for an address somebody typed. It trusts its own host and nothing else,
        /// which is the safest thing that can be assumed about an address nobody vetted.
        /// </summary>
        public static WebPageSite ForAddress(string address)
        {
            if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed))
                return new WebPageSite { Name = address, Address = address, Slug = "custom" };

            return new WebPageSite
            {
                Name = parsed.Host,
                Address = parsed.AbsoluteUri,
                Slug = Slugify(parsed.Host),
                Hosts = new List<string> { parsed.Host }
            };
        }

        /// <summary>Whether the window may go here, or whether it belongs in a real browser.</summary>
        public bool Allows(Uri address)
        {
            if (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp)
                return false;

            foreach (string host in Hosts)
            {
                if (address.Host.Equals(host, StringComparison.OrdinalIgnoreCase)) return true;

                // A suffix match, anchored on a dot so "evilgoogle.com" cannot pass as
                // "google.com".
                if (address.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static string Slugify(string host)
        {
            var kept = new System.Text.StringBuilder(host.Length);

            foreach (char c in host)
                kept.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');

            return kept.ToString();
        }
    }
}
