using System.Windows;

namespace VoiceWin;

public partial class App : Application
{
    private Services.TranscriptionOrchestrator? _orchestrator;
    private Services.SettingsService? _settingsService;

    public Services.SettingsService SettingsService => _settingsService!;
    public Services.TranscriptionOrchestrator Orchestrator => _orchestrator!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settingsService = new Services.SettingsService();
        _orchestrator = new Services.TranscriptionOrchestrator(_settingsService);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _orchestrator?.Dispose();
        base.OnExit(e);
    }
}
