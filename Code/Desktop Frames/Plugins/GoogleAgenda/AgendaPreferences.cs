using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The agenda's own choices, kept in the profile: a colour for each task list, and
    /// the hours of tasks Google does not pass on.
    ///
    /// Per profile rather than per frame, because both belong to the account's lists
    /// and not to one view of them. Two agenda frames side by side should not draw the
    /// same list in two colours, and an hour set from one of them should not be
    /// missing from the other.
    ///
    /// Stored as a small file next to the client and the sign-in, read once. Anything
    /// in it that is not the shape it should be is ignored rather than trusted: a
    /// hand-edited or half-written file is not a reason to break a frame.
    /// </summary>
    public sealed class AgendaPreferences
    {
        /// <summary>One per profile, for the same reason there is one session per profile.</summary>
        private static readonly Dictionary<string, AgendaPreferences> Shared =
            new Dictionary<string, AgendaPreferences>(StringComparer.OrdinalIgnoreCase);

        public static AgendaPreferences ForCurrentProfile()
        {
            string folder = AgendaCredentials.StorageFolder;

            lock (Shared)
            {
                if (!Shared.TryGetValue(folder, out AgendaPreferences? existing))
                {
                    existing = new AgendaPreferences(folder);
                    Shared[folder] = existing;
                }

                return existing;
            }
        }

        private static readonly Regex Hex = new Regex("^#[0-9A-Fa-f]{6}$", RegexOptions.CultureInvariant);

        private readonly string _folder;

        private readonly Dictionary<string, string> _listColours =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, AgendaHour> _hours =
            new Dictionary<string, AgendaHour>(StringComparer.Ordinal);

        private AgendaPreferences(string folder)
        {
            _folder = folder;
            Load();
        }

        private string FilePath => Path.Combine(_folder, "agenda_preferences.json");

        /// <summary>Colour by task list, as "#RRGGBB". A list not here is drawn in the default colour.</summary>
        public IReadOnlyDictionary<string, string> ListColours => _listColours;

        /// <summary>Kept hours, by <see cref="AgendaOverrides.TaskKey"/>.</summary>
        public IReadOnlyDictionary<string, AgendaHour> Hours => _hours;

        /// <summary>Replaces every list colour at once, the way the settings window saves them.</summary>
        public void SetListColours(IReadOnlyDictionary<string, string> colours)
        {
            _listColours.Clear();

            foreach (KeyValuePair<string, string> pair in colours)
                if (!string.IsNullOrEmpty(pair.Key) && Hex.IsMatch(pair.Value ?? string.Empty))
                    _listColours[pair.Key] = pair.Value!;

            Save();
        }

        public void RememberHour(string listId, string title, TimeSpan start, TimeSpan length)
        {
            var hour = new AgendaHour(start, length);
            if (!hour.IsUsable) return;

            _hours[AgendaOverrides.TaskKey(listId, title)] = hour;
            Save();
        }

        public void ForgetHour(string listId, string title)
        {
            if (_hours.Remove(AgendaOverrides.TaskKey(listId, title))) Save();
        }

        // ======================================================================
        // DISK
        // ======================================================================

        internal sealed class Stored
        {
            public Dictionary<string, string>? ListColours { get; set; }
            public Dictionary<string, StoredHour>? Hours { get; set; }
        }

        internal sealed class StoredHour
        {
            public string? Start { get; set; }
            public int Minutes { get; set; }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;

                // A few hundred bytes when it is ours. Far bigger is something else
                // that happens to have the name, and not worth reading.
                if (new FileInfo(FilePath).Length > 1024 * 1024) return;

                Stored? stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(FilePath));
                if (stored == null) return;

                foreach (KeyValuePair<string, string> pair in stored.ListColours ?? new Dictionary<string, string>())
                    if (!string.IsNullOrEmpty(pair.Key) && Hex.IsMatch(pair.Value ?? string.Empty))
                        _listColours[pair.Key] = pair.Value!;

                foreach (KeyValuePair<string, StoredHour> pair in stored.Hours ?? new Dictionary<string, StoredHour>())
                {
                    if (pair.Value?.Start == null) continue;

                    if (!TimeSpan.TryParseExact(pair.Value.Start, new[] { @"hh\:mm", @"h\:mm" },
                                                CultureInfo.InvariantCulture, out TimeSpan start))
                        continue;

                    var hour = new AgendaHour(start, TimeSpan.FromMinutes(pair.Value.Minutes));
                    if (hour.IsUsable) _hours[pair.Key] = hour;
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not read the agenda preferences: {ex.Message}");
            }
        }

        /// <summary>
        /// Written beside the old file and then moved over it, so that a program
        /// closed halfway through leaves the previous choices rather than half of the
        /// new ones.
        /// </summary>
        private void Save()
        {
            try
            {
                var stored = new Stored
                {
                    ListColours = new Dictionary<string, string>(_listColours),
                    Hours = new Dictionary<string, StoredHour>()
                };

                foreach (KeyValuePair<string, AgendaHour> pair in _hours)
                {
                    stored.Hours[pair.Key] = new StoredHour
                    {
                        Start = pair.Value.Start.ToString(@"hh\:mm", CultureInfo.InvariantCulture),
                        Minutes = (int)Math.Round(pair.Value.Length.TotalMinutes)
                    };
                }

                Directory.CreateDirectory(_folder);

                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary,
                    JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temporary, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: could not save the agenda preferences: {ex.Message}");
            }
        }
    }
}
