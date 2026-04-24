using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using System.ComponentModel;

namespace AgentFrameworkQuickStart
{
    public class BaseAgent
    {
        public readonly ModelProvider modelProvider;
        public BaseAgent(ModelProvider modelProvider)
        {
            this.modelProvider = modelProvider;
        }
        public async Task TalkShowAgent()
        {
            var agent = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .AsIChatClient()
                .AsAIAgent(instructions: "你是个脱口秀大师，可以很轻松的逗笑大家.", name: "脱口秀大师");

            await foreach (var update in agent.RunStreamingAsync("来一段简短的脱口秀表演"))
            {
                Console.Write(update);
            }
        }

        public async Task WatchPicture()
        {
            var agent = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .AsIChatClient()
                .AsAIAgent(instructions: "你是一个能够分析图像的实用助手。.", name: "视觉代理");

            ChatMessage message = new ChatMessage(ChatRole.User, [
                new TextContent("你在这张图片中看到了什么？"),
                new UriContent("https://hebei.xiaoxiaotong.org/AttachFile/2025/12/320102/639005488573011551.png", "image/png")
                ]);
            Console.WriteLine(await agent.RunAsync(message));

            await foreach (var update in agent.RunStreamingAsync(message))
            {
                Console.Write(update);
            }
        }

        public async Task SingleTool()
        {
            var agent = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .AsIChatClient()
                .AsAIAgent(instructions: "你是一个智能助手。", tools: [AIFunctionFactory.Create(GetWeather)]);

            Console.WriteLine(await agent.RunAsync("保定的天气怎么样?"));

        }

        public async Task ToolWithHuman()
        {
#pragma warning disable MEAI001 // 类型仅用于评估，在将来的更新中可能会被更改或删除。取消此诊断以继续。

            AIFunction weatherFunction = AIFunctionFactory.Create(GetWeather);
            AIFunction approvalRequiredWeatherFunction = new ApprovalRequiredAIFunction(weatherFunction);

            var agent = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .AsIChatClient()
                .AsAIAgent(instructions: "你是一个智能助手。", tools: [approvalRequiredWeatherFunction]);

            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse response = await agent.RunAsync("保定的天气如何?", session);

            var functionApprovalRequests = response.Messages
                .SelectMany(x => x.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();
            ToolApprovalRequestContent requestContent = functionApprovalRequests.First();
            Console.WriteLine($"我需要您的批准才能执行 '{requestContent.ToolCall}'");
            var approvalMessage = new ChatMessage(ChatRole.User, [requestContent.CreateResponse(true)]);
            Console.WriteLine(await agent.RunAsync(approvalMessage, session));
#pragma warning restore MEAI001 // 类型仅用于评估，在将来的更新中可能会被更改或删除。取消此诊断以继续。

        }

        [Description("Get the weather for a given location.")]
        static string GetWeather([Description("The location to get the weather for.")] string location)
               => $"The weather in {location} is cloudy with a high of 15°C.";

    }
}