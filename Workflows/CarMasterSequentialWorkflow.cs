using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.Text.RegularExpressions;

namespace AgentFrameworkQuickStart.Workflows
{
    public class CarMasterSequentialWorkflow
    {
        private readonly ModelProvider _modelProvider;

        public CarMasterSequentialWorkflow(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        public async Task ExecuteAsync(string userInput)
        {
            // 1. 创建ChatClient (遵循你的项目模式，不使用IChatClient接口)
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );

            // 这里就是OpenAI.ChatClient类型
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId);

            // 2. 分步创建Agent，并给出明确调试信息
            AnsiConsole.Write(new Rule("[bold green]🚗 汽车大师工作流启动[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[grey]诊断问题:[/] {userInput}");
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[yellow]1. 正在创建「汽车查询专员」...[/]");
            var queryAgent = chatClient.CreateAIAgent(
                instructions: "你是一名专业的汽车信息查询专员。请清晰理解用户关于车辆的问题，整理出品牌、型号、年份等关键信息，并进行初步分类和解答。你的回答将为诊断专家提供基础。"
                , name: "汽车查询专员");

            AnsiConsole.MarkupLine($"[green]   ✅ 「{queryAgent.Name}」创建完成[/]\n");

            AnsiConsole.MarkupLine("[yellow]2. 正在创建「汽车诊断专家」...[/]");
            var diagnosticAgent = chatClient.CreateAIAgent(instructions: "你是一名资深汽车维修专家，懂得关于汽车的大部分信息，机械素质，油耗，维修，保养，安全性，品牌保值率甚至补贴政策等等，你将收到查询专员整理的报告，请基于对该汽车做一个客观评价，告知用户该汽车在后续驾驶过程中有哪些需要注意的地方。这是给用户的最终回答，若用户无详细阐述的要求，回答应尽可能简短有效。"
                , name: "资深汽车维修专家");
            AnsiConsole.MarkupLine("[green]   ✅ 创建完成[/]\n");

            // 3. 构建并执行工作流
            AnsiConsole.MarkupLine("[yellow]3. 执行顺序工作流 (查询 -> 驾驶建议)...[/]");
            var workflow = AgentWorkflowBuilder.BuildSequential(new[] { queryAgent, diagnosticAgent });
            var messages = new List<ChatMessage> { new(ChatRole.User, userInput) };

            await using var run = await InProcessExecution.StreamAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            List<Microsoft.Extensions.AI.ChatMessage> result = new();
            // 4. 处理事件流 - 核心修正部分
            await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
            {
                // 主要监听 AgentRunUpdateEvent
                if (evt is AgentRunUpdateEvent agentUpdate)
                {
                    //AnsiConsole.MarkupLine($"[grey]{agentUpdate.ExecutorId}:{agentUpdate.Data}[/]");
                    AnsiConsole.Write($"{agentUpdate.Data}");
                }

                // 工作流最终完成事件
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    //result = (List<Microsoft.Extensions.AI.ChatMessage>)outputEvent.Data!;
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[green]   ✅ 所有步骤执行完毕[/]");
                    AnsiConsole.WriteLine();

                    // 输出最终结果
                    if (outputEvent.Data is List<ChatMessage> finalMessages && finalMessages.Count > 0)
                    {
                        var finalAnswer = finalMessages.Last().Text;
                        var panel = new Panel(finalAnswer ?? "未收到有效回答")
                            .Header("[bold yellow] 模型输出[/]")
                            .Border(BoxBorder.Rounded)
                            .Padding(1, 1);
                        AnsiConsole.Write(panel);
                    }
                    break;
                }
            }

            AnsiConsole.Write(new Rule("[grey]诊断流程结束[/]").LeftJustified());
            AnsiConsole.WriteLine();
        }

        // 自动暴走bug
        public async Task ExecuteInteractiveWorkflowAsyncError(string carIssue)
        {
            // 1. 创建ChatClient (遵循你的项目模式，不使用IChatClient接口)
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );

