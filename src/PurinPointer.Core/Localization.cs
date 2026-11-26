using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace PurinPointer.Core;

public sealed record Language(string Code, string NativeName);

/// <summary>
/// UI strings loaded from the embedded <c>Strings/*.json</c> files. Bind to the indexer
/// (<c>Loc.Instance["key"]</c>); switching <see cref="Code"/> raises a change notification so
/// every bound label updates at once. Missing keys fall back to English, then to the key itself.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static readonly IReadOnlyList<Language> Languages =
    [
        new("zh-Hant", "繁體中文"),
        new("zh-Hans", "简体中文"),
        new("en", "English"),
        new("ja", "日本語"),
        new("ko", "한국어"),
    ];

    public static Loc Instance { get; } = new();

    readonly Dictionary<string, string> _fallback = Load("en");
    Dictionary<string, string> _strings;
    string _code = "en";

    public event PropertyChangedEventHandler? PropertyChanged;

    Loc() => _strings = _fallback;

    public string Code
    {
        get => _code;
        set
        {
            var code = Languages.Any(l => l.Code == value) ? value : "en";
            if (code == _code) return;
            _code = code;
            _strings = Load(code);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        }
    }

    public string this[string key] =>
        _strings.TryGetValue(key, out var s) ? s : _fallback.TryGetValue(key, out var f) ? f : key;

    public string Format(string key, params object[] args) => string.Format(CultureInfo.CurrentCulture, this[key], args);

    /// <summary>Maps a system UI culture to one of the supported languages.</summary>
    public static string Detect(CultureInfo culture)
    {
        for (var c = culture; c != CultureInfo.InvariantCulture; c = c.Parent)
        {
            switch (c.Name)
            {
                case "zh-Hant" or "zh-TW" or "zh-HK" or "zh-MO": return "zh-Hant";
                case "zh-Hans" or "zh-CN" or "zh-SG": return "zh-Hans";
                case "zh": return "zh-Hans";
                case "ja": return "ja";
                case "ko": return "ko";
                case "en": return "en";
            }
        }
        return "en";
    }

    public static Dictionary<string, string> Load(string code)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Strings.{code}.json")
            ?? throw new InvalidOperationException($"Missing strings for {code}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
