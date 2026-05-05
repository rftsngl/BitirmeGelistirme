namespace WindowsAiAssistant.Infrastructure;

public enum ModelProviderKind
{
    Disabled = 0,
    OpenAI = 1,
    Gemini = 2,
    Anthropic = 3,
    OpenAICompatible = 4,
    Ollama = 5
}
