using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using OpenAI;
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
               .CreateAIAgent(instructions: "你是个资深汽车大师，了解很多汽车知识，包括配置，价格，驾驶体验的等等，回复内容尽可能简短高效，突出优缺点，给出综合购买建议，避免长篇大论", name: "汽车大师")
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
