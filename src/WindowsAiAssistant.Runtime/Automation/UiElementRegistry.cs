namespace WindowsAiAssistant.Runtime.Automation;

public sealed class UiElementRegistry
{
    private readonly Dictionary<string, UiElementReference> _elements = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UiElementReference> Elements => _elements;

    public void Clear() => _elements.Clear();

    public void Register(UiElementReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        _elements[reference.ElementId] = reference;
    }

    public bool TryGet(string elementId, out UiElementReference? reference)
    {
        if (string.IsNullOrWhiteSpace(elementId))
        {
            reference = null;
            return false;
        }

        return _elements.TryGetValue(elementId.Trim(), out reference);
    }
}