            // 这里就是OpenAI.ChatClient类型
            var _chatClient = openAIClient.GetChatClient(_modelProvider.ModelId);

            // 1. 定义执行器 (Executors)
            // 诊断专家
            var diagnosticAgent = _chatClient.CreateAIAgent(
                instructions: "你是诊断专家。基于用户描述的故障，提供维修方案和预估价格。末尾必须询问用户是否同意该方案。",
                name: "DiagnosticExpert");

            // 方案调整员 (如果用户不同意)
            var revisionAgent = _chatClient.CreateAIAgent(
                instructions: "你是调整员。用户对方案不满意，请根据反馈调整维修方案，尽量降低预算或更换备件。",
                name: "RevisionSpecialist");

            // 维修技师 (如果用户同意)
            var repairAgent = _chatClient.CreateAIAgent(
                instructions: "你是技师。收到指令，开始执行维修逻辑。",
                name: "RepairTechnician");

            // 2. 手动构建工作流 (这是核心！)
            var builder = new WorkflowBuilder(diagnosticAgent); // 明确起点

            // 定义分支：如果诊断完，该往哪走？
            // 注意：在 MAF 中，人工干预通常是通过暂停执行，获取输入后再恢复。
            // 我们先建立逻辑路径：
            builder.AddEdge(diagnosticAgent, revisionAgent);
            builder.AddEdge(diagnosticAgent, repairAgent);
            builder.AddEdge(revisionAgent, diagnosticAgent); // 调整完再重新诊断

            // 标记哪些节点可以作为结果输出
            builder.WithOutputFrom(repairAgent);
            builder.WithOutputFrom(revisionAgent);

            var workflow = builder.Build();

            // 3. 执行工作流（带有人机交互的循环）
            // 3.1 准备输入
            var messages = new List<ChatMessage> { new(ChatRole.User, carIssue) };

            // 3.2. 启动流式运行 (注意这里使用 StreamAsync)
            await using var run = await InProcessExecution.StreamAsync(workflow, messages);

