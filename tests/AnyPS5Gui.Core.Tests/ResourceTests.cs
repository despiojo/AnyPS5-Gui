using System.Text.RegularExpressions;
using System.Xml.Linq;
using AnyPS5Gui.Core.Components;
using AnyPS5Gui.Core.Conversion;
using AnyPS5Gui.Core.Games;

namespace AnyPS5Gui.Core.Tests;

/// <summary>
/// The app's texts live in Strings\&lt;language&gt;\Resources.resw. A key that is missing shows as a blank
/// control or as the key itself, and nothing fails at build time, so the files are checked here.
/// </summary>
public partial class ResourceTests
{
    private static readonly string App = Path.Combine(RepositoryRoot(), "src", "AnyPS5Gui");

    private static Dictionary<string, string> Load(string language) =>
        XDocument.Load(Path.Combine(App, "Strings", language, "Resources.resw")).Root!
            .Elements("data")
            .ToDictionary(data => data.Attribute("name")!.Value, data => data.Element("value")!.Value);

    [Fact]
    public void Both_languages_have_the_same_texts()
    {
        var english = Load("en-US");
        var spanish = Load("es-ES");

        Assert.Equal(english.Keys.Order(), spanish.Keys.Order());
        Assert.All(english.Concat(spanish), text => Assert.False(string.IsNullOrWhiteSpace(text.Value), text.Key));
        // A translation that drops or adds a {0} breaks the message at run time.
        Assert.All(english, text => Assert.Equal(Placeholders(text.Value), Placeholders(spanish[text.Key])));
    }

    [Fact]
    public void Every_text_the_xaml_asks_for_exists()
    {
        var english = Load("en-US");
        var uids = Directory.EnumerateFiles(App, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !file.Contains(@"\obj\") && !file.Contains(@"\bin\"))
            .SelectMany(file => Uid().Matches(File.ReadAllText(file)).Select(match => match.Groups[1].Value))
            .Distinct()
            .ToList();

        Assert.NotEmpty(uids);
        Assert.All(uids, uid => Assert.Contains(english.Keys, key => key.StartsWith(uid + ".")));
        // And nothing is left behind for a control that no longer exists.
        Assert.All(english.Keys.Where(key => key.Contains('.')),
            key => Assert.Contains(key[..key.IndexOf('.')], uids));
    }

    [Fact]
    public void Every_text_the_code_asks_for_exists()
    {
        var english = Load("en-US");
        var source = string.Concat(Directory.EnumerateFiles(App, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(@"\obj\") && !file.Contains(@"\bin\"))
            .Select(File.ReadAllText));

        var keys = Lookup().Matches(source).Select(match => match.Groups[1].Value).ToList();
        // Keys built from a prefix and an enum value.
        keys.AddRange(Enum.GetNames<GameIssue>().Select(name => "Issue_" + name));
        keys.AddRange(Enum.GetNames<ConversionProblem>().Select(name => "Problem_" + name));
        keys.AddRange(Enum.GetNames<ConversionStage>().Select(name => "Stage_" + name));
        keys.AddRange(Enum.GetNames<InstallStage>().Select(name => "Install_" + name));

        Assert.All(keys.Where(key => !key.EndsWith('_')), key => Assert.Contains(key, english.Keys));
        Assert.All(english.Keys.Where(key => !key.Contains('.')), key => Assert.Contains(key, keys));
    }

    private static List<string> Placeholders(string text) =>
        Placeholder().Matches(text).Select(match => match.Value).Distinct().Order().ToList();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AnyPS5Gui.slnx"))) return directory.FullName;
        }
        throw new DirectoryNotFoundException("AnyPS5Gui.slnx was not found above the test binaries.");
    }

    [GeneratedRegex("x:Uid=\"([^\"]+)\"")]
    private static partial Regex Uid();

    // Every string literal in the app that is shaped like a resource key, however it reaches Loc.
    [GeneratedRegex("\"((?:Convert|Games|Settings|Components|Issue|Problem|Stage|Install|Error)_[A-Za-z0-9]*)\"")]
    private static partial Regex Lookup();

    [GeneratedRegex(@"\{\d+\}")]
    private static partial Regex Placeholder();
}
