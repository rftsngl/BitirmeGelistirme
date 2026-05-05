using System.Collections.ObjectModel;
using WindowsAiAssistant.App.Models;
using WindowsAiAssistant.App.Mvvm;
using WindowsAiAssistant.Contracts.Interfaces;
using WindowsAiAssistant.Contracts.Models.Agent.Enums;

namespace WindowsAiAssistant.App.ViewModels;

public sealed class CapabilitiesViewModel : ObservableObject
{
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly List<CapabilityViewItem> _allItems = [];

    private string _searchText = string.Empty;

    public CapabilitiesViewModel(ICapabilityRegistry capabilityRegistry)
    {
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
        Items = [];
        ReloadFromRegistry();
    }

    public ObservableCollection<CapabilityViewItem> Items { get; }

    public int TotalCount => _allItems.Count;
    public int VisibleCount => Items.Count;

    public string CountSummary =>
        TotalCount == VisibleCount
            ? $"Toplam {TotalCount} yetenek"
            : $"{VisibleCount} / {TotalCount} yetenek gösteriliyor";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value ?? string.Empty))
            {
                ApplyFilter();
            }
        }
    }

    public void ReloadFromRegistry()
    {
        _allItems.Clear();
        foreach (var capability in _capabilityRegistry.GetAll().OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            _allItems.Add(new CapabilityViewItem
            {
                Name = capability.Name,
                IconGlyph = ResolveIconGlyph(capability.Name),
                TargetKindLabels = capability.SupportedTargetKinds.Select(k => k.ToString()).ToList(),
                Description = ResolveDescription(capability.Name)
            });
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();
        IEnumerable<CapabilityViewItem> source = _allItems;
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var query = _searchText.Trim();
            source = _allItems.Where(item =>
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.TargetKindLabels.Any(l => l.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        foreach (var item in source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(CountSummary));
    }

    private static string ResolveIconGlyph(string capabilityName) =>
        capabilityName switch
        {
            "ApplicationCapability" => "\uE7F4",
            "WindowProcessCapability" => "\uE737",
            "ProcessVerificationCapability" => "\uE9F5",
            "ForegroundAlignmentCapability" => "\uE74C",
            "ServiceStatusCapability" => "\uE9D9",
            "ServiceControlCapability" => "\uE9F3",
            "FileVerificationCapability" => "\uE73E",
            "FileOpenCapability" => "\uE8E5",
            "NoOpCapability" => "\uE711",
            _ => "\uE9CE"
        };

    private static string ResolveDescription(string capabilityName) =>
        capabilityName switch
        {
            "ApplicationCapability" => "Belirlenen uygulamayı açar veya odaklar.",
            "WindowProcessCapability" => "Pencere/işlem hedeflerinde bağlam çıkarır.",
            "ProcessVerificationCapability" => "İşlem varlığı doğrulaması yapar.",
            "ForegroundAlignmentCapability" => "Ön planda olan pencereyi hizalar.",
            "ServiceStatusCapability" => "Windows servis durumunu raporlar.",
            "ServiceControlCapability" => "Windows servisini başlat/durdur.",
            "FileVerificationCapability" => "Belirtilen dosyanın varlığını kontrol eder.",
            "FileOpenCapability" => "Dosyayı varsayılan uygulamayla açar.",
            "NoOpCapability" => "Bilinçli olarak hiçbir şey yapmaz (placeholder).",
            _ => string.Empty
        };
}
