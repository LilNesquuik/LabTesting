using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace LabTesting.Runner;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.SequenceEqual(["--selftest-sleeper"])) { await Task.Delay(60000); return 0; }
            if (args.SequenceEqual(["--selftest-parent"]))
            {
                using Process child = Process.Start(SelfTest.SelfStart("--selftest-sleeper"))!;
                Console.WriteLine(child.Id);
                await Task.Delay(60000);
                return 0;
            }
            if (args.SequenceEqual(["--selftest"])) return SelfTest.Run();
            if (args.Length > 0 && args[0] == "init") return Init.Run(args[1..]);
            Options o = Options.Parse(args);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            // Both handlers are unhooked before the using scope ends; ReSharper cannot see it.
            // ReSharper disable AccessToDisposedClosure
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            using PosixSignalRegistration? term = OperatingSystem.IsWindows() ? null :
                PosixSignalRegistration.Create(PosixSignal.SIGTERM, c => { c.Cancel = true; cancellation.Cancel(); });
            // ReSharper restore AccessToDisposedClosure
            try { return await Run(o, cancellation.Token); }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (Exception e) { Console.Error.WriteLine("[labtest] " + e); return 2; }
    }

    internal static async Task<int> Run(Options o, CancellationToken cancellation)
    {
        SuiteConfig c = o.Suite;
        string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N");
        string reports = Path.Combine(c.Reports, id);
        Directory.CreateDirectory(reports);
        Console.WriteLine("[labtest] reports: " + reports);
        string work = Path.Combine(c.Work ?? Path.GetTempPath(), "labtest-" + id);
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, ".labtesting-owned"), id);
        int exit = -1;
        string? error = null;
        try
        {
            using FileStream portLock = new FileStream(Path.Combine(Path.GetTempPath(), "labtest-port-" + c.Port + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            foreach (SocketType type in new[] { SocketType.Dgram, SocketType.Stream })
            {
                using Socket socket = new Socket(AddressFamily.InterNetworkV6, type,
                    type == SocketType.Dgram ? ProtocolType.Udp : ProtocolType.Tcp);
                socket.DualMode = true;
                socket.ExclusiveAddressUse = true;
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, c.Port));
            }
            Layout layout = Deployment.Prepare(o, work, reports, cancellation);
            exit = await Launch(layout.Executable, layout.Installation, layout.Config, reports, c, cancellation);
        }
        catch (Exception e) { error = e.ToString(); }
        finally
        {
            if (!c.Keep)
            {
                try
                {
                    if (File.ReadAllText(Path.Combine(work, ".labtesting-owned")) != id)
                        throw new IOException("Ownership marker was modified.");
                    for (int attempt = 0; ; attempt++)
                    {
                        try { Deployment.DeleteOwned(work); break; }
                        catch (Exception e) when (attempt < 20 && e is IOException or UnauthorizedAccessException)
                        { await Task.Delay(250); }
                    }
                }
                catch (Exception e) { error += "\nNettoyage: " + e.Message; }
            }
            else Console.WriteLine("[labtest] work kept: " + work);
        }
        string path = Path.Combine(reports, "labtesting-results.jsonl");
        Verdict verdict = Verdict.Parse(File.Exists(path) ? File.ReadLines(path) : [], o.List);
        if (exit != 0) verdict.Problems.Add("Server exit code: " + exit + " (crash, timeout or cancellation).");
        if (error != null) verdict.Problems.Add(error);
        verdict.WriteReports(reports);
        foreach (string line in verdict.Lines) Console.WriteLine("[labtest] " + line);
        foreach (string problem in verdict.Problems) Console.Error.WriteLine("[labtest] " + problem);
        Console.WriteLine("[labtest] " + verdict.Summary);
        return verdict.ExitCode(c.FailOnSkipped);
    }

    private static async Task<int> Launch(string exe, string cwd, string config, string reports,
        SuiteConfig c, CancellationToken cancellation)
    {
        ProcessStartInfo psi = new ProcessStartInfo(exe) {
            WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (OperatingSystem.IsLinux())
        {
            if (!File.Exists("/usr/bin/setsid")) throw new FileNotFoundException("Installez util-linux (/usr/bin/setsid).");
            psi.FileName = "/usr/bin/setsid";
            psi.ArgumentList.Add(exe);
        }
        foreach (string arg in new[] { "-nographics", "-batchmode", "-stdout", "-disableconfigvalidation",
                     "-configpath", config, "-port" + c.Port, "-id" + Environment.ProcessId })
            psi.ArgumentList.Add(arg);
        string home = Path.Combine(cwd, "home");
        Directory.CreateDirectory(home);
        foreach (string key in new[] { "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA", "XDG_CONFIG_HOME", "XDG_DATA_HOME" })
            psi.Environment[key] = home;
        psi.Environment["LABTESTING_ENABLED"] = "1";
        using Process process = new Process { StartInfo = psi };
        using ProcessContainment containment = new ProcessContainment();
        using FileStream stdout = new FileStream(Path.Combine(reports, "stdout.log"), FileMode.Create);
        using FileStream stderr = new FileStream(Path.Combine(reports, "stderr.log"), FileMode.Create);
        cancellation.ThrowIfCancellationRequested();
        process.Start();
        // Kill() is wired to ProcessExit and to cancellation, so it can fire after the using
        // scope disposed these. That is the point, and the catch below covers the race.
        // ReSharper disable AccessToDisposedClosure
        void Kill()
        {
            containment.Dispose();
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
        // ReSharper restore AccessToDisposedClosure
        EventHandler onExit = (_, _) => Kill();
        AppDomain.CurrentDomain.ProcessExit += onExit;
        using CancellationTokenRegistration registration = cancellation.Register(Kill);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(c.TimeoutSeconds));
        Task output = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task errors = process.StandardError.BaseStream.CopyToAsync(stderr);
        try
        {
            containment.Attach(process);
            await process.WaitForExitAsync(timeout.Token);
            containment.Dispose();
            await Task.WhenAll(output, errors).WaitAsync(timeout.Token);
            return cancellation.IsCancellationRequested ? -1 : process.ExitCode;
        }
        catch (OperationCanceledException) { return -1; }
        finally
        {
            Kill();
            await process.WaitForExitAsync();
            try { await Task.WhenAll(output, errors).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { }
            AppDomain.CurrentDomain.ProcessExit -= onExit;
        }
    }
}
