using GameCore;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;

namespace LabTesting;

/// <summary>
/// The LabAPI plugin that hosts the harness. Loading it turns a dedicated server into a test
/// runner.
/// </summary>
public sealed class TestPlugin : Plugin
{
    /// <summary>
    /// Name of the sentinel file the runner writes next to the server configuration. Without it
    /// the harness refuses to arm, so a production server cannot start running tests by accident.
    /// </summary>
    public const string SentinelFileName = "LABTESTING_ENABLED";

    /// <summary>
    /// Name of the result file, written in the server configuration directory.
    /// </summary>
    public const string VerdictFileName = "labtesting-results.jsonl";

    /// <inheritdoc/>
    public override string Name => "LabTesting";

    /// <inheritdoc/>
    public override string Description => "In-process test harness for LabAPI plugins.";

    /// <inheritdoc/>
    public override string Author => "LabTesting";

    /// <inheritdoc/>
    public override System.Version RequiredApiVersion => new(1, 1, 0);

    /// <inheritdoc/>
    public override LoadPriority Priority => LoadPriority.Highest;

    internal static SwallowedSink? Sink { get; private set; }

    /// <summary>
    /// Checks the safety conditions, installs the console sink and the pump, and starts the suite.
    /// </summary>
    public override void Enable()
    {
        string? refusal = Guard();
        if (refusal != null)
        {
            Logger.Warn("[LabTesting] load refused - " + refusal);
            return;
        }

        Sink = new SwallowedSink();
        Sink.Install();

        Pump.Install();

        string verdictPath = Path.Combine(new SuiteRequest().Reports, VerdictFileName);
        Logger.Info("[LabTesting] harness armed, verdict -> " + verdictPath);

        // Started without awaiting: the pump drives it forward.
        _ = Run(verdictPath);
    }

    /// <summary>
    /// Removes the console sink.
    /// </summary>
    /// <remarks>
    /// LabAPI never calls this method: the loader has no unload path.
    /// </remarks>
    public override void Disable()
    {
        Sink?.Uninstall();
        Sink = null;
    }

    private static async Task Run(string verdictPath)
    {
        try
        {
            await Runner.RunAll(verdictPath);
        }
        catch (Exception e)
        {
            Logger.Error("[LabTesting] " + e);
        }
        finally
        {
            Logger.Info("[LabTesting] shutting the test server down.");
            Shutdown.Quit();
        }
    }

    /// <summary>
    /// Returns why the harness must not arm, or null when it is safe to run.
    /// </summary>
    /// <remarks>
    /// The online mode flag is read from the configuration rather than from the static field that
    /// mirrors it, which may still hold its default value.
    /// </remarks>
    private static string? Guard()
    {
        if (!ServerStatic.IsDedicated)
            return "the server is not dedicated.";

        if (ConfigFile.ServerConfig == null)
            return "the server configuration is not loaded.";

        if (ConfigFile.ServerConfig.GetBool("online_mode", def: true))
            return "online_mode is enabled: a test server must run with online_mode: false.";

        string sentinel = Path.Combine(ConfigDirectory(), SentinelFileName);
        if (!File.Exists(sentinel))
            return "sentinel file missing (" + sentinel + ").";

        return null;
    }

    /// <summary>
    /// Returns the effective server configuration directory, which is where the sentinel is read
    /// and the result file is written.
    /// </summary>
    /// <remarks>
    /// This is the directory passed with the configuration path argument.
    /// </remarks>
    internal static string ConfigDirectory() =>
        FileManager.GetAppFolder(addSeparator: true, serverConfig: true);
}
