using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace LabTesting.Runner;

internal sealed class Verdict
{
    public int Passed => Results.Values.Count(x => Field(x, "outcome") == "passed");
    public int Failed => Results.Values.Count(x => Field(x, "outcome") == "failed");
    public int Skipped => Results.Values.Count(x => Field(x, "outcome") == "skipped");
    public int Errors => Results.Values.Count(x => Field(x, "outcome") == "error");
    public bool HasSummary { get; private set; }
    public List<string> Problems { get; } = [];
    public List<string> Lines { get; } = [];
    internal Dictionary<string, string> Results { get; } = new(StringComparer.Ordinal);
    internal Dictionary<string, string> Plan { get; } = new(StringComparer.Ordinal);
    private string? _metadata;
    private bool _list;

    public static Verdict Parse(IEnumerable<string> jsonl, bool listOnly = false)
    {
        var v = new Verdict { _list = listOnly };
        bool planSeen = false;
        string? summary = null;
        foreach (var raw in jsonl)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) throw new FormatException("Expected a JSON object.");
                if (root.EnumerateObject().GroupBy(p => p.Name).Any(g => g.Count() > 1))
                    throw new FormatException("Duplicate JSON key.");
                if (v.HasSummary) throw new FormatException("Data after the summary.");
                switch (Field(raw, "kind"))
                {
                    case "plan":
                        if (planSeen || v.Results.Count > 0) throw new FormatException("Duplicate or late plan.");
                        planSeen = true;
                        v._metadata = raw;
                        if (root.GetProperty("list").GetBoolean() != listOnly) throw new FormatException("Wrong plan mode.");
                        foreach (var test in root.GetProperty("tests").EnumerateArray())
                        {
                            var id = test.GetProperty("id").GetString()!;
                            if (string.IsNullOrWhiteSpace(id) || !v.Plan.TryAdd(id, test.GetRawText()))
                                throw new FormatException("Duplicate or empty identifier in the plan.");
                        }
                        break;
                    case "summary":
                        v.HasSummary = true;
                        summary = raw;
                        break;
                    case null:
                        if (!planSeen || listOnly) throw new FormatException("Result without an executable plan.");
                        string testId = root.GetProperty("id").GetString()!;
                        if (!v.Plan.ContainsKey(testId) || !v.Results.TryAdd(testId, raw))
                            throw new FormatException("Unexpected or duplicate result: " + testId);
                        string? outcome = Field(raw, "outcome");
                        if (outcome is not ("passed" or "failed" or "skipped" or "error"))
                            throw new FormatException("Invalid outcome.");
                        if (Field(raw, "collection") != Field(v.Plan[testId], "collection"))
                            throw new FormatException("Inconsistent collection.");
                        if (outcome == "passed" && (Objects(raw, "failures").Count > 0 ||
                            Objects(raw, "swallowed").Count > 0 || Field(raw, "harnessError") != null))
                            throw new FormatException("Passing test reported errors.");
                        v.Lines.Add(outcome + " " + testId);
                        break;
                    default: throw new FormatException("Unknown line type.");
                }
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException)
            { v.Problems.Add("Invalid JSONL: " + e.Message); }
        }
        if (!planSeen || v.Plan.Count == 0) v.Problems.Add("Missing or empty plan.");
        if (!v.HasSummary) v.Problems.Add("Missing summary.");
        if (!listOnly)
            foreach (var id in v.Plan.Keys.Except(v.Results.Keys)) v.Problems.Add("Missing result: " + id);
        else
            foreach (var id in v.Plan.Keys) v.Lines.Add(id);
        if (summary != null)
        {
            foreach (var pair in new[] { ("passed", v.Passed), ("failed", v.Failed), ("skipped", v.Skipped), ("errors", v.Errors) })
                if (!TryNumber(summary, pair.Item1, out var n) || n != pair.Item2)
                    v.Problems.Add("Inconsistent summary counter: " + pair.Item1);
            if (Field(summary, "harnessError") is { } error) v.Problems.Add(error);
        }
        return v;
    }

    public int ExitCode(bool failOnSkipped) => Problems.Count > 0 || Errors > 0 ? 2 : Failed > 0 || failOnSkipped && Skipped > 0 ? 1 : 0;

    public void WriteReports(string directory)
    {
        var suite = new XElement("testsuite", new XAttribute("name", _list ? "LabTesting discovery" : "LabTesting"));
        var properties = new XElement("properties");
        if (_metadata != null)
            foreach (string name in new[] { "serverVersion", "frameworkVersion", "labapiVersion", "harmonyVersion" })
                properties.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", Field(_metadata, name) ?? "unknown")));
        foreach (var assembly in Plan.Values.Select(p => Field(p, "assembly")).Distinct())
            properties.Add(new XElement("property", new XAttribute("name", "testAssembly"), new XAttribute("value", assembly ?? "unknown")));
        suite.Add(properties);
        if (!_list)
        {
            foreach (var entry in Plan)
            {
                Results.TryGetValue(entry.Key, out var result);
                string? outcome = result == null ? null : Field(result, "outcome");
                var test = new XElement("testcase", new XAttribute("name", entry.Key),
                    new XAttribute("classname", Field(entry.Value, "collection") ?? ""),
                    new XAttribute("time", ((result == null ? 0 : Number(result, "durationMilliseconds")) / 1000.0).ToString(CultureInfo.InvariantCulture)));
                if (outcome == null) test.Add(new XElement("error", new XAttribute("message", "Missing result")));
                else if (outcome == "skipped") test.Add(new XElement("skipped", Field(result!, "skip") ?? ""));
                else if (outcome != "passed") test.Add(new XElement(outcome == "failed" ? "failure" : "error",
                    new XAttribute("message", Field(result!, "harnessError") ?? "Assertions or exceptions"), result));
                if (result != null) test.Add(new XElement("system-out", result));
                suite.Add(test);
            }
        }
        foreach (var problem in Problems)
            suite.Add(new XElement("testcase", new XAttribute("name", "Infrastructure"),
                new XElement("error", new XAttribute("message", "Incomplete or invalid run"), problem)));
        var cases = suite.Elements("testcase").ToArray();
        suite.SetAttributeValue("tests", cases.Length);
        suite.SetAttributeValue("failures", cases.Count(x => x.Element("failure") != null));
        suite.SetAttributeValue("errors", cases.Count(x => x.Element("error") != null));
        suite.SetAttributeValue("skipped", cases.Count(x => x.Element("skipped") != null));
        // Console/exception messages can contain control bytes that XML 1.0 cannot encode.
        static string XmlSafe(string value) => new(value.Where(System.Xml.XmlConvert.IsXmlChar).ToArray());
        foreach (var node in suite.DescendantNodes().OfType<XText>()) node.Value = XmlSafe(node.Value);
        foreach (var attribute in suite.DescendantsAndSelf().Attributes()) attribute.Value = XmlSafe(attribute.Value);
        new XDocument(new XElement("testsuites", suite)).Save(Path.Combine(directory, "junit.xml"));
        static string Safe(string text) => System.Net.WebUtility.HtmlEncode(text).Replace("|", "&#124;").Replace("\r", "").Replace("\n", "<br>");
        var lines = new List<string> { "# LabTesting", "", $"{Passed} passed \u00b7 {Failed} failed \u00b7 {Skipped} skipped \u00b7 {Errors} errors", "",
            "| Test | Collection | Outcome | Duration (ms) |", "|---|---|---|---|" };
        var details = new List<string>();
        foreach (var entry in Plan)
        {
            Results.TryGetValue(entry.Key, out var result);
            lines.Add("| " + Safe(entry.Key) + " | " + Safe(Field(entry.Value, "collection") ?? "") + " | " +
                (result == null ? _list ? "discovered" : "missing" : Field(result, "outcome")) + " | " +
                (result == null ? "—" : Number(result, "durationMilliseconds")) + " |");
            if (result != null && Field(result, "outcome") != "passed")
                details.Add("\n<details><summary>" + Safe(entry.Key) + "</summary><pre>" + Safe(result) + "</pre></details>\n");
        }
        lines.AddRange(details);
        foreach (var problem in Problems) lines.Add("\n- " + Safe(problem));
        if (_metadata != null) lines.Add("\n<details><summary>Versions and plan</summary><pre>" + Safe(_metadata) + "</pre></details>");
        File.WriteAllLines(Path.Combine(directory, "summary.md"), lines);
    }

    internal static string? Field(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    }
    private static bool TryNumber(string json, string name, out int value)
    {
        value = 0;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value);
    }
    internal static int Number(string json, string name) => TryNumber(json, name, out int n) ? n : 0;
    internal static bool Bool(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
    }
    internal static List<string> Objects(string json, string name)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty(name, out var p) ? p.EnumerateArray().Select(x => x.GetRawText()).ToList() : [];
    }
}
