namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// One of the calendars the account can see.
    ///
    /// Kept apart from <see cref="AgendaEvent"/> because the two answer different
    /// questions: an event asks "when", a calendar asks "whose". The colour lives
    /// here for the same reason - Google gives it to the calendar, not to each
    /// entry, and copying it onto every event would be a hundred copies of one fact.
    /// </summary>
    public class AgendaCalendar
    {
        public string Id { get; set; } = string.Empty;

        /// <summary>The name shown to the user, as Google has it.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Background colour Google assigns, as "#rrggbb".</summary>
        public string ColourHex { get; set; } = string.Empty;

        /// <summary>
        /// True for the account's own calendar. Used only to put it first: it is the
        /// one somebody means when they say "my calendar".
        /// </summary>
        public bool IsPrimary { get; set; }

        /// <summary>
        /// True when the calendar can be written to. A calendar shared read-only, or
        /// a subscribed one like a holiday feed, cannot take new events, and offering
        /// to add one there would produce a refusal nobody could have predicted.
        /// </summary>
        public bool CanWrite { get; set; }
    }
}
