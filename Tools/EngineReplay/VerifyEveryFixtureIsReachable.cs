using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

/// <summary>
/// No fixture in this project can stand where the default suite never asks it.
///
/// <para><b>Why a check rather than a habit.</b> A fixture reachable only through a flag is a fixture the
/// harness has never asked: <c>verify.sh</c> runs the default table, <c>run-case.sh</c> selects inside it,
/// and nothing reads <c>Program.cs</c>'s flag list. <c>VerifyGatheringOpportunityDiscovery</c> stood behind
/// <c>--retained-course-opportunities</c> for as long as it existed and rotted there — two of its rows threw
/// <c>Sequence contains no elements</c> — and its rot was indistinguishable from health until somebody typed
/// the flag. So this walks the compiled assembly rather than anybody's list.</para>
///
/// <para><b>What counts as a fixture entry.</b> A static, parameterless, <c>int</c>-returning method on a type
/// whose name begins <c>Verify</c>, <c>Measure</c> or <c>Fuzz</c> — the shape every entry in
/// <c>DefaultCases()</c> and every flag in <c>Program.cs</c> dispatches to. Helpers with parameters, and
/// methods returning anything else, are not entries.</para>
///
/// <para><b>What counts as reached.</b> Every method a <c>DefaultCases()</c> body calls, transitively, read
/// from the bodies' own IL: <c>call</c>, <c>callvirt</c>, <c>newobj</c>, <c>ldftn</c> and <c>ldvirtftn</c> operands
/// inside this assembly, lambdas and local functions included because they are methods the IL names. So an
/// aggregate <c>Run</c> reached from a registered case reaches every row it calls, and a row registered on
/// its own is reached whether or not its aggregate is.</para>
///
/// <para><b>An exemption is a reason, and it is checked too.</b> An entry nothing registered reaches must be in
/// <see cref="Exempt"/> with the reason it is not a default case; an exemption naming an entry that no longer
/// exists, or one the suite now reaches, is itself a red, because a stale exemption is exactly how the next
/// fixture would hide.</para>
/// </summary>
internal static class VerifyEveryFixtureIsReachable
{
    /// <summary>
    /// Entries deliberately outside the default suite, each with why. The reason is the whole value of the
    /// line: "flag-only" is not one, because a flag is exactly where fixtures rot.
    /// </summary>
    internal static readonly Dictionary<string, string> Exempt = new(StringComparer.Ordinal)
    {
        ["MeasureBrainCost.Execute"] = "`--brain-cost`, a cost instrument: per-phase timings of the seeded scene printed for a person. It also "
            + "asserts determinism and that recording changes no decision, and that half is behaviour the default suite does not "
            + "carry anywhere else; it stays out because its scenes run the whole brain six times and nobody has priced that as a case.",
        ["MeasureCombatCost.Execute"] = "`--crowd-cost`, a cost instrument that prints the crowd scene under both clocks and asserts nothing; "
            + "its default-suite counterpart is the perf-tier crowd planning case.",
        ["VerifyBrainSectionProfiler.PrintSectionProfile"] = "`--section-profile`, an instrument that aggregates the section tree for a person "
            + "and asserts nothing; the profiler's contracts are the four default cases on the same class.",
    };

    private static readonly string[] EntryPrefixes = { "Verify", "Measure", "Fuzz" };

    public static int Run() => Check(typeof(VerifyEveryFixtureIsReachable).Assembly, Exempt, report: true);

