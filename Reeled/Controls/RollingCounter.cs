using System;
using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace Reeled.Controls;

/// <summary>
/// A lightweight animated numeric counter that rolls digits vertically with fade transitions
/// and accordion expansion/contraction in a slot-machine / game-currency counter style.
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
            new PropertyMetadata(320));

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
        double duration = Math.Max(150, DurationMs);

        // Pre-insert any newly required leading columns with initial width = 0 for smooth accordion expansion
        while (_columns.Count < maxLen)
        {
            var newCol = new DigitColumn(' ', this.FontSize, this.FontWeight, this.Foreground, this.FontFamily, digitWidth, initialWidth: 0.0);
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
                    // Empty spacer that is no longer needed: remove immediately
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
                // Expanding new digit: animate width from 0 to digitWidth and roll in
                col.ExpandAndRoll(newChar, duration, isIncreasing, digitWidth);
            }
            else if (oldChar != ' ' && newChar == ' ')
            {
                // Contracting retiring digit: roll out and collapse width to 0, then remove
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
        private readonly TranslateTransform _prevTranslate;
        private readonly TranslateTransform _currTranslate;
        private readonly RectangleGeometry _clipGeometry;
        private Storyboard? _storyboard;
        private double _targetWidth;

        public DigitColumn(char initialChar, double fontSize, Windows.UI.Text.FontWeight fontWeight, Brush foreground, FontFamily fontFamily, double digitWidth, double? initialWidth = null)
        {
            _targetWidth = digitWidth;
            double actualInitialWidth = initialWidth ?? digitWidth;

            Container = new Grid
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Width = actualInitialWidth
            };

            _clipGeometry = new RectangleGeometry();
            Container.Clip = _clipGeometry;

            _prevTranslate = new TranslateTransform();
            _currTranslate = new TranslateTransform();

            _prevBlock = new TextBlock
            {
                RenderTransform = _prevTranslate,
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.0
            };
            Typography.SetNumeralAlignment(_prevBlock, FontNumeralAlignment.Tabular);

            _currBlock = new TextBlock
            {
                RenderTransform = _currTranslate,
                TextAlignment = Microsoft.UI.Xaml.TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Text = initialChar == ' ' ? "" : initialChar.ToString(),
                Opacity = initialChar == ' ' ? 0.0 : 1.0
            };
            Typography.SetNumeralAlignment(_currBlock, FontNumeralAlignment.Tabular);

            Container.Children.Add(_prevBlock);
            Container.Children.Add(_currBlock);

            UpdateStyle(fontSize, fontWeight, foreground, fontFamily, digitWidth);
        }

        public void UpdateStyle(double fontSize, Windows.UI.Text.FontWeight fontWeight, Brush foreground, FontFamily fontFamily, double digitWidth)
        {
            _targetWidth = digitWidth;
            if (!IsRetiring && Container.Width > 0)
            {
                Container.Width = digitWidth;
            }

            _prevBlock.FontSize = fontSize;
            _prevBlock.FontWeight = fontWeight;
            _prevBlock.Foreground = foreground;
            _prevBlock.FontFamily = fontFamily;
            _prevBlock.Width = digitWidth;

            _currBlock.FontSize = fontSize;
            _currBlock.FontWeight = fontWeight;
            _currBlock.Foreground = foreground;
            _currBlock.FontFamily = fontFamily;
            _currBlock.Width = digitWidth;

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
            _prevBlock.Opacity = 0;
            _prevTranslate.Y = 0;

            _currBlock.Text = c == ' ' ? "" : c.ToString();
            _currBlock.Opacity = c == ' ' ? 0.0 : 1.0;
            _currTranslate.Y = 0;
        }

        public void StopAnimation()
        {
            if (_storyboard != null)
            {
                _storyboard.Stop();
                _storyboard = null;
            }
        }

        public void ExpandAndRoll(char newChar, double durationMs, bool isIncreasing, double targetWidth)
        {
            StopAnimation();
            IsRetiring = false;

            _prevBlock.Text = "";
            _prevBlock.Opacity = 0.0;

            _currBlock.Text = newChar.ToString();
            double slotHeight = Container.Height > 0 ? Container.Height : 18.0;

            double currStartY = isIncreasing ? slotHeight : -slotHeight;
            _currTranslate.Y = currStartY;
            _currBlock.Opacity = 0.0;
            Container.Width = 0.0;
            _clipGeometry.Rect = new Rect(0, 0, targetWidth, slotHeight);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var sb = new Storyboard();

            // 1. Expand width accordion
            var animWidth = new DoubleAnimation
            {
                From = 0.0,
                To = targetWidth,
                Duration = duration,
                EasingFunction = ease,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animWidth, Container);
            Storyboard.SetTargetProperty(animWidth, "Width");
            sb.Children.Add(animWidth);

            // 2. Slide Y into position
            var animCurrY = new DoubleAnimation
            {
                From = currStartY,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animCurrY, _currTranslate);
            Storyboard.SetTargetProperty(animCurrY, "Y");
            sb.Children.Add(animCurrY);

            // 3. Fade in opacity
            var animCurrOp = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animCurrOp, _currBlock);
            Storyboard.SetTargetProperty(animCurrOp, "Opacity");
            sb.Children.Add(animCurrOp);

            sb.Completed += (s, e) =>
            {
                Container.Width = targetWidth;
                _currTranslate.Y = 0.0;
                _currBlock.Opacity = 1.0;
                _storyboard = null;
            };

            _storyboard = sb;
            sb.Begin();
        }

        public void ContractAndRemove(char oldChar, double durationMs, bool isIncreasing, Action onCompleted)
        {
            StopAnimation();
            IsRetiring = true;

            _prevBlock.Text = oldChar.ToString();
            _prevBlock.Opacity = 1.0;
            _prevTranslate.Y = 0.0;

            _currBlock.Text = "";
            _currBlock.Opacity = 0.0;

            double slotHeight = Container.Height > 0 ? Container.Height : 18.0;
            double targetY = isIncreasing ? -slotHeight : slotHeight;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var sb = new Storyboard();

            // 1. Collapse width accordion
            var animWidth = new DoubleAnimation
            {
                From = Container.Width,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animWidth, Container);
            Storyboard.SetTargetProperty(animWidth, "Width");
            sb.Children.Add(animWidth);

            // 2. Slide Y out
            var animPrevY = new DoubleAnimation
            {
                From = 0.0,
                To = targetY,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animPrevY, _prevTranslate);
            Storyboard.SetTargetProperty(animPrevY, "Y");
            sb.Children.Add(animPrevY);

            // 3. Fade out opacity
            var animPrevOp = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animPrevOp, _prevBlock);
            Storyboard.SetTargetProperty(animPrevOp, "Opacity");
            sb.Children.Add(animPrevOp);

            sb.Completed += (s, e) =>
            {
                _storyboard = null;
                onCompleted();
            };

            _storyboard = sb;
            sb.Begin();
        }

        public void RollTo(char oldChar, char newChar, bool isIncreasing, double durationMs)
        {
            StopAnimation();
            IsRetiring = false;
            Container.Width = _targetWidth;

            _prevBlock.Text = oldChar.ToString();
            _currBlock.Text = newChar.ToString();

            double slotHeight = Container.Height > 0 ? Container.Height : 18.0;
            double prevTargetY = isIncreasing ? -slotHeight : slotHeight;
            double currStartY = isIncreasing ? slotHeight : -slotHeight;

            _prevTranslate.Y = 0.0;
            _prevBlock.Opacity = 1.0;

            _currTranslate.Y = currStartY;
            _currBlock.Opacity = 0.0;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var duration = TimeSpan.FromMilliseconds(durationMs);
            var sb = new Storyboard();

            var animPrevY = new DoubleAnimation
            {
                From = 0.0,
                To = prevTargetY,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animPrevY, _prevTranslate);
            Storyboard.SetTargetProperty(animPrevY, "Y");
            sb.Children.Add(animPrevY);

            var animPrevOp = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animPrevOp, _prevBlock);
            Storyboard.SetTargetProperty(animPrevOp, "Opacity");
            sb.Children.Add(animPrevOp);

            var animCurrY = new DoubleAnimation
            {
                From = currStartY,
                To = 0.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animCurrY, _currTranslate);
            Storyboard.SetTargetProperty(animCurrY, "Y");
            sb.Children.Add(animCurrY);

            var animCurrOp = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = duration,
                EasingFunction = ease
            };
            Storyboard.SetTarget(animCurrOp, _currBlock);
            Storyboard.SetTargetProperty(animCurrOp, "Opacity");
            sb.Children.Add(animCurrOp);

            sb.Completed += (s, e) =>
            {
                _prevBlock.Text = "";
                _prevBlock.Opacity = 0.0;
                _currTranslate.Y = 0.0;
                _currBlock.Opacity = 1.0;
                _storyboard = null;
            };

            _storyboard = sb;
            sb.Begin();
        }
    }
}
