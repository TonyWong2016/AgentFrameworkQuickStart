using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System;
using System.ClientModel;

namespace AgentFrameworkQuickStart
{
    public class ObservabilityAgent
    {
        public readonly ModelProvider modelProvider;
        public ObservabilityAgent(ModelProvider modelProvider)
        {
            this.modelProvider = modelProvider;
        }

        public async Task ObservabilityDemo()
        {
            using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("agent-telemetry-source")
            .AddConsoleExporter()
            .Build();

            var agent = new OpenAIClient(
               new ApiKeyCredential(modelProvider.ApiKey),
               new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
               .GetChatClient(modelProvider.ModelId)
               .AsIChatClient()
               .AsAIAgent(instructions: "你是个脱口秀大师，可以很轻松的逗笑大家", name: "脱口秀大师")
               .AsBuilder()
               .UseOpenTelemetry(sourceName: "agent-telemetry-source")
               .Build();

            await foreach (var update in agent.RunStreamingAsync("介绍一下新款宝马X3 25L这款车"))
            {
                Console.Write(update);
            }
        }
    }
}