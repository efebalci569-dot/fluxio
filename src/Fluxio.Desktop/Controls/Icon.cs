using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Fluxio.Desktop.Controls;

/// <summary>Draws one of the 24×24 stroke geometries from Icons.xaml in the current Foreground.</summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(Geometry), typeof(Icon), new PropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon), new PropertyMetadata(20d));

    public static readonly DependencyProperty StrokeThicknessProperty =
        DependencyProperty.Register(nameof(StrokeThickness), typeof(double), typeof(Icon), new PropertyMetadata(2d));

    static Icon()
    {
        // The look comes from the implicit style in Resources/Styles.xaml.
        FocusableProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
        IsTabStopProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(false));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}

/// <summary>Attached properties used by the Fluxio control templates.</summary>
public static class Ui
{
    /// <summary>Icon shown before a button's content.</summary>
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.RegisterAttached("Icon", typeof(Geometry), typeof(Ui), new FrameworkPropertyMetadata(null));

    /// <summary>Corner radius for templated controls.</summary>
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));

    /// <summary>Accent brush for nav items and cards.</summary>
    public static readonly DependencyProperty AccentProperty =
        DependencyProperty.RegisterAttached("Accent", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));

    public static Geometry? GetIcon(DependencyObject element) => (Geometry?)element.GetValue(IconProperty);

    public static void SetIcon(DependencyObject element, Geometry? value) => element.SetValue(IconProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) => (CornerRadius)element.GetValue(CornerRadiusProperty);

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) => element.SetValue(CornerRadiusProperty, value);

    public static Brush? GetAccent(DependencyObject element) => (Brush?)element.GetValue(AccentProperty);

    public static void SetAccent(DependencyObject element, Brush? value) => element.SetValue(AccentProperty, value);
}
