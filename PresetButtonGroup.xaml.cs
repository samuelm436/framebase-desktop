using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Collections.Generic;
using System.Linq;

namespace framebase_app
{
    public partial class PresetButtonGroup : UserControl
    {
        private List<Button> _presetButtons = new List<Button>();
        private string? _selectedPreset;
        
        public event EventHandler<string>? PresetSelected;
        
        public PresetButtonGroup()
        {
            InitializeComponent();
        }
        
        public void SetPresets(IEnumerable<string> presets, string? selectedPreset = null)
        {
            ButtonContainer.Children.Clear();
            _presetButtons.Clear();
            _selectedPreset = selectedPreset;
            
            foreach (var preset in presets)
            {
                var button = CreatePresetButton(preset);
                _presetButtons.Add(button);
                ButtonContainer.Children.Add(button);
            }
            
            if (!string.IsNullOrEmpty(selectedPreset) && _presetButtons.Any(b => b.Tag?.ToString() == selectedPreset))
            {
                SelectPreset(selectedPreset);
            }
            else if (_presetButtons.Count > 0)
            {
                SelectPreset(_presetButtons[0].Tag?.ToString());
            }
        }
        
        private Button CreatePresetButton(string presetName)
        {
            var button = new Button
            {
                Content = presetName,
                Tag = presetName,
                Style = (Style)FindResource("PresetButton"),
                Margin = new Thickness(0, 0, 8, 8), // Added bottom margin for wrapping
                Width = 111, // Fixed width for all buttons
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            button.Click += OnPresetButtonClick;
            return button;
        }
        
        private void OnPresetButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string preset)
            {
                SelectPreset(preset);
                PresetSelected?.Invoke(this, preset);
            }
        }
        
        public void SelectPreset(string? presetName)
        {
            if (string.IsNullOrEmpty(presetName)) return;
            
            _selectedPreset = presetName;
            
            foreach (var button in _presetButtons)
            {
                UpdateButtonState(button, button.Tag?.ToString() == presetName);
            }
        }
        
        private void UpdateButtonState(Button button, bool isSelected)
        {
            AnimateButton(button, isSelected);
        }
        
        private void AnimateButton(Button button, bool selected)
        {
            var duration = TimeSpan.FromMilliseconds(200);
            var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
            
            var backgroundAnimation = new ColorAnimation
            {
                Duration = duration,
                EasingFunction = easing,
                To = selected ? Color.FromRgb(0x63, 0x66, 0xF1) : Color.FromRgb(0x1F, 0x29, 0x37)
            };
            
            var borderAnimation = new ColorAnimation
            {
                Duration = duration,
                EasingFunction = easing,
                To = selected ? Color.FromRgb(0x3B, 0x82, 0xF6) : Color.FromRgb(0x1F, 0x29, 0x37)
            };
            
            var scaleXAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing,
                To = selected ? 1.05 : 1.0
            };
            
            var scaleYAnimation = new DoubleAnimation
            {
                Duration = duration,
                EasingFunction = easing,
                To = selected ? 1.05 : 1.0
            };
            
            var backgroundBrush = new SolidColorBrush();
            var borderBrush = new SolidColorBrush();
            var scaleTransform = new ScaleTransform();
            
            button.Background = backgroundBrush;
            button.BorderBrush = borderBrush;
            button.RenderTransform = scaleTransform;
            button.RenderTransformOrigin = new Point(0.5, 0.5);
            
            // Add glow effect for selected button
            if (selected)
            {
                var glowEffect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x63, 0x66, 0xF1),
                    BlurRadius = 12,
                    ShadowDepth = 0,
                    Opacity = 0.6
                };
                button.Effect = glowEffect;
            }
            else
            {
                button.Effect = null;
            }
            
            backgroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
            borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnimation);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnimation);
            
            var foregroundBrush = new SolidColorBrush();
            button.Foreground = foregroundBrush;
            var textColorAnimation = new ColorAnimation
            {
                Duration = duration,
                EasingFunction = easing,
                To = selected ? Colors.White : Color.FromRgb(0x9C, 0xA3, 0xAF)
            };
            foregroundBrush.BeginAnimation(SolidColorBrush.ColorProperty, textColorAnimation);
        }
        
        public string? SelectedPreset => _selectedPreset;
    }
}