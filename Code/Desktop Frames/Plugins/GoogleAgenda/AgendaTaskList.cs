namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// One of the lists a task can be filed under - "Shopping", "Work", whatever
    /// somebody made in Google Tasks.
    ///
    /// Kept apart from <see cref="AgendaCalendar"/> although the two look alike, because
    /// they are not interchangeable: a task list has no colour, no sharing, no notion of
    /// being writable by somebody else, and putting a list where a calendar is expected
    /// would compile and then fail at the API. The one screen that offers both simply
    /// asks for both.
    /// </summary>
    public class AgendaTaskList
    {
        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;
    }
}
