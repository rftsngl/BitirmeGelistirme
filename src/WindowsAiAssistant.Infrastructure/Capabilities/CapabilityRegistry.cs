using WindowsAiAssistant.Contracts.Interfaces;

namespace WindowsAiAssistant.Infrastructure.Capabilities;

public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly IReadOnlyCollection<ICapability> _capabilities;

    public CapabilityRegistry(IEnumerable<ICapability> capabilities)
    {
        _capabilities = capabilities.ToList();
    }

    public IReadOnlyCollection<ICapability> GetAll()
    {
        return _capabilities;
    }

    public ICapability? FindByName(string capabilityName)
    {
        if (string.IsNullOrWhiteSpace(capabilityName))
        {
            return null;
        }

        return _capabilities.FirstOrDefault(c =>
            c.Name.Equals(capabilityName, StringComparison.OrdinalIgnoreCase));
    }
}
