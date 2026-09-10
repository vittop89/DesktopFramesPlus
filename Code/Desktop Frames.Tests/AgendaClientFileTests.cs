using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// Which files are accepted as the Google client.
    ///
    /// Written after the first person to try the plugin on another machine found the
    /// sign-in buttons greyed out with no way forward: the client file was only ever
    /// copied into place by hand. Now it is chosen in the settings, and this is what
    /// stands between that dialog and a mistake that would otherwise surface much
    /// later, as a Google error page about redirects.
    /// </summary>
    public class AgendaClientFileTests
    {
        [Fact]
        public void A_desktop_client_is_accepted()
        {
            const string desktop = @"{
                ""installed"": {
                    ""client_id"": ""572346689052-abc.apps.googleusercontent.com"",
                    ""project_id"": ""desktopframes-agenda"",
                    ""auth_uri"": ""https://accounts.google.com/o/oauth2/auth"",
                    ""token_uri"": ""https://oauth2.googleapis.com/token"",
                    ""client_secret"": ""not-really-a-secret"",
                    ""redirect_uris"": [""http://localhost""]
                }
            }";

            Assert.Equal(ClientFileVerdict.Usable, AgendaClientFile.Check(desktop));
        }

        [Fact]
        public void A_web_client_is_named_as_the_wrong_kind()
        {
            // The mistake most likely to be made in Google Cloud Console, and the one
            // whose own failure message - at sign-in - explains it worst.
            const string web = @"{ ""web"": { ""client_id"": ""x.apps.googleusercontent.com"" } }";

            Assert.Equal(ClientFileVerdict.WebClient, AgendaClientFile.Check(web));
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("<html><body>a page saved by mistake</body></html>")]
        [InlineData("[1, 2, 3]")]
        public void Something_that_is_not_a_json_object_is_refused(string text)
        {
            Assert.Equal(ClientFileVerdict.NotJson, AgendaClientFile.Check(text));
        }

        [Theory]
        [InlineData(@"{ ""installed"": { ""project_id"": ""p"" } }")]
        [InlineData(@"{ ""installed"": { ""client_id"": """" } }")]
        [InlineData(@"{ ""installed"": { ""client_id"": ""   "" } }")]
        [InlineData(@"{ ""installed"": { ""client_id"": 42 } }")]
        [InlineData(@"{ ""installed"": ""a string instead of a client"" }")]
        [InlineData(@"{ ""something"": ""else"" }")]
        public void Json_without_a_client_identifier_is_refused(string json)
        {
            Assert.Equal(ClientFileVerdict.NoClientId, AgendaClientFile.Check(json));
        }
    }
}
