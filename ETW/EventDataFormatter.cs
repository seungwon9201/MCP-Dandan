using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Linq;
using System.Text.Json;

namespace ETW
{
    public static class EventDataFormatter
    {
        // Converts a TraceEvent into a standard JSON object based on the event type.
        public static string ToStandardJson(TraceEvent data, string eventType)
        {
            long tsNano = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000L;

            int pid = data.ProcessID;
            string pname = data.ProcessName ?? string.Empty;
            // Retrieve the recorded command line for this PID, if available
            string cmd = ProcessTracker.ProcCmdline.TryGetValue(pid, out var recordedCmd) ? recordedCmd : null;

            var result = new
            {
                ts = tsNano,
                producer = "agent-core",
                pid = pid,
                pname = pname,
                eventType = eventType,
                data = eventType switch
                {
                    "Process" => BuildProcess(data, pid, pname, cmd),
                    "File" => BuildFile(data, pid),
                    "MCP" => BuildMcp(data, pid, pname, cmd),
                    _ => new { }
                }
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        }

        // Builds the payload for Process events (Start/Stop)
        private static object BuildProcess(TraceEvent data, int pid, string pname, string cmd)
        {
            var proc = data as ProcessTraceData;
            int parentPid = proc?.ParentID ?? -1;
            string parentName = ProcessTracker.TrackedPids.TryGetValue(parentPid, out var pName) ? pName : "<unknown>";
            string imageFilename = proc?.ImageFileName ?? string.Empty;
            return new
            {
                task = data.EventName.Contains("Start") ? "Start" : "Stop",
                pid = pid,
                pname = pname,
                parent = new { pid = parentPid, name = parentName },
                imageFilename = imageFilename,
                commandLine = cmd ?? string.Empty,
                mcpTag = McpHelper.DetermineMcp(pid, cmd, null)
            };
        }

        // Builds the payload for File events (Read/Write/Delete/etc.)
        private static object BuildFile(TraceEvent data, int pid)
        {
            string filePath = string.Empty;
            // Extract file path from payload if present
            if (data.PayloadNames.Contains("FileName"))
            {
                filePath = data.PayloadByName("FileName")?.ToString() ?? string.Empty;
            }
            // Determine the MCP tag for this file event
            string mcpTag = string.Empty;
            if (ProcessTracker.ProcCmdline.TryGetValue(pid, out var tag))
            {
                mcpTag = McpHelper.DetermineMcp(pid, tag, filePath);
            }
            return new
            {
                task = data.EventName.ToUpperInvariant(),
                pid = pid,
                filePath = filePath,
                mcpTag = mcpTag
            };
        }

        // Builds the payload for MCP/network events (Send/Recv/Connect)
        private static object BuildMcp(TraceEvent data, int pid, string pname, string cmd)
        {
            var net = data as TcpIpTraceData;
            string mcpTag = McpHelper.DetermineMcp(pid, cmd, null);
            // Determine task based on event name
            string task;
            if (data.EventName.Contains("Send"))
                task = "SEND";
            else if (data.EventName.Contains("Recv"))
                task = "RECV";
            else
                task = "CONNECT";

            return new
            {
                task = task,
                pid = pid,
                pname = pname,
                mcpTag = mcpTag,
                transPort = "tcp",
                src = net?.saddr?.ToString() ?? string.Empty,
                sport = net?.sport ?? 0,
                dst = net?.daddr?.ToString() ?? string.Empty,
                dport = net?.dport ?? 0,
                bytes = net?.size ?? 0
            };
        }
    }
}
