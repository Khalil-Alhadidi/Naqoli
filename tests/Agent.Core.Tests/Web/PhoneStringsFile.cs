using System.Xml.Linq;
using Agent.Eval.Running;

namespace Agent.Core.Tests.Web;

/// <summary>The phone's resource file (<c>src/Agent.Web/Resources/PhoneStrings.resx</c>) as the tests read it: every key and its value.</summary>
internal static class PhoneStringsFile
{
    public static Dictionary<string, string> Values()
    {
        var path = Path.Combine(EvalPaths.RepositoryRoot(), "src", "Agent.Web", "Resources", "PhoneStrings.resx");
        return XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => d.Element("value")!.Value, StringComparer.Ordinal);
    }
}