            // 3.3. 核心：必须发送一个指令让它“跑起来”
            // 即使没有新消息，也要发送一个空的 TurnToken 告诉引擎：你可以开始处理当前上下文了
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // 3.4. 读取事件流
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorInvokedEvent invoked)
                {
                    AnsiConsole.MarkupLine($"[grey]>>> 正在调用: {invoked.ExecutorId}[/]");
                }

                if (evt is AgentRunUpdateEvent update)
                {
                    // 实时打印 AI 的 Token
                    AnsiConsole.Write(update.Data?.ToString() ?? "");
                }

                if (evt is ExecutorCompletedEvent completed)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[green]✔ {completed.ExecutorId} 执行完毕[/]");
                }

                if (evt is WorkflowOutputEvent output)
                {
                    AnsiConsole.MarkupLine("[bold yellow]✨ 工作流全部完成[/]");
                    break;
                }
            }
        }

        public async Task ExecuteInteractiveWorkflowAsync(string carIssue)
        {
            // 1. 初始化模型客户端
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId);

            // 2. 定义 Agent 角色
            // 注意：在提示词中加入状态判断，防止 Agent 在跳转前反复啰嗦
            var diagnosticAgent = chatClient.CreateAIAgent(
                "你是诊断专家。基于故障提供方案和价格。如果用户已同意，请只回复'确认：准备转接技师'。", "DiagnosticExpert");

            var repairAgent = chatClient.CreateAIAgent(
                "你是维修技师。你现在收到了用户的正式同意，请列出维修步骤：1.拆卸 2.清洗 3.更换。", "RepairTechnician");

            var revisionAgent = chatClient.CreateAIAgent(
                "你是方案调整员。用户觉得贵或不同意，请提供更优惠的备选方案。", "RevisionSpecialist");

            // 3. 构建工作流并配置“门禁逻辑”
            var builder = new WorkflowBuilder(diagnosticAgent);

            // 【关键修复】：手动进行 null 检查，防止 source 为空的运行时报错
            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, repairAgent, msgs => {
                if (msgs == null || msgs.Count == 0) return false;

                // 查找历史记录中最后一次用户的反馈
                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                bool agreed = lastUserMsg?.Text?.Contains("同意") ?? false;

                if (agreed) Console.WriteLine("\n[逻辑分支] 用户已同意，路径开启：RepairTechnician");
                return agreed;
            });

            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, revisionAgent, msgs => {
                if (msgs == null || msgs.Count == 0) return false;

                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                bool disagreed = (lastUserMsg?.Text?.Contains("不同意") ?? false) ||
                                 (lastUserMsg?.Text?.Contains("贵") ?? false);

                if (disagreed) Console.WriteLine("\n[逻辑分支] 用户不满意，路径开启：RevisionSpecialist");
                return disagreed;
            });

            // 调整完后回到诊断（闭环）
            builder.AddEdge(revisionAgent, diagnosticAgent);

            // 标记输出点，确保每个 Agent 说话后都会触发 WorkflowOutputEvent 让流程暂停
            //builder.WithOutputFrom(diagnosticAgent);
            builder.WithOutputFrom(repairAgent);
            builder.WithOutputFrom(revisionAgent);

            var workflow = builder.Build();

            // 【重要】：messages 必须定义在 while 循环外，作为持续更新的对话列表
            var messages = new List<ChatMessage> { new(ChatRole.User, carIssue) };

            // 4. 交互主循环
            bool isComplete = false;
            string currentAgent = "";
            while (!isComplete)
            {
                await using var run = await InProcessExecution.StreamAsync(workflow, messages);
                await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

                bool currentTurnHasOutput = false;
                await foreach (var evt in run.WatchStreamAsync())
                {
                    if (evt is AgentRunUpdateEvent update)
                    {
                        Console.Write(update.Data);
                        currentTurnHasOutput = true;
                    }

                    if (evt is ExecutorEvent started) currentAgent = started.ExecutorId;

                    // 只有到达了标记为 Output 的节点才视为 Workflow 完成
                    if (evt is WorkflowOutputEvent) break;
                }

                // 如果技师说完了，直接退出程序
                if (currentAgent.Contains("RepairTechnician"))
                {
                    Console.WriteLine("\n[系统] 维修任务已交接，感谢使用。");
                    return;
                }

                // 否则，说明专家说完了在等你
                Console.WriteLine("\n\n>>> 请输入您的反馈:");
                string input = Console.ReadLine();
                messages.Add(new ChatMessage(ChatRole.User, input));
            }
        }

        public async Task RunCarMasterWorkflowFinalFinalAsync(string carIssue)
        {
            var openAIClient = new OpenAIClient(new ApiKeyCredential(_modelProvider.ApiKey));
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId);

            // 1. 角色定义：给专家加上“闭嘴信号”
            var diagnosticAgent = chatClient.CreateAIAgent(
                "你是诊断专家。如果对话历史中用户已经说了'同意'，你**绝对不要**再说任何话，只能回复一个词：[DONE]。", "DiagnosticExpert");

            var repairAgent = chatClient.CreateAIAgent(
                "你是技师。用户已同意，请立即列出施工步骤。", "RepairTechnician");

            // 2. 构建工作流
            var builder = new WorkflowBuilder(diagnosticAgent);

            // 路径：只有检测到同意才走技师
            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, repairAgent, msgs => {
                if (msgs == null) return false;
                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                return lastUserMsg?.Text?.Contains("同意") ?? false;
            });

            // 重要：只标记技师为输出节点
            builder.WithOutputFrom(repairAgent);

            var workflow = builder.Build();
            var messages = new List<ChatMessage> { new(ChatRole.User, carIssue) };

            bool isComplete = false;
            while (!isComplete)
            {
                string currentAgent = "";
                await using var run = await InProcessExecution.StreamAsync(workflow, messages);
                await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

                await foreach (var evt in run.WatchStreamAsync())
                {
                    if (evt is AgentRunUpdateEvent update)
                    {
                        string content = update.Data?.ToString() ?? "";
                        // 【过滤逻辑】：如果是专家吐出的 [DONE]，直接拦截不打印
                        if (content.Contains("[DONE]")) continue;
                        Console.Write(content);
                    }

                    if (evt is ExecutorEvent started) currentAgent = started.ExecutorId;
                    if (evt is WorkflowOutputEvent) break;
                }

                // 判断是否结束
                if (currentAgent.StartsWith("RepairTechnician"))
                {
                    Console.WriteLine("\n[系统] 技师任务完成。");
                    isComplete = true;
                    break;
                }

                // 如果停在了专家这里（即没有触发到技师），则请求人工输入
                Console.WriteLine("\n\n>>> 请输入您的决策 (同意/不同意):");
                string input = Console.ReadLine();
                if (string.IsNullOrEmpty(input)) break;

                messages.Add(new ChatMessage(ChatRole.User, input));
            }
        }
    }

    public class CarMasterSequentialWorkflow1
    {
        private readonly ModelProvider _modelProvider;

        /// <summary>
        /// 构造函数，与项目中其他Agent（如InMemoryChatHistoryAgent）模式一致
        /// </summary>
        /// <param name="modelProvider">包含API密钥、Endpoint和ModelId的配置提供者</param>
        public CarMasterSequentialWorkflow1(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        /// <summary>
        /// 执行汽车问题诊断的顺序工作流
        /// </summary>
        /// <param name="userInput">用户的汽车问题描述</param>
        /// <returns>表示异步操作的任务</returns>
        public async Task ExecuteAsync(string userInput)
        {
            // 1. 遵循项目模式创建ChatClient和Agent
            var chatClient = new OpenAIClient(
                    new ApiKeyCredential(_modelProvider.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) })
                .GetChatClient(_modelProvider.ModelId);

            // 2. 创建顺序工作流中的两个专业Agent
            var queryAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车查询专员",
                Description = "你是一名专业的汽车信息查询员。清晰理解用户问题，整理车辆品牌、型号、年份等信息并进行初步解答。"
            });

            var diagnosticAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车诊断专家",
                Description = "你是一名资深汽车维修专家。基于查询专员提供的报告，诊断可能的故障原因，并提供专业、务实的安全处理建议。"
            });

            // 3. 使用Spectre.Console美化输出流程
            AnsiConsole.Write(new Rule("[green]🚗 汽车大师工作流启动[/]").LeftJustified());
            AnsiConsole.WriteLine();

            // 4. 构建并执行顺序工作流
            var workflow = AgentWorkflowBuilder.BuildSequential(new[] { queryAgent, diagnosticAgent });
            var messages = new List<Microsoft.Extensions.AI.ChatMessage> { new(ChatRole.User, userInput) };

            await using var run = await InProcessExecution.StreamAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // 5. 流式处理事件并美化输出
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    // 工作流完成，输出最终诊断建议
                    var finalMessages = (List<Microsoft.Extensions.AI.ChatMessage>)outputEvent.Data!;
                    var finalOutput = finalMessages.LastOrDefault();

                    if (finalOutput != null)
                    {
                        AnsiConsole.WriteLine();
                        var panel = new Panel(finalOutput.Text)
                            .Header("[bold yellow]🔧 最终诊断建议[/]")
                            .Border(BoxBorder.Rounded)
                            .Padding(1, 1);
                        AnsiConsole.Write(panel);
                    }
                    break;
                }
            }

            AnsiConsole.Write(new Rule("[grey]工作流执行完毕[/]").LeftJustified());
        }
    }
}
