using System;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// What a frame lets somebody do to an entry, handed to the drawing code in one
    /// piece.
    ///
    /// Gathered into an object rather than passed as separate parameters because they
    /// travel together through every level of a grid - strip, column, lane, block, chip
    /// - and none of those levels uses them. Passed one by one, each new gesture widens
    /// five signatures to reach the one place that cares.
    /// </summary>
    public class AgendaActions
    {
        /// <summary>Show the entry in Google, in a browser.</summary>
        public Action<AgendaEvent>? Open { get; set; }

        public Action<AgendaEvent>? Edit { get; set; }

        public Action<AgendaEvent>? Delete { get; set; }

        /// <summary>Tick a task off, or put it back. Never set for a plain event.</summary>
        public Action<AgendaEvent, bool>? SetDone { get; set; }

        /// <summary>
        /// Start something new at this moment - the half hour somebody clicked in an
        /// empty part of a day. Not about an entry, unlike the rest, but it travels the
        /// same way down to the one place that knows where the click landed.
        /// </summary>
        public Action<DateTime>? CreateAt { get; set; }

        /// <summary>
        /// Put an entry at another time, or give it another end - or, when the last
        /// argument is true, put a copy of it there and leave it where it is. What a
        /// drag in the grid asks for once the button is let go.
        /// </summary>
        public Action<AgendaEvent, DateTime, DateTime, bool>? Reschedule { get; set; }
    }
}
