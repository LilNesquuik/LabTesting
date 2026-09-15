using System;
using System.IO;
using GameCore;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using LabApi.Loader.Features.Plugins.Enums;

namespace LabTesting;

/// <summary>
/// The LabAPI plugin that hosts the harness. Loading it turns a dedicated server into a test
/// runner.
/// </summary>
/// <remarks>
/// Enable runs from ServerStatic.Awake, through the plugin loader (ServerStatic.cs:104). The very
/// next line (:105) is what subscribes the scene handler that eventually starts the lobby, so the
/// harness is always installed before the network comes up, without relying on Unity's Awake and
/// Start ordering.
/// <para>
/// The load priority is the highest available, because the loader sorts ascending and the highest
/// priority is the lowest numeric value. The harness is therefore enabled before the plugins it
/// observes, and can install its pump and its console sink before they subscribe to anything.
/// </para>
/// </remarks>
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
    public override string Description => "Harnais de tests in-process pour plugins LabAPI.";

    /// <inheritdoc/>
    public override string Author => "LabTesting";

    /// <inheritdoc/>
    public override System.Version RequiredApiVersion => new System.Version(1, 1, 0);

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
            // Refuse loudly but without throwing: the loader would only turn the exception into
            // a log line anyway (PluginLoader.cs:171-184), with a less readable message.
            Logger.Warn("[LabTesting] chargement refusé — " + refusal);
            return;
        }

        Sink = new SwallowedSink();
        Sink.Install();

        Pump.Install();

        string verdictPath = Path.Combine(new SuiteRequest().Reports, VerdictFileName);
        Logger.Info("[LabTesting] harnais armé, verdict → " + verdictPath);

        // Started without awaiting: the pump drives it forward. RunAll already catches its own
        // exceptions; the wrapper below is a last resort so nothing is lost silently.
        _ = Run(verdictPath);
    }

    /// <summary>
    /// Removes the console sink.
    /// </summary>
    /// <remarks>
    /// LabAPI never calls this method: the loader has no unload path. It exists to satisfy the
    /// plugin contract, not as part of the lifecycle the harness relies on.
    /// </remarks>
    public override void Disable()
    {
        Sink?.Uninstall();
        Sink = null;
    }

    private static async System.Threading.Tasks.Task Run(string verdictPath)
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
            Logger.Info("[LabTesting] arrêt du serveur de test.");
            Shutdown.Quit();
        }
    }

    /// <summary>
    /// Returns why the harness must not arm, or null when it is safe to run.
    /// </summary>
    /// <remarks>
    /// Three conditions, because none of them discriminates on its own: a production server is
    /// also dedicated, and a hosting setup may legitimately run offline. The sentinel file is what
    /// makes the intent explicit.
    /// <para>
    /// The online mode flag is read from the configuration rather than from the static field that
    /// mirrors it. That field is a plain static populated by the configuration type's static
    /// constructor, and on a dedicated server the other code path that would populate it is
    /// disabled. Touching the configuration forces that constructor to run; reading the field does
    /// not, and could observe its default value.
    /// </para>
    /// </remarks>
    private static string? Guard()
    {
        if (!ServerStatic.IsDedicated)
            return "le serveur n'est pas dédié.";

        if (ConfigFile.ServerConfig == null)
            return "la configuration serveur n'est pas chargée.";

        if (ConfigFile.ServerConfig.GetBool("online_mode", def: true))
            return "online_mode est actif : un serveur de test doit tourner en online_mode: false.";

        string sentinel = Path.Combine(ConfigDirectory(), SentinelFileName);
        if (!File.Exists(sentinel))
            return "fichier sentinelle absent (" + sentinel + ").";

        return null;
    }

    /// <summary>
    /// Returns the effective server configuration directory, which is where the sentinel is read
    /// and the result file is written.
    /// </summary>
    /// <remarks>
    /// This is the directory passed with the configuration path argument. The game falls back to
    /// its default silently when that path does not exist, which is why the runner creates and
    /// populates it before starting the server.
    /// </remarks>
    internal static string ConfigDirectory() =>
        FileManager.GetAppFolder(addSeparator: true, serverConfig: true);
}
