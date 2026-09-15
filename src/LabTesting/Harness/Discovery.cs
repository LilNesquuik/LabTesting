using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;

namespace LabTesting;

/// <summary>
/// One executable test case: a method, the arguments to call it with, and the options read from
/// its attributes.
/// </summary>
public sealed class TestCase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestCase"/> class and reads its options from
    /// the attributes on the method and its declaring type.
    /// </summary>
    /// <param name="fixture">Type declaring the test method.</param>
    /// <param name="method">Test method.</param>
    /// <param name="arguments">Arguments to invoke the method with.</param>
    /// <param name="id">Stable identifier reported in the verdict.</param>
    public TestCase(Type fixture, MethodInfo method, object?[] arguments, string id)
    {
        Fixture = fixture;
        Method = method;
        Arguments = arguments;
        Id = id;

        Collection = fixture.GetCustomAttribute<CollectionAttribute>()?.Name ?? fixture.Name;
        Isolation = method.GetCustomAttribute<IsolationAttribute>()?.Level ?? Dirty.Dummies;
        TimeoutTicks = method.GetCustomAttribute<TimeoutAttribute>()?.Ticks ?? DefaultTimeoutTicks;
        Perf = method.GetCustomAttribute<PerfAttribute>() != null;
        Seed = method.GetCustomAttribute<SeedAttribute>()?.Seed;
        Skip = method.GetCustomAttribute<FactAttribute>()?.Skip
               ?? method.GetCustomAttribute<TheoryAttribute>()?.Skip;
    }

    /// <summary>
    /// Default timeout, ten seconds at the default tickrate. Past that, a stuck test has to give
    /// the runner its turn back.
    /// </summary>
    public const int DefaultTimeoutTicks = 600;

    /// <summary>
    /// Gets the type declaring the test method.
    /// </summary>
    public Type Fixture { get; }

    /// <summary>
    /// Gets the test method.
    /// </summary>
    public MethodInfo Method { get; }

    /// <summary>
    /// Gets the arguments the method is invoked with.
    /// </summary>
    public object?[] Arguments { get; }

    /// <summary>
    /// Gets the stable identifier reported in the verdict.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the collection the test belongs to.
    /// </summary>
    public string Collection { get; }

    /// <summary>
    /// Gets the declared isolation level.
    /// </summary>
    public Dirty Isolation { get; }

    /// <summary>
    /// Gets the maximum number of ticks the test may run for.
    /// </summary>
    public int TimeoutTicks { get; }

    /// <summary>
    /// Gets a value indicating whether the test runs in performance mode.
    /// </summary>
    public bool Perf { get; }

    /// <summary>
    /// Gets the seed to apply, or null when the test does not ask for one.
    /// </summary>
    public int? Seed { get; }

    /// <summary>
    /// Gets the reason the test is skipped, or null when it runs. It is set either by the author
    /// or by plan validation.
    /// </summary>
    public string? Skip { get; internal set; }
}

/// <summary>
/// Builds the execution plan by reflecting over the loaded assemblies.
/// </summary>
/// <remarks>
/// The framework ships its own runner rather than hosting xUnit, because an xUnit runner owns the
/// thread and the process lifetime, and the game already owns both.
/// </remarks>
public static class Discovery
{
    /// <summary>
    /// Finds every test and returns them in execution order.
    /// </summary>
    /// <remarks>
    /// Tests are ordered by ascending isolation level so that the cheapest cleanups run first and
    /// round restarts are kept to a minimum, then grouped by collection so that related tests stay
    /// together, then by identifier so that the order is stable across runs.
    /// </remarks>
    public static List<TestCase> BuildPlan(IEnumerable<Assembly>? selected = null)
    {
        var cases = new List<TestCase>();

        Assembly self = typeof(FactAttribute).Assembly;
        string selfName = self.GetName().Name;

        foreach (Assembly assembly in selected ?? AppDomain.CurrentDomain.GetAssemblies())
        {
            // A test class necessarily references FactAttribute, hence this assembly. The filter
            // avoids reflecting over the roughly ten thousand types of the game assembly on every
            // server start.
            if (assembly != self && !References(assembly, selfName))
                continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                throw new InvalidOperationException("Discovery failed in " + assembly.FullName +
                    ": " + string.Join("\n", Array.ConvertAll(e.LoaderExceptions, x => x?.ToString())), e);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException("Discovery failed in " + assembly.FullName, e);
            }

            foreach (Type type in types)
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition)
                    continue;

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    Collect(cases, type, method);
            }
        }

        cases.Sort((a, b) =>
        {
            int byIsolation = a.Isolation.CompareTo(b.Isolation);
            if (byIsolation != 0)
                return byIsolation;

            int byCollection = string.CompareOrdinal(a.Collection, b.Collection);
            return byCollection != 0 ? byCollection : string.CompareOrdinal(a.Id, b.Id);
        });

        return cases;
    }

    private static bool References(Assembly assembly, string name)
    {
        try
        {
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (string.Equals(reference.Name, name, StringComparison.Ordinal))
                    return true;
            }
        }
        catch
        {
            // Dynamic or unloaded assembly: it carries no tests.
        }

        return false;
    }

    private static void Collect(List<TestCase> cases, Type type, MethodInfo method)
    {
        bool isFact = method.GetCustomAttribute<FactAttribute>() != null;
        bool isTheory = method.GetCustomAttribute<TheoryAttribute>() != null;
        if (!isFact && !isTheory)
            return;

        string baseId = type.Assembly.GetName().Name + ":" + type.FullName + "." + method.Name;

        if (isTheory)
        {
            var rows = (InlineDataAttribute[])method.GetCustomAttributes(typeof(InlineDataAttribute), false);
            if (rows.Length == 0)
            {
                cases.Add(Invalid(type, method, baseId, "[Theory] without [InlineData]."));
                return;
            }

            for (int i = 0; i < rows.Length; i++)
                cases.Add(Validate(new TestCase(type, method, rows[i].Data, baseId + "(" + i + ")")));

            return;
        }

        if (method.GetParameters().Length != 0)
        {
            cases.Add(Invalid(type, method, baseId, "[Fact] takes no parameter."));
            return;
        }

        cases.Add(Validate(new TestCase(type, method, Array.Empty<object?>(), baseId)));
    }

    /// <summary>
    /// Marks a test that cannot be executed as skipped, with the reason.
    /// </summary>
    /// <remarks>
    /// A test whose signature the runner cannot honour is reported rather than dropped, so a typo
    /// in a signature shows up in the verdict instead of silently reducing the suite.
    /// </remarks>
    private static TestCase Validate(TestCase test)
    {
        if (test.Method.ReturnType != typeof(Task) && test.Method.ReturnType != typeof(void))
            test.Skip = "the method must return Task or void, not " + test.Method.ReturnType.Name + ".";
        else if (test.Method.GetParameters().Length != test.Arguments.Length)
            test.Skip = "[InlineData] argument count does not match the signature.";
        else if (test.Fixture.GetConstructor(Type.EmptyTypes) == null)
            test.Skip = test.Fixture.Name + " has no parameterless constructor.";

        return test;
    }

    private static TestCase Invalid(Type type, MethodInfo method, string id, string reason)
    {
        var test = new TestCase(type, method, Array.Empty<object?>(), id);
        test.Skip = reason;
        return test;
    }
}
