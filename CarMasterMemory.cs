using AgentFrameworkQuickStart.Models;
using AgentFrameworkQuickStart.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Connectors.InMemory;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.Text;
using System.Text.Json;

namespace AgentFrameworkQuickStart
{
    // 汽车大师的认知记忆（提炼偏好）
    public class CarMasterMemory : AIContextProvider
    {
        private readonly IChatClient _innerClient;
        public CarPreference Preference { get; private set; }

        public CarMasterMemory(IChatClient client, CarPreference? pref = null)
        {
            _innerClient = client;
            Preference = pref ?? new CarPreference();
        }

        public CarMasterMemory(IChatClient client, JsonElement serializedState, JsonSerializerOptions? options = null)
        {
            _innerClient = client;
            // 健壮性检查：处理新线程时的空状态
            Preference = serializedState.ValueKind == JsonValueKind.Object
                ? serializedState.Deserialize<CarPreference>(options) ?? new CarPreference()
                : new CarPreference();
        }

        public override ValueTask<AIContext> InvokingAsync(InvokingContext context, CancellationToken ct = default)
        {
            // 将提炼出的偏好作为“元指令”注入每一轮对话
            var sb = new StringBuilder("\n[后台画像已加载]");
            if (Preference.BudgetMax > 0) sb.Append($" | 预算上限：{Preference.BudgetMax}万");
            if (Preference.EnergyType != "未指定") sb.Append($" | 能源偏好：{Preference.EnergyType}");
            if (Preference.MustHaves.Any()) sb.Append($" | 关键需求：{string.Join("、", Preference.MustHaves)}");

            return new ValueTask<AIContext>(new AIContext { Instructions = sb.ToString() });
        }


        public override async ValueTask InvokedAsync(InvokedContext context, CancellationToken ct = default)
        {
            if (context.RequestMessages.Any(m => m.Role == ChatRole.User))
            {
                try
                {
                    // 1. 获取最后一条用户消息，针对性提取
                    var lastUserMessage = context.RequestMessages.LastOrDefault(m => m.Role == ChatRole.User)?.Text;
                    if (string.IsNullOrEmpty(lastUserMessage)) return;

                    var analysisOptions = new ChatOptions
                    {
                        ResponseFormat = ChatResponseFormat.Json,
                        Instructions = """
                    你是一个数据提取器。请分析用户的输入，提取购车意向。
                    返回 JSON 格式如下：
                    {
                      "BudgetMax": 数字 (如果是30万请写30, 必须是万为单位的数字),
                      "EnergyType": "字符串 (如: 纯电/燃油/混动)",
                      "MustHaves": ["需求点1", "需求点2"] (如果没有提到任何具体配置或功能需求，请返回空数组 [])
                    }
                    注意：如果是配置需求(如: 智驾、全景天窗、大空间)，请放入 MustHaves。
                    """
                    };

                    // 2. 调用模型进行提取
                    var extraction = await _innerClient.GetResponseAsync<CarPreference>(
                        context.RequestMessages.TakeLast(2), // 只看最近一两轮
                        analysisOptions);

                    if (extraction.Result != null)
                    {
                        var newInfo = extraction.Result;

                        // 预算修正：防止模型给出的单位不统一 (如果大于5000，认为是元，自动除以10000)
                        if (newInfo.BudgetMax > 5000) newInfo.BudgetMax /= 10000;
                        if (newInfo.BudgetMax > 0) this.Preference.BudgetMax = newInfo.BudgetMax;

                        // 能源类型更新
                        if (!string.IsNullOrEmpty(newInfo.EnergyType) && newInfo.EnergyType != "未指定" && newInfo.EnergyType != "null")
                        {
                            this.Preference.EnergyType = newInfo.EnergyType;
                        }

                        // 需求点合并（关键修复点）
                        if (newInfo.MustHaves != null && newInfo.MustHaves.Any())
                        {
                            // 过滤掉模型可能返回的 "无"、"null" 等无效字符串
                            var validNewItems = newInfo.MustHaves
                                .Where(s => !string.IsNullOrWhiteSpace(s) && s != "无" && s != "null");

                            // 使用 HashSet 进行去重合并
                            var updatedList = this.Preference.MustHaves.Union(validNewItems, StringComparer.OrdinalIgnoreCase).ToList();
                            this.Preference.MustHaves = updatedList;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 调试用
                    // Console.WriteLine($"[DEBUG] 提取失败: {ex.Message}");
                }
            }
        }
        public override JsonElement Serialize(JsonSerializerOptions? options = null)
            => JsonSerializer.SerializeToElement(Preference, options);
    }

    public class CarMasterAgent : BaseAgent
    {
        private readonly VectorStore _vectorStore = new InMemoryVectorStore();

        public CarMasterAgent(ModelProvider modelProvider) : base(modelProvider) { }

        public async Task RunMasterAsync()
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) });

            var chatClient = client.GetChatClient(modelProvider.ModelId);

