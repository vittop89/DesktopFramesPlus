using System;
using System.Collections.Generic;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// Puts the two halves of a scheduled task back together.
    ///
    /// A task given an hour reaches this program twice, because Google keeps it in two
    /// services and neither half is the whole thing. Tasks has the title, the tick and
    /// the list, but hands out its due date with the time cut off. Calendar has the
    /// hour - carried by an untitled stand-in event that holds the slot and points back
    /// at the task it is standing in for.
    ///
    /// Drawn as they arrive, that is a nameless block at noon and, in the all-day strip
    /// above it, the same task again with no hour: one thing shown twice, neither time
    /// completely. Joined on the link they share, it is one entry - the task, with its
    /// title and its tick, at the hour somebody actually chose.
    /// </summary>
    public static class AgendaMerge
    {
        /// <summary>
        /// Merges the calendar's stand-ins into the tasks they stand for.
        ///
        /// <paramref name="unjoined"/> counts the stand-ins that found no task. Those are
        /// kept rather than dropped: an untitled block is poor, but it is better than an
        /// appointment quietly disappearing because the task behind it sat in a list the
        /// frame was not showing.
        /// </summary>
        public static List<AgendaEvent> Join(IReadOnlyList<AgendaEvent> events,
                                             IReadOnlyList<AgendaEvent> tasks,
                                             out int unjoined)
        {
            unjoined = 0;

            var byTaskId = new Dictionary<string, AgendaEvent>(StringComparer.Ordinal);

            foreach (AgendaEvent task in tasks)
            {
                string id = TaskIdIn(task.WebLink);
                if (id.Length > 0) byTaskId[id] = task;
            }


            var kept = new List<AgendaEvent>(events.Count + tasks.Count);

            foreach (AgendaEvent item in events)
            {
                string id = TaskIdIn(item.MirrorOfTask);

                if (id.Length == 0)
                {
                    kept.Add(item);
                    continue;
                }

                if (byTaskId.TryGetValue(id, out AgendaEvent? task))
                {
                    // The hour is the only thing the stand-in knows that the task does
                    // not, so the hour is the only thing taken from it. Everything else
                    // - title, tick, list, colour - stays with the task, which is where
                    // a person edits it.
                    task.Start = item.Start;
                    task.End = item.End;
                    task.IsAllDay = false;

                    // The stand-in also knows which colour Google draws this task in,
                    // which the Tasks service does not report at all.
                    if (!string.IsNullOrEmpty(item.ColourHex)) task.ColourHex = item.ColourHex;
                    continue;
                }

                unjoined++;
                kept.Add(item);
            }


            kept.AddRange(tasks);
            return kept;
        }

        /// <summary>
        /// The identifier inside a tasks.google.com link, or empty for anything else.
        ///
        /// The identifier is compared rather than the whole address because the two
        /// services do not write it the same way: the stand-in carries a bare link, and
        /// the task's own one arrives with query parameters after it.
        /// </summary>
        private static string TaskIdIn(string url)
        {
            if (string.IsNullOrEmpty(url)) return string.Empty;

            const string marker = "/task/";

            int at = url.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return string.Empty;

            string rest = url.Substring(at + marker.Length);
            int stop = rest.IndexOfAny(new[] { '?', '#', '/' });

            return stop < 0 ? rest : rest.Substring(0, stop);
        }
    }
}
