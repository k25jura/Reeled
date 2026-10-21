using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace Reeled.Helpers;

/// <summary>
/// The app's motion system: one set of curves and durations used everywhere, plus the single source of
/// truth for "should this animate?". Motion is reduced when the in-app Reduce Motion setting is on, or
/// when Windows "Animation effects" is turned off.
///
/// Every storyboard started through <see cref="Begin"/> is normalized to these tokens, so animations
/// written at different times (some gentle, some far too quick) all share the same rhythm:
/// entrances decelerate on an Apple-style curve, exits accelerate and are a little shorter, and
/// in-place changes ease in and out.
/// </summary>
public static class Motion
{
    private static readonly Windows.UI.ViewManagement.UISettings UiSettings = new();

    // ---- Duration tokens (ms) ----
    /// <summary>Press and hover feedback.</summary>
    public const double Micro = 120;
    /// <summary>Small fades, exits, state swaps.</summary>
    public const double Short = 220;
    /// <summary>Entrances of controls, cards, flyouts, banners.</summary>
    public const double Medium = 320;
    /// <summary>Panels, page and sidebar transitions, layout space opening up.</summary>
    public const double Long = 420;

    // ---- Curves ----
    // Apple's sheet curve, cubic-bezier(0.32, 0.72, 0, 1): quick start, long soft landing.
    public static readonly Windows.Foundation.Point DecelerateP1 = new(0.32, 0.72);
    public static readonly Windows.Foundation.Point DecelerateP2 = new(0, 1);
    // Exits: cubic-bezier(0.4, 0, 1, 1)
    public static readonly Windows.Foundation.Point AccelerateP1 = new(0.4, 0);
    public static readonly Windows.Foundation.Point AccelerateP2 = new(1, 1);
    // In-place changes: cubic-bezier(0.65, 0, 0.35, 1)
    public static readonly Windows.Foundation.Point StandardP1 = new(0.65, 0);
    public static readonly Windows.Foundation.Point StandardP2 = new(0.35, 1);

    /// <summary>Built-in approximation of the decelerate curve (PowerEase 4.5 fits it within ~4%).</summary>
    public static EasingFunctionBase Decelerate => new PowerEase { EasingMode = EasingMode.EaseOut, Power = 4.5 };
    public static EasingFunctionBase Accelerate => new PowerEase { EasingMode = EasingMode.EaseIn, Power = 3 };
    public static EasingFunctionBase Standard => new PowerEase { EasingMode = EasingMode.EaseInOut, Power = 3 };

    public static bool IsReduced
    {
        get
        {
            try
            {
                if (App.Services != null &&
                    App.GetService<Services.ILocalStorageService>().CurrentSettings.ReduceMotion)
                {
                    return true;
                }
                return !UiSettings.AnimationsEnabled;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Starts a storyboard on the shared motion tokens. When motion is reduced every child collapses to
    /// (almost) zero time so final values and Completed handlers still apply exactly as they would.
    /// </summary>
    public static void Begin(Storyboard storyboard)
    {
        if (IsReduced)
        {
            // Collapse every child to ~1ms so the storyboard still runs to completion and raises
            // Completed (much UI state is finalized there). SkipToFill() doesn't raise Completed, and a
            // huge SpeedRatio crashes inside Microsoft.UI.Xaml.
            foreach (var child in storyboard.Children)
            {
                Collapse(child);
            }
        }
        else
        {
            foreach (var child in storyboard.Children)
            {
                Normalize(child);
            }
        }
        storyboard.Begin();
    }

    /// <summary>Starts a storyboard as written (timing that must stay exact), still honouring reduced motion.</summary>
    public static void BeginExact(Storyboard storyboard)
    {
        if (IsReduced)
        {
            foreach (var child in storyboard.Children) Collapse(child);
        }
        storyboard.Begin();
    }

    private enum Role { Enter, Exit, Change, Keep }

    private static Role RoleOf(EasingFunctionBase? ease) => ease switch
    {
        null => Role.Enter,
        BackEase or ElasticEase or BounceEase or SineEase => Role.Keep,
        _ when ease.EasingMode == EasingMode.EaseIn => Role.Exit,
        _ when ease.EasingMode == EasingMode.EaseInOut => Role.Change,
        _ => Role.Enter
    };

    /// <summary>Maps a hand-picked duration onto the token scale for its role.</summary>
    private static double Snap(double ms, Role role)
    {
        if (ms <= Micro) return ms; // press feedback and discrete steps stay crisp
        return role switch
        {
            Role.Exit => ms <= 200 ? 180 : Short + 20,
            Role.Change => ms <= 260 ? 260 : Medium,
            Role.Keep => ms < Short ? Short : ms,
            _ => ms <= 260 ? Medium : (ms <= 380 ? Long : ms)
        };
    }

    private static EasingFunctionBase? CurveFor(Role role, EasingFunctionBase? original) => role switch
    {
        Role.Exit => Accelerate,
        Role.Change => Standard,
        Role.Keep => original,
        _ => Decelerate
    };

    private static void Normalize(Timeline timeline)
    {
        if (timeline.RepeatBehavior.Type == RepeatBehaviorType.Forever || timeline.AutoReverse) return;

        switch (timeline)
        {
            case DoubleAnimation da when da.Duration.HasTimeSpan:
            {
                var role = RoleOf(da.EasingFunction);
                double ms = da.Duration.TimeSpan.TotalMilliseconds;
                if (ms <= Micro) return;
                da.Duration = new Duration(TimeSpan.FromMilliseconds(Snap(ms, role)));
                da.EasingFunction = CurveFor(role, da.EasingFunction);
                break;
            }
            case DoubleAnimationUsingKeyFrames dk:
            {
                // Scale the frames so the last one lands on the token duration, and swap eased frames' curves
                double end = 0;
                Role role = Role.Enter;
                foreach (var frame in dk.KeyFrames)
                {
                    end = Math.Max(end, frame.KeyTime.TimeSpan.TotalMilliseconds);
                    if (frame is EasingDoubleKeyFrame ef) role = RoleOf(ef.EasingFunction);
                }
                if (end <= Micro) return;
                double scale = Snap(end, role) / end;
                foreach (var frame in dk.KeyFrames)
                {
                    frame.KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(frame.KeyTime.TimeSpan.TotalMilliseconds * scale));
                    if (frame is EasingDoubleKeyFrame ef)
                    {
                        ef.EasingFunction = CurveFor(RoleOf(ef.EasingFunction), ef.EasingFunction);
                    }
                }
                break;
            }
        }
    }

    private static readonly Duration Instant = new(TimeSpan.FromMilliseconds(1));
    private static readonly KeyTime InstantKeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1));

