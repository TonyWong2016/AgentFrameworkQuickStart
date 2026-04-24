using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AgentFrameworkQuickStart
{
    public sealed class VectorChatHistoryProvider : ChatHistoryProvider
    {
        private readonly VectorStore _vectorStore;
        private readonly ProviderSessionState<State> _sessionState;

        public VectorChatHistoryProvider(VectorStore vectorStore, Func<AgentSession?, State>? stateInitializer = null)
        {
            _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
            _sessionState = new ProviderSessionState<State>(
                stateInitializer ?? (_ => new State()),
                this.GetType().Name);
        }

        public string StateKey => _sessionState.StateKey;

        protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(InvokingContext context, CancellationToken cancellationToken = default)
        {
            var state = _sessionState.GetOrInitializeState(context.Session);

            if (string.IsNullOrEmpty(state.ThreadDbKey))
                return [];

            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            // 按时间倒序取最新 10 条
            var records = collection.GetAsync(
                filter: x => x.ThreadId == state.ThreadDbKey,
                top: 10,
                options: new() { OrderBy = x => x.Descending(y => y.Timestamp) },
                cancellationToken);

            var messages = new List<ChatMessage>();
            await foreach (var record in records)
            {
                messages.Add(JsonSerializer.Deserialize<ChatMessage>(record.SerializedMessage!)!);
            }

            // 必须按时间升序返回（旧 → 新）
            messages.Reverse();
            return messages;
        }

        protected override async ValueTask StoreChatHistoryAsync(InvokedContext context, CancellationToken cancellationToken = default)
        {
            var state = _sessionState.GetOrInitializeState(context.Session);

            // 首次调用时生成唯一线程 ID
            state.ThreadDbKey ??= Guid.NewGuid().ToString("N");

            var collection = _vectorStore.GetCollection<string, ChatHistoryItem>("ChatHistory");
            await collection.EnsureCollectionExistsAsync(cancellationToken);

            // 合并请求和响应消息
            var allNewMessages = context.RequestMessages.Concat(context.ResponseMessages ?? []);

            await collection.UpsertAsync(
                allNewMessages.Select(msg => new ChatHistoryItem
                {
                    Key = $"{state.ThreadDbKey}_{msg.MessageId}",
                    ThreadId = state.ThreadDbKey,
                    Timestamp = DateTimeOffset.UtcNow,
                    SerializedMessage = JsonSerializer.Serialize(msg),
                    MessageText = msg.Text
                }),
                cancellationToken);

            _sessionState.SaveState(context.Session, state);
        }

        // 数据模型（用于向量存储）
        private sealed class ChatHistoryItem
        {
            [VectorStoreKey] public string? Key { get; set; }
            [VectorStoreData] public string? ThreadId { get; set; }
            [VectorStoreData] public DateTimeOffset? Timestamp { get; set; }
            [VectorStoreData] public string? SerializedMessage { get; set; }
            [VectorStoreData] public string? MessageText { get; set; }
        }

        public sealed class State
        {
            public string? ThreadDbKey { get; set; }
        }
    }
}