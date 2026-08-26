using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace GetMan.Controls;

/// <summary>
/// Attached hover micro-interactions. Each element gets its own transform group built in code,
/// so there is no shared-freezable problem and the effects compose (scale + lift + slide).
///
///   HoverAssist.Scale="1.04"  HoverAssist.Lift="2"  HoverAssist.SlideX="3"  HoverAssist.Rotate="90"
/// </summary>
public static class HoverAssist
{
    private static readonly Duration In = new(TimeSpan.FromMilliseconds(130));
    private static readonly Duration Out = new(TimeSpan.FromMilliseconds(180));
    private static readonly Duration Press = new(TimeSpan.FromMilliseconds(80));
    /// <summary>Used when Windows has animations switched off for accessibility.</summary>
    private static readonly Duration Instant = new(TimeSpan.Zero);
    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };

    #region attached properties

    public static readonly DependencyProperty ScaleProperty = DependencyProperty.RegisterAttached(
        "Scale", typeof(double), typeof(HoverAssist), new PropertyMetadata(1.0, OnAnyChanged));

    public static readonly DependencyProperty PressScaleProperty = DependencyProperty.RegisterAttached(
        "PressScale", typeof(double), typeof(HoverAssist), new PropertyMetadata(1.0, OnAnyChanged));

    /// <summary>Pixels to raise the element while hovered.</summary>
    public static readonly DependencyProperty LiftProperty = DependencyProperty.RegisterAttached(
        "Lift", typeof(double), typeof(HoverAssist), new PropertyMetadata(0.0, OnAnyChanged));

    /// <summary>Pixels to nudge the element right while hovered.</summary>
    public static readonly DependencyProperty SlideXProperty = DependencyProperty.RegisterAttached(
        "SlideX", typeof(double), typeof(HoverAssist), new PropertyMetadata(0.0, OnAnyChanged));

    public static readonly DependencyProperty RotateProperty = DependencyProperty.RegisterAttached(
        "Rotate", typeof(double), typeof(HoverAssist), new PropertyMetadata(0.0, OnAnyChanged));

    public static void SetScale(DependencyObject d, double v) => d.SetValue(ScaleProperty, v);
    public static double GetScale(DependencyObject d) => (double)d.GetValue(ScaleProperty);
    public static void SetPressScale(DependencyObject d, double v) => d.SetValue(PressScaleProperty, v);
    public static double GetPressScale(DependencyObject d) => (double)d.GetValue(PressScaleProperty);
    public static void SetLift(DependencyObject d, double v) => d.SetValue(LiftProperty, v);
    public static double GetLift(DependencyObject d) => (double)d.GetValue(LiftProperty);
    public static void SetSlideX(DependencyObject d, double v) => d.SetValue(SlideXProperty, v);
    public static double GetSlideX(DependencyObject d) => (double)d.GetValue(SlideXProperty);
    public static void SetRotate(DependencyObject d, double v) => d.SetValue(RotateProperty, v);
    public static double GetRotate(DependencyObject d) => (double)d.GetValue(RotateProperty);

    #endregion

    private static readonly DependencyProperty AttachedProperty = DependencyProperty.RegisterAttached(
        "Attached", typeof(bool), typeof(HoverAssist), new PropertyMetadata(false));

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)element.GetValue(AttachedProperty)) return;
        element.SetValue(AttachedProperty, true);

        element.MouseEnter += (_, _) => Animate(element, true);
        element.MouseLeave += (_, _) => Animate(element, false);
        element.PreviewMouseLeftButtonDown += OnPressed;
        element.PreviewMouseLeftButtonUp += OnReleased;
        element.IsEnabledChanged += (_, args) =>
        {
            if (args.NewValue is false) Animate(element, false);
        };
    }

    private static void OnPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        var press = GetPressScale(element);
        if (Math.Abs(press - 1.0) < 0.0001) return;
        var duration = SystemParameters.ClientAreaAnimation ? Press : Instant;
        var t = EnsureTransforms(element);
        t.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Make(press, duration));
        t.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Make(press, duration));
    }

    private static void OnReleased(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element) Animate(element, element.IsMouseOver);
    }

    private static void Animate(FrameworkElement element, bool hovered)
    {
        var t = EnsureTransforms(element);
        var duration = SystemParameters.ClientAreaAnimation ? (hovered ? In : Out) : Instant;

        var scale = hovered ? GetScale(element) : 1.0;
        t.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, Make(scale, duration));
        t.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, Make(scale, duration));

        var lift = hovered ? -GetLift(element) : 0.0;
        var slide = hovered ? GetSlideX(element) : 0.0;
        t.Translate.BeginAnimation(TranslateTransform.YProperty, Make(lift, duration));
        t.Translate.BeginAnimation(TranslateTransform.XProperty, Make(slide, duration));

        var rotate = hovered ? GetRotate(element) : 0.0;
        t.Rotate.BeginAnimation(RotateTransform.AngleProperty, Make(rotate, duration));
    }

    private static DoubleAnimation Make(double to, Duration duration) =>
        new(to, duration) { EasingFunction = Ease, FillBehavior = FillBehavior.HoldEnd };

    private sealed class Transforms
    {
        public ScaleTransform Scale;
        public TranslateTransform Translate;
        public RotateTransform Rotate;
    }

    private static readonly DependencyProperty TransformsProperty = DependencyProperty.RegisterAttached(
        "Transforms", typeof(Transforms), typeof(HoverAssist), new PropertyMetadata(null));

    private static Transforms EnsureTransforms(FrameworkElement element)
    {
        if (element.GetValue(TransformsProperty) is Transforms existing) return existing;

        var t = new Transforms
        {
            Scale = new ScaleTransform(1, 1),
            Translate = new TranslateTransform(0, 0),
            Rotate = new RotateTransform(0)
        };
        var group = new TransformGroup();
        group.Children.Add(t.Scale);
        group.Children.Add(t.Rotate);
        group.Children.Add(t.Translate);

        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = group;
        element.SetValue(TransformsProperty, t);
        return t;
    }
}
