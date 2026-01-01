using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace framebase_app
{
    public partial class OverlayWindow : Window
    {
        private Polyline? _frametimeGraph;

        public OverlayWindow()
        {
            InitializeComponent();
            this.MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed) this.DragMove(); };
            this.Loaded += (s, e) => 
            {
                SetClickThrough(false);
                InitGraph();
            };
        }

        private void InitGraph()
        {
            _frametimeGraph = new Polyline
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 1,
                SnapsToDevicePixels = true
            };
            FrametimeCanvas.Children.Add(_frametimeGraph);
        }

        public void UpdateMetrics(double fps, double low1, double avg, bool isActive, bool isSupported)
        {
            FpsValue.Text = Math.Round(fps).ToString();
            LowValue.Text = Math.Round(low1).ToString();
            AvgValue.Text = Math.Round(avg).ToString();
            
            if (!isSupported)
            {
                StatusText.Text = "Game not supported";
                StatusText.Foreground = Brushes.Red;
            }
            else if (isActive)
            {
                StatusText.Text = "Active";
                StatusText.Foreground = Brushes.LimeGreen;
            }
            else
            {
                StatusText.Text = "Inactive";
                StatusText.Foreground = Brushes.Gray;
            }
        }

        public void UpdateHardwareInfo(FramebaseApp.HardwareMonitor.HardwareMetrics metrics)
        {
            // Column 1: Load %
            // Column 2: Temp °C or Absolute Value
            
            // CPU
            CpuLoad.Text = metrics.CpuLoad >= 0 ? $"{Math.Round(metrics.CpuLoad)}%" : "-";
            CpuTemp.Text = metrics.CpuTemp >= 0 ? $"{Math.Round(metrics.CpuTemp)}°C" : "-";
            
            // GPU
            GpuLoad.Text = metrics.GpuLoad >= 0 ? $"{Math.Round(metrics.GpuLoad)}%" : "-";
            GpuTemp.Text = metrics.GpuTemp >= 0 ? $"{Math.Round(metrics.GpuTemp)}°C" : "-";
            
            // RAM
            RamLoad.Text = metrics.RamLoad >= 0 ? $"{Math.Round(metrics.RamLoad)}%" : "-";
            RamUsed.Text = metrics.RamUsed >= 0 ? $"{Math.Round(metrics.RamUsed, 1)} GB" : "-";
            
            // VRAM
            VramLoad.Text = metrics.VramLoad >= 0 ? $"{Math.Round(metrics.VramLoad)}%" : "-";
            VramUsed.Text = metrics.VramUsed >= 0 ? $"{Math.Round(metrics.VramUsed, 1)} GB" : "-";
        }

        public void UpdateFrametimeGraph(List<double> frametimes)
        {
            if (frametimes == null || frametimes.Count == 0 || _frametimeGraph == null) return;

            double width = FrametimeCanvas.ActualWidth;
            double height = FrametimeCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            // Keep last N points to fill width
            int maxPoints = (int)width; 
            var points = frametimes.TakeLast(maxPoints).ToList();
            
            if (points.Count == 0) return;

            double maxFt = points.Max();
            if (maxFt < 16.6) maxFt = 16.6; // Scale at least to 60fps
            
            // Update text value
            FrametimeValue.Text = $"{Math.Round(points.Last(), 1)} ms";

            var collection = new PointCollection();
            double step = width / Math.Max(points.Count - 1, 1);

            for (int i = 0; i < points.Count; i++)
            {
                double x = i * step;
                // Invert Y because 0 is top
                double y = height - ((points[i] / maxFt) * height);
                collection.Add(new Point(x, y));
            }

            _frametimeGraph.Points = collection;
        }

        // --- Click-Through Logic ---

        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        public void SetClickThrough(bool clickThrough)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (clickThrough)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
                this.IsHitTestVisible = false;
                this.Focusable = false;
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
                this.IsHitTestVisible = true;
                this.Focusable = true;
            }
        }

        public void SetScale(double scale)
        {
            if (OverlayScale != null)
            {
                OverlayScale.ScaleX = scale;
                OverlayScale.ScaleY = scale;
            }
        }

        public void SetEditMode(bool enabled)
        {
            // In edit mode, we want to be able to click and drag, so clickThrough = false
            SetClickThrough(!enabled);
            
            if (enabled)
            {
                RootBorder.Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)); // Semi-transparent black
                RootBorder.BorderBrush = Brushes.White;
                RootBorder.BorderThickness = new Thickness(1);
            }
            else
            {
                RootBorder.Background = Brushes.Transparent;
                RootBorder.BorderBrush = Brushes.Transparent;
                RootBorder.BorderThickness = new Thickness(0);
            }
        }

        public void ToggleSection(string section, bool visible)
        {
            Visibility v = visible ? Visibility.Visible : Visibility.Collapsed;
            switch (section)
            {
                case "FPS": FpsSection.Visibility = v; break;
                case "Graph": GraphSection.Visibility = v; break;
                case "Hardware": HardwareSection.Visibility = v; break;
            }
        }
    }
}
