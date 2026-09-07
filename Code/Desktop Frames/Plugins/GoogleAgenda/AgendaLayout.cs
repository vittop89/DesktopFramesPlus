using System;
using System.Collections.Generic;
using System.Linq;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>Where one entry sits in a day column, before anything is drawn.</summary>
    public readonly struct AgendaPlacement
    {
        public AgendaPlacement(AgendaEvent item, int lane, int lanes, double topHours, double spanHours)
        {
            Item = item;
            Lane = lane;
            Lanes = lanes;
            TopHours = topHours;
            SpanHours = spanHours;
        }

        public AgendaEvent Item { get; }

        /// <summary>Which side-by-side column this entry is in, counting from zero.</summary>
        public int Lane { get; }

        /// <summary>How many lanes the day needs, which is what decides the width of each.</summary>
        public int Lanes { get; }

        /// <summary>Hours from the top of the visible window. Never below zero.</summary>
        public double TopHours { get; }

        /// <summary>How many hours tall, after clipping to the day.</summary>
        public double SpanHours { get; }
    }

    /// <summary>
    /// Works out where a day's entries go, and nothing else.
    ///
    /// Separated from the drawing because the two fail differently and only one of
    /// them can be checked. Deciding that three overlapping meetings need three
    /// lanes, that an entry beginning yesterday starts at the top of today, and
    /// that one running past midnight stops at the bottom, is arithmetic - it is
    /// either right or wrong, and a test can say which. Turning that into borders
    /// on a canvas is not, and has to be looked at.
    /// </summary>
    public static class AgendaLayout
    {
        /// <summary>
        /// The timed entries belonging to <paramref name="day"/>.
        ///
        /// All-day entries are excluded: they belong to the strip above the grid,
        /// not to an hour in it.
        /// </summary>
        public static List<AgendaEvent> OfDay(IEnumerable<AgendaEvent> events, DateTime day)
        {
            DateTime from = day.Date;
            DateTime to = from.AddDays(1);

            return events
                .Where(e => !e.IsAllDay && e.Start.Date <= from && e.End > from && e.Start < to)
                .OrderBy(e => e.Start)
                .ToList();
        }

        /// <summary>
        /// Places a day's entries in lanes and measures each one against the window
        /// that starts at <paramref name="firstHour"/>.
        ///
        /// An entry goes in the first lane whose last entry has already finished, so
        /// overlapping ones share the width rather than hiding one another - which is
        /// the whole reason a grid beats a list for a busy morning. The entries are
        /// expected in order of start, which <see cref="OfDay"/> guarantees; out of
        /// order, the packing still produces lanes that do not overlap, but it uses
        /// more of them than it needs.
        /// </summary>
        public static List<AgendaPlacement> Place(IReadOnlyList<AgendaEvent> ofDay, DateTime day, int firstHour)
        {
            var lanes = new List<List<AgendaEvent>>();
            var laneOf = new Dictionary<AgendaEvent, int>();

            foreach (AgendaEvent item in ofDay)
            {
                int index = lanes.FindIndex(l => l[l.Count - 1].End <= item.Start);

                if (index < 0)
                {
                    lanes.Add(new List<AgendaEvent>());
                    index = lanes.Count - 1;
                }

                lanes[index].Add(item);
                laneOf[item] = index;
            }

            DateTime opens = day.Date;
            DateTime closes = opens.AddDays(1);

            var placed = new List<AgendaPlacement>(ofDay.Count);

            foreach (AgendaEvent item in ofDay)
            {
                // Clipped to the day at both ends. Something that began yesterday and
                // is still running belongs at the top of today, not off the top of it,
                // and something running into tomorrow stops at the bottom.
                DateTime start = item.Start < opens ? opens : item.Start;
                DateTime end = item.End > closes ? closes : item.End;

                double top = (start - opens).TotalHours - firstHour;
                double span = (end - start).TotalHours;

                placed.Add(new AgendaPlacement(item, laneOf[item], lanes.Count,
                                               Math.Max(0, top), span));
            }

            return placed;
        }
    }
}
