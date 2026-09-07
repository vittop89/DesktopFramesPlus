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
    }
}
