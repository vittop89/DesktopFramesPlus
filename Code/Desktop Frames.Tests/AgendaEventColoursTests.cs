using Desktop_Frames.Plugins.GoogleAgenda;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// The colour an event was given in Google, as drawn here. Small, but it is what
    /// stands between "Tomato in Google" and "Tomato in the frame".
    /// </summary>
    public class AgendaEventColoursTests
    {
        [Theory]
        [InlineData("11", "#D50000")]    // Tomato
        [InlineData("7", "#039BE5")]     // Peacock
        [InlineData(" 1 ", "#7986CB")]   // Lavender, however it is spaced
        public void A_colour_number_becomes_the_shade_Google_draws(string id, string hex)
        {
            Assert.Equal(hex, AgendaEventColours.Hex(id));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("12")]
        [InlineData("red")]
        public void Anything_that_is_not_a_colour_number_means_the_calendar_colour(string? id)
        {
            Assert.Null(AgendaEventColours.Hex(id));
        }

        [Fact]
        public void All_eleven_are_there_and_are_colours()
        {
            for (int i = 1; i <= 11; i++)
            {
                string? hex = AgendaEventColours.Hex(i.ToString());

                Assert.NotNull(hex);
                Assert.Matches("^#[0-9A-F]{6}$", hex);
            }
        }
    }
}
