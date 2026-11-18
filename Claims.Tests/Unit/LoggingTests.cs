using System.Diagnostics;
using System.Text.Json;
using Claims.Api;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Claims.Tests.Unit;

public class LoggingTests
{
    [Fact]
    public void Formatter_includes_trace_and_hides_framework_details()
    {
        var formatter = new TraceJsonFormatter();
        using var activity = new Activity("request").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var output = new StringWriter();
        var state = new[] { new KeyValuePair<string, object?>("AuditId", "event-1") };
        var appEntry = new LogEntry<KeyValuePair<string, object?>[]>(
            LogLevel.Error, "Claims", new EventId(1), state,
            new InvalidOperationException("private data"), (_, _) => "Audit failed");
        formatter.Write(appEntry, null, output);
        using var appLog = JsonDocument.Parse(output.ToString());
        Assert.Equal(activity.TraceId.ToString(), appLog.RootElement.GetProperty("TraceId").GetString());
        Assert.Equal("event-1", appLog.RootElement.GetProperty("State").GetProperty("AuditId").GetString());
        Assert.DoesNotContain("private data", output.ToString());

        output.GetStringBuilder().Clear();
        var frameworkEntry = new LogEntry<string>(
            LogLevel.Error, "Microsoft.Test", new EventId(2), "private data",
            null, (value, _) => value);
        formatter.Write(frameworkEntry, null, output);
        Assert.DoesNotContain("private data", output.ToString());
        Assert.Contains("Framework log event", output.ToString());

        activity.Stop();
        output.GetStringBuilder().Clear();
        formatter.Write(appEntry, null, output);
        using var backgroundLog = JsonDocument.Parse(output.ToString());
        Assert.Equal(32, backgroundLog.RootElement.GetProperty("TraceId").GetString()!.Length);
    }
}
