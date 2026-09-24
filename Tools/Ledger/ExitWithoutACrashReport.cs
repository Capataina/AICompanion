#nullable enable

using System;
using System.Runtime.CompilerServices;

namespace AICompanion.Tools.Ledger;

/// <summary>
/// Turns an exception nothing caught into an ordinary failing exit, so a tool that throws prints what it was
/// doing and returns a code instead of aborting.
///
/// <b>Why it exists.</b> When an exception escapes <c>Main</c>, CoreCLR's unhandled-exception path ends in
/// <c>PROCAbort</c>, which raises SIGABRT. macOS then writes a crash report and opens a "dotnet quit
/// unexpectedly" dialog on the owner's screen, one per process. Thirty-two such reports were on this machine
/// between 18 and 23 September 2026 (<c>~/Library/Logs/DiagnosticReports/Retired/dotnet-*.ips</c>), in two
/// shapes: <c>NativeLibrary_LoadFromPath</c> throwing about 0.25 s into a process started without the native
/// library path, and <c>IL_Throw</c> out of <c>RunMain</c>, which is a fixture assertion escaping a standalone
/// flag (the exit 134 the EngineReplay guide describes). Nothing about the result changes: the process still
/// fails, the ledger still files the non-zero exit as an error row, and the message still reaches stderr. What
/// goes is the abort, and with it the dialog and the report.
///
/// <b>Why a module initializer rather than a try block in each <c>Main</c>.</b> Three of the six tools use
/// top-level statements and every tool already compiles <c>EmitLedgerRows.cs</c>, so a guard that runs
/// because its file is compiled cannot be forgotten by the next tool, where a wrapper would have to be
/// remembered in each one. <c>SelfTestTheStore</c> checks that every tool project still compiles this file.
///
/// <b>What it cannot catch.</b> A native crash (SIGSEGV inside a native library, or the collector faulting)
/// never becomes a managed exception, so it still aborts. The one such report in that set is the game's own
/// x86-64 process, not a tool.
/// </summary>
internal static class ExitWithoutACrashReport
{
    /// <summary>The exit code a tool returns when an exception escaped it. It is EX_SOFTWARE from
    /// sysexits.h, chosen because it is outside every code the tools return on purpose (0, 1 and 2) and is
    /// not 134, which is the abort this replaces.</summary>
    public const int UnhandledExceptionExitCode = 70;

    [ModuleInitializer]
    internal static void Install() =>
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            string process = Environment.ProcessPath ?? "dotnet";
            string arguments = string.Join(' ', Environment.GetCommandLineArgs());
            Console.Out.Flush();
            Console.Error.WriteLine($"unhandled exception in {arguments} ({process}); exiting {UnhandledExceptionExitCode} rather than aborting:");
            Console.Error.WriteLine(e.ExceptionObject);
            Console.Error.Flush();
            // Exiting from inside the handler ends the process before the runtime's abort, which is what
            // writes the crash report and opens the dialog.
            Environment.Exit(UnhandledExceptionExitCode);
        };
}
