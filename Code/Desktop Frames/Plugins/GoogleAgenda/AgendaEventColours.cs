using System;
using System.Collections.Generic;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The colours Google Calendar draws, as it draws them today.
    ///
    /// The API still describes colours in the palette of the previous design - an
    /// event's colour as a number from 1 to 11 whose shade the colors resource gives as
    /// a pale tint, a calendar's as a hex value from the same old set. Google's own app
    /// has drawn a different, deeper palette for years: Lavender is an indigo, not a
    /// pale blue; Graphite is dark grey, not the near white that white text cannot sit
    /// on. What somebody sees in Google Calendar should come out here looking the same,
    /// so both tables below carry the shades of the current design.
    ///
    /// A colour not in either table is left as it was found: an event then takes its
    /// calendar's colour, and a calendar keeps the value the API gave.
    /// </summary>
    public static class AgendaEventColours
    {
        /// <summary>
        /// The calendar palette, by the hex value the API reports for a calendar. The
        /// old value on the left is what calendarList answers; the right is how the
        /// same choice is drawn in Google Calendar.
        /// </summary>
        private static readonly Dictionary<string, string> Calendars =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["#ac725e"] = "#795548",   // Cocoa
                ["#d06b64"] = "#E67C73",   // Flamingo
                ["#f83a22"] = "#D50000",   // Tomato
                ["#fa573c"] = "#F4511E",   // Tangerine
                ["#ff7537"] = "#EF6C00",   // Pumpkin
                ["#ffad46"] = "#F09300",   // Mango
                ["#42d692"] = "#009688",   // Eucalyptus
                ["#16a765"] = "#0B8043",   // Basil
                ["#7bd148"] = "#7CB342",   // Pistachio
                ["#b3dc6c"] = "#C0CA33",   // Avocado
                ["#fbe983"] = "#E4C441",   // Citron
                ["#fad165"] = "#F6BF26",   // Banana
                ["#92e1c0"] = "#33B679",   // Sage
                ["#9fe1e7"] = "#039BE5",   // Peacock
                ["#9fc6e7"] = "#4285F4",   // Cobalt
                ["#4986e7"] = "#3F51B5",   // Blueberry
                ["#9a9cff"] = "#7986CB",   // Lavender
                ["#b99aff"] = "#B39DDB",   // Wisteria
                ["#c2c2c2"] = "#616161",   // Graphite
                ["#cabdbf"] = "#A79B8E",   // Birch
                ["#cca6ac"] = "#AD1457",   // Radicchio
                ["#f691b2"] = "#D81B60",   // Cherry Blossom
                ["#cd74e6"] = "#8E24AA",   // Grape
                ["#a47ae2"] = "#9E69AF"    // Amethyst
            };

        /// <summary>
        /// How Google Calendar draws a calendar the API reports in <paramref name="apiHex"/>,
        /// or null when the value is not one of the palette's - a colour somebody set by
        /// hand, which the API then reports as it is.
        /// </summary>
        public static string? CalendarHex(string? apiHex)
        {
            if (string.IsNullOrWhiteSpace(apiHex)) return null;

            return Calendars.TryGetValue(apiHex.Trim(), out string? hex) ? hex : null;
        }

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
