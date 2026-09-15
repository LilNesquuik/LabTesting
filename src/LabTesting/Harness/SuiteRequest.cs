using System.Reflection;
using System.Xml.Linq;

namespace LabTesting;

internal sealed class SuiteRequest
{
    private readonly XElement _xml;
    internal SuiteRequest()
    {
        _xml = XElement.Load(Path.Combine(TestPlugin.ConfigDirectory(), "labtesting-suite.xml"));
    }
    internal bool List => (bool?)_xml.Attribute("list") == true;
    internal string Reports => (string)_xml.Attribute("reports")!;
    private string[] Values(string name) => _xml.Element(name)?.Elements().Select(x => x.Value).ToArray() ?? [];

    internal List<TestCase> Plan()
    {
        List<Assembly> assemblies = new List<Assembly>();
        foreach (string path in Values("tests"))
        {
            AssemblyName name = AssemblyName.GetAssemblyName(path);
            Assembly loaded = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == name.Name)
                              ?? Assembly.LoadFrom(path);
            if (loaded.FullName != name.FullName) throw new InvalidOperationException("Ambiguous assembly: " + name);
            assemblies.Add(loaded);
        }
        bool framework = (bool?)_xml.Attribute("frameworkTests") == true;
        if (framework) assemblies.Add(typeof(FactAttribute).Assembly);
        string[] selected = Values("assemblies");
        foreach (string name in selected)
            if (!assemblies.Any(a => a.GetName().Name == name)) throw new InvalidOperationException("Missing assembly: " + name);
        if (selected.Length > 0) assemblies.RemoveAll(a => !selected.Contains(a.GetName().Name));
        List<TestCase> plan = Discovery.BuildPlan(assemblies);
        string[] collections = Values("collections");
        string[] tests = Values("testNames");
        foreach (string name in collections)
            if (!plan.Any(t => t.Collection == name)) throw new InvalidOperationException("Missing collection: " + name);
        if (collections.Length > 0) plan.RemoveAll(t => !collections.Contains(t.Collection));
        foreach (string name in tests)
            if (!plan.Any(t => t.Id == name)) throw new InvalidOperationException("Missing test: " + name);
        if (tests.Length > 0) plan.RemoveAll(t => !tests.Contains(t.Id));
        if (plan.Count == 0) throw new InvalidOperationException("No test in the requested suite.");
        foreach (Assembly assembly in assemblies.Where(a => a != typeof(FactAttribute).Assembly))
            if (!plan.Any(t => t.Fixture.Assembly == assembly))
                throw new InvalidOperationException("No test selected in " + assembly.FullName);
        if (plan.GroupBy(t => t.Id).Any(g => g.Count() != 1))
            throw new InvalidOperationException("Duplicate test identifiers.");
        return plan;
    }

    internal void CheckPlugins()
    {
        foreach (string name in Values("plugins"))
            if (!LabApi.Loader.PluginLoader.EnabledPlugins.Any(p => p.GetType().Assembly.GetName().Name == name))
                throw new InvalidOperationException("Required plugin is not enabled: " + name);
    }
}
