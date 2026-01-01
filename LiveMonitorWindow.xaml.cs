using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Controls.Primitives;
using FramebaseApp;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification; // For WPF NotifyIcon

namespace framebase_app
{
    public partial class LiveMonitorWindow : Window
    {
        private readonly UploadCoordinator _coordinator;
        private string? _currentGame;
        private readonly string[] _games = new[] { "Counter-Strike 2", "Fortnite", "Forza Horizon 5", "Valorant", "Cyberpunk 2077" };
        private PairingService _pairingService = new();

        private readonly (string DisplayName, string ProcessName, string[] Aliases)[] _gameDefinitions =
        {
            ("Counter-Strike 2", "cs2.exe", new[] { "cs2" }),
            ("Fortnite", "FortniteClient-Win64-Shipping.exe", new[] { "FortniteClient-Win64-Shipping" }),
            ("Forza Horizon 5", "ForzaHorizon5.exe", new[] { "ForzaHorizon5" }),
            ("Valorant", "VALORANT-Win64-Shipping.exe", new[] { "VALORANT-Win64-Shipping" }),
            ("Cyberpunk 2077", "Cyberpunk2077.exe", new[] { "Cyberpunk2077" })
        };
        private string? _lastDetectedProcess;
        private int _gameMissCounter = 0;
        private const int GameMissThreshold = 3;

        // System Tray components
        private TaskbarIcon? _trayIcon;
        private bool _isExiting = false;
        private bool _isResettingSetup = false;

        // Overlay
        private OverlayWindow? _overlay;
        private HardwareMonitor _hardwareMonitor;

        // UI: session duration is driven by coordinator SessionCounterChanged

