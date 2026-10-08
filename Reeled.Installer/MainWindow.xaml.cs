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

    public MainWindow()
    {
        InitializeComponent();

        ConfigureWindow();
        InitializeLocalization();
        UpdateStepView(animate: false);
    }

    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow != null)
        {
            _appWindow.Title = _loc["Installer_Title"];
            _appWindow.Resize(new SizeInt32(800, 560));

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

        if (animate)
        {
            PlayEntranceAnimation();
        }
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
        UpdateStepRow(StepIndicator1, StepIcon1, StepLabel1, WizardStep.Welcome, "\uE80F");
        UpdateStepRow(StepIndicator2, StepIcon2, StepLabel2, WizardStep.Options, "\uE713");
        UpdateStepRow(StepIndicator3, StepIcon3, StepLabel3, WizardStep.Installing, "\uE896");
        UpdateStepRow(StepIndicator4, StepIcon4, StepLabel4, WizardStep.Finished, "\uE73E");
    }

    private void UpdateStepRow(Border indicator, FontIcon icon, TextBlock label, WizardStep step, string defaultGlyph)
    {
        var primaryBrush = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var secondaryBrush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

        if (_currentStep == step)
        {
            indicator.Opacity = 1.0;
            icon.Glyph = defaultGlyph;
            icon.Foreground = primaryBrush;
            label.Foreground = primaryBrush;
            label.FontWeight = FontWeights.SemiBold;
        }
        else if (_currentStep > step)
        {
            indicator.Opacity = 0.0;
            icon.Glyph = "\uE73E"; // Completed checkmark
            icon.Foreground = secondaryBrush;
            label.Foreground = secondaryBrush;
            label.FontWeight = FontWeights.Normal;
        }
        else
        {
            indicator.Opacity = 0.0;
            icon.Glyph = defaultGlyph;
            icon.Foreground = secondaryBrush;
            label.Foreground = secondaryBrush;
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
            InstallingStatusText.Text = $"Error: {ex.Message}";
            CancelButton.IsEnabled = true;
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
    }
}
