using System.Configuration;
using System.Data;
using System.Windows;

namespace framebase_app;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            // Global exception handler
            this.DispatcherUnhandledException += (sender, args) =>
            {
                // Log error silently - no UI feedback
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception.Message}");
                args.Handled = true;
            };

            base.OnStartup(e);
            
            // Check if setup has been completed (filesystem flag)
            bool setupCompleted = SetupState.IsSetupCompleted();
            
            if (!setupCompleted)
            {
                var setupWindow = new SetupWindow();
                setupWindow.Show();
            }
            else
            {
                var liveMonitorWindow = new LiveMonitorWindow();
                liveMonitorWindow.Show();
            }
        }
        catch (Exception ex)
        {
            // Startup error - log silently, no UI feedback
            System.Diagnostics.Debug.WriteLine($"Startup exception: {ex.Message}");
        }
    }
}