        public LiveMonitorWindow()
        {
            InitializeComponent();

            // Check if device is connected - if not, redirect to setup
            if (!IsDeviceConnected())
            {
                var setupWindow = new SetupWindow();
                setupWindow.Show();
                this.Close();
                return;
            }

            // Initialize Hardware Monitor
            _hardwareMonitor = new HardwareMonitor();

            // Initialize Overlay
            _overlay = new OverlayWindow();
            
            if (FindName("OverlayToggle") is CheckBox overlayToggle)
            {
                overlayToggle.Checked += (s, e) => UpdateOverlayVisibility();
                overlayToggle.Unchecked += (s, e) => UpdateOverlayVisibility();
            }

            // Style combo removed

            if (FindName("EditModeToggle") is CheckBox editModeToggle)
            {
                editModeToggle.Checked += (s, e) => _overlay.SetEditMode(true);
                editModeToggle.Unchecked += (s, e) => _overlay.SetEditMode(false);
            }

            if (FindName("ScaleSlider") is Slider scaleSlider)
            {
                scaleSlider.ValueChanged += (s, e) => 
                {
                    _overlay.SetScale(e.NewValue);
                    if (FindName("ScaleValueText") is TextBlock text)
                        text.Text = $"{(int)(e.NewValue * 100)}%";
                };
            }

            if (FindName("ShowFpsCheck") is CheckBox fpsCheck)
            {
                fpsCheck.Checked += (s, e) => _overlay.ToggleSection("FPS", true);
                fpsCheck.Unchecked += (s, e) => _overlay.ToggleSection("FPS", false);
            }

            if (FindName("ShowGraphCheck") is CheckBox graphCheck)
            {
                graphCheck.Checked += (s, e) => _overlay.ToggleSection("Graph", true);
                graphCheck.Unchecked += (s, e) => _overlay.ToggleSection("Graph", false);
            }

            if (FindName("ShowHardwareCheck") is CheckBox hwCheck)
            {
                hwCheck.Checked += (s, e) => _overlay.ToggleSection("Hardware", true);
                hwCheck.Unchecked += (s, e) => _overlay.ToggleSection("Hardware", false);
            }

            // Close overlay when main window closes
            this.Closed += (s, e) => _overlay.Close();

            // Initialize System Tray
            InitializeSystemTray();

            // Event handlers
            OpenAllGamesButton.Click += (_, __) => ShowOverlay("presets", true);
            StopAndUploadButton.Click += async (_, __) => await StopAndUploadButton_Click();

            // Try to find account-related controls if they exist
            if (FindName("OpenAccountButton") is Button openAccountBtn)
                openAccountBtn.Click += (_, __) => ShowOverlay("account", true);
            if (FindName("OpenOverlaySettingsButton") is Button openOverlayBtn)
                openOverlayBtn.Click += (_, __) => ShowOverlay("overlay_settings", true);
            if (FindName("ClosePresetsOverlayButton") is Button closePresetsBtn)
                closePresetsBtn.Click += (_, __) => ShowOverlay("presets", false);
            if (FindName("CloseAccountOverlayButton") is Button closeAccountBtn)
                closeAccountBtn.Click += (_, __) => ShowOverlay("account", false);
            if (FindName("CloseOverlaySettingsButton") is Button closeOverlayBtn)
                closeOverlayBtn.Click += (_, __) => ShowOverlay("overlay_settings", false);

            if (FindName("ReconnectButton") is Button reconnectBtn)
                reconnectBtn.Click += ReconnectButton_Click;
            
            // Account control buttons
            if (FindName("UnpairButton") is Button unpairBtn)
                unpairBtn.Click += async (_, __) => await UnpairButton_Click();
            if (FindName("RefreshAccountButton") is Button refreshBtn)
                refreshBtn.Click += async (_, __) => await RefreshAccountButton_Click();
            if (FindName("ResetSetupButton") is Button resetBtn)
                resetBtn.Click += ResetBtn_Click;
            
            // Connection test button
            if (FindName("CloseAccountOverlayButton") is Button closeAccountBtn2)
            {
                // no-op here
            }
            // Add test connection button if exists
            var testBtn = new Button();
            if (FindName("AccountStatusText") is TextBlock accountStatus)
            {
                var btn = new Button { Content = "Test Connection", Style = (Style)FindResource("PrimaryButton"), Margin = new Thickness(0,8,0,0), Width = 160 };
                btn.Click += async (_, __) => await TestConnectionAsync();
                // find parent and insert after AccountStatusText
                var parent = accountStatus.Parent as Panel;
                parent?.Children.Add(btn);
            }

            // Coordinator setup (handles PresentMon, activity, upload, session counter)
            _coordinator = new UploadCoordinator();
        _coordinator.LiveMetrics += (fps, frametime, low1, state) =>
            {
                Dispatcher.Invoke(() =>
                {
                    FpsText.Text = Math.Round(fps).ToString();
                    FrametimeText.Text = Math.Round(frametime, 1).ToString();
                    Low1Text.Text = Math.Round(low1).ToString();
                    InactivityStatusText.Text = state;

                    // Update Overlay
                    if (_overlay != null && _overlay.IsVisible)
                    {
                        bool isActive = state == "Active";
                        bool isSupported = !string.IsNullOrEmpty(_currentGame);
                        
                        _overlay.UpdateMetrics(fps, low1, fps, isActive, isSupported);
                        
                        var metrics = _hardwareMonitor.GetMetrics();
                        _overlay.UpdateHardwareInfo(metrics);
                        
                        var history = _coordinator.GetFrametimeHistory();
                        _overlay.UpdateFrametimeGraph(history);
                    }
                    InactivityDot.Fill = state == "Active" ? (Brush)FindResource("Brush.SuccessText") : Brushes.Orange;
                    
                    // Check visibility periodically (e.g. if game starts/stops)
                    UpdateOverlayVisibility();
                });
            };
            _coordinator.SessionCounterChanged += seconds =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (FindName("SessionDurationText") is TextBlock sd)
                    {
                        var ts = TimeSpan.FromSeconds(seconds);
                        sd.Text = ts.ToString("hh\\:mm\\:ss");
                    }
                });
            };
            _coordinator.UploadCompleted += (ok, msg, avgFps, low1) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (FindName("LastSessionSummary") is TextBlock summary)
                    {
                        summary.Text = ok 
                            ? $"{Math.Round(avgFps, 1)} FPS | 1%: {Math.Round(low1, 1)}" 
                            : "Upload failed";
                        summary.Foreground = ok ? (Brush)FindResource("Brush.SuccessText") : Brushes.Red;
                        
                        // Set tooltip to show error details on failure
                        if (ok)
                        {
                            summary.ToolTip = null;
                        }
                        else
                        {
                            var tooltip = new System.Windows.Controls.ToolTip
                            {
                                Content = msg,
                                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                                Foreground = Brushes.White,
                                Padding = new Thickness(8),
                                BorderBrush = Brushes.Red,
                                BorderThickness = new Thickness(1)
                            };
                            summary.ToolTip = tooltip;
                        }
                    }
                });
            };

            Loaded += async (_, __) =>
            {
                await BuildOverlayAsync();
                BuildSystemSpecs();
                
                // Immediately detect if a game is already running
                await UpdateDetectedGameAsync(forceUpdate: true);
                
                // Start continuous detection loop
                _ = DetectGamesLoop();
                
                await RefreshAccountStatus();
                
                // Initialize game display
                UpdateGameDisplay(_currentGame);
            };
        }

    // Last uploads UI removed per spec; session counter reflects seconds since last reset

        private void ShowOverlay(string overlayType, bool show)
        {
            switch (overlayType)
            {
                case "presets":
                    PresetsOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "account":
                    if (FindName("AccountOverlay") is Grid accountOverlay)
                        accountOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    break;
                case "overlay_settings":
                    if (FindName("OverlaySettingsOverlay") is Grid overlaySettings)
                        overlaySettings.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    
                    // Auto-toggle edit mode: ON when opening settings, OFF when closing
                    if (FindName("EditModeToggle") is CheckBox editMode)
                    {
                        editMode.IsChecked = show;
                    }
                    break;
            }

            BackgroundLayer.IsEnabled = !show;
            BackgroundLayer.Effect = show ? new BlurEffect { Radius = 8 } : null;
            
            UpdateOverlayVisibility();
        }

        private void UpdateOverlayVisibility()
        {
            if (_overlay == null) return;

            bool isEnabled = false;
            if (FindName("OverlayToggle") is CheckBox toggle)
                isEnabled = toggle.IsChecked == true;

            if (!isEnabled)
            {
                _overlay.Hide();
                return;
            }

            bool isSettingsOpen = false;
            if (FindName("OverlaySettingsOverlay") is Grid settings)
                isSettingsOpen = settings.Visibility == Visibility.Visible;

            bool isGameActive = !string.IsNullOrEmpty(_currentGame);

            if (isSettingsOpen || isGameActive)
            {
                _overlay.Show();
            }
            else
            {
                _overlay.Hide();
            }
        }

        private void BuildSystemSpecs()
        {
            // Find the specs stack panel, create a simple fallback if not found
            if (FindName("SpecsStackPanel") is StackPanel specsPanel)
            {
                specsPanel.Children.Clear();

                try
                {
                    // CPU
                    var cpuCard = CreateSpecCard("🖥️ Processor", SystemInfoHelper.GetCpu());
                    cpuCard.Margin = new Thickness(0, 0, 0, 8);
                    specsPanel.Children.Add(cpuCard);

                    // RAM
                    var ramCard = CreateSpecCard("💾 Memory", SystemInfoHelper.GetRam());
                    ramCard.Margin = new Thickness(0, 0, 0, 8);
                    specsPanel.Children.Add(ramCard);

                    // GPU
                    var gpuCard = CreateSpecCard("🎮 Graphics Card", SystemInfoHelper.GetGpu());
                    gpuCard.Margin = new Thickness(0, 0, 0, 0);
                    specsPanel.Children.Add(gpuCard);
                }
                catch (Exception ex)
                {
                    var errorCard = CreateSpecCard("❌ Error", $"Specs could not be loaded: {ex.Message}");
                    specsPanel.Children.Add(errorCard);
                }
            }
        }

        private Border CreateSpecCard(string label, string value)
        {
            var card = new Border
            {
                Style = (Style)FindResource("SpecCard")
            };

            var stackPanel = new StackPanel();

            var labelText = new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.Medium,
                Foreground = (Brush)FindResource("Brush.TextMuted"),
                Margin = new Thickness(0, 0, 0, 4)
            };

            var valueText = new TextBlock
            {
                Text = value,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Brush.Text"),
                TextWrapping = TextWrapping.Wrap
            };

            stackPanel.Children.Add(labelText);
            stackPanel.Children.Add(valueText);
            card.Child = stackPanel;

            return card;
        }

        private async Task BuildOverlayAsync()
        {
            OverlayGamesGrid.Children.Clear();
            
            // Dynamic grid configuration: 3 columns, calculate rows based on game count
            OverlayGamesGrid.Columns = 3;
            OverlayGamesGrid.Rows = (int)Math.Ceiling(_games.Length / 3.0);
            
            foreach (var g in _games)
            {
                var card = new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(12), Margin = new Thickness(4, 4, 4, 2) };
                var root = new StackPanel();

                // Large centered game logo section
                var logoSection = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
                
                // Add game logo (optimized size for 6 games)
                var logoImage = new Image { Width = 40, Height = 40, Margin = new Thickness(0, 0, 0, 6), HorizontalAlignment = HorizontalAlignment.Center };
                bool logoLoaded = false;
                try
                {
                    var logoPath = GetGameLogoPath(g);
                    if (!string.IsNullOrEmpty(logoPath) && File.Exists(logoPath))
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(logoPath);
                        bitmap.EndInit();
                        logoImage.Source = bitmap;
                        logoLoaded = true;
                    }
                }
                catch
                {
                    logoLoaded = false;
                }
                
                if (logoLoaded)
                {
                    logoSection.Children.Add(logoImage);
                }
                else
                {
                    // Large fallback emoji
                    var emojiBlock = new TextBlock { 
                        Text = "🎮", 
                        FontSize = 28, 
                        Margin = new Thickness(0, 0, 0, 6), 
                        HorizontalAlignment = HorizontalAlignment.Center 
                    };
                    logoSection.Children.Add(emojiBlock);
                }
                
                // Game title below logo
                var titleBlock = new TextBlock { 
                    Text = g, 
                    FontSize = 14, 
                    FontWeight = FontWeights.Bold, 
                    Foreground = (Brush)FindResource("Brush.Text"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                logoSection.Children.Add(titleBlock);
                root.Children.Add(logoSection);

                // Replace ComboBox with PresetButtonGroup
                var presetButtonGroup = new PresetButtonGroup { Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Stretch };
                var presets = await GraphicsConfigurator.DownloadPresetConfigAsync(g);
                presetButtonGroup.SetPresets(presets.Keys.ToList());
                root.Children.Add(presetButtonGroup);

                var status = new TextBlock { 
                    Text = "Not checked yet", 
                    Margin = new Thickness(0, 0, 0, 8), 
                    Foreground = (Brush)FindResource("Brush.TextMuted"), 
                    FontSize = 11,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                };
                root.Children.Add(status);

                var btnApply = new Button { 
                    Content = "Apply Preset", 
                    Style = (Style)FindResource("PrimaryButton"), 
                    Margin = new Thickness(0, 0, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    FontSize = 12,
                    Padding = new Thickness(8, 6, 8, 6)
                };
                root.Children.Add(btnApply);

                card.Child = root;
                OverlayGamesGrid.Children.Add(card);

                // Handle preset selection
                presetButtonGroup.PresetSelected += (sender, selectedPreset) =>
                {
                    status.Text = $"Preset '{selectedPreset}' selected";
                    status.Foreground = (Brush)FindResource("Brush.Text");
                };

                btnApply.Click += async (_, __) => await ApplyOneAsync(g, presetButtonGroup, status, card);

                _ = CheckOneAsync(g, status, card, presetButtonGroup);
            }
        }

        private async Task CheckOneAsync(string game, TextBlock status, Border card, PresetButtonGroup presetButtonGroup)
        {
            try
            {
                status.Text = "Checking...";
                status.Foreground = (Brush)FindResource("Brush.TextMuted");
                var res = await GraphicsConfigurator.CheckPresetStatusAsync(game);
                if (!res.ConfigFound)
                {
                    status.Text = res.Message;
                    status.Foreground = (Brush)FindResource("Brush.ErrorText");
                    card.BorderBrush = (Brush)FindResource("Brush.ErrorText");
                    return;
                }
                if (res.PresetMatched)
                {
                    status.Text = $"✓ Preset detected: {res.MatchedPresetName}";
                    status.Foreground = (Brush)FindResource("Brush.SuccessText");
                    card.BorderBrush = (Brush)FindResource("Brush.SuccessText");

                    if (!string.IsNullOrEmpty(res.MatchedPresetName))
                    {
                        presetButtonGroup.SelectPreset(res.MatchedPresetName);
                    }
                }
                else
                {
                    status.Text = res.Message;
                    status.Foreground = (Brush)FindResource("Brush.WarningText");
                    card.BorderBrush = (Brush)FindResource("Brush.WarningText");
                }
            }
            catch (Exception ex)
            {
                status.Text = $"Error: {ex.Message}";
                status.Foreground = (Brush)FindResource("Brush.ErrorText");
                card.BorderBrush = (Brush)FindResource("Brush.ErrorText");
            }
        }

        private async Task ApplyOneAsync(string game, PresetButtonGroup presetButtonGroup, TextBlock status, Border card)
        {
            var selected = presetButtonGroup.SelectedPreset;
            if (string.IsNullOrEmpty(selected))
            {
                status.Text = "No preset selected";
                status.Foreground = (Brush)FindResource("Brush.WarningText");
                return;
            }

            try
            {
                status.Text = "Applying preset...";
                status.Foreground = (Brush)FindResource("Brush.TextMuted");

                bool ok = false;
                string lower = game.ToLower();
                if (lower == "cs2" || lower == "counter-strike 2")
                {
                    ok = await GraphicsConfigurator.UpdateCS2VideoConfigAsync(GraphicsConfigurator.GetCS2VideoConfigPath(), selected);
                }
                else if (lower == "fortnite")
                {
                    ok = await GraphicsConfigurator.UpdateFortniteVideoConfigAsync(GraphicsConfigurator.GetFortniteConfigPath(), selected);
                }
                else if (lower == "forza horizon 5")
                {
                    ok = await GraphicsConfigurator.UpdateForzaHorizon5ConfigAsync(GraphicsConfigurator.GetForzaHorizon5ConfigPath(), selected);
                }
                else if (lower == "valorant")
                {
                    var paths = GraphicsConfigurator.GetValorantGameUserSettingsPath();
                    ok = await GraphicsConfigurator.UpdateValorantConfigAsync(string.Join(";", paths.riotUserSettingsPath, paths.gameUserSettingsPath), selected);
                }
                else if (lower == "cyberpunk 2077")
                {
                    var (success, _) = await GraphicsConfigurator.UpdateCp2077VideoConfigAsync(GraphicsConfigurator.GetCp2077ConfigPath(), selected);
                    ok = success;
                }

                status.Text = ok ? "✓ Preset successfully applied" : "❌ Preset could not be applied";
                status.Foreground = (Brush)FindResource(ok ? "Brush.SuccessText" : "Brush.ErrorText");
                card.BorderBrush = (Brush)FindResource(ok ? "Brush.SuccessText" : "Brush.ErrorText");

                await Task.Delay(500);
                await CheckOneAsync(game, status, card, presetButtonGroup);
            }
            catch (Exception ex)
            {
                status.Text = $"❌ Error: {ex.Message}";
                status.Foreground = (Brush)FindResource("Brush.ErrorText");
                card.BorderBrush = (Brush)FindResource("Brush.ErrorText");
            }
        }

        private async void SetCurrentGame(string? game)
        {
            _currentGame = game;
            UpdateGameDisplay(game);
            
            // Update system tray status
            UpdateTrayGameStatus(game, !string.IsNullOrEmpty(game));
            
            // Enable/disable Stop & Upload button based on game detection
            StopAndUploadButton.IsEnabled = !string.IsNullOrEmpty(game);
            
            string setting = "Performance"; // Default
            if (!string.IsNullOrEmpty(game))
            {
                 try 
                 {
                     var res = await GraphicsConfigurator.CheckPresetStatusAsync(game);
                     if (res.PresetMatched && !string.IsNullOrEmpty(res.MatchedPresetName))
                     {
                         setting = res.MatchedPresetName;
                     }
                 }
                 catch { }
            }

            // Coordinator environment
            try { _coordinator.ConfigureEnvironment(SystemInfoHelper.GetCpu() ?? "Unknown CPU", SystemInfoHelper.GetGpu() ?? "Unknown GPU", "1920x1080", setting); } catch { }
        }

        private void UpdateGameDisplay(string? game)
        {
            if (string.IsNullOrEmpty(game))
            {
                CurrentGameText.Text = "No game detected";
                CurrentGameText.Foreground = (Brush)FindResource("Brush.TextMuted");
                
                // Show placeholder, hide game image
                if (FindName("CurrentGameImage") is Image gameImage)
                {
                    gameImage.Visibility = Visibility.Collapsed;
                }
                if (FindName("GamePlaceholder") is TextBlock placeholder)
                {
                    placeholder.Visibility = Visibility.Visible;
                }
            }
            else
            {
                CurrentGameText.Text = GetDisplayName(game);
                CurrentGameText.Foreground = (Brush)FindResource("Brush.Text");
                
                // Try to show game logo, fallback to placeholder
                if (FindName("CurrentGameImage") is Image gameImage && FindName("GamePlaceholder") is TextBlock placeholder)
                {
                    var logoPath = GetGameLogoPath(game);
                    if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
                    {
                        try
                        {
                            gameImage.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
                            gameImage.Visibility = Visibility.Visible;
                            placeholder.Visibility = Visibility.Collapsed;
                        }
                        catch
                        {
                            gameImage.Visibility = Visibility.Collapsed;
                            placeholder.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        gameImage.Visibility = Visibility.Collapsed;
                        placeholder.Visibility = Visibility.Visible;
                    }
                }
            }
        }

        private string GetDisplayName(string game)
        {
            var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cs2", "Counter-Strike 2" },
                { "valorant", "VALORANT" },
                { "cyberpunk", "Cyberpunk 2077" },
                { "fh5", "Forza Horizon 5" },
                { "fortnite", "Fortnite" }
            };
            
            return displayNames.TryGetValue(game, out var displayName) ? displayName : game;
        }

        private string? GetGameLogoPath(string game)
        {
            var logoNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "cs2", "cs2.png" },
                { "counter-strike 2", "cs2.png" },
                { "valorant", "valorant.png" },
                { "cyberpunk", "cyberpunk.png" },
                { "cyberpunk 2077", "cyberpunk.png" },
                { "fh5", "fh5.png" },
                { "forza horizon 5", "fh5.png" },
                { "fortnite", "fortnite.png" }
            };
            
            if (logoNames.TryGetValue(game, out var logoFile))
            {
                var exeDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(exeDir))
                {
                    return System.IO.Path.Combine(exeDir, "logos", logoFile);
                }
            }
            
            return null;
        }

        private async Task DetectGamesLoop()
        {
            while (!_isExiting)
            {
                await UpdateDetectedGameAsync(forceUpdate: false);
                await Task.Delay(TimeSpan.FromSeconds(3));
            }
        }

        private async Task UpdateDetectedGameAsync(bool forceUpdate)
        {
            try
            {
                var (displayName, processName) = await Task.Run(DetectRunningGame);

                if (!string.IsNullOrEmpty(processName))
                {
                    _gameMissCounter = 0;
                    bool processChanged = !string.Equals(processName, _lastDetectedProcess, StringComparison.OrdinalIgnoreCase);

                    if (processChanged || forceUpdate)
                    {
                        _lastDetectedProcess = processName;
                        await Dispatcher.InvokeAsync(() =>
                        {
                            SetCurrentGame(displayName);
                            _coordinator.Start(processName!, displayName);
                        });
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(_lastDetectedProcess))
                    {
                        _gameMissCounter++;
                        if (_gameMissCounter >= GameMissThreshold)
                        {
                            _lastDetectedProcess = null;
                            await Dispatcher.InvokeAsync(() => SetCurrentGame(null));
                        }
                    }
                    else if (forceUpdate)
                    {
                        await Dispatcher.InvokeAsync(() => SetCurrentGame(null));
                    }
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(
                        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "game_detection_errors.log"),
                        $"[{DateTime.Now:o}] {ex.Message}\n{ex.StackTrace}\n\n"
                    );
                }
                catch { }
            }
        }

        private (string? displayName, string? processName) DetectRunningGame()
        {
            foreach (var definition in _gameDefinitions)
            {
                foreach (var alias in definition.Aliases)
                {
                    if (IsProcessRunning(alias))
                    {
                        return (definition.DisplayName, definition.ProcessName);
                    }
                }
            }

            return (null, null);
        }

        private static bool IsProcessRunning(string processName)
        {
            Process[]? processes = null;
            try
            {
                processes = Process.GetProcessesByName(processName);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (processes != null)
                {
                    foreach (var proc in processes)
                    {
                        try { proc.Dispose(); } catch { }
                    }
                }
            }
        }

        private void ReconnectButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Close this window and open setup
                var setupWindow = new SetupWindow();
                setupWindow.Show();
                this.Close();
            }
            catch
            {
                // Error opening setup window
            }
        }

        private async Task UnpairButton_Click()
        {
            try
            {
                var (success, message) = await _pairingService.UnpairAsync();
                // Unpair completed - restart app to show setup window
                if (success)
                {
                    System.Diagnostics.Process.Start(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                    Application.Current.Shutdown();
                }
                else
                {
                    await RefreshAccountStatus();
                }
            }
            catch
            {
                // Unpair error - no UI feedback needed
            }
        }

        private async Task RefreshAccountButton_Click()
        {
            try
            {
                await RefreshAccountStatus();
            }
            catch
            {
                // Refresh error - no UI feedback needed
            }
        }

        private async Task RefreshAccountStatus()
        {
            try
            {
                var info = await _pairingService.GetConnectedUserInfoAsync();

                // Update UI via Dispatcher
                Dispatcher.Invoke(() =>
                {
                    if (FindName("AccountStatusText") is TextBlock statusText && FindName("AccountEmailText") is TextBlock emailText)
                    {
                        if (info.Success)
                        {
                            statusText.Text = "Connected";
                            statusText.Foreground = (Brush)FindResource("Brush.SuccessText");
                            emailText.Text = info.Email;
                            emailText.Foreground = (Brush)FindResource("Brush.Text");

                            // show connected panel
                            if (FindName("ConnectedPanel") is StackPanel connected)
                                connected.Visibility = Visibility.Visible;
                            if (FindName("NotConnectedPanel") is StackPanel notConnected)
                                notConnected.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            statusText.Text = "Not connected";
                            statusText.Foreground = (Brush)FindResource("Brush.WarningText");
                            emailText.Text = info.Message;
                            emailText.Foreground = (Brush)FindResource("Brush.TextMuted");

                            // show not connected panel
                            if (FindName("ConnectedPanel") is StackPanel connected)
                                connected.Visibility = Visibility.Collapsed;
                            if (FindName("NotConnectedPanel") is StackPanel notConnected)
                                notConnected.Visibility = Visibility.Visible;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                if (FindName("AccountStatusText") is TextBlock statusText)
                {
                    statusText.Text = "Error";
                    statusText.Foreground = (Brush)FindResource("Brush.ErrorText");
                }

                if (FindName("AccountEmailText") is TextBlock emailText)
                {
                    emailText.Text = $"Error: {ex.Message}";
                    emailText.Foreground = (Brush)FindResource("Brush.ErrorText");
                }
            }
        }

        private async Task TestConnectionAsync()
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var token = _pairingService.DeviceToken;
                if (!string.IsNullOrEmpty(token)) 
                {
                    client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                }

                // Simple test: Try to load user-info (simple API call without preset validation)
                var resp = await client.GetAsync("https://framebase.gg/api/user-info");

                if (resp.IsSuccessStatusCode)
                {
                    if (FindName("AccountStatusText") is TextBlock status)
                    {
                        status.Text = "Server reachable (Test successful)";
                        status.Foreground = (Brush)FindResource("Brush.SuccessText");
                    }
                }
                else
                {
                    if (FindName("AccountStatusText") is TextBlock status)
                    {
                        status.Text = $"Test failed: {resp.StatusCode}";
                        status.Foreground = (Brush)FindResource("Brush.ErrorText");
                    }
                }
            }
            catch (Exception ex)
            {
                if (FindName("AccountStatusText") is TextBlock status)
                {
                    status.Text = $"Test error: {ex.Message}";
                    status.Foreground = (Brush)FindResource("Brush.ErrorText");
                }
            }
        }

        private async Task StopAndUploadButton_Click()
        {
            try
            {
                StopAndUploadButton.IsEnabled = false;
                StopAndUploadButton.Content = "⏳ Uploading...";
                
                // Call the async method directly without redundant Task.Run
                await _coordinator.StopAndUploadAsync();
                
                StopAndUploadButton.Content = "✓ Uploaded";
                StopAndUploadButton.IsEnabled = !string.IsNullOrEmpty(_currentGame);
                
                await Task.Delay(2000);
                StopAndUploadButton.Content = "Stop & Upload";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error uploading: {ex.Message}", "Upload Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StopAndUploadButton.Content = "Stop & Upload";
                StopAndUploadButton.IsEnabled = !string.IsNullOrEmpty(_currentGame);
            }
        }

        private async void RefreshAccount_Click(object? sender, RoutedEventArgs e)
        {
            await RefreshAccountStatus();
        }

        private void ResetBtn_Click(object? sender, RoutedEventArgs e)
        {
            // Show confirmation dialog
            var result = MessageBox.Show(
                "Möchtest du das Setup wirklich zurücksetzen? Das startet den Setup-Assistenten neu und alle Einstellungen gehen verloren.", 
                "Setup zurücksetzen", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Yes)
                return;
                
            try
            {
                SetupState.Reset();
                // Close this window and re-open setup
                _isResettingSetup = true;  // Prevents minimize to tray
                var setup = new SetupWindow();
                setup.Show();
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resetting: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region System Tray Implementation

        private void InitializeSystemTray()
        {
            try
            {
                // Create WPF tray icon
                _trayIcon = new TaskbarIcon
                {
                    IconSource = new BitmapImage(new Uri("pack://application:,,,/framebase_logo.ico")),
                    ToolTipText = "Framebase Monitor",
                    Visibility = Visibility.Hidden
                };

                // Create context menu in XAML style
                var contextMenu = new ContextMenu();
                
                // Game status menu item
                var gameStatusItem = new MenuItem
                {
                    Header = "No game detected",
                    IsEnabled = false
                };
                contextMenu.Items.Add(gameStatusItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Open Live Monitor
                var openItem = new MenuItem
                {
                    Header = "Open Live Monitor"
                };
                openItem.Click += (_, __) => ShowWindow();
                contextMenu.Items.Add(openItem);
                
                contextMenu.Items.Add(new Separator());
                
                // Exit completely
                var exitItem = new MenuItem
                {
                    Header = "Exit"
                };
                exitItem.Click += (_, __) => ExitApplication();
                contextMenu.Items.Add(exitItem);

                _trayIcon.ContextMenu = contextMenu;
                
                // Double-click to open window
                _trayIcon.TrayMouseDoubleClick += (_, __) => ShowWindow();
            }
            catch
            {
            }
        }

        private void UpdateTrayGameStatus(string? gameName, bool isRecording)
        {
            if (_trayIcon?.ContextMenu?.Items.Count > 0 && _trayIcon.ContextMenu.Items[0] is MenuItem statusItem)
            {
                if (isRecording && !string.IsNullOrEmpty(gameName))
                {
                    statusItem.Header = $"Recording: {gameName}";
                    statusItem.Foreground = Brushes.Green;
                    _trayIcon.ToolTipText = $"Framebase Monitor - {gameName}";
                }
                else
                {
                    statusItem.Header = "No game detected";
                    statusItem.Foreground = Brushes.Gray;
                    _trayIcon.ToolTipText = "Framebase Monitor";
                }
            }
        }

        private void ShowWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
            if (_trayIcon != null)
                _trayIcon.Visibility = Visibility.Hidden;
        }

        private void ExitApplication()
        {
            _isExiting = true;
            _trayIcon?.Dispose();
            try { _coordinator?.Stop(); } catch { }
            Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting && !_isResettingSetup)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                this.Hide();
                if (_trayIcon != null)
                {
                    _trayIcon.Visibility = Visibility.Visible;
                }
                
                return;
            }
            
            base.OnClosing(e);
        }

        private bool IsDeviceConnected()
        {
            // Check if any token file exists
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            return File.Exists(Path.Combine(exeDir, PairingService.TOKEN_FILE)) || 
                   File.Exists(Path.Combine(exeDir, PairingService.TOKEN_FILE_LEGACY)) ||
                   File.Exists(PairingService.TOKEN_FILE) || 
                   File.Exists(PairingService.TOKEN_FILE_LEGACY);
        }

        #endregion

        protected override void OnClosed(EventArgs e)
        {
            if (_isExiting)
            {
                try { _coordinator?.Stop(); } catch { }
                _trayIcon?.Dispose();
                Application.Current.Shutdown();
            }
            base.OnClosed(e);
        }
    }
}
