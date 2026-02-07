using AgentFrameworkQuickStart;
using AgentFrameworkQuickStart.Workflows;
using Microsoft.Extensions.Configuration;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.Text;

Console.OutputEncoding = Encoding.UTF8; // 👈 关键！
var config = new ConfigurationBuilder()
    .AddJsonFile($"llm.json", optional: false, reloadOnChange: true)
    .Build();
var modelProvider = new ModelProvider()
{
    ApiKey = config["ModelProvider:ApiKey"] ?? string.Empty,
    ModelId = config["ModelProvider:ModelId"] ?? string.Empty,
    Endpoint = config["ModelProvider:Endpoint"] ?? string.Empty,
};
Console.WriteLine($"正在使用【${modelProvider.ModelId}】模型",ConsoleColor.Yellow);

//await new BaseAgent(modelProvider).TalkShowAgent();

//await new ObservabilityAgent(modelProvider).ObservabilityDemo();

//await new PersistingAndResumingAgent(modelProvider).PersistAndResumeDemo();

//var agent = new InMemoryChatHistoryAgent(modelProvider, "inmem_thread.json");
//await agent.RunInteractiveChatAsync();

//var carAgent = new CarMasterAgent(modelProvider);
//await carAgent.RunMasterStreamAsync();
//await carAgent.RunMasterWithToolsAsync();

// 2. 实例化工作流（与InMemoryChatHistoryAgent模式一致）
//var carMasterWorkflow = new CarMasterSequentialWorkflow(modelProvider);

//// 3. 使用Spectre.Console创建交互界面
//AnsiConsole.Write(new FigletText("Car Master").LeftJustified().Color(Color.Green));
//AnsiConsole.MarkupLine("[yellow]欢迎使用汽车大师智能助手！请描述您的车辆问题。[/]");
//AnsiConsole.MarkupLine("[grey](输入 'exit' 退出程序)[/]");

//while (true)
//{
//    var userQuestion = AnsiConsole.Ask<string>("[bold cyan]您的问题：[/]");
//    if (userQuestion.Equals("exit", StringComparison.OrdinalIgnoreCase)) break;

//    // 4. 执行工作流
//    await carMasterWorkflow.ExecuteAsync(userQuestion);
//    AnsiConsole.WriteLine(); // 空行分隔每次对话
//}

//简单工作流
//await new SequentialFlow().Run();
string input = Console.ReadLine()?? "帮我把这段话翻译成英语：老铁 666";
await new AgentsInWorkFlow(modelProvider).RunBranchingWorkflow(input);

public class ModelProvider
{
    public string ApiKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
}