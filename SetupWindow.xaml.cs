using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Windows.Media.Animation;
using FramebaseApp;

namespace framebase_app
{
    public partial class SetupWindow : Window
    {
        private readonly PairingService _pairing = new();
        private readonly List<string> _detectedGames = new();
        private readonly Dictionary<string, string> _selectedPresets = new();
        private readonly Dictionary<string, GraphicsConfigurator.PresetCheckResult> _presetStatusByGame = new(StringComparer.OrdinalIgnoreCase);
        private bool _isPaired = false;
        private bool _autostartEnabled = true; // Enabled by default

        public SetupWindow()
        {
            InitializeComponent();
            PairBtn.Click += PairBtn_Click;
            RefreshGamesButton.Click += RefreshGamesButton_Click;
            ContinueButton.Click += ContinueButton_Click;
            FinishSetupButton.Click += FinishSetup_Click;
            PairingCodeBox.TextChanged += PairingCodeBox_TextChanged;
            
            // Load autostart status and update toggle accordingly
            _autostartEnabled = AutostartHelper.IsAutostartEnabled();
            UpdateToggleVisual();
            
            _ = Task.Run(async () => await DetectGameConfigs());
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        private void PairingCodeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Clear error message when user types
            if (!string.IsNullOrEmpty(PairingStatus.Text) && 
                (PairingStatus.Text.Contains("Invalid code") || PairingStatus.Text.Contains("Error pairing")))
            {
                PairingStatus.Text = "";
                try
                {
                    PairingStatus.Foreground = (Brush)FindResource("Brush.TextMuted");
                }
                catch
                {
                    PairingStatus.Foreground = new SolidColorBrush(Colors.Gray);
                }
            }
        }

        private void AutostartToggle_Click(object sender, MouseButtonEventArgs e)
        {
            _autostartEnabled = !_autostartEnabled;
            UpdateToggleVisual();
            
            if (_autostartEnabled)
            {
                AutostartHelper.EnableAutostart();
            }
            else
            {
                AutostartHelper.DisableAutostart();
            }
        }

        private void UpdateToggleVisual()
        {
            if (_autostartEnabled)
            {
                // Toggle is active - knob right, blue color
                var moveAnimation = new DoubleAnimation
                {
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new QuadraticEase()
                };
                AutostartToggleTransform.BeginAnimation(TranslateTransform.XProperty, moveAnimation);
                
                try
                {
                    AutostartToggle.Background = (Brush)FindResource("Brush.Accent");
                }
                catch
                {
                    AutostartToggle.Background = new SolidColorBrush(Color.FromRgb(0, 120, 215));
                }
            }
            else
            {
                // Toggle is inactive - knob left, gray color
                var moveAnimation = new DoubleAnimation
                {
                    To = -18,
                    Duration = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new QuadraticEase()
                };
                AutostartToggleTransform.BeginAnimation(TranslateTransform.XProperty, moveAnimation);
                
                AutostartToggle.Background = new SolidColorBrush(Color.FromRgb(100, 100, 100));
            }
        }

        private async Task DetectGameConfigs()
        {
            _detectedGames.Clear();
            _presetStatusByGame.Clear();
            
            var allGames = new[] { "CS2", "Fortnite", "Forza Horizon 5", "Valorant", "Cyberpunk 2077" };
            
            foreach (var game in allGames)
            {
                try
                {
                    var result = await GraphicsConfigurator.CheckPresetStatusAsync(game);
                    if (result.ConfigFound)
                    {
                        _detectedGames.Add(game);
                        _presetStatusByGame[game] = result;
                    }
                }
                catch { /* Skip game on error */ }
            }
            
            Dispatcher.Invoke(() => UpdateDetectedGamesDisplay());
        }

        private void UpdateDetectedGamesDisplay()
        {
            DetectedGamesPanel.Children.Clear();
            
            if (_detectedGames.Count == 0)
            {
                GamesStatusText.Text = "No games with configuration files found. Launch a supported game once and click 🔄.";
            }
            else
            {
                GamesStatusText.Text = $"{_detectedGames.Count} game(s) with configuration files found:";
                foreach (var game in _detectedGames)
                {
                    DetectedGamesPanel.Children.Add(CreateGameDisplayPanel(game));
                }
            }
        }

