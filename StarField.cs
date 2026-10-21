using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace PCLink.Net;

public sealed class StarField : Canvas
{
    private record Star(Ellipse Shape, double X, double Y);

    private record Glow(Ellipse Shape, bool Left);

    private record Spark(Ellipse Shape, double X);

    private record AnimationEntry(IAnimatable Target, DependencyProperty Property, AnimationTimeline Timeline);

    private static readonly Color Gold = Color.FromRgb(247, 197, 107);

    private readonly List<Star> stars = [];
    private readonly List<Glow> glows = [];
    private readonly List<Spark> sparks = [];
    private readonly List<AnimationEntry> animations = [];
    private readonly Random random = new();
    private bool built;

    public StarField()
    {
        IsHitTestVisible = false;
        ClipToBounds = true;
        Loaded += (_, _) => Build();
        SizeChanged += (_, _) => PlaceShapes();
        IsVisibleChanged += (_, _) => SetRunning(IsVisible);
    }

    private double Between(double a, double b) => a + random.NextDouble() * (b - a);

    private static Duration Seconds(double s) => new(TimeSpan.FromSeconds(s));

    private void Animate(IAnimatable target, DependencyProperty property, AnimationTimeline timeline) =>
        animations.Add(new AnimationEntry(target, property, timeline));

    private void SetRunning(bool running)
    {
        foreach (var a in animations) a.Target.BeginAnimation(a.Property, running ? a.Timeline : null);
    }

    private void Build()
    {
        if (built) return;
        built = true;
        AddGlows();
        AddStars();
        AddSparks();
        SetRunning(IsVisible);
        PlaceShapes();
    }

    private void AddGlows()
    {
        foreach (var (color, alpha, left) in new[] { (Color.FromRgb(63, 111, 163), (byte)70, true), (Color.FromRgb(217, 112, 44), (byte)45, false) })
        {
            var glow = new Ellipse
            {
                Fill = new RadialGradientBrush(Color.FromArgb(alpha, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B)),
            };
            Animate(glow, OpacityProperty, new DoubleAnimation(0.55, 1, Seconds(Between(14, 22))) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            Children.Add(glow);
            glows.Add(new Glow(glow, left));
        }
    }

    private void AddStars()
    {
        for (var i = 0; i < 70; i++)
        {
            var size = Between(1.2, 2.4);
            var star = new Ellipse
            {
                Width = size, Height = size, Opacity = 0.4,
                Fill = new SolidColorBrush(i % 9 == 0 ? Gold : Color.FromRgb(243, 234, 217)),
            };
            Animate(star, OpacityProperty, new DoubleAnimation(0.25, 0.95, Seconds(Between(4, 9)))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(Between(0, 6)),
            });
            Children.Add(star);
            stars.Add(new Star(star, random.NextDouble(), random.NextDouble()));
        }
    }

    private void AddSparks()
    {
        var rise = SystemParameters.PrimaryScreenHeight + 60;
        for (var i = 0; i < 14; i++)
        {
            var size = Between(4, 7);
            var move = new TranslateTransform();
            var spark = new Ellipse
            {
                Width = size, Height = size, Opacity = 0,
                Fill = new RadialGradientBrush
                {
                    GradientStops =
                    {
                        new GradientStop(Color.FromArgb(255, Gold.R, Gold.G, Gold.B), 0),
                        new GradientStop(Color.FromArgb(210, Gold.R, Gold.G, Gold.B), 0.3),
                        new GradientStop(Color.FromArgb(0, Gold.R, Gold.G, Gold.B), 1),
                    },
                },
                RenderTransform = move,
            };
            var drift = Between(-30, 30);
            var duration = Seconds(Between(18, 34));
            var begin = TimeSpan.FromSeconds(Between(0, 20));
            Animate(move, TranslateTransform.YProperty, new DoubleAnimation(0, -rise, duration) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            Animate(move, TranslateTransform.XProperty, new DoubleAnimation(0, drift, duration) { RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin });
            Animate(spark, OpacityProperty, new DoubleAnimationUsingKeyFrames
            {
                Duration = duration, RepeatBehavior = RepeatBehavior.Forever, BeginTime = begin,
                KeyFrames =
                {
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(0)),
                    new LinearDoubleKeyFrame(0.75, KeyTime.FromPercent(0.15)),
                    new LinearDoubleKeyFrame(0.35, KeyTime.FromPercent(0.8)),
                    new LinearDoubleKeyFrame(0, KeyTime.FromPercent(1)),
                },
            });
            SetBottom(spark, -12);
            Children.Add(spark);
            sparks.Add(new Spark(spark, random.NextDouble()));
        }
    }

    private void PlaceShapes()
    {
        if (!built || ActualWidth <= 0) return;
        foreach (var s in stars)
        {
            SetLeft(s.Shape, s.X * ActualWidth);
            SetTop(s.Shape, s.Y * ActualHeight);
        }
        var d = Math.Max(ActualWidth, ActualHeight) * 0.7;
        foreach (var g in glows)
        {
            g.Shape.Width = g.Shape.Height = d;
            SetLeft(g.Shape, g.Left ? -d * 0.3 : ActualWidth - d * 0.7);
            SetTop(g.Shape, g.Left ? ActualHeight * 0.05 : ActualHeight - d * 0.7);
        }
        foreach (var s in sparks) SetLeft(s.Shape, s.X * ActualWidth);
    }
}
