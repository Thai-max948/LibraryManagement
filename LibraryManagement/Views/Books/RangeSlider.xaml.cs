using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LibraryManagement.Views.Books;

public partial class RangeSlider : UserControl
{
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(RangeSlider), new PropertyMetadata(0d, OnRangePropertyChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RangeSlider), new PropertyMetadata(100d, OnRangePropertyChanged));

    public static readonly DependencyProperty LowerValueProperty = DependencyProperty.Register(
        nameof(LowerValue), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(0d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangePropertyChanged));

    public static readonly DependencyProperty UpperValueProperty = DependencyProperty.Register(
        nameof(UpperValue), typeof(double), typeof(RangeSlider), new FrameworkPropertyMetadata(100d,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRangePropertyChanged));

    public static readonly DependencyProperty SmallChangeProperty = DependencyProperty.Register(
        nameof(SmallChange), typeof(double), typeof(RangeSlider), new PropertyMetadata(1d));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double LowerValue { get => (double)GetValue(LowerValueProperty); set => SetValue(LowerValueProperty, value); }
    public double UpperValue { get => (double)GetValue(UpperValueProperty); set => SetValue(UpperValueProperty, value); }
    public double SmallChange { get => (double)GetValue(SmallChangeProperty); set => SetValue(SmallChangeProperty, value); }

    public RangeSlider()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisuals();
    }

    private static void OnRangePropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
        => ((RangeSlider)dependencyObject).NormalizeAndUpdate(e.Property);

    private void NormalizeAndUpdate(DependencyProperty changedProperty)
    {
        if (Maximum < Minimum)
            SetCurrentValue(MaximumProperty, Minimum);

        double lower = LowerValue;
        double upper = UpperValue;
        if (changedProperty == LowerValueProperty)
            lower = Math.Clamp(lower, Minimum, upper);
        else if (changedProperty == UpperValueProperty)
            upper = Math.Clamp(upper, lower, Maximum);
        else
        {
            lower = Math.Clamp(lower, Minimum, Maximum);
            upper = Math.Clamp(upper, lower, Maximum);
        }

        if (!lower.Equals(LowerValue)) SetCurrentValue(LowerValueProperty, lower);
        if (!upper.Equals(UpperValue)) SetCurrentValue(UpperValueProperty, upper);
        UpdateVisuals();
    }

    private void SliderCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateVisuals();

    private void UpdateVisuals()
    {
        if (SliderCanvas is null || Track is null || SelectedTrack is null || LowerThumb is null || UpperThumb is null)
            return;

        double width = Math.Max(0, SliderCanvas.ActualWidth);
        double trackWidth = Math.Max(0, width - LowerThumb.Width);
        double trackLeft = LowerThumb.Width / 2;
        double range = Maximum - Minimum;
        double lowerRatio = range <= 0 ? 0 : Math.Clamp((LowerValue - Minimum) / range, 0, 1);
        double upperRatio = range <= 0 ? 0 : Math.Clamp((UpperValue - Minimum) / range, 0, 1);
        double lowerCenter = trackLeft + lowerRatio * trackWidth;
        double upperCenter = trackLeft + upperRatio * trackWidth;

        Track.Width = trackWidth;
        Canvas.SetLeft(Track, trackLeft);
        Canvas.SetLeft(SelectedTrack, lowerCenter);
        SelectedTrack.Width = Math.Max(0, upperCenter - lowerCenter);
        Canvas.SetLeft(LowerThumb, lowerCenter - LowerThumb.Width / 2);
        Canvas.SetLeft(UpperThumb, upperCenter - UpperThumb.Width / 2);
    }

    private void LowerThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double range = Maximum - Minimum;
        double trackWidth = Math.Max(1, SliderCanvas.ActualWidth - LowerThumb.Width);
        double value = LowerValue + e.HorizontalChange / trackWidth * range;
        SetCurrentValue(LowerValueProperty, Math.Clamp(value, Minimum, UpperValue));
    }

    private void UpperThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        double range = Maximum - Minimum;
        double trackWidth = Math.Max(1, SliderCanvas.ActualWidth - UpperThumb.Width);
        double value = UpperValue + e.HorizontalChange / trackWidth * range;
        SetCurrentValue(UpperValueProperty, Math.Clamp(value, LowerValue, Maximum));
    }

    private void SliderCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Thumb || Maximum <= Minimum) return;
        double trackWidth = Math.Max(1, SliderCanvas.ActualWidth - LowerThumb.Width);
        double position = Math.Clamp(e.GetPosition(SliderCanvas).X - LowerThumb.Width / 2, 0, trackWidth);
        double value = Minimum + position / trackWidth * (Maximum - Minimum);
        if (Math.Abs(value - LowerValue) <= Math.Abs(value - UpperValue))
            SetCurrentValue(LowerValueProperty, Math.Clamp(value, Minimum, UpperValue));
        else
            SetCurrentValue(UpperValueProperty, Math.Clamp(value, LowerValue, Maximum));
        e.Handled = true;
    }

    private void Thumb_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Thumb thumb) return;
        bool isLowerThumb = ReferenceEquals(thumb, LowerThumb);
        double delta = e.Key switch
        {
            Key.Left or Key.Down => -SmallChange,
            Key.Right or Key.Up => SmallChange,
            Key.Home when isLowerThumb => Minimum - LowerValue,
            Key.End when !isLowerThumb => Maximum - UpperValue,
            _ => 0
        };
        if (delta == 0) return;
        if (isLowerThumb)
            SetCurrentValue(LowerValueProperty, Math.Clamp(LowerValue + delta, Minimum, UpperValue));
        else
            SetCurrentValue(UpperValueProperty, Math.Clamp(UpperValue + delta, LowerValue, Maximum));
        e.Handled = true;
    }
}
