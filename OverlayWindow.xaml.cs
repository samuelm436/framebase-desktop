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
        private Queue<(DateTime time, float gpu, float ram, float vram)> _loadHistory = new();
        private const int BOTTLENECK_CHECK_SECONDS = 5;

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
            // Only show Load %
            CpuLoad.Text = metrics.CpuLoad >= 0 ? $"{Math.Round(metrics.CpuLoad)}%" : "-";
            GpuLoad.Text = metrics.GpuLoad >= 0 ? $"{Math.Round(metrics.GpuLoad)}%" : "-";
            RamLoad.Text = metrics.RamLoad >= 0 ? $"{Math.Round(metrics.RamLoad)}%" : "-";
            VramLoad.Text = metrics.VramLoad >= 0 ? $"{Math.Round(metrics.VramLoad)}%" : "-";

            // Track load history for bottleneck detection
            _loadHistory.Enqueue((DateTime.Now, metrics.GpuLoad, metrics.RamLoad, metrics.VramLoad));
            
            // Remove old entries (older than 5 seconds)
            var cutoff = DateTime.Now.AddSeconds(-BOTTLENECK_CHECK_SECONDS);
            while (_loadHistory.Count > 0 && _loadHistory.Peek().time < cutoff)
                _loadHistory.Dequeue();

            // Bottleneck analysis
            AnalyzeBottleneck(metrics);
        }

        private void AnalyzeBottleneck(FramebaseApp.HardwareMonitor.HardwareMetrics metrics)
        {
            // Skip if not enough data
            if (_loadHistory.Count < 3)
            {
                BottleneckWarning.Visibility = Visibility.Collapsed;
                return;
            }

            var recent = _loadHistory.ToList();
            float avgGpu = recent.Average(x => x.gpu);
            float avgRam = recent.Average(x => x.ram);
            float avgVram = recent.Average(x => x.vram);

            // GPU > 90% = All good (GPU is the bottleneck, which is ideal)
            if (avgGpu >= 90)
            {
                BottleneckWarning.Visibility = Visibility.Collapsed;
                GpuLoad.Foreground = Brushes.LimeGreen;
                RamLoad.Foreground = Brushes.White;
                VramLoad.Foreground = Brushes.White;
                return;
            }

            // RAM or VRAM > 90% = Memory bottleneck
            if (avgRam >= 90)
            {
                BottleneckWarning.Visibility = Visibility.Visible;
                BottleneckText.Text = "⚠ RAM LIMIT";
                BottleneckText.Foreground = Brushes.Orange;
                RamLoad.Foreground = Brushes.Orange;
                GpuLoad.Foreground = Brushes.White;
                VramLoad.Foreground = Brushes.White;
                return;
            }

            if (avgVram >= 90)
            {
                BottleneckWarning.Visibility = Visibility.Visible;
                BottleneckText.Text = "⚠ VRAM LIMIT";
                BottleneckText.Foreground = Brushes.Orange;
                VramLoad.Foreground = Brushes.Orange;
                GpuLoad.Foreground = Brushes.White;
                RamLoad.Foreground = Brushes.White;
                return;
            }

            // All < 90% for sustained period = CPU Bottleneck
            if (avgGpu < 90 && avgRam < 90 && avgVram < 90)
            {
                BottleneckWarning.Visibility = Visibility.Visible;
                BottleneckText.Text = "⚠ CPU BOTTLENECK";
                BottleneckText.Foreground = Brushes.Red;
                CpuLoad.Foreground = Brushes.Red;
                GpuLoad.Foreground = Brushes.White;
                RamLoad.Foreground = Brushes.White;
                VramLoad.Foreground = Brushes.White;
                return;
            }

            // Default state
            BottleneckWarning.Visibility = Visibility.Collapsed;
            CpuLoad.Foreground = Brushes.White;
            GpuLoad.Foreground = Brushes.White;
            RamLoad.Foreground = Brushes.White;
            VramLoad.Foreground = Brushes.White;
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
