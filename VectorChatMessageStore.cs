using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AgentFrameworkQuickStart
{
    public sealed class VectorChatMessageStore : ChatMessageStore
    {
        private readonly VectorStore _vectorStore;
        public string? ThreadDbKey { get; private set; }

        // 用于反序列化恢复状态
        public VectorChatMessageStore(
            VectorStore vectorStore,
            JsonElement serializedStoreState,
            JsonSerializerOptions? jsonSerializerOptions = null)
        {
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            if (serializedStoreState.ValueKind == JsonValueKind.String)
                ThreadDbKey = serializedStoreState.Deserialize<string>();
        }

        public override async Task AddMessagesAsync(
        IEnumerable<ChatMessage> messages,
        CancellationToken cancellationToken = default)
        {
            // 首次调用时生成唯一线程 ID
            ThreadDbKey ??= Guid.NewGuid().ToString("N");

            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            await collection.UpsertAsync(
                messages.Select(msg => new ChatHistoryItem
                {
                    Key = $"{ThreadDbKey}_{msg.MessageId}",
                    ThreadId = ThreadDbKey,
                    Timestamp = DateTimeOffset.UtcNow,
                    SerializedMessage = JsonSerializer.Serialize(msg),
                    MessageText = msg.Text
                }),
                cancellationToken);
        }

        public override async Task<IEnumerable<ChatMessage>> GetMessagesAsync(
        CancellationToken cancellationToken = default)
        {
            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            // 按时间倒序取最新 N 条（示例取 10 条）
            var records = collection.GetAsync(
                filter: x => x.ThreadId == ThreadDbKey,
                top: 10,
                options: new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken);

            var messages = new List<ChatMessage>();
            await foreach (var record in records)
            {
                messages.Add(JsonSerializer.Deserialize<ChatMessage>(record.SerializedMessage!)!);
            }

            // 注意：必须按时间升序返回（旧 → 新）
            messages.Reverse();
            return messages;
        }

        // 序列化状态：只需保存 ThreadDbKey
        public override JsonElement Serialize(JsonSerializerOptions? options = null)
            => JsonSerializer.SerializeToElement(ThreadDbKey);

        // 数据模型（用于向量存储）
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
