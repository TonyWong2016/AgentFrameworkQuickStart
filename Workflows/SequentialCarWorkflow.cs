using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;

namespace AgentFrameworkQuickStart.Workflows
{
    public class SequentialCarWorkflow : BaseAgent
    {
        public SequentialCarWorkflow(ModelProvider modelProvider) : base(modelProvider) { }

        public async Task RunSequentialWorkflowAsync()
        {
            var chatClient = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId);

            // --- 1. 定义第一个 Agent：车型专家 ---
            var carExpert = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "CarExpert",
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是一个精通车型的专家。根据用户预算推荐1款最合适的车，并给出理由。输出后请说：[DONE_CAR]"
                }
            });

            // --- 2. 定义第二个 Agent：金融顾问 ---
            var financeAdvisor = chatClient.CreateAIAgent(new ChatClientAgentOptions
            {
                Name = "FinanceAdvisor",
                ChatOptions = new ChatOptions
                {
                    Instructions = "你是一个汽车金融专家。根据前面专家推荐的车型，计算一个大致的首付（30%）和月供方案。输出后请说：[DONE_FINANCE]"
                }
            });

            // --- 3. 构建顺序工作流 (AgentGroupChat) ---
            // 注意：文档中提到的模式通常涉及 SelectionStrategy (谁接下来说话)
            // 这里我们使用 SequentialSelectionStrategy，它会按列表顺序依次执行
            var chat = new AgentGroupChat(carExpert, financeAdvisor)
            {
                ExecutionSettings = new()
                {
                    SelectionStrategy = new SequentialSelectionStrategy()
                }
            };

            // --- 4. 运行工作流 ---
            string userInput = "我有30万预算，想买个家庭SUV，看重安全性。";
            Console.WriteLine($"[用户需求]: {userInput}\n");

            // 添加初始消息
            chat.AddChatMessage(new ChatMessage(ChatRole.User, userInput));

            // 循环处理，直到工作流完成
            // 在顺序流中，每个 Agent 会轮流说一次
            await foreach (var response in chat.InvokeAsync())
            {
                Console.WriteLine("--------------------------------------");
                Console.WriteLine($"[{response.AgentName}]:");
                Console.WriteLine(response.Content);
            }

            Console.WriteLine("\n--- 工作流执行完毕 ---");
        }
    }
}
