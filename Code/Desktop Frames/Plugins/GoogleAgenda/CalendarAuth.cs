using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Signing in to Google, and staying signed in.
    ///
    /// Two entrances on purpose. <see cref="TryResumeAsync"/> is the quiet one: it
    /// answers from what is already on disk and never opens a browser, so starting
    /// the program cannot throw a consent page at somebody who only turned their
    /// computer on. <see cref="SignInAsync"/> is the loud one, and only a deliberate
    /// click reaches it.
    /// </summary>
    public static class CalendarAuth
    {
        /// <summary>
        /// Reading and writing events, and nothing else. Not the calendar list, not
        /// settings, not other people's calendars: an application that asks for more
        /// than it uses is one whose consent screen nobody can sensibly agree to.
        /// </summary>
        private static readonly string[] Scopes =
        {
            // Reading and writing events...
            CalendarService.Scope.CalendarEvents,

            // ...and reading which calendars exist. Written out rather than taken from
            // CalendarService.Scope because the client library predates Google's
            // granular calendar scopes and still only offers the broad ones. The
            // alternatives it does offer are worse: CalendarReadonly also hands over
            // settings and every event, and Calendar allows creating and deleting whole
            // calendars. A consent screen should ask for what the program does and
            // nothing past it, so the string is spelled out here on purpose.
            "https://www.googleapis.com/auth/calendar.calendarlist.readonly",


            // Tasks are a separate service with a separate permission. The read and
            // write scope, because ticking one off is the point of showing it.
            Google.Apis.Tasks.v1.TasksService.Scope.Tasks
        };

        /// <summary>
        /// Names the stored token. One account per profile, and profiles are how this
        /// program already separates one set of frames from another - so a second
        /// Google account is a second profile, not a setting.
        /// </summary>
        private const string UserKey = "default";

        /// <summary>
        /// The credential for a session that was already granted, or null when there
        /// is none. Never opens a browser, never blocks on a person.
        /// </summary>
        public static async Task<UserCredential?> TryResumeAsync(CancellationToken token)
        {
            try
            {
                GoogleAuthorizationCodeFlow? flow = CreateFlow();
                if (flow == null) return null;

                TokenResponse stored = await flow.LoadTokenAsync(UserKey, token).ConfigureAwait(false);
                if (stored == null) return null;

                // The library refreshes an expired access token by itself on the first
                // call that needs one; what matters here is that a refresh token is
                // present, because without it the session cannot be revived at all.
                if (string.IsNullOrEmpty(stored.RefreshToken)) return null;

                // A token granted before a permission was added does not carry it, and
                // the failure would otherwise arrive much later as a refused request
                // that looks like a network fault. Better to treat it as signed out and
                // let the person grant the missing permission deliberately.
                if (!CoversEveryScope(stored.Scope))
                {
                    LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                        "GoogleAgenda: the stored session predates a permission this version needs; discarding it.");

                    // Discarded, not merely refused. The library keeps its own idea of
                    // whether a stored token is good enough, and it is more forgiving
                    // than this one: left on disk, the next sign-in would hand the same
                    // token straight back without ever opening a consent page, and every
                    // request would then fail with "insufficient authentication scopes"
                    // - an error nobody could act on, from a button that appeared to work.
                    await new ProfileTokenStore().ClearAsync().ConfigureAwait(false);
                    return null;
                }

                return new UserCredential(flow, UserKey, stored);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"GoogleAgenda: no session to resume ({ex.GetType().Name}: {ex.Message}).");
                return null;
            }
        }

        /// <summary>
        /// Opens the browser, waits for consent, and stores the result.
        ///
        /// Blocks until the person finishes or gives up, which is why the caller must
        /// be off the interface thread and must pass a token it can cancel: a consent
        /// page left open on a second monitor should not be able to hold a frame.
        /// </summary>
        public static async Task<UserCredential> SignInAsync(CancellationToken token)
        {
            ClientSecrets secrets = ReadClientSecrets();
            AgendaCredentials.EnsureStorageFolder();

            return await GoogleWebAuthorizationBroker.AuthorizeAsync(
                secrets,
                Scopes,
                UserKey,
                token,
                new ProfileTokenStore()).ConfigureAwait(false);
        }

        /// <summary>
        /// Tells Google to forget the grant, then forgets it here too.
        ///
        /// The order matters. Revoking needs the token, so the local copy goes second;
        /// and if the network call fails the local copy goes anyway, because a person
        /// who asked to sign out has to end up signed out either way. Google expires
        /// the grant on its own soon enough.
        /// </summary>
        public static async Task SignOutAsync(UserCredential? credential, CancellationToken token)
        {
            if (credential != null)
            {
                try
                {
                    await credential.RevokeTokenAsync(token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                        $"GoogleAgenda: could not revoke the token with Google: {ex.Message}");
                }
            }

            await new ProfileTokenStore().ClearAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Whether a granted session covers everything this version asks for. Google
        /// returns the granted scopes as one space-separated string.
        /// </summary>
        private static bool CoversEveryScope(string? granted)
        {
            if (string.IsNullOrWhiteSpace(granted)) return false;

            var have = new HashSet<string>(
                granted.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.Ordinal);

            return Scopes.All(have.Contains);
        }

        /// <summary>
        /// The flow used to revive a stored session, or null when this installation
        /// has no client to identify itself with.
        /// </summary>
        private static GoogleAuthorizationCodeFlow? CreateFlow()
        {
            if (!AgendaCredentials.AreAvailable()) return null;

            return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = ReadClientSecrets(),
                Scopes = Scopes,
                DataStore = new ProfileTokenStore()
            });
        }

        /// <summary>
        /// The client downloaded from Google Cloud Console, as it sits in the profile.
        /// </summary>
        private static ClientSecrets ReadClientSecrets()
        {
            string path = AgendaCredentials.ClientSecretPath;

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "No Google client has been added to this profile yet.", path);

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                // Google hands out two shapes of this file and names the outer key after
                // the client type. A desktop client is "installed"; a file saved from
                // the wrong client type says "web", and would fail much later with an
                // error about the redirect instead of here, where it can be explained.
                GoogleClientSecrets loaded = GoogleClientSecrets.FromStream(stream);

                if (loaded?.Secrets == null || string.IsNullOrEmpty(loaded.Secrets.ClientId))
                    throw new InvalidDataException(
                        "The Google client file does not contain a desktop client.");

                return loaded.Secrets;
            }
        }
    }
}
