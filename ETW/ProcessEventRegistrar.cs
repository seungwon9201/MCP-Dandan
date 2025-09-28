using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;

namespace ETW
{
    public static class ProcessEventRegistrar
    {
        public static void Register(TraceEventSource source)
        {
            // -------------------------------
            // 프로세스 이벤트
            // -------------------------------
            source.Kernel.ProcessStart += ev =>
            {
                string cmdline = ev.CommandLine ?? ProcessHelper.TryGetCommandLineForPid(ev.ProcessID);

                if (ev.ImageFileName != null &&
                    ev.ImageFileName.EndsWith(ProcessTracker.TargetProcName, StringComparison.OrdinalIgnoreCase))
                {
                    // Claude 메인 프로세스
                    ProcessTracker.RootPid = ev.ProcessID;
                    ProcessTracker.TrackedPids[ev.ProcessID] = ev.ImageFileName;
                    ProcessTracker.ProcCmdline[ev.ProcessID] = McpHelper.TagFromCommandLine(cmdline);

                    string runtime = ProcessHelper.GuessRuntime(ev.ImageFileName, cmdline);

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[PROC START] PID={ev.ProcessID} Runtime={runtime} {ev.ImageFileName} CMD={cmdline}");
                    Console.ResetColor();
                }
                else if (ProcessTracker.RootPid > 0 && ProcessTracker.TrackedPids.ContainsKey(ev.ParentID))
                {
                    // Claude 자손 프로세스 (자식, 손자, 증손자 포함)
                    ProcessTracker.TrackedPids[ev.ProcessID] = ev.ImageFileName;
                    ProcessTracker.ProcCmdline[ev.ProcessID] = McpHelper.TagFromCommandLine(cmdline);

                    string runtime = ProcessHelper.GuessRuntime(ev.ImageFileName, cmdline);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[MCP CHILD] PID={ev.ProcessID} Parent={ev.ParentID} Runtime={runtime} {ev.ImageFileName} CMD={cmdline}");
                    Console.ResetColor();

                    // MCP 후보 프로세스 → 상세 덤프
                    ProcessInspector.DumpProcessDetails(ev.ProcessID, "child-start");
                }
            };

            source.Kernel.ProcessStop += ev =>
            {
                if (ProcessTracker.TrackedPids.TryRemove(ev.ProcessID, out _))
                {
                    ProcessTracker.ProcCmdline.TryRemove(ev.ProcessID, out _);
                    ProcessTracker.LastResolvedHostByPid.TryRemove(ev.ProcessID, out _);

                    if (ev.ProcessID == ProcessTracker.RootPid)
                        ProcessTracker.RootPid = -1; // Claude 메인 종료 시 초기화

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[PROC STOP] PID={ev.ProcessID}");
                    Console.ResetColor();
                }
            };

            // -------------------------------
            // 파일 I/O 이벤트
            // -------------------------------
            source.Kernel.FileIOFileCreate += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("CREATE", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            source.Kernel.FileIOWrite += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("WRITE", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            source.Kernel.FileIOFileDelete += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("DELETE", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            // -------------------------------
            // 네트워크 이벤트
            // -------------------------------
            source.Kernel.TcpIpConnect += ev =>
            {
                if (!ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID)) return;

                string saddr = ev.saddr?.ToString() ?? "";
                string daddr = ev.daddr?.ToString() ?? "";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NET-CONNECT] PID={ev.ProcessID} -> {daddr}:{ev.dport}");
                Console.ResetColor();

                // MCP 가능성: 루프백 통신 시 상세 덤프
                if (saddr == "127.0.0.1" || daddr == "127.0.0.1" ||
                    saddr == "::1" || daddr == "::1")
                {
                    ProcessInspector.DumpProcessDetails(ev.ProcessID, "loopback-connect");
                }
            };

            source.Kernel.TcpIpSend += ev =>
            {
                if (!ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID)) return;

                string daddr = ev.daddr?.ToString() ?? "";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NET-SEND] PID={ev.ProcessID} -> {daddr}:{ev.dport} Bytes={ev.size}");
                Console.ResetColor();
            };

            source.Kernel.TcpIpRecv += ev =>
            {
                if (!ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID)) return;

                string saddr = ev.saddr?.ToString() ?? "";

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NET-RECV] PID={ev.ProcessID} <- {saddr}:{ev.sport} Bytes={ev.size}");
                Console.ResetColor();
            };
        }
    }
}
