using CommandSystem;
using LabApi.Loader;
using LabApi.Loader.Features.Plugins;
using RemoteAdmin;

namespace LabTesting;

/// <summary>
/// Outcome of a command execution.
/// </summary>
public readonly struct CommandResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommandResult"/> structure.
    /// </summary>
    /// <param name="success">Whether the command reported success.</param>
    /// <param name="response">The command response; null is accepted and stored as empty.</param>
    public CommandResult(bool success, string? response)
    {
        Success = success;
        Response = response ?? string.Empty;
    }

    /// <summary>
    /// Gets the boolean the command returned.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// Gets the response the command wrote, never null.
    /// </summary>
    public string Response { get; }

    /// <inheritdoc/>
    public override string ToString() => (Success ? "OK" : "KO") + ": " + Response;
}

/// <summary>
/// The three command registries the game exposes. A command is only found in the registry it was
/// registered with.
/// </summary>
public enum Where
{
    /// <summary>
    /// Server console commands.
    /// </summary>
    GameConsole,

    /// <summary>
    /// Remote admin commands. This is the default for <see cref="Commands.Run(string, string[])"/>.
    /// </summary>
    RemoteAdmin,

    /// <summary>
    /// Client commands, the ones players invoke with a leading dot.
    /// </summary>
    Client
}

/// <summary>
/// Runs server commands by calling them directly, without going through the console.
/// </summary>
/// <remarks>
/// Calling the command interface removes any dependency on the internal prompter queue or on the
/// TCP console channel. Two details make a naive implementation fail silently, and both shape this
/// API.
/// <para>
/// First, the permission check casts the sender to the game's abstract CommandSender class and
/// returns false for anything else (Misc.cs:229-249). A hand-written ICommandSender therefore
/// fails every permission check, even though the interface itself only has two members.
/// </para>
/// <para>
/// Second, many commands require the sender to be a player and reject anything else
/// (BringCommand.cs:30, OverwatchCommand.cs:40, and others). Hence two senders rather than one.
/// </para>
/// <para>
/// Commands are resolved from the registry instead of being constructed, because a stateful
/// command, or one registered by a plugin, would not be the same instance.
/// </para>
/// </remarks>
public static class Commands
{
    /// <summary>
    /// Gets the server-scoped sender, the instance the game already builds for its own console.
    /// It has full permissions, which short-circuits the permission check.
    /// </summary>
    public static ICommandSender Server => ServerConsole.Scs;

    /// <summary>
    /// Runs a remote admin command as the server.
    /// </summary>
    /// <param name="name">Command name or alias.</param>
    /// <param name="args">Arguments passed to the command.</param>
    public static CommandResult Run(string name, params string[] args) =>
        Run(name, Where.RemoteAdmin, Server, args);

    /// <summary>
    /// Runs a remote admin command as a player.
    /// </summary>
    /// <param name="player">Player the command is attributed to.</param>
    /// <param name="name">Command name or alias.</param>
    /// <param name="args">Arguments passed to the command.</param>
    /// <remarks>
    /// This is the only accepted path for player-scoped commands. The permissions used are the
    /// player's own, which for a freshly spawned dummy means none: raise them first when testing
    /// the nominal path, and leave them alone when testing the refusal.
    /// </remarks>
    public static CommandResult RunAs(TestPlayer player, string name, params string[] args) =>
        Run(name, Where.RemoteAdmin, new PlayerCommandSender(player.Hub), args);

    /// <summary>
    /// Resolves a command in the given registry and runs it.
    /// </summary>
    /// <returns>
    /// The command result, or an unsuccessful result explaining that the command was not found.
    /// </returns>
    public static CommandResult Run(string name, Where where, ICommandSender sender, params string[] args)
    {
        if (!TryResolve(name, where, out ICommand command))
            return new CommandResult(false, "command '" + name + "' not found in " + where + ".");

        return Run(command, sender, args);
    }

    /// <summary>
    /// Runs an already resolved command.
    /// </summary>
    /// <remarks>
    /// A command that throws is reported as an unsuccessful result rather than propagated: direct
    /// invocation has none of the try/catch the real dispatcher provides, and a throwing command
    /// is a test finding, not a harness failure.
    /// </remarks>
    public static CommandResult Run(ICommand command, ICommandSender sender, params string[]? args)
    {
        ArraySegment<string> segment = new ArraySegment<string>(args ?? []);
        try
        {
            bool success = command.Execute(segment, sender, out string response);
            return new CommandResult(success, response);
        }
        catch (Exception e)
        {
            return new CommandResult(false, e.GetType().Name + ": " + e.Message);
        }
    }

    /// <summary>
    /// Looks up a registered command by name or alias.
    /// </summary>
    public static bool TryResolve(string name, Where where, out ICommand command) =>
        Handler(where).TryGetCommand(name, out command);

    /// <summary>
    /// Returns the commands a given plugin registered, so a test can reach them without knowing
    /// their names or which registry they went into.
    /// </summary>
    public static IReadOnlyList<ICommand> Of(Plugin plugin)
    {
        if (!CommandLoader.RegisteredCommands.TryGetValue(plugin, out IEnumerable<ICommand> commands))
            return [];

        return new List<ICommand>(commands);
    }

    private static ICommandHandler Handler(Where where)
    {
        switch (where)
        {
            case Where.GameConsole:
                return GameCore.Console.ConsoleCommandHandler;
            case Where.Client:
                return QueryProcessor.DotCommandHandler;
            default:
                return CommandProcessor.RemoteAdminCommandHandler;
        }
    }
}