            var agent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车大师",
                Description = "你是一个毒舌但专业的汽车大师。你会根据后台画像（预算、需求）给出精准建议。",
                // 1. 对话记录存入向量数据库（你写的逻辑）
                ChatMessageStoreFactory = ctx => new VectorChatMessageStore(_vectorStore, ctx.SerializedState, ctx.JsonSerializerOptions),
                // 2. 画像提炼存入上下文提供者（我优化的逻辑）
                AIContextProviderFactory = ctx => new CarMasterMemory(chatClient.AsIChatClient(), ctx.SerializedState, ctx.JsonSerializerOptions)
            });

            var thread = agent.GetNewThread();

            while (true)
            {
                var input = AnsiConsole.Ask<string>("[white]你:[/]");
                if (input == "exit") break;

                var response = await agent.RunAsync(input, thread);
                AnsiConsole.MarkupLine($"\n[cyan]大师: {response}[/]");

                // 实时展示结构化记忆（画像）
                var mem = thread.GetService<CarMasterMemory>()?.Preference;
                AnsiConsole.MarkupLine($"[grey]>>> 系统画像更新 | 预算: {mem?.BudgetMax}w | 能源: {mem?.EnergyType} | 需求数: {mem?.MustHaves.Count}[/]");
            }
        }

        public async Task RunMasterStreamAsync()
        {
            var client = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) });

            var chatClient = client.GetChatClient(modelProvider.ModelId);

            var agent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车大师",
                Description = "你是一个毒舌但专业的汽车大师。你会根据后台画像（预算、需求）给出精准建议。",
                // 1. 对话记录存入向量数据库（你写的逻辑）
                ChatMessageStoreFactory = ctx => new VectorChatMessageStore(_vectorStore, ctx.SerializedState, ctx.JsonSerializerOptions),
                // 2. 画像提炼存入上下文提供者（我优化的逻辑）
                AIContextProviderFactory = ctx => new CarMasterMemory(chatClient.AsIChatClient(), ctx.SerializedState, ctx.JsonSerializerOptions)
            });

            var thread = agent.GetNewThread();

            AnsiConsole.MarkupLine("[bold green]--- 汽车大师已上线 (流式模式) ---[/]");

            while (true)
            {
                var input = AnsiConsole.Ask<string>("\n[white]你:[/]");
                if (input == "exit") break;

                AnsiConsole.Markup("[cyan]大师:[/] ");

                // 关键：使用 RunStreamingAsync 开启流式传输
                // 它返回的是 IAsyncEnumerable<string>
                await foreach (var chunk in agent.RunStreamingAsync(input, thread))
                {
                    // 直接输出片段，不换行
                    Console.Write(chunk);
                }

                Console.WriteLine(); // 结束后手动换行

                // --- 实时展示画像（画像提取仍然是在后台 InvokedAsync 异步完成的） ---
                // 注意：因为 InvokedAsync 可能在流结束后需要一点点时间处理，
                // 这里可以加一个极短的延迟或者直接显示（通常流结束时提取也完成了）
                await Task.Delay(500);
                var mem = thread.GetService<CarMasterMemory>()?.Preference;

                // 使用 Spectre.Console 画个精美的小面板展示当前状态
                var panel = new Panel($"""
            [yellow]预算限制：[/] {mem?.BudgetMax} 万
            [yellow]能源偏好：[/] {mem?.EnergyType}
            [yellow]核心需求：[/] {(mem?.MustHaves.Any() == true ? string.Join("、", mem.MustHaves) : "尚不明确")}
            """)
                {
                    Header = new PanelHeader("🚗 [bold]当前画像记录[/]"),
                    Border = BoxBorder.Rounded
                };

                AnsiConsole.Write(panel);
            }
        }

        public async Task RunMasterWithToolsAsync()
        {
            var chatClient = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId);

            // 注册工具
            var agent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车大师",
                Description = "一个从业20年的专业汽车顾问，擅长结合用户画像进行精准推荐。",

                // 关键修正：将推理相关的配置放入 ChatOptions
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是一个专业的汽车推荐助手。请优先参考后台画像。如果用户询问具体推荐，请调用 SearchCars 工具。",
                    Tools = [AIFunctionFactory.Create(new CarTool().SearchCars)]
                },

                // 记忆逻辑保持在顶层，因为它们属于 Agent 的生命周期管理
                AIContextProviderFactory = ctx => new CarMasterMemory(
                    chatClient.AsIChatClient(),
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions),

                ChatMessageStoreFactory = ctx => new VectorChatMessageStore(
                    _vectorStore,
                    ctx.SerializedState,
                    ctx.JsonSerializerOptions)
            });

            var thread = agent.GetNewThread();

            while (true)
            {
                var input = AnsiConsole.Ask<string>("\n[white]你:[/]");
                if (input == "exit") break;

                // 使用流式输出
                AnsiConsole.Markup("[cyan]大师:[/] ");
                await foreach (var chunk in agent.RunStreamingAsync(input, thread))
                {
                    Console.Write(chunk);
                }
                Console.WriteLine();

                // 依然展示画像
                var mem = thread.GetService<CarMasterMemory>()?.Preference;
                AnsiConsole.Write(new Panel($"预算: {mem?.BudgetMax}w | 能源: {mem?.EnergyType}").Border(BoxBorder.Rounded));
            }
        }
    }
}
