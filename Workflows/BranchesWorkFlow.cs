using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using Spectre.Console;
using System;
using System.ClientModel;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentFrameworkQuickStart.Workflows
{

    

    public class BranchesWorkFlow
    {
        private readonly ModelProvider _modelProvider;

        public BranchesWorkFlow(ModelProvider modelProvider)
        {
            _modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
        }


        public async Task Run()
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Rule("[yellow]📧 邮件智能分类工作流启动中...[/]").RuleStyle("cyan").Centered());
            AnsiConsole.WriteLine();
            var chatClient = new OpenAIClient(
                 new ApiKeyCredential(_modelProvider.ApiKey),
                 new OpenAIClientOptions { Endpoint = new Uri(_modelProvider.Endpoint) }
             );

            //var chatClient = new AzureOpenAIClient(new Uri(_modelProvider.Endpoint), new ApiKeyCredential(_modelProvider.ApiKey));
            // Create agents
            AIAgent spamDetectionAgent = GetSpamDetectionAgent(chatClient.GetChatClient(_modelProvider.ModelId).AsIChatClient());
            AIAgent emailAssistantAgent = GetEmailAssistantAgent(chatClient.GetChatClient(_modelProvider.ModelId).AsIChatClient());
            // Create executors
            var spamDetectionExecutor = new SpamDetectionExecutor(spamDetectionAgent);
            var emailAssistantExecutor = new EmailAssistantExecutor(emailAssistantAgent);
            var sendEmailExecutor = new SendEmailExecutor();
            var handleSpamExecutor = new HandleSpamExecutor();

            // Build the workflow by adding executors and connecting them
            var workflow = new WorkflowBuilder(spamDetectionExecutor)
                .AddEdge(spamDetectionExecutor, emailAssistantExecutor, condition: GetCondition(expectedResult: false))
                .AddEdge(emailAssistantExecutor, sendEmailExecutor)
                .AddEdge(spamDetectionExecutor, handleSpamExecutor, condition: GetCondition(expectedResult: true))
                .WithOutputFrom(handleSpamExecutor, sendEmailExecutor)
                .Build();

            // Read a email from a text file
            //string email = "对于外贸从业者来说，海关数据是非常宝贵的信息资源，但是你真的会用它找到精准客户吗？\r\n\r\n网易外贸通的「海关数据」结合网易AI大模型后，能够深度分析市场供需行情，直接在海关数据中找到商机，并匹配客户最新联系方式！点击这里，我将为您演示网易外贸通海关数据与普通海关数据开发客户的区别>> 文末可免费获取定制版《2026精准采购/供应商报告》";
            //string email = "Congratulations! You've won $1,000,000! Click here to claim your prize now!";
            string email = "Hi, I wanted to follow up on our meeting yesterday and get your thoughts on the project proposal.";
            // Execute the workflow
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, email));
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    AnsiConsole.MarkupLine($"[cyan]{outputEvent}[/]");
                }
            }
        }

        private static Func<object?, bool> GetCondition(bool expectedResult) =>
    detectionResult => detectionResult is DetectionResult result && result.IsSpam == expectedResult;

        

        /// <summary>
        /// Creates a spam detection agent.
        /// </summary>
        /// <returns>A ChatClientAgent configured for spam detection</returns>
        private static ChatClientAgent GetSpamDetectionAgent(IChatClient chatClient) =>
            new(chatClient, new ChatClientAgentOptions()
            {                
                ChatOptions = new()
                {
                    Instructions= "You are a spam detection assistant that identifies spam emails.",
                    ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(DetectionResult)))
                }
            });

        /// <summary>
        /// Creates an email assistant agent.
        /// </summary>
        /// <returns>A ChatClientAgent configured for email assistance</returns>
        private static ChatClientAgent GetEmailAssistantAgent(IChatClient chatClient) =>
            new(chatClient, new ChatClientAgentOptions()
            {
                ChatOptions = new()
                {
                    Instructions = "You are an email assistant that helps users draft professional responses to emails.",
                    ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(EmailResponse)))
                }
            });
    }

    /// <summary>
    /// Executor that detects spam using an AI agent.
    /// </summary>
    internal sealed class SpamDetectionExecutor : Executor<Microsoft.Extensions.AI.ChatMessage, DetectionResult>
    {
        private readonly AIAgent _spamDetectionAgent;

        /// <summary>
        /// Creates a new instance of the <see cref="SpamDetectionExecutor"/> class.
        /// </summary>
        /// <param name="spamDetectionAgent">The AI agent used for spam detection</param>
        public SpamDetectionExecutor(AIAgent spamDetectionAgent) : base("SpamDetectionExecutor")
        {
            this._spamDetectionAgent = spamDetectionAgent;
        }

        public override async ValueTask<DetectionResult> HandleAsync(Microsoft.Extensions.AI.ChatMessage message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            AnsiConsole.MarkupLine("[cyan]🔍 垃圾邮件检测开始处理邮件内容：[/]{0}", message.Text);
            // Generate a random email ID and store the email content to the shared state
            var newEmail = new Email
            {
                EmailId = Guid.NewGuid().ToString("N"),
                EmailContent = message.Text
            };
            await context.QueueStateUpdateAsync(newEmail.EmailId, newEmail, scopeName: EmailStateConstants.EmailStateScope, cancellationToken);
            AnsiConsole.MarkupLine("[cyan]💾 状态存储已保存邮件到共享状态，ID：[dim]{0}[/][/]", newEmail.EmailId);
            // Invoke the agent
            var response = await this._spamDetectionAgent.RunAsync(message, cancellationToken: cancellationToken);

            AnsiConsole.MarkupLine("[yellow]🤖 AI 响应 垃圾邮件检测模型原始输出：[/]{0}", response.Text);

            var detectionResult = JsonSerializer.Deserialize<DetectionResult>(response.Text);
            detectionResult!.EmailId = newEmail.EmailId;

            string spamStatus = detectionResult.IsSpam ? "[red]是[/]" : "[green]否[/]";
            AnsiConsole.MarkupLine("✅ [yellow]检测结果 是否为垃圾邮件：{0}，原因：{1}[/]", spamStatus, detectionResult.Reason);
            return detectionResult;
        }
    }

    /// <summary>
    /// Executor that assists with email responses using an AI agent.
    /// </summary>
    internal sealed class EmailAssistantExecutor : Executor<DetectionResult, EmailResponse>
    {
        private readonly AIAgent _emailAssistantAgent;

        /// <summary>
        /// Creates a new instance of the <see cref="EmailAssistantExecutor"/> class.
        /// </summary>
        /// <param name="emailAssistantAgent">The AI agent used for email assistance</param>
        public EmailAssistantExecutor(AIAgent emailAssistantAgent) : base("EmailAssistantExecutor")
        {
            this._emailAssistantAgent = emailAssistantAgent;
        }

        public override async ValueTask<EmailResponse> HandleAsync(DetectionResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            if (message.IsSpam)
            {
                throw new InvalidOperationException("This executor should only handle non-spam messages.");
            }
            AnsiConsole.MarkupLine("[cyan]📬 邮件助手正在处理非垃圾邮件，ID：{0}[/]", message.EmailId);

            // Retrieve the email content from the shared state
            var email = await context.ReadStateAsync<Email>(message.EmailId, scopeName: EmailStateConstants.EmailStateScope, cancellationToken)
                ?? throw new InvalidOperationException("Email not found.");

            AnsiConsole.MarkupLine("[cyan]📄 邮件内容读取到原始邮件：[/]{0}", email.EmailContent);

            // Invoke the agent
            var response = await this._emailAssistantAgent.RunAsync(email.EmailContent, cancellationToken: cancellationToken);
            AnsiConsole.MarkupLine("[blue]🤖 AI 响应邮件助手生成的回复草稿：[/]{0}", response.Text);

            var emailResponse = JsonSerializer.Deserialize<EmailResponse>(response.Text);

            return emailResponse!;
        }
    }

    /// <summary>
    /// Executor that sends emails.
    /// </summary>
    internal sealed class SendEmailExecutor() : Executor<EmailResponse>("SendEmailExecutor")
    {
        /// <summary>
        /// Simulate the sending of an email.
        /// </summary>
        //public override async ValueTask HandleAsync(EmailResponse message, IWorkflowContext context, CancellationToken cancellationToken = default) =>
        //    await context.YieldOutputAsync($"Email sent: {message.Response}", cancellationToken);
        public override async ValueTask HandleAsync(EmailResponse message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            AnsiConsole.MarkupLine("[green]📤 发送邮件准备发送回复：[/]{0}", message.Response);
            await context.YieldOutputAsync($"邮件已发送：{message.Response}", cancellationToken);
        }
    }

    /// <summary>
    /// Executor that handles spam messages.
    /// </summary>
    internal sealed class HandleSpamExecutor() : Executor<DetectionResult>("HandleSpamExecutor")
    {
        /// <summary>
        /// Simulate the handling of a spam message.
        /// </summary>
        //public override async ValueTask HandleAsync(DetectionResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
        //{
        //    if (message.IsSpam)
        //    {
        //        await context.YieldOutputAsync($"Email marked as spam: {message.Reason}", cancellationToken);
        //    }
        //    else
        //    {
        //        throw new InvalidOperationException("This executor should only handle spam messages.");
        //    }
        //}
        public override async ValueTask HandleAsync(DetectionResult message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            if (!message.IsSpam)
            {
                throw new InvalidOperationException("此执行器仅处理垃圾邮件。");
            }

            AnsiConsole.MarkupLine("[red]🗑️ 垃圾邮件处理标记邮件为垃圾邮件，原因：[italic]{0}[/][/]", message.Reason);
            await context.YieldOutputAsync($"邮件被标记为垃圾邮件：{message.Reason}", cancellationToken);
        }
    }

    /// <summary>
    /// Represents the result of spam detection.
    /// </summary>
    public sealed class DetectionResult
    {
        [JsonPropertyName("is_spam")]
        public bool IsSpam { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        // Email ID is generated by the executor, not the agent
        [JsonIgnore]
        public string EmailId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an email.
    /// </summary>
    internal sealed class Email
    {
        [JsonPropertyName("email_id")]
        public string EmailId { get; set; } = string.Empty;

        [JsonPropertyName("email_content")]
        public string EmailContent { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents the response from the email assistant.
    /// </summary>
    public sealed class EmailResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }

    /// <summary>
    /// Constants for shared state scopes.
    /// </summary>
    internal static class EmailStateConstants
    {
        public const string EmailStateScope = "EmailState";
    }
}
