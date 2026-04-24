// InMemoryChatHistoryAgent.cs
using AgentFrameworkQuickStart.Models;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using OpenAI;
using OpenAI.Chat;
using Spectre.Console;
using System.ClientModel;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentFrameworkQuickStart;

public class InMemoryChatHistoryAgent
{
    private readonly ModelProvider _modelProvider;
    private readonly string _threadStatePath;

    public InMemoryChatHistoryAgent(ModelProvider modelProvider, string threadStateFileName = "thread_state.json")
    {
        _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        _threadStatePath = Path.Combine(Directory.GetCurrentDirectory(), threadStateFileName);
    }

    public async Task RunInteractiveChatAsync()
    {
        // 1. 创建带自定义消息存储的 Agent
        AIAgent agent = new OpenAIClient(
            new ApiKeyCredential(_modelProvider.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) })
        .GetChatClient(_modelProvider.ModelId)
        .AsIChatClient()
        .AsAIAgent(
        new ChatClientAgentOptions
        {
            Name = "记忆大师",
            ChatOptions = new ChatOptions
            {
                Instructions = "你是一个有长期记忆的助手，能记住之前的对话。"
            }
        });

        // 2. 尝试恢复 session
        AgentSession session;
        if (File.Exists(_threadStatePath))
        {
            Console.WriteLine("🔍 检测到已保存的对话状态，正在恢复...");
            string json = await File.ReadAllTextAsync(_threadStatePath);
            var element = JsonSerializer.Deserialize<JsonElement>(json, JsonSerializerOptions.Web);
            session = await agent.DeserializeSessionAsync(element);
            Console.WriteLine("✅ 对话已恢复！");
        }
        else
        {
            Console.WriteLine("🆕 开始新对话（使用 InMemory 向量存储记录历史）...");
            session = await agent.CreateSessionAsync();
        }

        // 3. 交互循环
        while (true)
        {
            Console.Write("\n💬 你: ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                // 保存 session 状态
                var state = await agent.SerializeSessionAsync(session);
                await File.WriteAllTextAsync(_threadStatePath, state.GetRawText());
                Console.WriteLine("💾 线程状态已保存，再见！");
                break;
            }

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                // 清除：删除状态文件 + 新建 session
                if (File.Exists(_threadStatePath)) File.Delete(_threadStatePath);
                session = await agent.CreateSessionAsync();
                Console.WriteLine("🧹 已开启全新对话（旧历史不可见）");
                continue;
            }

            try
            {
                var response = await agent.RunAsync(input, session);
                Console.WriteLine($"\n🤖 助手: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ 错误: {ex.Message}");
                continue;
            }

            // 每次交互后自动保存 session 状态
            var updatedState = await agent.SerializeSessionAsync(session);
            await File.WriteAllTextAsync(_threadStatePath, updatedState.GetRawText());
        }
    }
}