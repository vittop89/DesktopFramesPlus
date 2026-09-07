using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// A scroll bar narrow enough to belong to a frame.
    ///
    /// The stock one is seventeen pixels of grey with a button at each end - built for a
    /// document window, and in a frame the width of a calendar column it takes a
    /// noticeable share of the space it is meant to help navigate. This is eight pixels
    /// of translucent white with no buttons: visible against the frame's dark ground,
    /// and gone from mind when it is not needed.
    ///
    /// Written as markup rather than assembled control by control because a control
    /// template is a tree, and a tree reads as a tree. It is parsed once and shared.
    /// </summary>
    public static class SlimScrollBars
    {
        private static ResourceDictionary? _shared;

        /// <summary>The style, parsed on first use and reused after.</summary>
        public static ResourceDictionary Resources => _shared ??= Parse();

        private static ResourceDictionary Parse()
        {
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Markup));
                return (ResourceDictionary)XamlReader.Load(stream);
            }
            catch (Exception ex)
            {
                // A frame with the stock scroll bar is a frame that still works.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: the slim scroll bar style would not load: {ex.Message}");

                return new ResourceDictionary();
            }
        }

        private const string Markup = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <Style x:Key='{x:Type ScrollBar}' TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid x:Name='Root' Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb x:Name='Grip'>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='Bar'
                              Margin='2,0,2,0'
                              CornerRadius='2'
                              Background='#55FFFFFF'/>
                      <ControlTemplate.Triggers>
                        <Trigger Property='IsMouseOver' Value='True'>
                          <Setter TargetName='Bar' Property='Background' Value='#99FFFFFF'/>
                        </Trigger>
                        <Trigger Property='IsDragging' Value='True'>
                          <Setter TargetName='Bar' Property='Background' Value='#CCFFFFFF'/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>

              <!-- Kept, so a click on the track still pages, but drawn as nothing:
                   the arrows at the ends are what made the stock bar wide. -->
              <Track.IncreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False'
                              Opacity='0' Background='Transparent' BorderThickness='0'/>
              </Track.IncreaseRepeatButton>
              <Track.DecreaseRepeatButton>
                <RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False'
                              Opacity='0' Background='Transparent' BorderThickness='0'/>
              </Track.DecreaseRepeatButton>
            </Track>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

</ResourceDictionary>";
    }
}
