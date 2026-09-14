using Desktop_Frames;
using Xunit;

namespace Desktop_Frames.Tests
{
    /// <summary>
    /// When a frame docked under another still belongs there.
    ///
    /// The case that made this: a frame with two frames docked under it was dragged to
    /// the other side of the screen, and the next change in its height sent both of them
    /// behind the taskbar.
    /// </summary>
    public class DockRuleTests
    {
        [Fact]
        public void A_frame_right_above_holds()
        {
            Assert.True(DockRule.StillHolds(3426, 4, 670, 3426, 534, 670));
        }

        [Fact]
        public void A_frame_dragged_to_the_other_side_of_the_screen_does_not()
        {
            // The numbers of the evening it happened.
            Assert.False(DockRule.StillHolds(0, 540.8, 730, 3425.6, 534, 670));
        }

        [Fact]
        public void A_frame_now_below_the_docked_one_does_not()
        {
            Assert.False(DockRule.StillHolds(3426, 900, 670, 3426, 534, 670));
        }

        [Theory]
        [InlineData(3426 + 670 - 60, false)]   // 60 px of 670: under a fifth
        [InlineData(3426 + 670 - 200, true)]   // 200 px of 670: over a fifth
        public void A_partial_overlap_holds_from_a_fifth_of_the_narrower_frame(double childLeft, bool holds)
        {
            Assert.Equal(holds, DockRule.StillHolds(3426, 4, 670, childLeft, 534, 670));
        }

        [Fact]
        public void Two_co_parents_side_by_side_each_hold_the_wide_frame_under_them()
        {
            // A 730 px frame under a 490 px one and a 220 px one.
            Assert.True(DockRule.StillHolds(4.8, 0.8, 490, 0, 540.8, 730));
            Assert.True(DockRule.StillHolds(505.6, 0.8, 220, 0, 540.8, 730));
        }
    }
}
