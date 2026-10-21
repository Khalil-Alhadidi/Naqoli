extern alias AgentWeb;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using AgentWeb::Agent.Web.Services;

namespace Agent.Core.Tests.Web;

/// <summary>
/// UI pass, task 6: every setting the console can change has a label, a one-sentence description, a unit, a range and a
/// default, and the range and default are those of the options classes, so the settings component cannot drift from
/// the code.
/// </summary>
public class SettingDescriptionsTests
{
    private const string ConsolePrefix = ConsoleOptions.SectionName + ":";

    private static IEnumerable<string> EditableKeys
        => RuntimeSettings.EditableNames.Concat(RuntimeSettings.EditableConsoleNames.Select(n => ConsolePrefix + n));

    [Fact]
    public void EveryEditableSettingHasADescription()
    {
        Assert.Equal(EditableKeys.Order(StringComparer.Ordinal), SettingDescriptions.Editable.Keys.Order(StringComparer.Ordinal));
        Assert.Contains("Console:GridPageSize", SettingDescriptions.Editable.Keys);
        Assert.All(SettingDescriptions.Editable, entry =>
        {
            Assert.Equal(entry.Key, entry.Value.Name);
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.Label), entry.Key);
            Assert.False(string.IsNullOrWhiteSpace(entry.Value.Unit), entry.Key);
        });
    }

    [Fact]
    public void EveryRangeAndDefaultMatchesTheOptions()
    {
        var agent = new AgentOptions();
        var console = new ConsoleOptions();
        Assert.All(SettingDescriptions.Editable.Values, description =>
        {
            var (type, instance, name) = description.Name.StartsWith(ConsolePrefix, StringComparison.Ordinal)
                ? (typeof(ConsoleOptions), (object)console, description.Name[ConsolePrefix.Length..])
                : (typeof(AgentOptions), agent, description.Name);
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property);
            var range = property.GetCustomAttribute<RangeAttribute>();
            Assert.NotNull(range);

            Assert.Equal(AsLong(range.Minimum), description.Min);
            Assert.Equal(AsLong(range.Maximum), description.Max);
            Assert.Equal(AsLong(property.GetValue(instance)!), description.Default);
        });
    }

    [Fact]
    public void EveryDescriptionIsOneSentence()
        => Assert.All(SettingDescriptions.Editable.Values, description =>
        {
            var text = description.Description;
            Assert.EndsWith(".", text, StringComparison.Ordinal);
            Assert.Equal(1, text.Count(c => c == '.'));
            Assert.True(text.Length < 160, description.Name + " is " + text.Length + " characters.");
        });

    // A [Range] on an int holds ints; one declared with typeof(long) holds the bounds as strings.
    private static long AsLong(object value) => value switch
    {
        string text => long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture),
        _ => Convert.ToInt64(value, CultureInfo.InvariantCulture),
    };
}
