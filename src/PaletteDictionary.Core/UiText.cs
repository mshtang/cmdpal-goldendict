using System.Globalization;
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]

namespace PaletteDictionary.Core;

/// <summary>Shared UI and diagnostic strings; dictionary content and configuration names are never translated.</summary>
public static class UiText
{
    private static readonly ResourceManager Resources = new("PaletteDictionary.Core.Resources.Strings", typeof(UiText).Assembly);
    private static readonly CultureInfo SystemCulture = CultureInfo.CurrentUICulture;
    private static string _language = "auto";
    public static event Action? LanguageChanged;

    public static CultureInfo Culture => ResolveCulture(Volatile.Read(ref _language), SystemCulture);

    public static CultureInfo ResolveCulture(string language, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo((language == "auto" ? systemCulture.Name : language)
            .StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en");

    public static void SetLanguage(string language)
    {
        if (language is not ("auto" or "en" or "zh-Hans")) throw new ArgumentOutOfRangeException(nameof(language));
        if (Interlocked.Exchange(ref _language, language) != language) LanguageChanged?.Invoke();
    }

    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing UI string: {key}");
    public static string Format(string key, params object[] arguments) => string.Format(Culture, Get(key), arguments);
    public static string Correction(int distance) => Format(distance == 1 ? "CorrectionSingular" : "Correction", distance);
}
