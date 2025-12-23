using Microsoft.Extensions.AI;
using System.Text.Json.Serialization;

namespace AgentFrameworkQuickStart.Models
{
    [JsonSerializable(typeof(ChatMessage))]
    internal partial class SourceGenerationContext : JsonSerializerContext
    {
    }
}
