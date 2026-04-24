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
            // 1. 创建ChatClient
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );

            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // 2. 分步创建Agent，并给出明确调试信息
            AnsiConsole.Write(new Rule("[bold green]🚗 汽车大师工作流启动[/]").LeftJustified());
            AnsiConsole.MarkupLine($"[grey]诊断问题:[/] {userInput}");
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[yellow]1. 正在创建「汽车查询专员」...[/]");
            var queryAgent = chatClient.AsAIAgent(
                instructions: "你是一名专业的汽车信息查询专员。请清晰理解用户关于车辆的问题，整理出品牌、型号、年份等关键信息，并进行初步分类和解答。你的回答将为诊断专家提供基础。"
                , name: "汽车查询专员");

            AnsiConsole.MarkupLine($"[green]   ✅ 「{queryAgent.Name}」创建完成[/]\n");

            AnsiConsole.MarkupLine("[yellow]2. 正在创建「汽车诊断专家」...[/]");
            var diagnosticAgent = chatClient.AsAIAgent(instructions: "你是一名资深汽车维修专家，懂得关于汽车的大部分信息，机械素质，油耗，维修，保养，安全性，品牌保值率甚至补贴政策等等，你将收到查询专员整理的报告，请基于对该汽车做一个客观评价，告知用户该汽车在后续驾驶过程中有哪些需要注意的地方。这是给用户的最终回答，若用户无详细阐述的要求，回答应尽可能简短有效。"
                , name: "资深汽车维修专家");
            AnsiConsole.MarkupLine("[green]   ✅ 创建完成[/]\n");

            // 3. 构建并执行工作流
            AnsiConsole.MarkupLine("[yellow]3. 执行顺序工作流 (查询 -> 驾驶建议)...[/]");
            var workflow = new WorkflowBuilder(queryAgent)
                .AddEdge(queryAgent, diagnosticAgent)
                .WithOutputFrom(diagnosticAgent)
                .Build();
            var messages = new List<ChatMessage> { new(ChatRole.User, userInput) };

            await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            List<Microsoft.Extensions.AI.ChatMessage> result = new();
            // 4. 处理事件流
            await foreach (var evt in run.WatchStreamAsync().ConfigureAwait(false))
            {
                if (evt is AgentResponseUpdateEvent agentUpdate)
                {
                    AnsiConsole.Write($"{agentUpdate.Data}");
                }

                if (evt is WorkflowOutputEvent outputEvent)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine("[green]   ✅ 所有步骤执行完毕[/]");
                    AnsiConsole.WriteLine();

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
                else if (evt is WorkflowErrorEvent workflowError)
                {
                    AnsiConsole.MarkupLine($"[red]工作流错误: {workflowError.Exception?.Message}[/]");
                    break;
                }
                else if (evt is ExecutorFailedEvent executorFailed)
                {
                    AnsiConsole.MarkupLine($"[red]执行器 '{executorFailed.ExecutorId}' 失败: {executorFailed.Data}[/]");
                    break;
                }
            }

            AnsiConsole.Write(new Rule("[grey]诊断流程结束[/]").LeftJustified());
            AnsiConsole.WriteLine();
        }

        public async Task ExecuteInteractiveWorkflowAsync(string carIssue)
        {
            // 1. 初始化模型客户端
            var openAIClient = new OpenAIClient(
                new ApiKeyCredential(_modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
            );
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // 2. 定义 Agent 角色
            var diagnosticAgent = chatClient.AsAIAgent(
                "你是诊断专家。基于故障提供方案和价格。如果用户已同意，请只回复'确认：准备转接技师'。", "DiagnosticExpert");

            var repairAgent = chatClient.AsAIAgent(
                "你是维修技师。你现在收到了用户的正式同意，请列出维修步骤：1.拆卸 2.清洗 3.更换。", "RepairTechnician");

            var revisionAgent = chatClient.AsAIAgent(
                "你是方案调整员。用户觉得贵或不同意，请提供更优惠的备选方案。", "RevisionSpecialist");

            // 3. 构建工作流并配置"门禁逻辑"
            var builder = new WorkflowBuilder(diagnosticAgent);

            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, repairAgent, msgs =>
            {
                if (msgs == null || msgs.Count == 0) return false;

                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                bool agreed = lastUserMsg?.Text?.Contains("同意") ?? false;

                if (agreed) Console.WriteLine("\n[逻辑分支] 用户已同意，路径开启：RepairTechnician");
                return agreed;
            });

            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, revisionAgent, msgs =>
            {
                if (msgs == null || msgs.Count == 0) return false;

                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                bool disagreed = (lastUserMsg?.Text?.Contains("不同意") ?? false) ||
                                 (lastUserMsg?.Text?.Contains("贵") ?? false);

                if (disagreed) Console.WriteLine("\n[逻辑分支] 用户不满意，路径开启：RevisionSpecialist");
                return disagreed;
            });

            // 调整完后回到诊断（闭环）
            builder.AddEdge(revisionAgent, diagnosticAgent);

            builder.WithOutputFrom(repairAgent);
            builder.WithOutputFrom(revisionAgent);

            var workflow = builder.Build();

            var messages = new List<ChatMessage> { new(ChatRole.User, carIssue) };

            // 4. 交互主循环
            bool isComplete = false;
            string currentAgent = "";
            while (!isComplete)
            {
                await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
                await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

                await foreach (var evt in run.WatchStreamAsync())
                {
                    if (evt is AgentResponseUpdateEvent update)
                    {
                        Console.Write(update.Data);
                    }

                    if (evt is ExecutorEvent started) currentAgent = started.ExecutorId;

                    if (evt is WorkflowOutputEvent) break;
                }

                if (currentAgent.Contains("RepairTechnician"))
                {
                    Console.WriteLine("\n[系统] 维修任务已交接，感谢使用。");
                    return;
                }

                Console.WriteLine("\n\n>>> 请输入您的反馈:");
                string input = Console.ReadLine();
                messages.Add(new ChatMessage(ChatRole.User, input));
            }
        }

        public async Task RunCarMasterWorkflowFinalFinalAsync(string carIssue)
        {
            var openAIClient = new OpenAIClient(new ApiKeyCredential(_modelProvider.ApiKey));
            var chatClient = openAIClient.GetChatClient(_modelProvider.ModelId).AsIChatClient();

            // 1. 角色定义
            var diagnosticAgent = chatClient.AsAIAgent(
                "你是诊断专家。如果对话历史中用户已经说了'同意'，你**绝对不要**再说任何话，只能回复一个词：[DONE]。", "DiagnosticExpert");

            var repairAgent = chatClient.AsAIAgent(
                "你是技师。用户已同意，请立即列出施工步骤。", "RepairTechnician");

            // 2. 构建工作流
            var builder = new WorkflowBuilder(diagnosticAgent);

            builder.AddEdge<List<ChatMessage>>(diagnosticAgent, repairAgent, msgs =>
            {
                if (msgs == null) return false;
                var lastUserMsg = msgs.LastOrDefault(m => m.Role == ChatRole.User);
                return lastUserMsg?.Text?.Contains("同意") ?? false;
            });

            builder.WithOutputFrom(repairAgent);

            var workflow = builder.Build();
            var messages = new List<ChatMessage> { new(ChatRole.User, carIssue) };

            bool isComplete = false;
            while (!isComplete)
            {
                string currentAgent = "";
                await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
                await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

                await foreach (var evt in run.WatchStreamAsync())
                {
                    if (evt is AgentResponseUpdateEvent update)
                    {
                        string content = update.Data?.ToString() ?? "";
                        if (content.Contains("[DONE]")) continue;
                        Console.Write(content);
                    }

                    if (evt is ExecutorEvent started) currentAgent = started.ExecutorId;
                    if (evt is WorkflowOutputEvent) break;
                }

                if (currentAgent.StartsWith("RepairTechnician"))
                {
                    Console.WriteLine("\n[系统] 技师任务完成。");
                    isComplete = true;
                    break;
                }

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

        public CarMasterSequentialWorkflow1(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }

        public async Task ExecuteAsync(string userInput)
        {
            var chatClient = new OpenAIClient(
                    new ApiKeyCredential(_modelProvider.ApiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) })
                .GetChatClient(_modelProvider.ModelId)
                .AsIChatClient();

            // 2. 创建顺序工作流中的两个专业Agent
            var queryAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车查询专员",
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是一名专业的汽车信息查询员。清晰理解用户问题，整理车辆品牌、型号、年份等信息并进行初步解答。"
                }
            });

            var diagnosticAgent = chatClient.AsAIAgent(new ChatClientAgentOptions
            {
                Name = "汽车诊断专家",
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是一名资深汽车维修专家。基于查询专员提供的报告，诊断可能的故障原因，并提供专业、务实的安全处理建议。"
                }
            });

            // 3. 使用Spectre.Console美化输出流程
            AnsiConsole.Write(new Rule("[green]🚗 汽车大师工作流启动[/]").LeftJustified());
            AnsiConsole.WriteLine();

            // 4. 构建并执行顺序工作流
            var workflow = new WorkflowBuilder(queryAgent)
                .AddEdge(queryAgent, diagnosticAgent)
                .WithOutputFrom(diagnosticAgent)
                .Build();
            var messages = new List<Microsoft.Extensions.AI.ChatMessage> { new(ChatRole.User, userInput) };

            await using var run = await InProcessExecution.RunStreamingAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // 5. 流式处理事件并美化输出
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
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