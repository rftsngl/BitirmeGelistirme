namespace WindowsAiAssistant.Agent;

public sealed class AiClientException : Exception
{
    public AiClientException(string message)
        : base(message)
    {
    }

    public AiClientException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
