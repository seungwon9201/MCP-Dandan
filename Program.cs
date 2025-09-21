using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using Microsoft.Diagnostics.Tracing.Session;

namespace CursorProcessTree
{
    class Program
    {
        class ProcessInfo
        {
            public int Pid { get; set; }
            public int ParentPid { get; set; }
            public string Name { get; set; } = "";
            public string CmdLine { get; set; } = "";
        }

        // --- 관리용 맵 ---
        static readonly Dictionary<int, ProcessInfo> processMap = new();
        static readonly Dictionary<int, List<int>> treeMap = new();
        static readonly Dictionary<ulong, string> fileKeyPath = new();
        static readonly HashSet<int> mcpPids = new();

        // 타입 추론 결과 (PID -> type)
        static readonly ConcurrentDictionary<int, string> _pidTypeMap = new();

        // Root PID
        static int rootPid = -1;
        static string targetProcess = "";

        // 로그 파일
        static readonly string logPath = "etw_events_log.txt";
        static StreamWriter logWriter;
        static readonly object logLock = new();

        // 제외할 경로
        static readonly string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        static readonly string[] excludePrefixes = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cursor") + Path.DirectorySeparatorChar,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor") + Path.DirectorySeparatorChar,
            Path.Combine(userProfile, ".cursor", "extensions") + Path.DirectorySeparatorChar
        };

        static void Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("Enter target process name (e.g., Cursor.exe, Claude.exe): ");
            Console.ResetColor();
            targetProcess = Console.ReadLine()?.Trim() ?? "";

            if (string.IsNullOrEmpty(targetProcess))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[-] No process name entered. Exiting...");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[+] Monitoring {targetProcess}, MCP file + network events...");
            Console.WriteLine($"[+] Logging events to: {Path.GetFullPath(logPath)} (append mode)");
            Console.ResetColor();

            if (!(TraceEventSession.IsElevated() ?? false))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[-] Please run as Administrator!");
                Console.ResetColor();
                return;
            }

            logWriter = new StreamWriter(logPath, true, System.Text.Encoding.UTF8) { AutoFlush = true };

            using var session = new TraceEventSession("ProcessTreeSession");
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.Process |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.NetworkTCPIP |
                KernelTraceEventParser.Keywords.ImageLoad
            );

            // --- Process start ---
            session.Source.Kernel.ProcessStart += data =>
            {
                var info = new ProcessInfo
                {
                    Pid = data.ProcessID,
                    ParentPid = data.ParentID,
                    Name = data.ImageFileName ?? "",
                    CmdLine = data.CommandLine ?? ""
                };

                lock (processMap)
                {
                    processMap[info.Pid] = info;
                    if (!treeMap.ContainsKey(info.ParentPid))
                        treeMap[info.ParentPid] = new List<int>();
                    treeMap[info.ParentPid].Add(info.Pid);
                }

                // 타입 추론
                string inferredType = InferType(info.Name, info.CmdLine) ?? "unknown";
                _pidTypeMap[info.Pid] = inferredType;

                LogLine($"[Process Start] PID={info.Pid}, PPID={info.ParentPid}, Name={info.Name}, Type={inferredType}, Cmd={info.CmdLine}");

                if (rootPid == -1 &&
                    info.Name.Equals(targetProcess, StringComparison.OrdinalIgnoreCase))
                {
                    rootPid = info.Pid;
                    PrintProcessEvent("[ROOT FOUND]", info, inferredType, ConsoleColor.Cyan);
                }

                if (IsChildOfTarget(info.Pid))
                    PrintProcessEvent("[START]", info, inferredType, ConsoleColor.Green);
            };

            // --- Process stop ---
            session.Source.Kernel.ProcessStop += data =>
            {
                var pid = data.ProcessID;
                lock (processMap)
                {
                    if (processMap.TryGetValue(pid, out var info))
                    {
                        string typeLabel = _pidTypeMap.TryRemove(pid, out var storedType) ? storedType : "unknown";
                        LogLine($"[Process Stop ] PID={pid}, Name={info.Name}, Type={typeLabel}");

                        if (IsChildOfTarget(pid))
                            PrintProcessEvent("[EXIT]", info, typeLabel, ConsoleColor.DarkGray);

                        processMap.Remove(pid);
                        mcpPids.Remove(pid);
                    }
                }
            };

            // --- Image Load ---
            session.Source.Kernel.ImageLoad += data =>
            {
                try
                {
                    if (IsChildOfTarget(data.ProcessID))
                    {
                        _pidTypeMap.TryGetValue(data.ProcessID, out var knownType);
                        LogLine($"[Image Load   ] PID={data.ProcessID}, DLL={data.FileName}, Type={(knownType ?? "unknown")}");
                    }
                }
                catch { }
            };

            // --- File Write/Delete ---
            session.Source.Kernel.FileIOWrite += data =>
                HandleFileEvent("[WRITE]", data.ProcessID, data.FileName);
            session.Source.Kernel.FileIOFileDelete += data =>
                HandleFileEvent("[DELETE]", data.ProcessID, data.FileName);

            // --- File Rename ---
            session.Source.Kernel.FileIORename += data =>
            {
                if (!IsChildOfTarget(data.ProcessID)) return;

                var fk = TryGetULong(data, "FileKey") ?? 0UL;
                string newPath = data.FileName;
                string oldPath = null;

                if (fk != 0 && fileKeyPath.TryGetValue(fk, out var prev))
                    oldPath = prev;

                if (IsPathExcluded(oldPath) || IsPathExcluded(newPath)) return;

                int indent = GetIndentLevel(data.ProcessID);
                string spaces = new string(' ', indent * 2);

                Console.ForegroundColor = ConsoleColor.Yellow;
                if (!string.IsNullOrEmpty(oldPath))
                    Console.WriteLine($"{spaces}[RENAME] {oldPath} -> {newPath} (PID={data.ProcessID})");
                else
                    Console.WriteLine($"{spaces}[RENAME] (unknown_old) -> {newPath} (PID={data.ProcessID})");
                Console.ResetColor();

                LogLine($"[RENAME] PID={data.ProcessID}, {oldPath} -> {newPath}");

                if (fk != 0) fileKeyPath[fk] = newPath;
            };

            // --- Network events ---
            session.Source.Kernel.TcpIpSend += data =>
                HandleNetworkEvent("[TCP SEND]", data.ProcessID, data);
            session.Source.Kernel.TcpIpRecv += data =>
                HandleNetworkEvent("[TCP RECV]", data.ProcessID, data);
            session.Source.Kernel.UdpIpSend += data =>
                HandleNetworkEvent("[UDP SEND]", data.ProcessID, data);
            session.Source.Kernel.UdpIpRecv += data =>
                HandleNetworkEvent("[UDP RECV]", data.ProcessID, data);

            session.Source.Process();
        }

        // ---------------- Helpers ----------------

        static void LogLine(string line)
        {
            string tsLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            lock (logLock)
            {
                logWriter.WriteLine($"[{tsLocal}] {line}");
            }
        }

        static bool IsChildOfTarget(int pid)
        {
            if (rootPid == -1) return false;

            int current = pid;
            while (processMap.ContainsKey(current))
            {
                if (current == rootPid) return true;
                current = processMap[current].ParentPid;
            }
            return false;
        }

        static void HandleFileEvent(string eventType, int pid, string path)
        {
            if (!IsChildOfTarget(pid)) return;
            if (string.IsNullOrWhiteSpace(path)) return;

            string normPath = path.Replace('/', '\\');
            string fileName = Path.GetFileName(normPath);

            bool isMcpLog = fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) &&
                            fileName.IndexOf("MCP", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isMcpLog && IsPathExcluded(normPath)) return;

            if (eventType == "[WRITE]" && isMcpLog)
            {
                lock (mcpPids) mcpPids.Add(pid);
            }

            int indent = GetIndentLevel(pid);
            string spaces = new string(' ', indent * 2);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{spaces}{eventType} File {normPath} (PID={pid})");
            Console.ResetColor();

            LogLine($"{eventType} File {normPath} (PID={pid})");
        }

        static void HandleNetworkEvent(string eventType, int pid, dynamic data)
        {
            if (!IsChildOfTarget(pid)) return;

            lock (mcpPids)
            {
                if (!mcpPids.Contains(pid)) return;
            }

            string saddr = "", daddr = "";
            int sport = -1, dport = -1, size = -1;

            try
            {
                saddr = data.saddr?.ToString() ?? "";
                daddr = data.daddr?.ToString() ?? "";
                sport = Convert.ToInt32(data.sport);
                dport = Convert.ToInt32(data.dport);
                size = Convert.ToInt32(data.size);
            }
            catch { }

            int indent = GetIndentLevel(pid);
            string spaces = new string(' ', indent * 2);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"{spaces}{eventType} PID={pid} {saddr}:{sport} -> {daddr}:{dport}, Size={size}");
            Console.ResetColor();

            LogLine($"{eventType} PID={pid} {saddr}:{sport} -> {daddr}:{dport}, Size={size}");
        }

        static void PrintProcessEvent(string type, ProcessInfo info, string typeLabel, ConsoleColor color)
        {
            int indent = GetIndentLevel(info.Pid);
            string spaces = new string(' ', indent * 2);

            Console.ForegroundColor = color;
            Console.WriteLine($"{spaces}{type} {info.Name} (PID={info.Pid}, PPID={info.ParentPid}, Type={typeLabel})");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{spaces}   Cmd: {info.CmdLine}");
            Console.ResetColor();
        }

        static string InferType(string imageName, string cmd)
        {
            string lowerCmd = (cmd ?? "").ToLowerInvariant();
            string lowerName = Path.GetFileName(imageName ?? "").ToLowerInvariant();

            if (lowerName == "cmd.exe") return "cmd";
            if (lowerName == "conhost.exe") return "conhost";
            if (lowerName == "reg.exe") return "reg";
            if (lowerName == "git.exe") return "git";

            if (lowerCmd.Contains("--type=renderer")) return "renderer";
            if (lowerCmd.Contains("--type=gpu")) return "gpu";
            if (lowerCmd.Contains("crashpad")) return "crashpad";
            if (lowerCmd.Contains("network")) return "network";
            if (lowerCmd.Contains("node")) return "node";
            if (lowerCmd.Contains("--type=utility")) return "utility";
            if (lowerCmd.Contains("--type=zygote")) return "zygote";

            if (lowerCmd.Contains("--type="))
            {
                int idx = lowerCmd.IndexOf("--type=");
                int start = idx + 7;
                int end = lowerCmd.IndexOf(' ', start);
                if (end == -1) end = lowerCmd.Length;
                try
                {
                    string t = lowerCmd.Substring(start, end - start).Trim();
                    if (!string.IsNullOrEmpty(t) && t != "main")
                        return t;
                }
                catch { }
            }
            return null;
        }

        static bool IsPathExcluded(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            foreach (var prefix in excludePrefixes)
                if (path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        static ulong? TryGetULong(TraceEvent e, string name)
        {
            if (e.PayloadNames == null) return null;
            if (!e.PayloadNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                return null;
            try
            {
                var v = e.PayloadByName(name);
                return v switch
                {
                    ulong ul => ul,
                    long l => unchecked((ulong)l),
                    uint u => u,
                    int i => unchecked((ulong)i),
                    _ => null
                };
            }
            catch { return null; }
        }

        static int GetIndentLevel(int pid)
        {
            int depth = 0;
            while (processMap.ContainsKey(pid))
            {
                int parent = processMap[pid].ParentPid;
                if (!processMap.ContainsKey(parent)) break;
                pid = parent;
                depth++;
            }
            return depth;
        }
    }
}
