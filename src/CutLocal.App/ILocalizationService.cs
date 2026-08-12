namespace CutLocal.App;

/// <summary>Switches UI resources and resolves status text for supported cultures.</summary>
public interface ILocalizationService
{
    /// <summary>Gets the supported culture choices.</summary>
    IReadOnlyList<CultureOption> Cultures { get; }
    /// <summary>Gets the active culture name.</summary>
    string CurrentCulture { get; }
    /// <summary>Gets a localized string or the key when it is missing.</summary>
    string GetString(string key);
    /// <summary>Changes the active UI culture and resource dictionary.</summary>
    void SetCulture(string cultureName);
    /// <summary>Occurs after UI resources change.</summary>
    event EventHandler? CultureChanged;
}

/// <summary>Represents one supported UI language.</summary>
public sealed record CultureOption(string Name, string DisplayName);
