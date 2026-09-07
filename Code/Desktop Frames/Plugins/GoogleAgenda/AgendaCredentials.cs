using System;
using System.IO;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Where this installation keeps the Google client it identifies itself with.
    ///
    /// The client is not compiled in. It lives in the profile, next to the frames
    /// and the settings, for the reason that decides most things in this program:
    /// it is portable. A profile that carries its own credentials travels with the
    /// folder onto another machine or a memory stick and still works, and nothing
    /// secret ever reaches the repository.
    ///
    /// It is not a password. Google says as much about installed applications: a
    /// program sitting on someone's computer can always be opened and read, so the
    /// client secret identifies the application and protects nothing. It is kept
    /// out of the repository so that a stranger cannot spend the quota of somebody
    /// else's project, not because reading it would give access to a calendar.
    /// </summary>
    public static class AgendaCredentials
    {
        /// <summary>Folder holding everything this plugin stores, inside the current profile.</summary>
        public static string StorageFolder =>
            Path.Combine(ProfileManager.CurrentProfileDir, "GoogleAgenda");

        /// <summary>
        /// The client downloaded from Google Cloud Console, as imported by the
        /// settings window. The name says what it is, so that somebody finding it
        /// in a backup knows what they are looking at.
        /// </summary>
        public static string ClientSecretPath =>
            Path.Combine(StorageFolder, "google_client.json");

        /// <summary>
        /// True when this installation has a client to identify itself with.
        ///
        /// Only the presence of the file: whether Google accepts it is Google's
        /// answer to give, and asking here would mean a network call before the
        /// frame can draw anything at all.
        /// </summary>
        public static bool AreAvailable()
        {
            try
            {
                return File.Exists(ClientSecretPath);
            }
            catch (Exception)
            {
                // An unreadable profile folder is not a reason to take the frame
                // down with it; the frame will say it is not set up.
                return false;
            }
        }

        /// <summary>Creates the storage folder if it is not there yet.</summary>
        public static void EnsureStorageFolder()
        {
            Directory.CreateDirectory(StorageFolder);
        }
    }
}
