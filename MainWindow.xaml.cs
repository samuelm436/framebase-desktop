using System.Windows;
using System.Windows.Threading;
using FramebaseApp;

namespace framebase_app
{
    public partial class MainWindow : Window
    {
        private UploadCoordinator _coordinator;
        private DispatcherTimer _mockUiTimer = new();
        public MainWindow()
        {
            InitializeComponent();
            
            _coordinator = new UploadCoordinator();
            _coordinator.LiveMetrics += (fps, frametime, low1, state) =>
            {
                this.Title = $"Framebase - {fps:F1} FPS | {frametime:F1}ms | 1%: {low1:F1} | {state}";
            };
            _coordinator.SessionCounterChanged += seconds =>
            {
                // reflect counter in title as well
                if (!this.Title.Contains("Framebase")) return;
            };
            _coordinator.UploadCompleted += (ok, msg, avgFps, low1) =>
            {
                // Upload completed - no UI feedback needed
            };

            _coordinator.ConfigureEnvironment(SystemInfoHelper.GetCpu() ?? "Unknown CPU", SystemInfoHelper.GetGpu() ?? "Unknown GPU", "1920x1080");
        }
    }
}