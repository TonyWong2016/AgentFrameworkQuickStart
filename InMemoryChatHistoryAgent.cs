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
            Console.WriteLine("检测到已保存的对话状态，正在恢复...");
            string json = await File.ReadAllTextAsync(_threadStatePath);
            var element = JsonSerializer.Deserialize<JsonElement>(json, JsonSerializerOptions.Web);
            thread = agent.DeserializeThread(element, JsonSerializerOptions.Web);
            Console.WriteLine("✅ 对话已恢复！");
        }
        else
        {
            Console.WriteLine("🆕 开始新对话（使用 InMemory 向量存储记录历史）...");
            thread = agent.GetNewThread();
        }

        // 3. 交互循环
        while (true)
        {
            Console.Write("\n💬 你: ");
            string? input = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(input)) continue;

            if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                // 保存线程状态（仅元数据，消息存在 vector store）
                var state = thread.Serialize(JsonSerializerOptions.Web).GetRawText();
                await File.WriteAllTextAsync(_threadStatePath, state);
                Console.WriteLine("💾 线程状态已保存，再见！");
                break;
            }

            if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                // 清除：删除状态文件 + 新建 session
                if (File.Exists(_threadStatePath)) File.Delete(_threadStatePath);
                thread = agent.GetNewThread();
                Console.WriteLine("🧹 已开启全新对话（旧历史不可见）");
                continue;
            }

            try
            {
                var response = await agent.RunAsync(input, thread);
                Console.WriteLine($"\n🤖 助手: {response}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                continue;
            }

            // 每次交互后自动保存线程状态（关键！）
            var updatedState = thread.Serialize(JsonSerializerOptions.Web).GetRawText();
            await File.WriteAllTextAsync(_threadStatePath, updatedState);
        }
    }

    // === 内嵌的 ChatMessageStore 实现（基于 InMemoryVectorStore）===
    private sealed class VectorChatMessageStore : ChatMessageStore
    {
        private readonly VectorStore _vectorStore;
        public string? ThreadDbKey { get; private set; }

        public VectorChatMessageStore(
            VectorStore vectorStore,
            JsonElement serializedStoreState,
            JsonSerializerOptions? jsonSerializerOptions = null)
        {
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            if (serializedStoreState.ValueKind == JsonValueKind.String)
                ThreadDbKey = serializedStoreState.Deserialize<string>(jsonSerializerOptions);
        }

        public override async Task AddMessagesAsync(
            IEnumerable<ChatMessage> messages,
            CancellationToken cancellationToken = default)
        {
            ThreadDbKey ??= Guid.NewGuid().ToString("N");

            AnsiConsole.MarkupLine($"💾 [cyan]【Add】 ThreadKey: {ThreadDbKey}, 消息数: {messages.Count()}[/]");

            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            await collection.UpsertAsync(
                messages.Select(msg => new ChatHistoryItem
                {
                    Key = $"{ThreadDbKey}_{msg.MessageId}",
                    ThreadId = ThreadDbKey,
                    Timestamp = DateTimeOffset.UtcNow,
                    SerializedMessage = JsonSerializer.Serialize(msg, SourceGenerationContext.Default.ChatMessage),
                    MessageText = msg.Text ?? ""
                }),
                cancellationToken);
        }

        public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(ThreadDbKey))
                return [];

            AnsiConsole.MarkupLine($"📥 [yellow]【Get】 从 ThreadKey: {ThreadDbKey} 读取消息[/]");


            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            // 获取该线程的所有消息（按时间倒序取最新 10 条）
            var records = collection.GetAsync(
                filter: x => x.ThreadId == ThreadDbKey,
                top: 10,
                options: new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken);

            var messages = new List<ChatMessage>();
            await foreach (var record in records)
            {
                messages.Add(JsonSerializer.Deserialize<ChatMessage>(
                    record.SerializedMessage!,
                    SourceGenerationContext.Default.ChatMessage)!);
            }

            messages.Reverse(); // 转为时间升序（旧 → 新）
            return messages;
        }

        public override JsonElement Serialize(JsonSerializerOptions? options = null)
            => JsonSerializer.SerializeToElement(ThreadDbKey, options);

        private sealed class ChatHistoryItem
        {
            [VectorStoreKey] public string? Key { get; set; }
            [VectorStoreData] public string? ThreadId { get; set; }
            [VectorStoreData] public DateTimeOffset? Timestamp { get; set; }
            [VectorStoreData] public string? SerializedMessage { get; set; }
            [VectorStoreData] public string? MessageText { get; set; }
        }
    }
}