    private static void Collapse(Timeline timeline)
    {
        timeline.BeginTime = TimeSpan.Zero;
        switch (timeline)
        {
            case DoubleAnimationUsingKeyFrames d:
                // Same KeyTime for all frames: the last frame's value wins, i.e. the end state
                foreach (var frame in d.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case ColorAnimationUsingKeyFrames c:
                foreach (var frame in c.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case ObjectAnimationUsingKeyFrames o:
                foreach (var frame in o.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            case PointAnimationUsingKeyFrames p:
                foreach (var frame in p.KeyFrames) frame.KeyTime = InstantKeyTime;
                break;
            default:
                if (timeline.RepeatBehavior.Type != RepeatBehaviorType.Forever)
                {
                    timeline.Duration = Instant;
                }
                break;
        }
    }

    /// <summary>Returns the duration to use, or a near-zero duration when motion is reduced.</summary>
    public static TimeSpan Duration(double milliseconds) =>
        IsReduced ? TimeSpan.FromMilliseconds(1) : TimeSpan.FromMilliseconds(milliseconds);

    // ---------------------------------------------------------------------------------------------
    // Building blocks
    // ---------------------------------------------------------------------------------------------

    /// <summary>A double animation on the exact decelerate (Apple) curve via a key spline.</summary>
    public static DoubleAnimationUsingKeyFrames Spline(DependencyObject target, string property, double to,
        double durationMs = Medium, double? from = null, double delayMs = 0, bool accelerate = false, bool dependent = false)
    {
        var anim = new DoubleAnimationUsingKeyFrames { EnableDependentAnimation = dependent };
        if (from is double f)
        {
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = f });
            if (delayMs > 0)
            {
                anim.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs)), Value = f });
            }
        }
        anim.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(delayMs + durationMs)),
            Value = to,
            KeySpline = new KeySpline
            {
                ControlPoint1 = accelerate ? AccelerateP1 : DecelerateP1,
                ControlPoint2 = accelerate ? AccelerateP2 : DecelerateP2
            }
        });
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, property);
        return anim;
    }

    /// <summary>Progress (0..1) of the decelerate curve at linear time t, for animations stepped by hand every frame.</summary>
    public static double DecelerateAt(double t) => CubicBezierAt(t, DecelerateP1, DecelerateP2);

    /// <summary>Progress (0..1) of the accelerate curve at linear time t, for animations stepped by hand every frame.</summary>
    public static double AccelerateAt(double t) => CubicBezierAt(t, AccelerateP1, AccelerateP2);

    private static double CubicBezierAt(double x, Windows.Foundation.Point p1, Windows.Foundation.Point p2)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        // Newton's method on the x component finds the curve parameter; it is monotonic for control points in 0..1
        double u = x;
        for (int i = 0; i < 8; i++)
        {
            double error = Bezier(u, p1.X, p2.X) - x;
            if (Math.Abs(error) < 1e-5) break;
            double slope = BezierSlope(u, p1.X, p2.X);
            if (Math.Abs(slope) < 1e-6) break;
            u = Math.Clamp(u - error / slope, 0, 1);
        }
        return Bezier(u, p1.Y, p2.Y);
    }

    private static double Bezier(double u, double a, double b)
    {
        double v = 1 - u;
        return 3 * v * v * u * a + 3 * v * u * u * b + u * u * u;
    }

    private static double BezierSlope(double u, double a, double b)
    {
        double v = 1 - u;
        return 3 * v * v * a + 6 * v * u * (b - a) + 3 * u * u * (1 - b);
    }

    /// <summary>Exact decelerate curve for composition animations.</summary>
    public static Microsoft.UI.Composition.CompositionEasingFunction DecelerateCurve(Microsoft.UI.Composition.Compositor c) =>
        c.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.32f, 0.72f), new System.Numerics.Vector2(0f, 1f));

    /// <summary>Exact accelerate curve for composition animations.</summary>
    public static Microsoft.UI.Composition.CompositionEasingFunction AccelerateCurve(Microsoft.UI.Composition.Compositor c) =>
        c.CreateCubicBezierEasingFunction(new System.Numerics.Vector2(0.4f, 0f), new System.Numerics.Vector2(1f, 1f));

    /// <summary>
    /// Opens space for an element first and then brings its content in: the height grows from 0 to the
    /// measured size, and the element fades and rises 8px into the space it just made. The reverse
    /// (<paramref name="show"/> = false) fades it out and then closes the gap. Used for banners, cards and
    /// anything that appears inside a layout, so nothing below it ever jumps.
    ///
    /// Do not put a revealed element in a StackPanel with Spacing: a visible element always takes a spacing
    /// gap (a negative margin cannot cancel it, desired size never goes below zero), so the gap would pop in
    /// at the start of opening and vanish at the end of closing. Put it in a zero-spacing host instead and
    /// give the element its own padding for the gap, which animates with the height.
    /// </summary>
    public static void Reveal(FrameworkElement element, bool show, Action? completed = null)
    {
        var translate = element.RenderTransform as Microsoft.UI.Xaml.Media.TranslateTransform;
        if (translate == null)
        {
            translate = new Microsoft.UI.Xaml.Media.TranslateTransform();
            element.RenderTransform = translate;
        }

        if (IsReduced)
        {
            element.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            element.Height = double.NaN;
            element.Opacity = show ? 1 : 0;
            translate.Y = 0;
            completed?.Invoke();
            return;
        }

        var sb = new Storyboard();
        var token = new object();
        RevealTokens.Remove(element);
        RevealTokens.Add(element, token);

        // Finishes on the exact geometry the animation ended on. The storyboard is stopped first because a
        // held animation value outranks local values, then every property is set to its resting state in
        // the same dispatch, so no frame sits between the last animated frame and the final state.
        void Finish(bool shown)
        {
            if (!RevealTokens.TryGetValue(element, out var current) || !ReferenceEquals(current, token)) return; // superseded
            if (!shown) element.Visibility = Visibility.Collapsed;
            sb.Stop();
            element.Height = double.NaN;
            element.Opacity = shown ? 1 : 0;
            translate.Y = 0;
            completed?.Invoke();
        }

        if (show)
        {
            element.Visibility = Visibility.Visible;
            double start = element.ActualHeight > 0 && element.Opacity > 0 ? element.ActualHeight : 0;
            double width = element.ActualWidth > 0 ? element.ActualWidth
                : (element.Parent as FrameworkElement)?.ActualWidth ?? 400;
            element.Height = double.NaN;
            element.Measure(new Windows.Foundation.Size(width, double.PositiveInfinity));
            // DesiredSize includes the element's own margin, but Height does not
            double target = Math.Max(0, element.DesiredSize.Height - element.Margin.Top - element.Margin.Bottom);
            element.Height = start;

            sb.Children.Add(Spline(element, "Height", target, Long, from: start, dependent: true));
            sb.Children.Add(Spline(element, "Opacity", 1, Medium, from: element.Opacity, delayMs: 90));
            sb.Children.Add(Spline(translate, "Y", 0, Medium, from: 8, delayMs: 90));
            sb.Completed += (s, e) => Finish(true);
        }
        else
        {
            double start = element.ActualHeight;
            sb.Children.Add(Spline(element, "Opacity", 0, 160, accelerate: true));
            sb.Children.Add(Spline(translate, "Y", -4, 160, accelerate: true));
            sb.Children.Add(Spline(element, "Height", 0, Medium, from: start, delayMs: 120, dependent: true));
            sb.Completed += (s, e) => Finish(false);
        }
        sb.Begin();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement, object> RevealTokens = new();
}
