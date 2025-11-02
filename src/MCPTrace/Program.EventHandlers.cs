using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public partial class Program
{
    /// 새 프로세스가 시작될 때 호출되는 이벤트 핸들러입니다.
    private static void HandleProcessStart(ProcessTraceData data)
    {
        bool isTargetProcess = data.ProcessName.Equals(TargetProcName, StringComparison.OrdinalIgnoreCase);
        bool isChildOfTarget = TrackedPids.Contains(data.ParentID);

        if (isChildOfTarget || isTargetProcess)
        {
            string mcpNameTag = MCPRegistry.GetNameTag(data.ParentID);
            if (string.IsNullOrEmpty(mcpNameTag))
            {
                mcpNameTag = MCPRegistry.SetNameTag(data.ProcessID, Program.TargetProcName);
            }
            else if (mcpNameTag == Program.TargetProcName)
            {
                mcpNameTag = MCPRegistry.Submit(data.ProcessID, data.CommandLine);
            }
            else
            {
                mcpNameTag = MCPRegistry.SetNameTag(data.ProcessID, mcpNameTag);
            }

            TrackedPids.Add(data.ProcessID);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[PROCESS Start] " +
                              $"Time: {data.TimeStamp.ToLocalTime()}, " +
                              $"Name: {data.ProcessName}.exe, " +
                              $"PID: {data.ProcessID}, " +
                              $"Parent PID: {data.ParentID}, " +
                              $"Command Line: {data.CommandLine} ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"└─ MCP Name Tag: '{mcpNameTag}'");
            Console.ResetColor();

            // 명세서에 맞는 형식으로 Collector에 전송
            SendToCollector("Process", new
            {
                task = "Start",
                pid = data.ProcessID,
                pname = data.ProcessName,
                parent = new
                {
                    pid = data.ParentID,
                    name = GetParentProcessName(data.ParentID)
                },
                imageFilename = data.ImageFileName ?? "",
                commandLine = data.CommandLine ?? "",
                mcpTag = mcpNameTag
            });
        }
    }

    /// <summary>
    /// 프로세스가 중지될 때 호출되는 이벤트 핸들러입니다.
    /// </summary>
    private static void HandleProcessStop(ProcessTraceData data)
    {
        if (TrackedPids.Remove(data.ProcessID))
        {
            MCPRegistry.Remove(data.ProcessID);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[PROCESS Stop] Process stopped: {data.ProcessName} (PID: {data.ProcessID})");
            Console.ResetColor();

            // 명세서에 맞는 형식으로 Collector에 전송
            SendToCollector("Process", new
            {
                task = "Stop",
                pid = data.ProcessID,
                pname = data.ProcessName,
                parent = new
                {
                    pid = data.ParentID,
                    name = GetParentProcessName(data.ParentID)
                },
                imageFilename = data.ImageFileName ?? "",
                commandLine = ""
            });
        }
    }

    private static string GetParentProcessName(int parentPid)
    {
        try
        {
            var parent = System.Diagnostics.Process.GetProcessById(parentPid);
            return parent.ProcessName;
        }
        catch
        {
            return "unknown";
        }
    }

    /// <summary>
    /// 파일 읽기 이벤트에 대한 이벤트 핸들러입니다.
    /// </summary>
    private static void HandleFileIORead(FileIOReadWriteTraceData data)
    {
        if (TrackedPids.Contains(data.ProcessID))
        {
            string mcpTag = MCPRegistry.GetNameTag(data.ProcessID);

            // MCP 서버 프로세스만 표시 (TargetProcName이 아닌 다른 태그를 가진 경우)
            if (string.IsNullOrEmpty(mcpTag) || mcpTag.Equals(TargetProcName, StringComparison.OrdinalIgnoreCase))
                return;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[FILE Read] " +
                              $"Time: {data.TimeStamp.ToLocalTime()}, " +
                              $"PID: {data.ProcessID}, " +
                              $"File: {data.FileName}, " +
                              $"Size: {data.IoSize} bytes");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"└─ MCP Tag: '{mcpTag}'");
            Console.ResetColor();

            SendToCollector("FileIO", new
            {
                task = "Read",
                pid = data.ProcessID,
                pname = data.ProcessName,
                fileName = data.FileName ?? "",
                ioSize = data.IoSize,
                offset = data.Offset,
                mcpTag = mcpTag
            });
        }
    }

    /// <summary>
    /// 파일 쓰기 이벤트에 대한 이벤트 핸들러입니다.
    /// </summary>
    private static void HandleFileIOWrite(FileIOReadWriteTraceData data)
    {
        if (TrackedPids.Contains(data.ProcessID))
        {
            string mcpTag = MCPRegistry.GetNameTag(data.ProcessID);

            // MCP 서버 프로세스만 표시 (TargetProcName이 아닌 다른 태그를 가진 경우)
            if (string.IsNullOrEmpty(mcpTag) || mcpTag.Equals(TargetProcName, StringComparison.OrdinalIgnoreCase))
                return;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[FILE Write] " +
                              $"Time: {data.TimeStamp.ToLocalTime()}, " +
                              $"PID: {data.ProcessID}, " +
                              $"File: {data.FileName}, " +
                              $"Size: {data.IoSize} bytes");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"└─ MCP Tag: '{mcpTag}'");
            Console.ResetColor();

            SendToCollector("FileIO", new
            {
                task = "Write",
                pid = data.ProcessID,
                pname = data.ProcessName,
                fileName = data.FileName ?? "",
                ioSize = data.IoSize,
                offset = data.Offset,
                mcpTag = mcpTag
            });
        }
    }

    /// <summary>
    /// 파일 생성 이벤트에 대한 이벤트 핸들러입니다.
    /// </summary>
    private static void HandleFileIOCreate(FileIOCreateTraceData data)
    {
        if (TrackedPids.Contains(data.ProcessID))
        {
            string mcpTag = MCPRegistry.GetNameTag(data.ProcessID);

            // MCP 서버 프로세스만 표시 (TargetProcName이 아닌 다른 태그를 가진 경우)
            if (string.IsNullOrEmpty(mcpTag) || mcpTag.Equals(TargetProcName, StringComparison.OrdinalIgnoreCase))
                return;

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine($"[FILE Create] " +
                              $"Time: {data.TimeStamp.ToLocalTime()}, " +
                              $"PID: {data.ProcessID}, " +
                              $"File: {data.FileName}");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"└─ MCP Tag: '{mcpTag}'");
            Console.ResetColor();

            SendToCollector("FileIO", new
            {
                task = "Create",
                pid = data.ProcessID,
                pname = data.ProcessName,
                fileName = data.FileName ?? "",
                mcpTag = mcpTag
            });
        }
    }

    /// <summary>
    /// 파일 이름 변경 이벤트에 대한 동적 이벤트 핸들러입니다.
    /// </summary>
    private static void HandleFileIORenameDynamic(TraceEvent data)
    {
        int pid = data.ProcessID;

        if (TrackedPids.Contains(pid))
        {
            string mcpTag = MCPRegistry.GetNameTag(pid);

            // MCP 서버 프로세스만 표시 (TargetProcName이 아닌 다른 태그를 가진 경우)
            if (string.IsNullOrEmpty(mcpTag) || mcpTag.Equals(TargetProcName, StringComparison.OrdinalIgnoreCase))
                return;

            string fileName = "";
            string newFileName = "";

            try
            {
                // FileIORename 이벤트의 필드 추출 시도
                if (data.PayloadNames.Contains("FileName"))
                {
                    fileName = data.PayloadString(data.PayloadIndex("FileName")) ?? "";
                }
                if (data.PayloadNames.Contains("NewFileName"))
                {
                    newFileName = data.PayloadString(data.PayloadIndex("NewFileName")) ?? "";
                }
            }
            catch
            {
                // 필드 접근 실패 시 무시
            }

            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[FILE Rename] " +
                              $"Time: {data.TimeStamp.ToLocalTime()}, " +
                              $"PID: {pid}, " +
                              $"From: {fileName}, " +
                              $"To: {newFileName}");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"└─ MCP Tag: '{mcpTag}'");
            Console.ResetColor();

            SendToCollector("FileIO", new
            {
                task = "Rename",
                pid = pid,
                pname = data.ProcessName,
                fileName = fileName,
                newFileName = newFileName,
                mcpTag = mcpTag
            });
        }
    }
}