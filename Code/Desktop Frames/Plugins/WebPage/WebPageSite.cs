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

                // The whole of google.com rather than the two subdomains the site is
                // reached at. Signing in wanders further than it looks - account
                // chooser, consent, cookie check, two-step verification, and back - and
                // each of those lives somewhere slightly different. Naming the
                // subdomains meant the sign-in finished somewhere unlisted and the
                // window handed the finished session to the real browser.
                //
                // googleusercontent.com is where an attachment opens.
                Hosts = WithGoogleSignIn("google.com", "googleusercontent.com", "youtube.com")
            },
            new WebPageSite
            {
                Name = "Google Tasks",
                Address = "https://tasks.google.com/",
                Slug = "tasks",
                Hosts = WithGoogleSignIn("google.com", "googleusercontent.com")
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

        /// <summary>
        /// The given hosts, plus the sign-in page of every country domain Google has.
        ///
        /// A Google sign-in does not end on accounts.google.com. It then visits the same
        /// page on the account's country domain - accounts.google.it for an Italian one -
        /// to set the session there too, and only then returns to the site. Without these
        /// hosts that step was handed to the real browser, with the session token in its
        /// address, and the sign-in inside the frame never finished.
        ///
        /// Only the accounts page of each, and only the domains Google lists as its own. A
        /// pattern such as "google.*" would also admit whatever somebody manages to
        /// register under some country's domain.
        /// </summary>
        private static List<string> WithGoogleSignIn(params string[] hosts)
        {
            var all = new List<string>(hosts);

            foreach (string country in GoogleDomains.Countries)
                all.Add("accounts." + country);

            return all;
        }

        /// <summary>
        /// Google's domains other than google.com, as Google publishes them at
        /// https://www.google.com/supported_domains.
        ///
        /// A class of its own because the presets above are built while this type is
        /// initialised, and a field declared further down would still be empty then.
        /// </summary>
        private static class GoogleDomains
        {
            public static readonly string[] Countries =
            {
                "google.ad", "google.ae", "google.com.af", "google.com.ag", "google.al", "google.am",
                "google.co.ao", "google.com.ar", "google.as", "google.at", "google.com.au", "google.az",
                "google.ba", "google.com.bd", "google.be", "google.bf", "google.bg", "google.com.bh",
                "google.bi", "google.bj", "google.com.bn", "google.com.bo", "google.com.br", "google.bs",
                "google.bt", "google.co.bw", "google.by", "google.com.bz", "google.ca", "google.cd",
                "google.cf", "google.cg", "google.ch", "google.ci", "google.co.ck", "google.cl",
                "google.cm", "google.cn", "google.com.co", "google.co.cr", "google.com.cu", "google.cv",
                "google.com.cy", "google.cz", "google.de", "google.dj", "google.dk", "google.dm",
                "google.com.do", "google.dz", "google.com.ec", "google.ee", "google.com.eg", "google.es",
                "google.com.et", "google.fi", "google.com.fj", "google.fm", "google.fr", "google.ga",
                "google.ge", "google.gg", "google.com.gh", "google.com.gi", "google.gl", "google.gm",
                "google.gr", "google.com.gt", "google.gy", "google.com.hk", "google.hn", "google.hr",
                "google.ht", "google.hu", "google.co.id", "google.ie", "google.co.il", "google.im",
                "google.co.in", "google.iq", "google.is", "google.it", "google.je", "google.com.jm",
                "google.jo", "google.co.jp", "google.co.ke", "google.com.kh", "google.ki", "google.kg",
                "google.co.kr", "google.com.kw", "google.kz", "google.la", "google.com.lb", "google.li",
                "google.lk", "google.co.ls", "google.lt", "google.lu", "google.lv", "google.com.ly",
                "google.co.ma", "google.md", "google.me", "google.mg", "google.mk", "google.ml",
                "google.com.mm", "google.mn", "google.com.mt", "google.mu", "google.mv", "google.mw",
                "google.com.mx", "google.com.my", "google.co.mz", "google.com.na", "google.com.ng", "google.com.ni",
                "google.ne", "google.nl", "google.no", "google.com.np", "google.nr", "google.nu",
                "google.co.nz", "google.com.om", "google.com.pa", "google.com.pe", "google.com.pg", "google.com.ph",
                "google.com.pk", "google.pl", "google.pn", "google.com.pr", "google.ps", "google.pt",
                "google.com.py", "google.com.qa", "google.ro", "google.ru", "google.rw", "google.com.sa",
                "google.com.sb", "google.sc", "google.se", "google.com.sg", "google.sh", "google.si",
                "google.sk", "google.com.sl", "google.sn", "google.so", "google.sm", "google.sr",
                "google.st", "google.com.sv", "google.td", "google.tg", "google.co.th", "google.com.tj",
                "google.tl", "google.tm", "google.tn", "google.to", "google.com.tr", "google.tt",
                "google.com.tw", "google.co.tz", "google.com.ua", "google.co.ug", "google.co.uk", "google.com.uy",
                "google.co.uz", "google.com.vc", "google.co.ve", "google.co.vi", "google.com.vn", "google.vu",
                "google.ws", "google.rs", "google.co.za", "google.co.zm", "google.co.zw", "google.cat"
            };
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
