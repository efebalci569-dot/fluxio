using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fluxio.Transfer.Tests;

/// <summary>
/// Guards the desktop app's central localization: every key used in XAML or code exists in
/// both languages, and both files have the same keys and the same {n} placeholders.
/// (Reads the source files, so it runs without WPF.)
/// </summary>
public partial class DesktopLocalizationTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string StringsFolder = Path.Combine(Root, "src", "Fluxio.Desktop", "Resources", "Strings");

    [Fact]
    public void Turkish_and_english_have_the_same_keys_and_placeholders()
    {
        var en = Load("en");
        var tr = Load("tr");

        Assert.Empty(en.Keys.Except(tr.Keys));
        Assert.Empty(tr.Keys.Except(en.Keys));
        foreach (var (key, value) in en)
        {
            Assert.True(Placeholders(value).SetEquals(Placeholders(tr[key])), $"Placeholders differ for {key}");
            Assert.False(string.IsNullOrWhiteSpace(tr[key]), $"Empty Turkish text for {key}");
        }
    }

    [Fact]
    public void Every_key_used_by_the_app_exists()
    {
        var en = Load("en");
        var source = Path.Combine(Root, "src", "Fluxio.Desktop");
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in Directory.EnumerateFiles(source, "*.xaml", SearchOption.AllDirectories))
        {
            foreach (Match match in XamlKey().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Value);
            }
        }

        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories).Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match match in CodeKey().Matches(File.ReadAllText(file)))
            {
                used.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
            }
        }

        // Keys built at runtime from enum names.
        foreach (var code in Enum.GetNames<FluxioErrorCode>())
        {
            used.Add($"Error.{code}");
        }

        foreach (var reason in Enum.GetNames<Sessions.CloseReason>())
        {
            used.Add($"Session.Reason.{reason}");
        }

        foreach (var failure in Enum.GetNames<Transfers.FileFailure>())
        {
            used.Add($"Failure.{failure}");
        }

        foreach (var outcome in Enum.GetNames<Storage.HistoryOutcome>())
        {
            used.Add($"Outcome.{outcome}");
        }

        var missing = used.Where(k => !en.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Missing keys: " + string.Join(", ", missing));
    }

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(StringsFolder, language + ".json")))!;

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Fluxio.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex(@"\{l:Tr\s+([A-Za-z0-9.]+)\}")]
    private static partial Regex XamlKey();

    // Loc.Instance["Key"], Loc.Instance.Format("Key", …), loc["Key"], loc.Format("Key", …), Dialogs.PickFolder(…, "Key")
    [GeneratedRegex(@"(?:Loc\.Instance|loc)(?:\[|\.Format\()""([A-Z][A-Za-z0-9]*\.[A-Za-z0-9.]+)""|PickFolder\([^,]+,\s*""([A-Z][A-Za-z0-9.]+)""")]
    private static partial Regex CodeKey();
}
