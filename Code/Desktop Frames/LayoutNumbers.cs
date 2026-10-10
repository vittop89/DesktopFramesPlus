using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace Desktop_Frames.Layouts
{
    /// <summary>
    /// Reads a frame's geometry out of the saved JSON.
    ///
    /// Its own file, and its own tests, because of what the obvious version of it
    /// did. The layout system seeded itself with
    ///
    ///     double.TryParse(data[key]?.ToString(), NumberStyles.Float,
    ///                     CultureInfo.InvariantCulture, out double value)
    ///
    /// and <c>JValue.ToString()</c> formats with the *current* culture. On a
    /// computer set to Italian - or German, French, Spanish, Polish, Russian,
    /// Portuguese - a stored 494.4 came back as the string "494,4", the invariant
    /// parse of it failed, and the silent fallback stood in: 20, 20, 230, 130.
    ///
    /// Nothing said so. The seeded layout was then applied over the windows and
    /// saved back, so every frame whose position or size had a decimal part - and
    /// at 125% scaling they nearly all do, being multiples of 0.8 - ended up piled
    /// in the top left corner at the default size, with the real geometry gone.
    /// Whole numbers were read correctly, which is why a frame or two survived and
    /// the rest did not.
    ///
    /// So: a number is read as a number; a number that arrives as text is tried
    /// both ways round; and nothing that cannot be read is quietly replaced by a
    /// number that would then be applied to somebody's screen.
    /// </summary>
    public static class LayoutNumbers
    {
        /// <summary>
        /// The number, or false when the file does not hold one.
        ///
        /// Seeding uses this rather than <see cref="Read"/>: a frame whose
        /// geometry cannot be read belongs outside the layout, because a frame
        /// inside it with made-up numbers is a frame that gets *moved* to them.
        /// </summary>
        public static bool TryRead(JObject data, string key, out double value)
        {
            value = 0;

            if (data == null) return false;

            JToken? token = data[key];
            if (token == null || token.Type == JTokenType.Null) return false;

            switch (token.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    try { value = token.Value<double>(); }
                    catch { return false; }
                    break;

                case JTokenType.String:
                    // Written by hand, or by some older version: the decimal mark
                    // may be a comma. Both readings are tried, invariant first.
                    string text = token.Value<string>() ?? string.Empty;

                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                     && !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
                        return false;
                    break;

                default:
                    return false;
            }

            // NaN and infinity come out of a hand-edited file, and a window placed
            // at infinity cannot be found again.
            if (!double.IsFinite(value))
            {
                value = 0;
                return false;
            }

            return true;
        }

        public static double Read(JObject data, string key, double fallback) =>
            TryRead(data, key, out double value) ? value : fallback;
    }
}
