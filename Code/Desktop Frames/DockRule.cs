using System;

namespace Desktop_Frames
{
    /// <summary>
    /// Whether a frame docked under another still belongs under it.
    ///
    /// A dock is made when a frame is dropped just below another it overlaps, and it is
    /// kept by id. The frame above could then be dragged anywhere and the dock stayed:
    /// the next time that frame's height changed, the frames docked to it were moved to
    /// just below its new bottom edge - wherever on the screen that now was, and once
    /// off the bottom of it. A dock holds only while the frame above is still above, and
    /// still overlaps the one below by the share it took to make the dock.
    ///
    /// Plain arithmetic on positions, kept apart from the windows so it can be checked.
    /// </summary>
    public static class DockRule
    {
        /// <summary>How much of the narrower frame two frames must overlap by, as when a dock is made.</summary>
        public const double MinOverlapShare = 0.2;

        public static bool StillHolds(double parentLeft, double parentTop, double parentWidth,
                                      double childLeft, double childTop, double childWidth)
        {
            if (parentTop >= childTop) return false;

            double overlap = Math.Min(parentLeft + parentWidth, childLeft + childWidth)
                           - Math.Max(parentLeft, childLeft);

            return overlap > Math.Min(parentWidth, childWidth) * MinOverlapShare;
        }
    }
}
