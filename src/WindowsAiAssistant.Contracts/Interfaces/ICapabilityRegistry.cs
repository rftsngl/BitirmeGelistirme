namespace WindowsAiAssistant.Contracts.Interfaces;

public interface ICapabilityRegistry
{
    IReadOnlyCollection<ICapability> GetAll();
    ICapability? FindByName(string capabilityName);
}
