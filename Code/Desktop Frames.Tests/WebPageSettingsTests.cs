using Desktop_Frames.Plugins.WebPage;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Which browser profile a web page frame signs in with.
    ///
    /// A profile is where a session lives, so these rules decide whether two frames can
    /// hold two accounts - and whether a frame that already works is still signed in
    /// after the change that made profiles per frame.
    /// </summary>
    public class WebPageSettingsTests
    {
        private static Dictionary<string, object> Stored(params (string Key, object Value)[] values)
        {
            var stored = new Dictionary<string, object>();
            foreach ((string key, object value) in values) stored[key] = value;
            return stored;
        }

        [Fact]
        public void A_frame_set_up_before_profiles_keeps_the_site_folder()
        {
            WebPageSettings settings = WebPageSettings.Read(Stored(("WebPageAddress", "https://keep.google.com/")));

            // Its session is already there, and a new folder would sign it out.
            Assert.Equal("keep", settings.ProfileFolder);

            settings.EnsureProfile();
            Assert.Equal("keep", settings.ProfileFolder);
        }

        [Fact]
        public void A_new_frame_gets_a_profile_of_its_own()
        {
            var first = new WebPageSettings { Address = "https://keep.google.com/" };
            var second = new WebPageSettings { Address = "https://keep.google.com/" };

            first.EnsureProfile();
            second.EnsureProfile();

            Assert.StartsWith("keep-", first.ProfileFolder);
            Assert.NotEqual(first.ProfileFolder, second.ProfileFolder);
        }

        [Fact]
        public void The_profile_survives_a_save_and_a_read()
        {
            var settings = new WebPageSettings { Address = "https://keep.google.com/" };
            settings.EnsureProfile();

            var stored = new Dictionary<string, object>();
            settings.WriteTo(stored);

            Assert.Equal(settings.ProfileFolder, WebPageSettings.Read(stored).ProfileFolder);
        }

        [Fact]
        public void Another_site_gets_another_profile()
        {
            var settings = new WebPageSettings { Address = "https://keep.google.com/" };
            settings.EnsureProfile();
            string keep = settings.ProfileFolder;

            settings.Address = "https://app.todoist.com/";
            settings.EnsureProfile();

            Assert.NotEqual(keep, settings.ProfileFolder);
            Assert.StartsWith("todoist-", settings.ProfileFolder);
        }

        [Theory]
        [InlineData("..\\..\\Windows")]
        [InlineData("../elsewhere")]
        [InlineData("C:\\temp")]
        [InlineData("")]
        public void A_profile_that_is_not_a_plain_folder_name_is_not_used(string profile)
        {
            WebPageSettings settings = WebPageSettings.Read(Stored(
                ("WebPageAddress", "https://keep.google.com/"),
                ("WebPageProfile", profile)));

            // Read from a file anyone can edit: a path written there must not decide
            // where a browser keeps cookies and a signed-in session.
            Assert.Equal("keep", settings.ProfileFolder);
        }
    }
}
