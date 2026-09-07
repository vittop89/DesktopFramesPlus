using Google.Apis.Json;
using Google.Apis.Util.Store;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Keeps the Google token inside the profile, encrypted for this Windows user.
    ///
    /// The library's own store writes to %APPDATA%, which is wrong twice over here.
    /// This program is portable and keeps its data beside itself, so a token left in
    /// the roaming profile does not travel with the folder and is not removed when
    /// the folder is deleted - it would outlive the installation that created it.
    ///
    /// Encrypted because the refresh token is not a preference: it is standing
    /// access to somebody's calendar, and it sits in a folder made to be copied onto
    /// memory sticks. DPAPI ties it to the Windows account that saved it, so a copy
    /// carried to another machine, or read by another user of this one, is bytes and
    /// nothing more.
    ///
    /// Not a substitute for the machine being trusted: whoever is signed in as this
    /// user can decrypt it, exactly as they could open the browser and read the
    /// calendar there. It removes the copied-folder case, which is the one this
    /// program's own design creates.
    /// </summary>
    public class ProfileTokenStore : IDataStore
    {
        /// <summary>
        /// Mixed into the encryption so a token file cannot be lifted into another
        /// program running as the same user and decrypted there by accident.
        /// </summary>
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes("Desktop Frames + / Google Agenda / token");

        public Task StoreAsync<T>(string key, T value)
        {
            AgendaCredentials.EnsureStorageFolder();

            string json = NewtonsoftJsonSerializer.Instance.Serialize(value);
            byte[] plain = Encoding.UTF8.GetBytes(json);
            byte[] sealed_ = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);

            File.WriteAllBytes(PathFor<T>(key), sealed_);
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            string path = PathFor<T>(key);
            if (!File.Exists(path)) return Task.FromResult(default(T)!);

            try
            {
                byte[] sealed_ = File.ReadAllBytes(path);
                byte[] plain = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(plain);

                return Task.FromResult(NewtonsoftJsonSerializer.Instance.Deserialize<T>(json));
            }
            catch (Exception ex)
            {
                // A token that cannot be read is a token this user does not have:
                // the profile was copied from another machine or another account, or
                // the file is damaged. Answering "nothing stored" sends the caller
                // down the sign-in path, which is the right answer to all three.
                // Throwing here would leave the frame with an error it cannot act on.
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General,
                    $"GoogleAgenda: stored token could not be read, signing in again ({ex.GetType().Name}).");

                TryDelete(path);
                return Task.FromResult(default(T)!);
            }
        }

        public Task DeleteAsync<T>(string key)
        {
            TryDelete(PathFor<T>(key));
            return Task.CompletedTask;
        }

        /// <summary>Removes every token, which is what signing out means.</summary>
        public Task ClearAsync()
        {
            try
            {
                if (Directory.Exists(AgendaCredentials.StorageFolder))
                {
                    foreach (string file in Directory.GetFiles(AgendaCredentials.StorageFolder, "*.token"))
                        TryDelete(file);
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not clear the stored tokens: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// One file per key and type, named so a human looking into the profile can
        /// tell what it is. The key comes from the library and can hold characters a
        /// file name cannot, so anything outside a safe set becomes an underscore.
        /// </summary>
        private static string PathFor<T>(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("A token key is required.", nameof(key));

            string name = typeof(T).Name + "-" + key;
            var safe = new StringBuilder(name.Length);

            foreach (char c in name)
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');

            return Path.Combine(AgendaCredentials.StorageFolder, safe + ".token");
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // A token left behind is not worth failing a sign-out over; the next
                // read will refuse it anyway if it no longer decrypts.
            }
        }
    }
}
