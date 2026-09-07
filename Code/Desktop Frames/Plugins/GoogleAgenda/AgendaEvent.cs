using System;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// One entry in the agenda, as the frame needs it.
    ///
    /// Deliberately not Google's own event type. Everything above this line -
    /// the plugin, the list, the settings window - is written against this and
    /// knows nothing about calendars living in the cloud. Only CalendarService
    /// crosses that border, which is what keeps a second source (a local file,
    /// another provider) from becoming a rewrite.
    /// </summary>
    public class AgendaEvent
    {
        /// <summary>Identifier given by the source, needed to update or delete it.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Which calendar it belongs to, for the same reason.</summary>
        public string CalendarId { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Local time. Conversion from the source's time zone happens once, on the way in.</summary>
        public DateTime Start { get; set; }
        public DateTime End { get; set; }

        /// <summary>An all-day entry has no meaningful time, only a date.</summary>
        public bool IsAllDay { get; set; }

        /// <summary>Colour of the calendar it came from, drawn as a dot beside the title.</summary>
        public string ColourHex { get; set; } = string.Empty;

        /// <summary>
        /// Where to open it for real. The frame shows a summary; a double click
        /// belongs in the calendar itself, which can do everything this cannot.
        /// </summary>
        public string WebLink { get; set; } = string.Empty;

        /// <summary>
        /// Version marker from the source, kept so an update can refuse to
        /// overwrite a change made elsewhere in the meantime.
        /// </summary>
        public string ETag { get; set; } = string.Empty;

        /// <summary>True when the entry has been deleted at the source and should leave the list.</summary>
        public bool IsCancelled { get; set; }

        /// <summary>
        /// True when the calendar this came from accepts changes. A holiday feed or a
        /// calendar shared for reading does not, and offering to edit an entry there
        /// would produce a refusal nobody could have predicted.
        /// </summary>
        public bool CanWrite { get; set; }

        /// <summary>
        /// True for something with a tick box rather than a span of time.
        ///
        /// Kept as a flag on the same type rather than a type of its own: the frame
        /// draws one list, and a second type would force every view to know about both
        /// to put them in the same day in the right order.
        /// </summary>
        public bool IsTask { get; set; }

        /// <summary>Ticked. Meaningless unless <see cref="IsTask"/>.</summary>
        public bool IsDone { get; set; }

        public TimeSpan Duration => End - Start;

        /// <summary>The day it belongs under, which is how the list groups entries.</summary>
        public DateTime Day => Start.Date;
    }
}