        private Panel CreateGameDisplayPanel(string game)
        {
            var gamePanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            
            var logoImage = new Image { Width = 20, Height = 20, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
            var logoPath = GetGameLogoPath(game);
            if (logoPath != null)
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath);
                    bitmap.EndInit();
                    logoImage.Source = bitmap;
                    gamePanel.Children.Add(logoImage);
                }
                catch { gamePanel.Children.Add(new TextBlock { Text = GetGameEmoji(game), FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }); }
            }
            else
            {
                gamePanel.Children.Add(new TextBlock { Text = GetGameEmoji(game), FontSize = 14, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            }

            gamePanel.Children.Add(new TextBlock { Text = GetGameDisplayName(game), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
            gamePanel.Children.Add(new TextBlock { Text = "✅ Config found", FontSize = 11, Foreground = (Brush)FindResource("Brush.TextMuted"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

            return gamePanel;
        }

        private async void RefreshGamesButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshGamesButton.IsEnabled = false;
            RefreshGamesButton.Content = "⏳";
            await DetectGameConfigs();
            Dispatcher.Invoke(() =>
            {
                RefreshGamesButton.Content = "🔄";
                RefreshGamesButton.IsEnabled = true;
            });
        }

        private void ContinueButton_Click(object sender, RoutedEventArgs e)
        {
            if (_detectedGames.Any())
            {
                ShowPresetSelection();
                ContinueButton.Visibility = Visibility.Collapsed;
                FinishSetupButton.Visibility = Visibility.Visible;
                PairingCard.Visibility = Visibility.Collapsed;
                DetectedGamesCard.Visibility = Visibility.Collapsed;
                PresetScroll.Focus();
            }
        }

        private void ShowPresetSelection()
        {
            PresetSelectionCard.Visibility = Visibility.Visible;
            PresetSelectionPanel.Children.Clear();
            
            foreach (var game in _detectedGames)
            {
                var gameCard = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 0, 6), Padding = new Thickness(10) };
                var gamePanel = new StackPanel();
                
                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
                var logoImage = new Image { Width = 18, Height = 18, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
                var logoPath = GetGameLogoPath(game);
                if (logoPath != null)
                {
                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(logoPath);
                        bitmap.EndInit();
                        logoImage.Source = bitmap;
                        headerPanel.Children.Add(logoImage);
                    }
                    catch { headerPanel.Children.Add(new TextBlock { Text = GetGameEmoji(game), FontSize = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }); }
                }
                else
                {
                    headerPanel.Children.Add(new TextBlock { Text = GetGameEmoji(game), FontSize = 12, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                }
                headerPanel.Children.Add(new TextBlock { Text = GetGameDisplayName(game), FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Brush.Text"), VerticalAlignment = VerticalAlignment.Center });
                gamePanel.Children.Add(headerPanel);

                var presetPanel = new StackPanel { Orientation = Orientation.Horizontal };
                var available = _presetStatusByGame.TryGetValue(game, out var status) && status.AvailablePresets.Any() ? status.AvailablePresets : new List<string> { "Performance", "Quality" };
                foreach (var presetName in available)
                {
                    var btn = new Button { Content = presetName, Style = (Style)FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 6, 0), Width = 90, Height = 28, FontSize = 11, Tag = $"{game}|{presetName}" };
                    btn.Click += PresetButton_Click;
                    presetPanel.Children.Add(btn);
                }

                if (_presetStatusByGame.TryGetValue(game, out var st) && st.PresetMatched && !string.IsNullOrEmpty(st.MatchedPresetName))
                {
                    _selectedPresets[game] = st.MatchedPresetName!;
                }

                gamePanel.Children.Add(presetPanel);
                gameCard.Child = gamePanel;
                PresetSelectionPanel.Children.Add(gameCard);
            }
            ApplySelectionStyles();
        }

        private void PresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                var parts = tag.Split('|');
                if (parts.Length == 2)
                {
                    var game = parts[0];
                    var preset = parts[1];
                    _selectedPresets[game] = preset;
                    ApplySelectionStyles();
                }
            }
        }

        private void ApplySelectionStyles()
        {
            foreach (var card in PresetSelectionPanel.Children.OfType<Border>())
            {
                if (card.Child is StackPanel sp && sp.Children.Count >= 2 && sp.Children[1] is StackPanel btnRow)
                {
                    foreach (var btn in btnRow.Children.OfType<Button>())
                    {
                        if (btn.Tag is string tag && tag.Contains("|"))
                        {
                            var parts = tag.Split('|');
                            var game = parts[0];
                            var preset = parts[1];
                            btn.Style = _selectedPresets.TryGetValue(game, out var selected) && string.Equals(selected, preset, StringComparison.OrdinalIgnoreCase)
                                ? (Style)FindResource("PrimaryButton")
                                : (Style)FindResource("SecondaryButton");
                        }
                    }
                }
            }
        }

        private async void PairBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                PairBtn.IsEnabled = false;
                var code = PairingCodeBox.Text?.Trim() ?? string.Empty;
                PairingStatus.Text = "Pairing...";
                
                var result = await _pairing.PairDeviceAsync(code);
                
                if (result.Contains("success", StringComparison.OrdinalIgnoreCase))
                {
                    _isPaired = true;
                    PairingInputPanel.Visibility = Visibility.Collapsed;
                    PairingSuccessPanel.Visibility = Visibility.Visible;
                    var email = result.Split('(').Length > 1 ? result.Split('(')[1].TrimEnd(')') : "your account";
                    ConnectedEmailText.Text = $"Connected to: {email}";
                    PairingStatus.Text = "";
                    ContinueButton.IsEnabled = true;
                }
                else
                {
                    _isPaired = false;
                    PairingStatus.Text = $"Invalid code: {result}";
                    PairingStatus.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
            catch (Exception ex)
            {
                _isPaired = false;
                PairingStatus.Text = $"Error pairing: {ex.Message}";
                PairingStatus.Foreground = new SolidColorBrush(Colors.Red);
            }
            finally
            {
                if (!_isPaired) { PairBtn.IsEnabled = true; }
            }
        }

        private void FinishSetup_Click(object sender, RoutedEventArgs e)
        {
            FinishSetupButton.IsEnabled = false;
            FinishSetupButton.Content = "Applying...";

            Task.Run(async () =>
            {
                foreach (var (game, preset) in _selectedPresets)
                {
                    try
                    {
                        if (GraphicsConfigurator.AreGraphicsSettingsUnchanged(game, preset)) continue;

                        if (_presetStatusByGame.TryGetValue(game, out var status) && !string.IsNullOrEmpty(status.ConfigPath))
                        {
                            switch (game)
                            {
                                case "CS2": 
                                    await GraphicsConfigurator.UpdateCS2VideoConfigAsync(status.ConfigPath, preset);
                                    break;
                                case "Fortnite":
                                    await GraphicsConfigurator.UpdateFortniteVideoConfigAsync(status.ConfigPath, preset);
                                    break;
                                case "Forza Horizon 5":
                                    await GraphicsConfigurator.UpdateForzaHorizon5ConfigAsync(status.ConfigPath, preset);
                                    break;
                                case "Valorant":
                                    await GraphicsConfigurator.UpdateValorantConfigAsync(status.ConfigPath, preset);
                                    break;
                                case "Cyberpunk 2077":
                                    await GraphicsConfigurator.UpdateCp2077VideoConfigAsync(status.ConfigPath, preset);
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() => MessageBox.Show($"Error applying preset for {game}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error));
                    }
                }
                
                Dispatcher.Invoke(() =>
                {
                    SetupState.MarkCompleted();
                    // After successful setup, switch to LiveMonitor
                    var liveMonitor = new LiveMonitorWindow();
                    liveMonitor.Show();
                    Close();
                });
            });
        }

        private string? GetGameLogoPath(string game)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var logoFileName = game switch
            {
                "CS2" => "cs2.png",
                "Fortnite" => "fortnite.png", 
                "Forza Horizon 5" => "fh5.png",
                "Valorant" => "valorant.png",
                "Cyberpunk 2077" => "cyberpunk.png",
                _ => game.ToLower().Replace(" ", "").Replace(":", "") + ".png"
            };
            var path = Path.Combine(baseDir, "logos", logoFileName);
            return File.Exists(path) ? path : null;
        }

        private string GetGameEmoji(string game) => game switch
        {
            "CS2" => "💣", "Fortnite" => "⛏️", "Forza Horizon 5" => "🏎️",
            "Valorant" => "🔫", "Cyberpunk 2077" => "🤖", _ => "🎮"
        };

        private string GetGameDisplayName(string game) => game switch
        {
            "CS2" => "Counter-Strike 2", "Forza Horizon 5" => "Forza Horizon 5", _ => game
        };
    }
}
