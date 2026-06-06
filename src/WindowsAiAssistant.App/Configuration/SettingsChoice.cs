namespace WindowsAiAssistant.App.Configuration;

/// <summary>
/// Ayar ekranlarında ComboBox ve benzeri kontroller için görünen ad + değer + kısa açıklama.
/// </summary>
public sealed record SettingsChoice(string Value, string Display, string Description);
