using System.Globalization;
using Desktop_Frames.Layouts;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// The frame geometry has to read the same on every computer.
    ///
    /// These are written against a real Italian culture because that is where the
    /// damage happened: the first version formatted the stored number with the
    /// current culture and then parsed it with the invariant one, so on this
    /// machine a frame at 494.4 was read as "494,4", failed to parse, and fell
    /// back to the default 20 - which was then applied to the window and saved.
    /// </summary>
    public class LayoutNumbersTests
    {
        private static void InCulture(string name, Action body)
        {
            CultureInfo before = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(name);

            try { body(); }
            finally { CultureInfo.CurrentCulture = before; }
        }

        [Theory]
        [InlineData("it-IT")]
        [InlineData("de-DE")]
        [InlineData("fr-FR")]
        [InlineData("pl-PL")]
        [InlineData("ru-RU")]
        [InlineData("en-US")]
        [InlineData("zh-Hans")]
        public void A_fraction_reads_the_same_whatever_the_decimal_mark(string culture)
        {
            var data = JObject.FromObject(new { X = 494.4, Y = 166.8, Width = 188.8, UnrolledHeight = 514.4 });

            InCulture(culture, () =>
            {
                Assert.Equal(494.4, LayoutNumbers.Read(data, "X", 20), 6);
                Assert.Equal(166.8, LayoutNumbers.Read(data, "Y", 20), 6);
                Assert.Equal(188.8, LayoutNumbers.Read(data, "Width", 230), 6);
                Assert.Equal(514.4, LayoutNumbers.Read(data, "UnrolledHeight", 130), 6);
            });
        }

        [Fact]
        public void A_whole_number_reads_as_itself()
        {
            var data = JObject.FromObject(new { X = 3376.0, Width = 720 });

            InCulture("it-IT", () =>
            {
                Assert.Equal(3376, LayoutNumbers.Read(data, "X", 20), 6);
                Assert.Equal(720, LayoutNumbers.Read(data, "Width", 230), 6);
            });
        }

        [Fact]
        public void A_negative_position_is_kept()
        {
            // A frame on a monitor to the left of the main one has a negative X,
            // and moving it to 20 would drag it onto the wrong screen.
            var data = JObject.FromObject(new { X = -1920.8 });

            InCulture("it-IT", () => Assert.Equal(-1920.8, LayoutNumbers.Read(data, "X", 20), 6));
        }

        [Theory]
        [InlineData("494.4", 494.4)]
        [InlineData("494,4", 494.4)]
        [InlineData("20", 20.0)]
        public void A_number_stored_as_text_is_read_both_ways_round(string stored, double expected)
        {
            var data = new JObject { ["X"] = stored };

            InCulture("it-IT", () => Assert.Equal(expected, LayoutNumbers.Read(data, "X", 99), 6));
        }

        [Fact]
        public void Nothing_usable_leaves_the_fallback()
        {
            var data = new JObject
            {
                ["Missing"] = null,
                ["Text"] = "quite wide",
                ["Flag"] = true,
                ["Nested"] = new JObject { ["X"] = 10 }
            };

            InCulture("it-IT", () =>
            {
                Assert.Equal(20, LayoutNumbers.Read(data, "Absent", 20), 6);
                Assert.Equal(20, LayoutNumbers.Read(data, "Missing", 20), 6);
                Assert.Equal(230, LayoutNumbers.Read(data, "Text", 230), 6);
                Assert.Equal(130, LayoutNumbers.Read(data, "Flag", 130), 6);
                Assert.Equal(130, LayoutNumbers.Read(data, "Nested", 130), 6);
            });
        }

        [Fact]
        public void A_number_that_is_not_a_number_leaves_the_fallback()
        {
            // NaN and infinity come back from a hand-edited file, and a window
            // placed at infinity cannot be found again.
            var data = new JObject { ["X"] = double.NaN, ["Y"] = double.PositiveInfinity };

            Assert.Equal(20, LayoutNumbers.Read(data, "X", 20), 6);
            Assert.Equal(20, LayoutNumbers.Read(data, "Y", 20), 6);
        }
        [Fact]
        public void The_cause_itself_the_number_went_through_the_current_culture()
        {
            // Not a test of our code: a test of the claim about the broken one, so
            // that the diagnosis is on record and not merely argued.
            var data = JObject.FromObject(new { X = 494.4 });

            InCulture("it-IT", () =>
            {
                Assert.Equal("494,4", data["X"]!.ToString());
                Assert.False(double.TryParse(data["X"]!.ToString(), NumberStyles.Float,
                                             CultureInfo.InvariantCulture, out _));
            });
        }
        [Fact]
        public void A_frame_whose_geometry_is_missing_is_not_given_one()
        {
            // What the seeding does with this is skip the frame; what it must not
            // do is report a number nobody saved.
            var data = new JObject { ["X"] = 494.4, ["Y"] = null, ["Width"] = "as wide as that" };

            InCulture("it-IT", () =>
            {
                Assert.True(LayoutNumbers.TryRead(data, "X", out double x));
                Assert.Equal(494.4, x, 6);

                Assert.False(LayoutNumbers.TryRead(data, "Y", out double y));
                Assert.Equal(0, y);

                Assert.False(LayoutNumbers.TryRead(data, "Width", out _));
                Assert.False(LayoutNumbers.TryRead(data, "Absent", out _));
            });
        }
    }
}
