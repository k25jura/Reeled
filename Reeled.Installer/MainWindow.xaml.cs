using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Reeled.Installer.Services;
using Windows.Graphics;

namespace Reeled.Installer;

public sealed partial class MainWindow : Window
{
    private enum WizardStep { Welcome = 1, Options = 2, Installing = 3, Finished = 4 }
    private WizardStep _currentStep = WizardStep.Welcome;

    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly InstallService _installService = new();
    private CancellationTokenSource? _installCts;

    private AppWindow? _appWindow;
    private ElementTheme _currentTheme = ElementTheme.Default;

    // Sliding indicator state
    private double _currentIndicatorY = 0;
    private double _targetIndicatorY = 0;
    private bool _isIndicatorVisible = false;
    private Storyboard? _indicatorStoryboard;

    public MainWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        InitializeLocalization();
        UpdateStepView(animate: false);

        RootGrid.Loaded += (s, e) =>
        {
            UpdateTitleBarTheme(_currentTheme);
            AnimateIndicatorTo(GetStepRow(_currentStep), false);
        };
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Installer_Title"];
            _appWindow.Resize(new SizeInt32(800, 560));
            CenterWindowOnScreen(800, 560);

            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "AppIcon.ico");
            if (File.Exists(iconPath))
            {
                _appWindow.SetIcon(iconPath);
            }

            if (_appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;
                presenter.IsMaximizable = false;
            }

            UpdateTitleBarTheme(_currentTheme);
        }

        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch { }

        // Default path
        InstallPathTextBox.Text = _installService.DefaultInstallDirectory;
        UpdateDiskSpace();
    }

    private void CenterWindowOnScreen(int width, int height)
    {
        if (_appWindow == null) return;

        try
        {
            var displayArea = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest);
            if (displayArea != null)
            {
                var workArea = displayArea.WorkArea;
                int x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
                int y = workArea.Y + Math.Max(0, (workArea.Height - height) / 2);
                _appWindow.Move(new PointInt32(x, y));
            }
        }
        catch { }
    }

    private void UpdateTitleBarTheme(ElementTheme actualTheme)
    {
        if (Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported() && _appWindow?.TitleBar != null)
        {
            var titleBar = _appWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

            var themeToUse = actualTheme == ElementTheme.Default
                ? RootGrid.ActualTheme
                : actualTheme;

            if (themeToUse == ElementTheme.Dark)
            {
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 255, 255, 255);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(180, 255, 255, 255);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 255, 255, 255);
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(120, 255, 255, 255);
            }
            else
            {
                titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 24, 24, 27);
                titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 24, 24, 27);
                titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(30, 0, 0, 0);
                titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(180, 24, 24, 27);
                titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(50, 0, 0, 0);
                titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(120, 0, 0, 0);
            }
        }
    }

    private void InitializeLocalization()
    {
        _loc.LanguageChanged += ApplyLocalization;

        for (int i = 0; i < LanguageComboBox.Items.Count; i++)
        {
            if (LanguageComboBox.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), _loc.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedIndex = i;
                break;
            }
        }

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Installer_Title"];
        }

        // Left Banner Labels
        StepLabel1.Text = _loc["Installer_Step_Welcome"];
        StepLabel2.Text = _loc["Installer_Step_Options"];
        StepLabel3.Text = _loc["Installer_Step_Installing"];
        StepLabel4.Text = _loc["Installer_Step_Finished"];

        // Step 1
        Feature1Title.Text = _loc["Welcome_Feature1_Title"];
        Feature1Desc.Text = _loc["Welcome_Feature1_Desc"];
        Feature2Title.Text = _loc["Welcome_Feature2_Title"];
        Feature2Desc.Text = _loc["Welcome_Feature2_Desc"];
        Feature3Title.Text = _loc["Welcome_Feature3_Title"];
        Feature3Desc.Text = _loc["Welcome_Feature3_Desc"];
        LanguageLabel.Text = _loc["Welcome_Language"];

        // Step 2
        DestinationFolderLabel.Text = _loc["Options_Destination"];
        BrowseButton.Content = _loc["Options_Browse"];
        DesktopShortcutCheckBox.Content = _loc["Options_Shortcut_Desktop"];
        StartMenuShortcutCheckBox.Content = _loc["Options_Shortcut_StartMenu"];
        VideoAssociationCheckBox.Content = _loc["Options_Association_Video"];
        UpdateDiskSpace();

        // Step 4
        FinishedTitleText.Text = _loc["Finished_Title"];
        FinishedSubtitleText.Text = _loc["Finished_Subtitle"];
        LaunchAppCheckBox.Content = _loc["Finished_LaunchApp"];

        // Buttons
        BackButton.Content = _loc["Btn_Back"];
        CancelButton.Content = _loc["Btn_Cancel"];

        UpdateStepHeaders();
    }

    private void UpdateStepHeaders()
    {
        switch (_currentStep)
        {
            case WizardStep.Welcome:
                StepHeaderTitle.Text = _loc["Welcome_Title"];
                StepHeaderSubtitle.Text = _loc["Welcome_Subtitle"];
                NextButton.Content = _loc["Btn_Next"];
                break;
            case WizardStep.Options:
                StepHeaderTitle.Text = _loc["Options_Title"];
                StepHeaderSubtitle.Text = _loc["Options_Subtitle"];
                NextButton.Content = _loc["Btn_Install"];
                break;
            case WizardStep.Installing:
                StepHeaderTitle.Text = _loc["Installing_Title"];
                StepHeaderSubtitle.Text = _loc["Installing_Subtitle"];
                break;
            case WizardStep.Finished:
                StepHeaderTitle.Text = _loc["Finished_Title"];
                StepHeaderSubtitle.Text = _loc["Finished_Subtitle"];
                NextButton.Content = _loc["Btn_Finish"];
                break;
        }
    }

    private FrameworkElement GetStepRow(WizardStep step) => step switch
    {
        WizardStep.Welcome => StepRow1,
        WizardStep.Options => StepRow2,
        WizardStep.Installing => StepRow3,
        WizardStep.Finished => StepRow4,
        _ => StepRow1
    };

    private void UpdateStepView(bool animate = true)
    {
        WelcomeStepPanel.Visibility = _currentStep == WizardStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        OptionsStepPanel.Visibility = _currentStep == WizardStep.Options ? Visibility.Visible : Visibility.Collapsed;
        InstallingStepPanel.Visibility = _currentStep == WizardStep.Installing ? Visibility.Visible : Visibility.Collapsed;
        FinishedStepPanel.Visibility = _currentStep == WizardStep.Finished ? Visibility.Visible : Visibility.Collapsed;

        // Button states
        BackButton.IsEnabled = _currentStep == WizardStep.Options;
        BackButton.Visibility = (_currentStep == WizardStep.Installing || _currentStep == WizardStep.Finished)
            ? Visibility.Collapsed : Visibility.Visible;

        CancelButton.IsEnabled = _currentStep != WizardStep.Installing;
        CancelButton.Visibility = _currentStep == WizardStep.Finished ? Visibility.Collapsed : Visibility.Visible;

        NextButton.IsEnabled = _currentStep != WizardStep.Installing;

        UpdateStepIndicators();
        UpdateStepHeaders();

        AnimateIndicatorTo(GetStepRow(_currentStep), animate);

        if (animate)
        {
            PlayEntranceAnimation();
        }
    }

    private void AnimateIndicatorTo(FrameworkElement targetRow, bool animate)
    {
        if (ActiveIndicatorPill == null || StepsNavRoot == null || IndicatorTranslation == null)
            return;

        try
        {
            var transform = targetRow.TransformToVisual(StepsNavRoot);
            var pt = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            double targetY = pt.Y + (targetRow.ActualHeight - 18.0) / 2.0;

            if (!_isIndicatorVisible)
            {
                _currentIndicatorY = targetY;
                _targetIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                ActiveIndicatorPill.Opacity = 1.0;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                _isIndicatorVisible = true;
                return;
            }

            if (Math.Abs(_targetIndicatorY - targetY) < 1.0 && _isIndicatorVisible)
            {
                return;
            }

            double fromY = _currentIndicatorY;
            _targetIndicatorY = targetY;

            if (_indicatorStoryboard != null)
            {
                _indicatorStoryboard.Stop();
                _indicatorStoryboard = null;
                IndicatorTranslation.Y = fromY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
            }

            if (!animate)
            {
                _currentIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                ActiveIndicatorPill.Opacity = 1.0;
                return;
            }

            double distance = Math.Abs(targetY - fromY);
            var moveSb = new Storyboard();
            var appleEase = new QuarticEase { EasingMode = EasingMode.EaseOut };

            // 1. Vertical Glide Animation (Y translation) with fluid Apple curve
            var animTranslate = new DoubleAnimation
            {
                From = fromY,
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = appleEase
            };
            moveSb.Children.Add(animTranslate);
            Storyboard.SetTarget(animTranslate, IndicatorTranslation);
            Storyboard.SetTargetProperty(animTranslate, "Y");

            // 2. Fluid stretch during transit
            if (distance > 5.0 && IndicatorScale != null)
            {
                double stretch = Math.Min(1.18, 1.0 + (distance / 400.0));
                var animScaleKeyFrames = new DoubleAnimationUsingKeyFrames();
                animScaleKeyFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame
                {
                    Value = 1.0,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero)
                });
                animScaleKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = stretch,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(80)),
                    EasingFunction = appleEase
                });
                animScaleKeyFrames.KeyFrames.Add(new EasingDoubleKeyFrame
                {
                    Value = 1.0,
                    KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
                    EasingFunction = appleEase
                });

                moveSb.Children.Add(animScaleKeyFrames);
                Storyboard.SetTarget(animScaleKeyFrames, IndicatorScale);
                Storyboard.SetTargetProperty(animScaleKeyFrames, "ScaleY");
            }

            moveSb.Completed += (s, e) =>
            {
                _currentIndicatorY = targetY;
                IndicatorTranslation.Y = targetY;
                if (IndicatorScale != null) IndicatorScale.ScaleY = 1.0;
                _indicatorStoryboard = null;
            };

            _indicatorStoryboard = moveSb;
            moveSb.Begin();
        }
        catch { }
    }

    private void PlayEntranceAnimation()
    {
        var storyboard = new Storyboard();

        var opacityAnim = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(220)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnim, StepContentContainer);
        Storyboard.SetTargetProperty(opacityAnim, "Opacity");
        storyboard.Children.Add(opacityAnim);

        var slideAnim = new DoubleAnimation
        {
            From = 10.0,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(240)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slideAnim, StepContentTransform);
        Storyboard.SetTargetProperty(slideAnim, "Y");
        storyboard.Children.Add(slideAnim);

        storyboard.Begin();
    }

    private void UpdateStepIndicators()
    {
        UpdateStepRow(StepIcon1, StepLabel1, WizardStep.Welcome, "\uE80F");
        UpdateStepRow(StepIcon2, StepLabel2, WizardStep.Options, "\uE713");
        UpdateStepRow(StepIcon3, StepLabel3, WizardStep.Installing, "\uE896");
        UpdateStepRow(StepIcon4, StepLabel4, WizardStep.Finished, "\uE73E");
    }

    private void UpdateStepRow(FontIcon icon, TextBlock label, WizardStep step, string defaultGlyph)
    {
        if (_currentStep == step)
        {
            icon.Glyph = defaultGlyph;
            icon.Opacity = 1.0;
            label.Opacity = 1.0;
            label.FontWeight = FontWeights.SemiBold;
        }
        else if (_currentStep > step)
        {
            icon.Glyph = "\uE73E"; // Completed checkmark
            icon.Opacity = 0.7;
            label.Opacity = 0.7;
            label.FontWeight = FontWeights.Normal;
        }
        else
        {
            icon.Glyph = defaultGlyph;
            icon.Opacity = 0.55;
            label.Opacity = 0.55;
            label.FontWeight = FontWeights.Normal;
        }
    }

    private void OnNextClicked(object sender, RoutedEventArgs e)
    {
        switch (_currentStep)
        {
            case WizardStep.Welcome:
                _currentStep = WizardStep.Options;
                UpdateStepView();
                break;

            case WizardStep.Options:
                _currentStep = WizardStep.Installing;
                UpdateStepView();
                StartInstallation();
                break;

            case WizardStep.Finished:
                CompleteAndExit();
                break;
        }
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
    {
        if (_currentStep == WizardStep.Options)
        {
            _currentStep = WizardStep.Welcome;
            UpdateStepView();
        }
    }

    private async void StartInstallation()
    {
        string dest = InstallPathTextBox.Text.Trim();
        if (string.IsNullOrEmpty(dest))
        {
            dest = _installService.DefaultInstallDirectory;
        }

        bool desktop = DesktopShortcutCheckBox.IsChecked ?? true;
        bool startMenu = StartMenuShortcutCheckBox.IsChecked ?? true;
        bool videoAssoc = VideoAssociationCheckBox.IsChecked ?? true;

        _installCts = new CancellationTokenSource();
        InstallProgressRing.IsActive = true;
        var progress = new Progress<InstallProgress>(p =>
        {
            InstallProgressBar.Value = p.Percentage;
            InstallPercentageText.Text = $"{(int)p.Percentage}%";
            InstallingStatusText.Text = p.StatusText;
        });

        try
        {
            await _installService.InstallAsync(dest, desktop, startMenu, videoAssoc, progress, _installCts.Token);
            _currentStep = WizardStep.Finished;
            UpdateStepView();
        }
        catch (OperationCanceledException)
        {
            _currentStep = WizardStep.Options;
            UpdateStepView();
        }
        catch (Exception ex)
        {
            InstallProgressRing.IsActive = false;
            InstallingStatusText.Text = ex.Message;
            CancelButton.IsEnabled = true;
            BackButton.IsEnabled = true;
            BackButton.Visibility = Visibility.Visible;
        }
    }

    private void CompleteAndExit()
    {
        if (LaunchAppCheckBox.IsChecked == true)
        {
            string dest = InstallPathTextBox.Text.Trim();
            string exe = Path.Combine(dest, "Reeled.exe");
            if (File.Exists(exe))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = dest,
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }

        this.Close();
    }

    private async void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = _loc["Dialog_Cancel_Title"],
            Content = _loc["Dialog_Cancel_Message"],
            PrimaryButtonText = _loc["Dialog_Cancel_Yes"],
            CloseButtonText = _loc["Dialog_Cancel_No"],
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = RootGrid.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            _installCts?.Cancel();
            this.Close();
        }
    }

    private async void OnBrowseClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                InstallPathTextBox.Text = Path.Combine(folder.Path, "Reeled");
                UpdateDiskSpace();
            }
        }
        catch
        {
            // Fallback
        }
    }

    private void OnInstallPathTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDiskSpace();
    }

    private void UpdateDiskSpace()
    {
        string path = InstallPathTextBox?.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path)) path = "C:\\";

        long freeBytes = _installService.GetDriveFreeSpaceBytes(path);
        double freeGb = freeBytes / (1024.0 * 1024.0 * 1024.0);

        if (SpaceRequiredText != null)
            SpaceRequiredText.Text = _loc.Format("Options_SpaceRequired", 220);

        if (SpaceAvailableText != null)
            SpaceAvailableText.Text = _loc.Format("Options_SpaceAvailable", $"{freeGb:0.#}");
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string langCode)
        {
            _loc.SetLanguage(langCode);
        }
    }

    private void OnThemeToggleClicked(object sender, RoutedEventArgs e)
    {
        _currentTheme = _currentTheme switch
        {
            ElementTheme.Dark => ElementTheme.Light,
            ElementTheme.Light => ElementTheme.Dark,
            _ => (RootGrid.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark)
        };

        RootGrid.RequestedTheme = _currentTheme;
        ThemeIcon.Glyph = _currentTheme == ElementTheme.Dark ? "\uE708" : "\uE706";
        UpdateTitleBarTheme(_currentTheme);
    }
}
