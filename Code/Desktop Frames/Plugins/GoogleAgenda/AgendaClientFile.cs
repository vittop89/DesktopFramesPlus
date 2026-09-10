using System.Text.Json;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>What a file offered as the Google client turned out to be.</summary>
    public enum ClientFileVerdict
    {
        /// <summary>A desktop client: the kind this program signs in with.</summary>
        Usable,

        /// <summary>Not JSON at all - usually the wrong file picked in the dialog.</summary>
        NotJson,

        /// <summary>
        /// A client of the "Web application" type. It parses, it looks right, and it
        /// fails at sign-in with a redirect error nobody can act on, so it is caught
        /// here with a sentence that says which kind to create instead.
        /// </summary>
        WebClient,

        /// <summary>JSON, but not a Google client: no client identifier where one belongs.</summary>
        NoClientId,

        /// <summary>Nothing was chosen. Not a bad file - no file, and nothing to complain about.</summary>
        Cancelled
    }

    /// <summary>
    /// Decides whether a file is a Google client this program can use, before it is
    /// copied anywhere.
    ///
    /// Checked on the way in because everything after this point is slow to fail.
    /// A wrong file would sit in the profile looking installed, the frame would
    /// offer to sign in, and the mistake would surface as a browser page from Google
    /// saying something about redirects - several steps and one context switch away
    /// from the choice that caused it.
    ///
    /// Only the shape is checked, not whether Google will accept the client: that is
    /// Google's answer to give, at sign-in, and asking here would mean a network call
    /// to validate a file somebody picked a second ago.
    /// </summary>
    public static class AgendaClientFile
    {
        public static ClientFileVerdict Check(string json)
        {
            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object) return ClientFileVerdict.NotJson;

                // Google Cloud Console writes a desktop client under "installed" and a
                // web one under "web"; the two are otherwise near enough alike to be
                // confused by looking.
                if (root.TryGetProperty("installed", out JsonElement installed))
                    return HasClientId(installed) ? ClientFileVerdict.Usable : ClientFileVerdict.NoClientId;

                if (root.TryGetProperty("web", out _)) return ClientFileVerdict.WebClient;

                return ClientFileVerdict.NoClientId;
            }
            catch (JsonException)
            {
                return ClientFileVerdict.NotJson;
            }
        }

        private static bool HasClientId(JsonElement client) =>
            client.ValueKind == JsonValueKind.Object
            && client.TryGetProperty("client_id", out JsonElement id)
            && id.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(id.GetString());
    }
}
