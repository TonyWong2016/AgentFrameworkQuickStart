using Microsoft.Agents.AI.Workflows;
using System;
using System.Collections.Generic;
using System.Text;

namespace AgentFrameworkQuickStart.Workflows
{
    public class SequentialFlow
    {
        public async Task Run()
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
            //await using Run run = await InProcessExecution.RunAsync(workflow, "Hello, World!");
            //foreach (WorkflowEvent evt in run.NewEvents)
            //{
            //    if (evt is ExecutorCompletedEvent executorComplete)
            //    {
            //        Console.WriteLine($"{executorComplete.ExecutorId}: {executorComplete.Data}");
            //    }
            //}
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, input: "Hello, World!");
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorCompletedEvent executorCompleted)
                {
                    //await Task.Delay(300);
                    Console.WriteLine($"{executorCompleted.ExecutorId}: {executorCompleted.Data}");
                }

            }
        }
    }

    /// <summary>
    /// Second executor: reverses the input text and completes the workflow.
    /// </summary>
    public sealed class ReverseTextExecutor() : Executor<string, string>("ReverseTextExecutor")
    {
        /// <summary>
        /// Processes the input message by reversing the text.
        /// </summary>
        /// <param name="message">The input text to reverse</param>
        /// <param name="context">Workflow context for accessing workflow services and adding events</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests.
        /// The default is <see cref="CancellationToken.None"/>.</param>
        /// <returns>The input text reversed</returns>
        public override ValueTask<string> HandleAsync(string message, IWorkflowContext context, CancellationToken cancellationToken = default)
        {
            // Because we do not suppress it, the returned result will be yielded as an output from this executor.
            return ValueTask.FromResult(string.Concat(message.Reverse()));
        }
    }
}
