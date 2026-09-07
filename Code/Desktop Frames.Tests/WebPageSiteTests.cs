using Desktop_Frames.Plugins.WebPage;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Which addresses a web page frame may follow.
    ///
    /// This is the security-carrying part of that plugin. The frame has no address
    /// bar, so somebody looking at it cannot tell where they are - which is exactly
    /// the condition a convincing sign-in page needs. The rule that keeps that from
    /// mattering is a suffix match anchored on a dot, and an anchor is the kind of
    /// thing a later tidy-up removes without noticing.
    /// </summary>
    public class WebPageSiteTests
    {
        private static readonly WebPageSite Keep = new WebPageSite
        {
            Name = "Google Keep",
            Address = "https://keep.google.com/",
            Hosts = new List<string> { "google.com" }
        };

        [Theory]
        [InlineData("https://google.com/")]
        [InlineData("https://keep.google.com/")]
        [InlineData("https://accounts.google.com/signin")]
        [InlineData("https://accounts.google.com/v3/signin/identifier?continue=x")]
        public void The_site_and_its_subdomains_are_allowed(string address)
        {
            Assert.True(Keep.Allows(new Uri(address)));
        }

        [Theory]
        [InlineData("https://evilgoogle.com/")]        // the reason for the dot
        [InlineData("https://notgoogle.com/signin")]
        [InlineData("https://google.com.attacker.net/")]
        [InlineData("https://example.com/")]
        public void Anything_else_is_refused(string address)
        {
            Assert.False(Keep.Allows(new Uri(address)));
        }

        [Theory]
        [InlineData("file:///C:/Windows/System32/drivers/etc/hosts")]
        [InlineData("ftp://google.com/")]
        public void Only_web_addresses_are_allowed(string address)
        {
            // A frame that followed file:// would be a frame that opens local files
            // on a click, and the host would even match.
            Assert.False(Keep.Allows(new Uri(address)));
        }

        [Fact]
        public void A_typed_address_trusts_only_its_own_host()
        {
            WebPageSite typed = WebPageSite.ForAddress("https://app.todoist.com/");

            Assert.True(typed.Allows(new Uri("https://app.todoist.com/today")));

            // Not the parent domain: nobody vetted it, and the safest assumption
            // about an address somebody typed is that it means only itself.
            Assert.False(typed.Allows(new Uri("https://todoist.com/")));
            Assert.False(typed.Allows(new Uri("https://other.example/")));
        }

        [Fact]
        public void A_typed_address_gets_a_folder_name_that_is_a_folder_name()
        {
            WebPageSite typed = WebPageSite.ForAddress("https://app.todoist.com/");

            // The slug names a directory holding cookies and a session, so it must
            // not carry dots or slashes out of the host it came from.
            Assert.DoesNotContain('.', typed.Slug);
            Assert.DoesNotContain('/', typed.Slug);
            Assert.NotEmpty(typed.Slug);
        }

        [Fact]
        public void Nonsense_does_not_throw()
        {
            // The address is typed by hand into a settings box, and a frame that
            // refused to open at all would be worse than one that goes nowhere.
            WebPageSite typed = WebPageSite.ForAddress("not an address");

            Assert.False(typed.Allows(new Uri("https://google.com/")));
        }

        [Fact]
        public void Every_preset_trusts_something_and_starts_on_a_web_address()
        {
            Assert.NotEmpty(WebPageSite.Presets);

            foreach (WebPageSite preset in WebPageSite.Presets)
            {
                Assert.NotEmpty(preset.Hosts);
                Assert.NotEmpty(preset.Slug);

                var start = new Uri(preset.Address);

                // A preset that did not trust its own landing page would send the
                // frame straight back out to the browser on opening.
                Assert.True(preset.Allows(start), preset.Name + " does not allow its own address");
            }
        }
    }
}
