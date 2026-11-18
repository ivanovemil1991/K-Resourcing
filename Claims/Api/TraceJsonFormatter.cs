using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace Claims.Api;

/// <summary>Writes structured logs with a request trace or process correlation ID.</summary>
public sealed class TraceJsonFormatter() : ConsoleFormatter("trace-json")
{
    private static readonly string ProcessTraceId = ActivityTraceId.CreateRandom().ToString();

    public override void Write<TState>(
        in LogEntry<TState> entry, IExternalScopeProvider? scopes, TextWriter writer)
    {
        var applicationLog = entry.Category is "Program" or "Claims" ||
            entry.Category.StartsWith("Claims.", StringComparison.Ordinal);
        var record = new Dictionary<string, object?>
        {
            ["Timestamp"] = DateTimeOffset.UtcNow,
            ["LogLevel"] = entry.LogLevel.ToString(),
            ["Category"] = entry.Category,
            ["EventId"] = entry.EventId.Id,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? ProcessTraceId,
            ["Message"] = applicationLog ? entry.Formatter(entry.State, null) : "Framework log event"
        };
        if (entry.Exception is not null)
            record["ExceptionType"] = entry.Exception.GetType().Name;

        if (applicationLog && entry.State is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            var state = new Dictionary<string, string?>();
            foreach (var property in properties)
                state[property.Key] = property.Value?.ToString();
            record["State"] = state;
        }

        var scopeValues = new Dictionary<string, string?>();
        scopes?.ForEachScope((scope, values) =>
        {
            if (scope is not IEnumerable<KeyValuePair<string, object?>> properties)
                return;
            foreach (var property in properties)
                if (property.Key is "TraceId" or "SpanId" or "RequestId")
                    values[property.Key] = property.Value?.ToString();
        }, scopeValues);
        if (scopeValues.Count > 0)
            record["Scopes"] = scopeValues;

        writer.WriteLine(JsonSerializer.Serialize(record));
    }
}
