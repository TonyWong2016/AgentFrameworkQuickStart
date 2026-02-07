using Microsoft.Agents.AI.Workflows;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AgentFrameworkQuickStart.Workflows
{
    // 定义输入模型（可选）
    public record CarConsultationInput(string VehicleModel, string Issue, string[] Intentions);

    public class CarExpertWorkflowDemo
    {
        public async Task FirstCase()
        {
            // Create the executors
            Func<string, string> uppercaseFunc = s => s.ToUpperInvariant();
            var uppercase = uppercaseFunc.BindAsExecutor("UppercaseExecutor");

            ReverseTextExecutor reverse = new();

            // Build the workflow by connecting executors sequentially
            WorkflowBuilder builder = new(uppercase);
            builder.AddEdge(uppercase, reverse).WithOutputFrom(reverse);
            var workflow = builder.Build();

            // Execute the workflow with input data
            await using Run run = await InProcessExecution.RunAsync(workflow, "Hello, World!");
            foreach (WorkflowEvent evt in run.NewEvents)
            {
                if (evt is ExecutorCompletedEvent executorComplete)
                {
                    Console.WriteLine($"{executorComplete.ExecutorId}: {executorComplete.Data}");
                }
            }
        }
    }
}
