using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Spectre.Console;
using System.ClientModel;
using System.Text.Json;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace AgentFrameworkQuickStart.Workflows
{
    public class AgentsInWorkFlow
    {
        private readonly ModelProvider _modelProvider;

        public AgentsInWorkFlow(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        public async Task Run()
        {
            var openAIClient = new OpenAIClient(
                 new ApiKeyCredential(_modelProvider.ApiKey),
                 new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
             );

            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // 1. 创建翻译 Agent (设置 Name 属性以便识别)
            AIAgent englishAgent = GetTranslationAgent("英语", chatClient, "EnglishAgent");
            AIAgent spanishAgent = GetTranslationAgent("西班牙语", chatClient, "SpanishAgent");
            AIAgent frenchAgent = GetTranslationAgent("法语", chatClient, "FrenchAgent");

            // 2. 构建接力流：中 -> 英 -> 西 -> 法
            var workflow = new WorkflowBuilder(englishAgent)
                .AddEdge(englishAgent, spanishAgent)
                .AddEdge(spanishAgent, frenchAgent)
                .Build();

            AnsiConsole.Write(new Rule("[bold green]🌍 多语言接力翻译启动[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[grey]原始输入:[/] 哈喽啊老铁，啥时候来保定转转啊");
            AnsiConsole.WriteLine();

            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, new ChatMessage(ChatRole.User, "哈喽啊老铁，啥时候来保定转转啊"));
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // 记录当前正在工作的 Agent ID
            string lastAgentId = string.Empty;

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                // 监听调用事件：当一个 Agent 开始工作时触发
                if (evt is ExecutorInvokedEvent invoked)
                {
                    // 只取下划线前的部分，让控制台更干净
                    var displayName = invoked.ExecutorId.Split('_')[0];
                    if (displayName != lastAgentId)
                    {
                        lastAgentId = displayName;
                        AnsiConsole.WriteLine();
                        AnsiConsole.Write(new Rule($"[bold yellow]▶ {displayName}[/]").LeftJustified());
                    }
                }

                // 监听流式更新事件：处理具体的文字输出
                if (evt is AgentRunUpdateEvent update)
                {
                    if (update.Data == null) continue;

                    // 使用不同的颜色区分输出
                    var color = lastAgentId.Contains("English") ? "cyan" :
                                lastAgentId.Contains("Spanish") ? "green" : "magenta";

                    AnsiConsole.Markup($"[{color}]{update.Data.ToString()?.EscapeMarkup()}[/]");
                }

                // 监听工作流结束
                if (evt is WorkflowOutputEvent)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[bold green]✅ 翻译链条全部完成[/]").LeftJustified());
                }
            }
        }

        private static ChatClientAgent GetTranslationAgent(string targetLanguage, IChatClient chatClient, string agentName)
        {
            string instructions = $@"你是一个专门负责接力翻译的 Agent。
你的目标语言是：{targetLanguage}。
任务规则：
1. 忽略对话历史中最早的原始指令,忽略上一个 Agent 输出的语种标签（如 'English' 或 'Spanish'）。
2. 只翻译你接收到的‘最后一条’消息内容。
3. 直接输出翻译结果，不要带任何解释。";
            // 使用 Options 来初始化，这是设置 Name 的正确姿势
            var options = new ChatClientAgentOptions
            {
                Name = agentName,
                Description = $"负责翻译成{targetLanguage}的专业Agent"
            };

            return new ChatClientAgent(chatClient, instructions, options.Name,options.Description);
        }

        public async Task ConcurrentRun()
        {
            var openAIClient = new OpenAIClient(
                 new ApiKeyCredential(_modelProvider.ApiKey),
                 new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
             );

            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // Create the executors
            ChatClientAgent physicist = new(
                chatClient,
                name: "物理学家",
                instructions: "你是物理学专家。你从物理角度回答问题."
            );
            ChatClientAgent chemist = new(
                chatClient,
                name: "化学家",
                instructions: "你是化学专家。你从化学角度回答问题."
            );

            var startExecutor = new ConcurrentStartExecutor();
            var aggregationExecutor = new ConcurrentAggregationExecutor();

            // Build the workflow by adding executors and connecting them
            var workflow = new WorkflowBuilder(startExecutor)
                .AddFanOutEdge(startExecutor, [physicist, chemist])
                .AddFanInEdge([physicist, chemist], aggregationExecutor)
                .WithOutputFrom(aggregationExecutor)
                .Build();

            // Execute the workflow in streaming mode
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: "今天天气如何？");
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                Console.WriteLine(evt);
                if (evt is WorkflowOutputEvent output)
                {
                    Console.WriteLine($"Workflow completed with results:\n{output.Data}");
                }
            }
        }


        public async Task RunBranchingWorkflow(string userInput)
        {
            // 依然使用你的自定义 OpenAI 客户端设置
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // --- 创建 Agent ---
            // 1. 路由 Agent：负责分析需求
            // 1. 定义你的系统指令
            string routerInstructions = "你是一个分流器。分析用户想翻译成什么语言。如果是英语，只回复'English'；如果是西班牙语，只回复'Spanish'。";

            // 2. 配置其他选项（如 Name 和 Description）
            var routerOptions = new ChatClientAgentOptions
            {
                Name = "RouterAgent",
                Description = "负责识别语言意图并进行分支分流的路由代理"
            };

            // 3. 使用正确的构造函数：(client, instructions, options)
            AIAgent routerAgent = new ChatClientAgent(chatClient, routerInstructions, routerOptions.Name, routerOptions.Description);

            // 2. 英语 Agent
            AIAgent englishAgent = GetTranslationAgent("英语", chatClient, "EnglishAgent");

            // 3. 西班牙语 Agent
            AIAgent spanishAgent = GetTranslationAgent("西班牙语", chatClient, "SpanishAgent");

            // --- 构建带分支的工作流 ---
            //var workflow = new WorkflowBuilder(routerAgent)
            //    .AddEdge(routerAgent, englishAgent, async (messages) => await IsEnglishPath(messages)) // 如果满足 English 条件，走这条线
            //    .AddEdge(routerAgent, spanishAgent, condition: IsSpanishPath) // 如果满足 Spanish 条件，走这条线
            //    .Build();
            // 明确指定状态类型为 TranslationRouteState
            var workflow = new WorkflowBuilder(routerAgent)
                // 英语分支
                .AddEdge<TranslationRouteState>(
                    routerAgent,
                    englishAgent,
                    condition: state => state?.TargetLanguage == "English")
                // 西班牙语分支
                .AddEdge<TranslationRouteState>(
                    routerAgent,
                    spanishAgent,
                    condition: state => state?.TargetLanguage == "Spanish")
                .Build();
            // --- 执行与 Spectre.Console 展示 ---
            AnsiConsole.Write(new Rule("[bold cyan]分路路由工作流启动[/]").LeftJustified());

            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, new ChatMessage(ChatRole.User, userInput));
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            string lastAgentId = string.Empty;

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                //Console.WriteLine(evt);
                if (evt is ExecutorInvokedEvent invoked)
                {
                    var cleanName = invoked.ExecutorId.Split('_')[0];
                    if (cleanName != lastAgentId)
                    {
                        lastAgentId = cleanName;
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[bold yellow]🔍 节点激活: {cleanName}[/]");
                    }
                }

                if (evt is AgentRunUpdateEvent update && update.Data != null)
                {
                    AnsiConsole.Write(update.Data.ToString());
                }

                if (evt is WorkflowOutputEvent)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.Write(new Rule("[bold green]流程结束[/]").LeftJustified());
                }
            }
        }

        // 简单的路由逻辑：判断上一个 Agent 的输出里包含哪个语种关键词
        // 这里的参数类型要与 InProcessExecution 或 Workflow 传递的消息列表类型一致
        private static async Task<bool> IsEnglishPath(IReadOnlyList<ChatMessage> messages)
        {
            // 获取 RouterAgent 的最后一次输出
            var lastResponse = messages.LastOrDefault()?.Text?.ToLower() ?? "";
            // 只要异步返回 Task<bool> 即可，这里其实不需要等待，但签名必须匹配
            return await Task.FromResult(lastResponse.Contains("english"));
        }

        private static async Task<bool> IsSpanishPath(IReadOnlyList<ChatMessage> messages)
        {
            var lastResponse = messages.LastOrDefault()?.Text?.ToLower() ?? "";
            return await Task.FromResult(lastResponse.Contains("spanish"));
        }
    }
    public class TranslationRouteState
    {
        // 对应官网案例中的 AnalysisResult
        public string TargetLanguage { get; set; } = string.Empty;
    }

    /// <summary>
    /// Executor that starts the concurrent processing by sending messages to the agents.
    /// </summary>
    internal sealed partial class ConcurrentStartExecutor : Executor<string>
    {
        public ConcurrentStartExecutor() : base("ConcurrentStartExecutor")
        {
        }

        /// <summary>
        /// Starts the concurrent processing by broadcasting the message and a turn token.
        /// </summary>
        public override async ValueTask HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            // 广播用户消息给所有监听者（通常是多个 Agent）
            await context.SendMessageAsync(new ChatMessage(ChatRole.User, message), cancellationToken: cancellationToken);

            // 发送 TurnToken 触发所有监听者开始处理
            await context.SendMessageAsync(new TurnToken(emitEvents: true), cancellationToken: cancellationToken);
        }

        // 必须重写，即使不使用路由
        protected override RouteBuilder ConfigureRoutes(RouteBuilder routeBuilder)
        {
            // 返回原 builder，表示无额外路由配置
            return routeBuilder;
        }
    }



    /// <summary>
    /// Executor that aggregates the results from the concurrent agents.
    /// </summary>
    internal sealed class ConcurrentAggregationExecutor() :
        Executor<List<ChatMessage>>("ConcurrentAggregationExecutor")
    {
        private readonly List<ChatMessage> _messages = [];

        /// <summary>
        /// Handles incoming messages from the agents and aggregates their responses.
        /// </summary>
        /// <param name="message">The messages from the agent</param>
        /// <param name="context">Workflow context for accessing workflow services and adding events</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.
        /// The default is <see cref="CancellationToken.None"/>.</param>
        /// <returns>A task representing the asynchronous operation</returns>
        public override async ValueTask HandleAsync(List<ChatMessage> message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            this._messages.AddRange(message);

            if (this._messages.Count == 2)
            {
                var formattedMessages = string.Join(Environment.NewLine, this._messages.Select(m => $"{m.AuthorName}: {m.Text}"));
                await context.YieldOutputAsync(formattedMessages, cancellationToken);
            }
        }
    }


}