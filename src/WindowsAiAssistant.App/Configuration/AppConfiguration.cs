using Microsoft.Extensions.Configuration;
using WindowsAiAssistant.Agent;
using WindowsAiAssistant.Runtime.Config;

namespace WindowsAiAssistant.App.Configuration;

public static class AppConfiguration
{
    public static (AgentOptions Agent, RuntimeOptions Runtime) Load()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        var agent = new AgentOptions();
        configuration.GetSection("Agent").Bind(agent);

        var runtime = new RuntimeOptions();
        configuration.GetSection("Runtime").Bind(runtime);

        return (agent, runtime);
    }
}
