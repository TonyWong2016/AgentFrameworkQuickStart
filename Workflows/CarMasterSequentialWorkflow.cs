using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;
using System.ClientModel;

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
            var diagnosticAgent = chatClient.CreateAIAgent(instructions:"你是一名资深汽车维修专家，懂得关于汽车的大部分信息，机械素质，油耗，维修，保养，安全性，品牌保值率甚至补贴政策等等，你将收到查询专员整理的报告，请基于对该汽车做一个客观评价，告知用户该汽车是否值得买，是否有同品牌其他类型更值得推荐。这是给用户的最终回答，若用户无详细阐述的要求，回答应尽可能简短有效。"
                , name: "资深汽车维修专家");
            AnsiConsole.MarkupLine("[green]   ✅ 创建完成[/]\n");

            // 3. 构建并执行工作流
            AnsiConsole.MarkupLine("[yellow]3. 执行顺序工作流 (查询 -> 是否值得买)...[/]");
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
            var messages = new List<ChatMessage> { new(ChatRole.User, userInput) };

            await using var run = await InProcessExecution.StreamAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            // 5. 流式处理事件并美化输出
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    // 工作流完成，输出最终诊断建议
                    var finalMessages = (List<ChatMessage>)outputEvent.Data!;
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
