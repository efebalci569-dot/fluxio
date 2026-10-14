using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows.Data;
using System.Windows.Markup;

namespace Fluxio.Desktop.Services;

/// <summary>
/// Central localization: every user-visible string comes from Resources/Strings/{lang}.json.
/// XAML binds through <see cref="TrExtension"/>; switching the language updates the UI live.
/// Adding a language = adding a JSON file with the same keys.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly string[] Languages = ["tr", "en"];

    private Dictionary<string, string> _strings = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    private Loc()
    {
        _fallback = LoadStrings("en");
        SetLanguage("tr");
    }

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? LanguageChanged;

    public string Language { get; private set; } = "tr";

    public CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("tr-TR");

    public string this[string key] => Get(key);

    public string Get(string key) =>
        _strings.TryGetValue(key, out var value) ? value
        : _fallback.TryGetValue(key, out var fallback) ? fallback
        : key;

    public string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    public void SetLanguage(string language)
    {
        if (!Languages.Contains(language))
        {
            language = "en";
        }

        _strings = LoadStrings(language);
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language == "tr" ? "tr-TR" : "en-US");
        CultureInfo.CurrentUICulture = Culture;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyCollection<string> Keys(string language) => LoadStrings(language).Keys;

    private static Dictionary<string, string> LoadStrings(string language)
    {
        var assembly = typeof(Loc).Assembly;
        var name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith($".Strings.{language}.json", StringComparison.OrdinalIgnoreCase));
        if (name is null)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(reader.ReadToEnd()) ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }
}

/// <summary>XAML: Text="{loc:Tr Home.Title}" – a live binding to the localized string.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
