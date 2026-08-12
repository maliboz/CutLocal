using System.Globalization;
using System.Windows;

namespace CutLocal.App;

/// <summary>Uses replaceable WPF resource dictionaries for Turkish and English.</summary>
public sealed class LocalizationService : ILocalizationService
{
    private static readonly HashSet<string> SupportedCultures =
        new(["tr-TR", "en-US"], StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Fallback =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr-TR"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Status.Ready"] = "Hazır",
                ["Status.NoInput"] = "Henüz görsel seçilmedi",
                ["Status.Selected"] = "Görsel hazır",
                ["Status.PreparingModel"] = "Model hazırlanıyor",
                ["Status.Decoding"] = "Görsel çözümleniyor",
                ["Status.Preprocessing"] = "Tensor hazırlanıyor",
                ["Status.Inferring"] = "Arka plan ayrılıyor",
                ["Status.Postprocessing"] = "Alfa maskesi işleniyor",
                ["Status.Encoding"] = "PNG kaydediliyor",
                ["Status.Completed"] = "Tamamlandı",
                ["Status.Cancelled"] = "İşlem iptal edildi",
            },
            ["en-US"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Status.Ready"] = "Ready",
                ["Status.NoInput"] = "No image selected",
                ["Status.Selected"] = "Image ready",
                ["Status.PreparingModel"] = "Preparing model",
                ["Status.Decoding"] = "Decoding image",
                ["Status.Preprocessing"] = "Preparing tensor",
                ["Status.Inferring"] = "Removing background",
                ["Status.Postprocessing"] = "Refining alpha mask",
                ["Status.Encoding"] = "Saving PNG",
                ["Status.Completed"] = "Completed",
                ["Status.Cancelled"] = "Processing cancelled",
            },
        };

    /// <inheritdoc />
    public IReadOnlyList<CultureOption> Cultures { get; } =
    [
        new("tr-TR", "Türkçe"),
        new("en-US", "English"),
    ];

    /// <inheritdoc />
    public string CurrentCulture { get; private set; } = "tr-TR";

    /// <inheritdoc />
    public event EventHandler? CultureChanged;

    /// <inheritdoc />
    public string GetString(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (System.Windows.Application.Current?.TryFindResource(key) is string value)
        {
            return value;
        }

        return Fallback.TryGetValue(CurrentCulture, out IReadOnlyDictionary<string, string>? strings)
            && strings.TryGetValue(key, out string? fallback)
                ? fallback
                : key;
    }

    /// <inheritdoc />
    public void SetCulture(string cultureName)
    {
        if (!SupportedCultures.Contains(cultureName))
        {
            throw new ArgumentException("The requested UI culture is not supported.", nameof(cultureName));
        }

        CurrentCulture = SupportedCultures.Single(item =>
            item.Equals(cultureName, StringComparison.OrdinalIgnoreCase));
        CultureInfo culture = CultureInfo.GetCultureInfo(CurrentCulture);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (System.Windows.Application.Current is { } application)
        {
            ResourceDictionary? previous = application.Resources.MergedDictionaries.FirstOrDefault(
                dictionary => dictionary.Source?.OriginalString.Contains(
                    "Resources/Strings.",
                    StringComparison.OrdinalIgnoreCase) == true);
            if (previous is not null)
            {
                application.Resources.MergedDictionaries.Remove(previous);
            }

            application.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri($"Resources/Strings.{CurrentCulture}.xaml", UriKind.Relative),
            });
        }

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
