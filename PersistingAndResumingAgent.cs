using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace AgentFrameworkQuickStart
{
    public class PersistingAndResumingAgent
    {
        public readonly ModelProvider modelProvider;
        private readonly string threadFilePath;
        public PersistingAndResumingAgent(ModelProvider modelProvider)
        {
            this.modelProvider = modelProvider;
            this.threadFilePath = Path.Combine(Directory.GetCurrentDirectory(), "saved_agent_thread.json");
        }

        public async Task PersistAndResumeDemo()
        {
            AIAgent agent = new OpenAIClient(
                new ApiKeyCredential(modelProvider.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(modelProvider.Endpoint) })
                .GetChatClient(modelProvider.ModelId)
                .AsIChatClient()
                .AsAIAgent(instructions: "你是个脱口秀大师，可以很轻松的逗笑大家.", name: "脱口秀大师");


            // 2. 尝试从文件加载已有 session；若无，则新建
            AgentSession session;
            if (File.Exists(threadFilePath))
            {
                Console.WriteLine("🔍 检测到已保存的对话，正在恢复...");
                string json = await File.ReadAllTextAsync(threadFilePath);
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(json, JsonSerializerOptions.Web);
                session = await agent.DeserializeSessionAsync(jsonElement);
                Console.WriteLine("✅ 对话已恢复！");
            }
            else
            {
                Console.WriteLine("🆕 开始新对话...");
                session = await agent.CreateSessionAsync();
            }

            // 3. 获取用户输入并交互
            while (true)
            {
                Console.Write("\r\n 💬：请输入你的问题（输入 'exit' 退出，'clear' 清除历史）: ");
                string? input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input)) continue;

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    // 保存当前 session 后退出
                    var serialized = await agent.SerializeSessionAsync(session);
                    await File.WriteAllTextAsync(threadFilePath, serialized.GetRawText());
                    Console.WriteLine("💾 对话已保存，再见！");
                    break;
                }

                if (input.Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    // 清除历史：删除文件 + 新建 session
                    if (File.Exists(threadFilePath)) File.Delete(threadFilePath);
                    session = await agent.CreateSessionAsync();
                    Console.WriteLine("🧹 对话历史已清除，开启全新对话！");
                    continue;
                }

                // 4. 调用代理生成回复
                try
                {
                    var response = await agent.RunAsync(input, session);
                    Console.WriteLine($"\n🎭 脱口秀大师: {response}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ 发生错误: {ex.Message}");
                    continue;
                }

                // 5. 自动保存 session（每次交互后都保存，确保不丢上下文）
                var updatedJson = (await agent.SerializeSessionAsync(session)).GetRawText();
                await File.WriteAllTextAsync(threadFilePath, updatedJson);
            }
        }
    }
}