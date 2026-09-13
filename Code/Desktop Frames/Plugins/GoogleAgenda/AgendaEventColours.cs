using System;
using System.Collections.Generic;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The colours an event can be given in Google Calendar, apart from its calendar's.
    ///
    /// Google stores the choice as a number from 1 to 11 on the event. The shade each
    /// number stands for is written here as Google Calendar itself draws it - Tomato,
    /// Flamingo, Tangerine and the rest - rather than taken from the API's colors
    /// resource, which still answers with the older, paler set of the previous design:
    /// Lavender as a pale blue rather than the indigo on screen, Graphite as near white,
    /// which white text cannot sit on. What somebody chose in Google's own app should
    /// come out here looking the same.
    ///
    /// A number not in the table means the calendar's colour, which is what the event
    /// showed before it was given one of its own.
    /// </summary>
    public static class AgendaEventColours
    {
        private static readonly Dictionary<string, string> Palette =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["1"] = "#7986CB",    // Lavender
                ["2"] = "#33B679",    // Sage
                ["3"] = "#8E24AA",    // Grape
                ["4"] = "#E67C73",    // Flamingo
                ["5"] = "#F6BF26",    // Banana
                ["6"] = "#F4511E",    // Tangerine
                ["7"] = "#039BE5",    // Peacock
                ["8"] = "#616161",    // Graphite
                ["9"] = "#3F51B5",    // Blueberry
                ["10"] = "#0B8043",   // Basil
                ["11"] = "#D50000"    // Tomato
            };

        /// <summary>The shade for Google's colour number, or null for anything that is not one.</summary>
        public static string? Hex(string? colourId)
        {
            if (string.IsNullOrWhiteSpace(colourId)) return null;

            return Palette.TryGetValue(colourId.Trim(), out string? hex) ? hex : null;
        }
    }
}
