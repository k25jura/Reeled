using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Reeled.Services;

namespace Reeled.Controls;

/// <summary>
/// A high-performance animated numeric counter that rolls digits vertically with fade transitions
/// powered by DirectComposition (DWM) running smoothly at native monitor refresh rates (120Hz/240Hz).
/// </summary>
public sealed class RollingCounter : UserControl
{
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(
            nameof(Value),
            typeof(int),
            typeof(RollingCounter),
            new PropertyMetadata(0, OnValueChanged));

    public static readonly DependencyProperty DurationMsProperty =
        DependencyProperty.Register(
            nameof(DurationMs),
            typeof(int),
            typeof(RollingCounter),
            new PropertyMetadata(280));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int DurationMs
    {
        get => (int)GetValue(DurationMsProperty);
        set => SetValue(DurationMsProperty, value);
    }

    private readonly StackPanel _rootStackPanel;
    private readonly List<DigitColumn> _columns = new();
    private int _displayedValue = 0;
    private bool _hasRenderedInitial = false;

    public RollingCounter()
    {
        _rootStackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 0
        };

        this.Content = _rootStackPanel;
        this.Loaded += OnLoaded;
        this.RegisterPropertyChangedCallback(FontSizeProperty, OnTextStyleChanged);
        this.RegisterPropertyChangedCallback(FontWeightProperty, OnTextStyleChanged);
        this.RegisterPropertyChangedCallback(ForegroundProperty, OnTextStyleChanged);
    }

    private static bool IsReduceMotionEnabled()
    {
        try
        {
            return App.GetService<ILocalStorageService>()?.CurrentSettings.ReduceMotion == true;
        }
        catch
        {
            return false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!_hasRenderedInitial)
        {
            _displayedValue = Value;
            RenderStaticValue(Value);
            _hasRenderedInitial = true;
        }
    }

    private void OnTextStyleChanged(DependencyObject sender, DependencyProperty dp)
    {
        double digitWidth = MeasureDigitWidth();
        foreach (var col in _columns)
        {
            col.UpdateStyle(this.FontSize, this.FontWeight, this.Foreground, this.FontFamily, digitWidth);
        }
    }

    private double MeasureDigitWidth()
    {
        var sample = new TextBlock
        {
            Text = "0",
            FontSize = this.FontSize,
            FontWeight = this.FontWeight,
            FontFamily = this.FontFamily
        };
        Typography.SetNumeralAlignment(sample, FontNumeralAlignment.Tabular);
        sample.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return Math.Max(1.0, Math.Ceiling(sample.DesiredSize.Width));
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RollingCounter counter)
        {
            int oldVal = (int)e.OldValue;
            int newVal = (int)e.NewValue;
            counter.HandleValueChange(oldVal, newVal);
        }
    }

    private void HandleValueChange(int oldVal, int newVal)
    {
        if (!_hasRenderedInitial)
        {
            _displayedValue = newVal;
            RenderStaticValue(newVal);
            _hasRenderedInitial = true;
            return;
        }

        if (oldVal == newVal && _displayedValue == newVal)
        {
            return;
        }

        if (IsReduceMotionEnabled())
        {
            _displayedValue = newVal;
            RenderStaticValue(newVal);
            return;
        }

        int fromVal = _displayedValue;
        _displayedValue = newVal;

        AnimateTransition(fromVal, newVal);
    }

    private void RenderStaticValue(int val)
    {
        _rootStackPanel.Children.Clear();
        _columns.Clear();

        double digitWidth = MeasureDigitWidth();
        string valStr = val.ToString();
        foreach (char c in valStr)
        {
            var col = new DigitColumn(c, this.FontSize, this.FontWeight, this.Foreground, this.FontFamily, digitWidth);
            _columns.Add(col);
            _rootStackPanel.Children.Add(col.Container);
        }
    }

    private void AnimateTransition(int fromVal, int toVal)
    {
        // Cancel in-flight animations and clean up any retiring columns
        for (int k = _columns.Count - 1; k >= 0; k--)
        {
            var c = _columns[k];
            c.StopAnimation();
            if (c.IsRetiring)
            {
                _columns.RemoveAt(k);
                _rootStackPanel.Children.Remove(c.Container);
            }
        }

        string fromStr = fromVal.ToString();
        string toStr = toVal.ToString();
        bool isIncreasing = toVal >= fromVal;

        int maxLen = Math.Max(fromStr.Length, toStr.Length);
        string paddedFrom = fromStr.PadLeft(maxLen, ' ');
        string paddedTo = toStr.PadLeft(maxLen, ' ');

        double digitWidth = MeasureDigitWidth();
        double duration = Math.Max(120, DurationMs);

        // Pre-insert any newly required leading columns with initial zero width so existing digits do not jump
        while (_columns.Count < maxLen)
        {
            var newCol = new DigitColumn(' ', this.FontSize, this.FontWeight, this.Foreground, this.FontFamily, digitWidth);
            newCol.Container.Width = 0.0;
            _columns.Insert(0, newCol);
            _rootStackPanel.Children.Insert(0, newCol.Container);
        }

        for (int i = 0; i < maxLen; i++)
        {
            char oldChar = paddedFrom[i];
            char newChar = paddedTo[i];
            var col = _columns[i];

            col.UpdateStyle(this.FontSize, this.FontWeight, this.Foreground, this.FontFamily, digitWidth);

            if (oldChar == newChar)
            {
                if (newChar == ' ')
                {
                    col.StopAnimation();
                    _columns.RemoveAt(i);
                    _rootStackPanel.Children.Remove(col.Container);
                    i--;
                    maxLen--;
                }
                else
                {
                    col.SetStaticChar(newChar);
                }
            }
            else if (oldChar == ' ' && newChar != ' ')
            {
                // Expanding new digit: roll in with fade while smoothly animating width from 0 to digitWidth
                col.ExpandAndRollTo(newChar, isIncreasing, duration);
            }
            else if (oldChar != ' ' && newChar == ' ')
            {
                // Contracting retiring digit: roll out and fade while smoothly shrinking width to 0, then remove
                col.ContractAndRemove(oldChar, duration, isIncreasing, () =>
                {
                    _columns.Remove(col);
                    _rootStackPanel.Children.Remove(col.Container);
                });
            }
            else
            {
                // Standard in-place digit transition
                col.RollTo(oldChar, newChar, isIncreasing, duration);
            }
        }
    }

    private sealed class DigitColumn
    {
        public Grid Container { get; }
        public bool IsRetiring { get; private set; }

        private readonly TextBlock _prevBlock;
        private readonly TextBlock _currBlock;
        private readonly Visual _prevVisual;
        private readonly Visual _currVisual;
        private readonly Compositor _compositor;
        private readonly CompositionEasingFunction _easeOut;
        private readonly RectangleGeometry _clipGeometry;
        private CompositionScopedBatch? _scopedBatch;
        private Microsoft.UI.Xaml.Media.Animation.Storyboard? _widthStoryboard;
        private double _targetWidth;

        public DigitColumn(char initialChar, double fontSize, Windows.UI.Text.FontWeight fontWeight, Brush foreground, FontFamily fontFamily, double digitWidth)
        {
            _targetWidth = digitWidth;

            Container = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = digitWidth
            };

            _clipGeometry = new RectangleGeometry();
            Container.Clip = _clipGeometry;

            _prevBlock = new TextBlock
            {
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.0
            };
            Typography.SetNumeralAlignment(_prevBlock, FontNumeralAlignment.Tabular);

            _currBlock = new TextBlock
            {
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = initialChar == ' ' ? "" : initialChar.ToString(),
                Opacity = initialChar == ' ' ? 0.0 : 1.0
            };
            Typography.SetNumeralAlignment(_currBlock, FontNumeralAlignment.Tabular);

            Container.Children.Add(_prevBlock);
            Container.Children.Add(_currBlock);

            _prevVisual = ElementCompositionPreview.GetElementVisual(_prevBlock);
            _currVisual = ElementCompositionPreview.GetElementVisual(_currBlock);
            _compositor = _currVisual.Compositor;
            _easeOut = _compositor.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1.0f));

            UpdateStyle(fontSize, fontWeight, foreground, fontFamily, digitWidth);
        }

        public void UpdateStyle(double fontSize, Windows.UI.Text.FontWeight fontWeight, Brush foreground, FontFamily fontFamily, double digitWidth)
        {
            _targetWidth = digitWidth;
            if (!IsRetiring && _widthStoryboard == null)
            {
                Container.Width = digitWidth;
            }

            _prevBlock.FontSize = fontSize;
            _prevBlock.FontWeight = fontWeight;
            _prevBlock.Foreground = foreground;
            _prevBlock.FontFamily = fontFamily;

            _currBlock.FontSize = fontSize;
            _currBlock.FontWeight = fontWeight;
            _currBlock.Foreground = foreground;
            _currBlock.FontFamily = fontFamily;

            double slotHeight = Math.Ceiling(fontSize * 1.35);
            Container.Height = slotHeight;
            _clipGeometry.Rect = new Rect(0, 0, digitWidth, slotHeight);
        }

        public void SetStaticChar(char c)
        {
            StopAnimation();
            IsRetiring = false;
            Container.Width = _targetWidth;
            _clipGeometry.Rect = new Rect(0, 0, _targetWidth, Container.Height);

            _prevBlock.Text = "";
            _prevVisual.Opacity = 0.0f;
            _prevVisual.Offset = Vector3.Zero;

            _currBlock.Text = c == ' ' ? "" : c.ToString();
            _currVisual.Opacity = c == ' ' ? 0.0f : 1.0f;
            _currVisual.Offset = Vector3.Zero;
        }

        public void StopAnimation()
        {
            if (_widthStoryboard != null)
            {
                _widthStoryboard.Stop();
                _widthStoryboard = null;
            }

            if (_scopedBatch != null)
            {
                _prevVisual.StopAnimation("Offset.Y");
                _prevVisual.StopAnimation("Opacity");
                _currVisual.StopAnimation("Offset.Y");
                _currVisual.StopAnimation("Opacity");
                _scopedBatch = null;
            }
        }

        public void ContractAndRemove(char oldChar, double durationMs, bool isIncreasing, Action onCompleted)
        {
            StopAnimation();
            IsRetiring = true;

            _prevBlock.Text = oldChar.ToString();
            _prevVisual.Offset = Vector3.Zero;
            _prevVisual.Opacity = 1.0f;

            _currBlock.Text = "";
            _currVisual.Opacity = 0.0f;

            float slotHeight = (float)(Container.Height > 0 ? Container.Height : 18.0);
            float targetY = isIncreasing ? -slotHeight : slotHeight;
            var duration = TimeSpan.FromMilliseconds(durationMs);

            var prevYAnim = _compositor.CreateScalarKeyFrameAnimation();
            prevYAnim.InsertKeyFrame(0.0f, 0.0f);
            prevYAnim.InsertKeyFrame(1.0f, targetY, _easeOut);
            prevYAnim.Duration = duration;

            var prevOpAnim = _compositor.CreateScalarKeyFrameAnimation();
            prevOpAnim.InsertKeyFrame(0.0f, 1.0f);
            prevOpAnim.InsertKeyFrame(1.0f, 0.0f, _easeOut);
            prevOpAnim.Duration = duration;

            var widthAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = Container.Width > 0 ? Container.Width : _targetWidth,
                To = 0.0,
                Duration = duration,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            var widthSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(widthAnim, Container);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(widthAnim, "Width");
            widthSb.Children.Add(widthAnim);
            _widthStoryboard = widthSb;
            widthSb.Begin();

            _scopedBatch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _scopedBatch.Completed += (s, e) =>
            {
                _scopedBatch = null;
                _widthStoryboard?.Stop();
                _widthStoryboard = null;
                Container.Width = 0.0;
                onCompleted();
            };

            _prevVisual.StartAnimation("Offset.Y", prevYAnim);
            _prevVisual.StartAnimation("Opacity", prevOpAnim);

            _scopedBatch.End();
        }

        public void ExpandAndRollTo(char newChar, bool isIncreasing, double durationMs)
        {
            StopAnimation();
            IsRetiring = false;

            _prevBlock.Text = "";
            _currBlock.Text = newChar.ToString();

            float slotHeight = (float)(Container.Height > 0 ? Container.Height : 18.0);
            float currStartY = isIncreasing ? slotHeight : -slotHeight;

            _prevVisual.Offset = Vector3.Zero;
            _prevVisual.Opacity = 0.0f;

            _currVisual.Offset = new Vector3(0, currStartY, 0);
            _currVisual.Opacity = 0.0f;

            var duration = TimeSpan.FromMilliseconds(durationMs);

            var currYAnim = _compositor.CreateScalarKeyFrameAnimation();
            currYAnim.InsertKeyFrame(0.0f, currStartY);
            currYAnim.InsertKeyFrame(1.0f, 0.0f, _easeOut);
            currYAnim.Duration = duration;

            var currOpAnim = _compositor.CreateScalarKeyFrameAnimation();
            currOpAnim.InsertKeyFrame(0.0f, 0.0f);
            currOpAnim.InsertKeyFrame(1.0f, 1.0f, _easeOut);
            currOpAnim.Duration = duration;

            var widthAnim = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
            {
                From = Container.Width,
                To = _targetWidth,
                Duration = duration,
                EasingFunction = new Microsoft.UI.Xaml.Media.Animation.CubicEase { EasingMode = Microsoft.UI.Xaml.Media.Animation.EasingMode.EaseOut },
                EnableDependentAnimation = true
            };
            var widthSb = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(widthAnim, Container);
            Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(widthAnim, "Width");
            widthSb.Children.Add(widthAnim);
            _widthStoryboard = widthSb;
            widthSb.Begin();

            _scopedBatch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            _currVisual.StartAnimation("Offset.Y", currYAnim);
            _currVisual.StartAnimation("Opacity", currOpAnim);

            _scopedBatch.Completed += (s, e) =>
            {
                _prevBlock.Text = "";
                _prevVisual.Opacity = 0.0f;
                _prevVisual.Offset = Vector3.Zero;

                _currVisual.Offset = Vector3.Zero;
                _currVisual.Opacity = 1.0f;
                _scopedBatch = null;

                _widthStoryboard?.Stop();
                _widthStoryboard = null;
                Container.Width = _targetWidth;
            };

            _scopedBatch.End();
        }

        public void RollTo(char oldChar, char newChar, bool isIncreasing, double durationMs)
        {
            StopAnimation();
            IsRetiring = false;
            Container.Width = _targetWidth;

            _prevBlock.Text = oldChar == ' ' ? "" : oldChar.ToString();
            _currBlock.Text = newChar == ' ' ? "" : newChar.ToString();

            float slotHeight = (float)(Container.Height > 0 ? Container.Height : 18.0);
            float prevTargetY = isIncreasing ? -slotHeight : slotHeight;
            float currStartY = isIncreasing ? slotHeight : -slotHeight;

            _prevVisual.Offset = Vector3.Zero;
            _prevVisual.Opacity = oldChar == ' ' ? 0.0f : 1.0f;

            _currVisual.Offset = new Vector3(0, currStartY, 0);
            _currVisual.Opacity = 0.0f;

            var duration = TimeSpan.FromMilliseconds(durationMs);

            var currYAnim = _compositor.CreateScalarKeyFrameAnimation();
            currYAnim.InsertKeyFrame(0.0f, currStartY);
            currYAnim.InsertKeyFrame(1.0f, 0.0f, _easeOut);
            currYAnim.Duration = duration;

            var currOpAnim = _compositor.CreateScalarKeyFrameAnimation();
            currOpAnim.InsertKeyFrame(0.0f, 0.0f);
            currOpAnim.InsertKeyFrame(1.0f, 1.0f, _easeOut);
            currOpAnim.Duration = duration;

            _scopedBatch = _compositor.CreateScopedBatch(CompositionBatchTypes.Animation);

            if (oldChar != ' ')
            {
                var prevYAnim = _compositor.CreateScalarKeyFrameAnimation();
                prevYAnim.InsertKeyFrame(0.0f, 0.0f);
                prevYAnim.InsertKeyFrame(1.0f, prevTargetY, _easeOut);
                prevYAnim.Duration = duration;

                var prevOpAnim = _compositor.CreateScalarKeyFrameAnimation();
                prevOpAnim.InsertKeyFrame(0.0f, 1.0f);
                prevOpAnim.InsertKeyFrame(1.0f, 0.0f, _easeOut);
                prevOpAnim.Duration = duration;

                _prevVisual.StartAnimation("Offset.Y", prevYAnim);
                _prevVisual.StartAnimation("Opacity", prevOpAnim);
            }

            _currVisual.StartAnimation("Offset.Y", currYAnim);
            _currVisual.StartAnimation("Opacity", currOpAnim);

            _scopedBatch.Completed += (s, e) =>
            {
                _prevBlock.Text = "";
                _prevVisual.Opacity = 0.0f;
                _prevVisual.Offset = Vector3.Zero;

                _currVisual.Offset = Vector3.Zero;
                _currVisual.Opacity = 1.0f;
                _scopedBatch = null;
            };

            _scopedBatch.End();
        }
    }
}
