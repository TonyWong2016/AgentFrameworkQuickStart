using AgentFrameworkQuickStart;
using Microsoft.Extensions.Configuration;
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

var carAgent = new CarMasterAgent(modelProvider);
await carAgent.RunMasterWithToolsAsync();

public class ModelProvider
{
    public string ApiKey { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string Endpoint { get; init; } = string.Empty;
}