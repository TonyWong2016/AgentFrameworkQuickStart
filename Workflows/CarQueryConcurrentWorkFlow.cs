using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentFrameworkQuickStart.Workflows
{
    internal class CarQueryConcurrentWorkFlow
    {
        private readonly ModelProvider _modelProvider;

        public CarQueryConcurrentWorkFlow(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        public async Task Run()
        {
            var openAIClient = new OpenAIClient(
                 new ApiKeyCredential(_modelProvider.ApiKey),
                 new OpenAIClientOptions
                 {
                     Endpoint = new Uri(_modelProvider.Endpoint)
                 }
             );

            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            var amazonExecutor = new PlatformPriceExecutor("AmazonPriceAgent", chatClient, "你是Amazon平台价格查询Agent。返回格式：价格=$XXX，库存状态=充足/紧张，配送说明=Prime会员免运费/标准配送。");
            var ebayExecutor = new PlatformPriceExecutor("eBayPriceAgent", chatClient, "你是eBay平台价格查询Agent。返回格式：价格=$XXX，商品状态=全新/二手XX新，运费说明=包邮/买家承担。");
            var shopeeExecutor = new PlatformPriceExecutor("ShopeePriceAgent", chatClient, "你是Shopee平台价格查询Agent。返回格式：价格=$XXX（含税），区域=东南亚/台湾，促销信息=满减活动/无。");
            var startExecutor = new PriceQueryStartExecutor();
            var strategyExecutor = new PricingStrategyExecutor(3);

            var workflow = new WorkflowBuilder(startExecutor)
                .AddFanOutEdge(startExecutor, [amazonExecutor, ebayExecutor, shopeeExecutor])
                .AddFanInEdge([amazonExecutor, ebayExecutor, shopeeExecutor], strategyExecutor)
                .WithOutputFrom(strategyExecutor)
                .Build();

            Console.WriteLine("✅ Loop Workflow 构建完成");

            var priceQuery = new PriceQueryDto(productId: "IPHONE15-PRO-256", productName: "iPhone 15 Pro 256GB", targetRegion: "US");

            await using (var run = await InProcessExecution.StreamAsync(workflow, priceQuery))
            {
                await foreach (var evt in run.WatchStreamAsync())
                {
                    switch (evt)
                    {
                        case ExecutorInvokedEvent started:
                            Console.WriteLine($"🚀 {started.ExecutorId} 开始运行");
                            break;
                        case ExecutorCompletedEvent completed:
                            Console.WriteLine($"✅ {completed.ExecutorId} 结束运行");
                            break;
                        case WorkflowOutputEvent outputEvent: 
                            Console.WriteLine("🎉 Fan-in 汇总输出："); 
                            Console.WriteLine($"{outputEvent.Data}");
                            break;
                        case WorkflowErrorEvent errorEvent: 
                            Console.WriteLine("✨ 收到 Workflow Error Event："); 
                            Console.WriteLine($"{errorEvent.Data}");
                            break;
                    }
                }
            }
        }
    }

    internal class PriceQueryDto
    {
        public string ProductId { get; private set; }
        public string ProductName { get; private set; }
        public string TargetRegion { get; private set; }
        public PriceQueryDto(string productId, string productName, string targetRegion)
        {
            ProductId = productId;
            ProductName = productName;
            TargetRegion = targetRegion;
        }
    }

    internal sealed class PlatformPriceExecutor : Executor<ChatMessage>
    {
        private readonly string _instructions; private readonly IChatClient _chatClient;
        public PlatformPriceExecutor(string id, IChatClient chatClient, string platformInstructions) : base(id) { _chatClient = chatClient; _instructions = platformInstructions; }
        public override async ValueTask HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var messages = new List<ChatMessage> { new(ChatRole.System, _instructions), message };
            var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken); var replyMessage = new ChatMessage(ChatRole.Assistant, response.Text ?? string.Empty) { AuthorName = this.Id };
            await context.SendMessageAsync(replyMessage, cancellationToken: cancellationToken); Console.WriteLine($"✅ {this.Id} 完成查询");
        }
    }

    internal sealed class PriceQueryStartExecutor() : Executor<PriceQueryDto>(nameof(PriceQueryStartExecutor))
    {
        public override async ValueTask HandleAsync(PriceQueryDto query, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            var userPrompt = $@"商品ID: {query.ProductId}商品名称: {query.ProductName}目标区域: {query.TargetRegion}
请查询该商品在你的平台上的当前价格、库存状态和配送信息。"; await context.SendMessageAsync(new ChatMessage(ChatRole.User, userPrompt), cancellationToken: cancellationToken); await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
            Console.WriteLine("📡 Fan-out 价格查询广播已发送");
        }
    }

    internal sealed class PricingStrategyExecutor : Executor<ChatMessage>
    {
        private readonly List<ChatMessage> _messages = [];
        private readonly int _targetCount;
        public PricingStrategyExecutor(int targetCount) : base(nameof(PricingStrategyExecutor))
        {
            _targetCount = targetCount;
        }
        public override async ValueTask HandleAsync(ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            this._messages.Add(message); Console.WriteLine($"📊 已收集 {_messages.Count}/{_targetCount} 个平台数据 - 来自 {message.AuthorName}");
            if (this._messages.Count == this._targetCount)
            {
                var platformData = string.Join("\n", this._messages.Select(m => $"• {m.AuthorName}: {m.Text}"));
                var strategyReport = $@"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━📊 多平台价格汇总（共 {this._messages.Count} 个平台）━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
{platformData}
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━💡 智能定价建议━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━基于以上数据，建议分析竞争对手价格区间，制定差异化定价策略。考虑因素：库存压力、配送成本、平台佣金率、目标利润率。";
                await context.YieldOutputAsync(strategyReport, cancellationToken);
                Console.WriteLine("✨ Fan-in 定价策略生成完成");
            }
        }
    }
}
