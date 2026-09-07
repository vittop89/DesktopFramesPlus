using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Markup;

namespace Desktop_Frames.Plugins.GoogleAgenda
{
    /// <summary>
    /// The look of the controls inside an agenda frame.
    ///
    /// Everything here is drawn in translucent white rather than in a colour of its own,
    /// and that is the whole idea: white at fifteen percent over a magenta frame is a
    /// paler magenta, over a grey frame a paler grey. The controls take the frame's
    /// colour without ever being told what it is, and keep taking it when somebody
    /// changes it - which a fixed palette could not do, and a copy of the frame's colour
    /// would only manage until the next change.
    ///
    /// Written as markup rather than assembled control by control because a control
    /// template is a tree, and a tree reads as a tree. Parsed once and shared.
    /// </summary>
    public static class AgendaStyles
    {
        private static ResourceDictionary? _shared;

        /// <summary>The styles, parsed on first use and reused after.</summary>
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
                // A frame with the stock controls is a frame that still works.
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.General,
                    $"GoogleAgenda: the frame styles would not load: {ex.Message}");

                return new ResourceDictionary();
            }
        }

        private const string Markup = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <!-- The stock scroll bar is seventeen pixels of grey with a button at each end,
       built for a document window. In a frame the width of a calendar column that is
       a noticeable share of the space it exists to help navigate. -->
  <Style x:Key='{x:Type ScrollBar}' TargetType='ScrollBar'>
    <Setter Property='Width' Value='8'/>
    <Setter Property='MinWidth' Value='8'/>
    <Setter Property='Background' Value='Transparent'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='ScrollBar'>
          <Grid Background='Transparent'>
            <Track x:Name='PART_Track' IsDirectionReversed='True'>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType='Thumb'>
                      <Border x:Name='Bar' Margin='2,0,2,0' CornerRadius='2'
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

  <!-- Translucent white over whatever the frame is tinted with, so the button is the
       frame's own colour, lighter. -->
  <Style x:Key='{x:Type Button}' TargetType='Button'>
    <Setter Property='Foreground' Value='White'/>
    <Setter Property='FontWeight' Value='SemiBold'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='Template'>
      <Setter.Value>
        <ControlTemplate TargetType='Button'>
          <Border x:Name='Face'
                  CornerRadius='4'
                  Background='#26FFFFFF'
                  BorderBrush='#38FFFFFF'
                  BorderThickness='1'>
            <ContentPresenter HorizontalAlignment='Center'
                              VerticalAlignment='Center'
                              Margin='{TemplateBinding Padding}'/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property='IsMouseOver' Value='True'>
              <Setter TargetName='Face' Property='Background' Value='#44FFFFFF'/>
              <Setter TargetName='Face' Property='BorderBrush' Value='#66FFFFFF'/>
            </Trigger>
            <Trigger Property='IsPressed' Value='True'>
              <Setter TargetName='Face' Property='Background' Value='#66FFFFFF'/>
            </Trigger>
            <Trigger Property='IsEnabled' Value='False'>
              <Setter TargetName='Face' Property='Opacity' Value='0.35'/>
              <Setter Property='Foreground' Value='#99FFFFFF'/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <!-- The same treatment for the tick boxes on tasks, which were a white square with
       a blue system tick and belonged to a different program. -->
  <Style x:Key='{x:Type CheckBox}' TargetType='CheckBox'>
    <Setter Property='Foreground' Value='White'/>
  </Style>

</ResourceDictionary>";
    }
}
