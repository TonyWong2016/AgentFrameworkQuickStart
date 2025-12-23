using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OpenAI;
using System.ClientModel;

namespace AgentFrameworkQuickStart
{
    public class McpAgent
    {
        public readonly ModelProvider modelProvider;
        public McpAgent(ModelProvider modelProvider)
        {
            this.modelProvider = modelProvider;
        }

        public async Task ExposingMcpServer()
        {
            var agent = new OpenAIClient(
                    new ApiKeyCredential(modelProvider.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .CreateAIAgent(instructions: "你是个笑话大师.", name: "笑话大师");
            var jokerMcpTool = McpServerTool.Create(agent.AsAIFunction());
            var builder = Host.CreateEmptyApplicationBuilder(settings: null);
            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithTools([jokerMcpTool]);
            await builder
                .Build()
                .RunAsync();
        }
    }
}