    /// <summary>The check over an assembly and an exemption list, apart from the suite's own, so a planted
    /// type can be proved red without touching the real list.</summary>
    internal static int Check(Assembly assembly, IReadOnlyDictionary<string, string> exempt, bool report)
    {
        List<MethodInfo> entries = Entries(assembly).ToList();
        HashSet<MethodBase> reached = Reached(assembly, RegisteredBodies());
        var unreached = entries.Where(entry => !reached.Contains(entry)).Select(Name).ToHashSet(StringComparer.Ordinal);
        var names = entries.Select(Name).ToHashSet(StringComparer.Ordinal);

        var hidden = unreached.Where(name => !exempt.ContainsKey(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var gone = exempt.Keys.Where(name => !names.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();
        var nowReached = exempt.Keys.Where(name => names.Contains(name) && !unreached.Contains(name)).OrderBy(name => name, StringComparer.Ordinal).ToList();

        if (report)
            Console.WriteLine($"fixture reach: {entries.Count} fixture entries, {entries.Count - unreached.Count} reached from the default suite, "
                + $"{unreached.Count - hidden.Count} exempt by name, {hidden.Count} hidden");
        int failures = 0;
        foreach (string name in hidden)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"fixture reach: {name} is reachable from no default case and is not exempt by name — register it in DefaultCases() or exempt it with its reason");
            failures++;
        }
        foreach (string name in gone)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"fixture reach: the exemption for {name} names no fixture entry in this assembly");
            failures++;
        }
        foreach (string name in nowReached)
        {
            AICompanion.Tools.Ledger.EmitLedgerRows.Detail($"fixture reach: {name} is exempt but the default suite reaches it; drop the exemption");
            failures++;
        }
        return failures;
    }

    internal static string Name(MethodInfo method) => $"{method.DeclaringType!.Name}.{method.Name}";

    private static IEnumerable<MethodInfo> Entries(Assembly assembly)
        => assembly.GetTypes()
            .Where(type => EntryPrefixes.Any(prefix => type.Name.StartsWith(prefix, StringComparison.Ordinal))
                           && !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(type => type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Where(method => method.ReturnType == typeof(int) && method.GetParameters().Length == 0
                             && !method.IsDefined(typeof(CompilerGeneratedAttribute), false) && !method.Name.Contains('<'));

    /// <summary>Every <c>DefaultCases()</c> body, read through reflection because the table is private to
    /// <c>VerifyEngineMotion</c> and one private method is not worth widening for a reader.</summary>
    private static IEnumerable<MethodInfo> RegisteredBodies()
    {
        MethodInfo table = typeof(VerifyEngineMotion).GetMethod("DefaultCases", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("VerifyEngineMotion.DefaultCases is gone, so fixture reach has nothing to read");
        var cases = (IEnumerable<(string Name, Func<int> Body)>)table.Invoke(null, null)!;
        return cases.Select(entry => entry.Body.Method);
    }

    private static readonly Dictionary<short, OperandType> Operands = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .GroupBy(code => code.Value).ToDictionary(group => group.Key, group => group.First().OperandType);

    /// <summary>The methods reachable from the roots through this assembly's own IL.</summary>
    internal static HashSet<MethodBase> Reached(Assembly assembly, IEnumerable<MethodBase> roots)
    {
        var seen = new HashSet<MethodBase>();
        var queue = new Queue<MethodBase>();
        foreach (MethodBase root in roots) if (seen.Add(root)) queue.Enqueue(root);
        while (queue.Count > 0)
        {
            MethodBase method = queue.Dequeue();
            foreach (MethodBase callee in Callees(method))
                if (callee.Module.Assembly == assembly && seen.Add(callee)) queue.Enqueue(callee);
        }
        return seen;
    }

    private static IEnumerable<MethodBase> Callees(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } owner ? owner.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        int at = 0;
        while (at < il.Length)
        {
            short value = il[at] == 0xFE ? (short)(0xFE00 | il[at + 1]) : il[at];
            at += il[at] == 0xFE ? 2 : 1;
            OperandType operand = Operands.TryGetValue(value, out OperandType known) ? known : OperandType.InlineNone;
            if (operand == OperandType.InlineMethod)
            {
                int metadataHandle = BitConverter.ToInt32(il, at);
                MethodBase? callee = null;
                try { callee = method.Module.ResolveMethod(metadataHandle, typeArguments, methodArguments); }
                // A generic member this reader cannot close is outside the question (the fixtures are not generic).
                catch (ArgumentException) { }
                if (callee != null) yield return callee is MethodInfo { IsGenericMethod: true } generic && !generic.IsGenericMethodDefinition
                    ? generic.GetGenericMethodDefinition() : callee;
            }
            at += operand switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
                _ => 4,
            };
        }
    }
}
