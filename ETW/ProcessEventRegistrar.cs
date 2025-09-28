using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;
using System;
using System.Linq;

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
                    // Claude 자손 프로세스
                    ProcessTracker.TrackedPids[ev.ProcessID] = ev.ImageFileName;
                    ProcessTracker.ProcCmdline[ev.ProcessID] = McpHelper.TagFromCommandLine(cmdline);

                    string runtime = ProcessHelper.GuessRuntime(ev.ImageFileName, cmdline);

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[MCP CHILD] PID={ev.ProcessID} Parent={ev.ParentID} Runtime={runtime} {ev.ImageFileName} CMD={cmdline}");
                    Console.ResetColor();

                    // --- MCP 이름 매핑 ---
                    string mcpName = McpHelper.MapCmdlineToMcp(cmdline);

                    // Filesystem MCP 감지: NodeService + anthropic.filesystem
                    if (string.IsNullOrEmpty(mcpName))
                    {
                        if (cmdline.IndexOf("node.mojom.NodeService", StringComparison.OrdinalIgnoreCase) >= 0 &&
                            cmdline.IndexOf("anthropic.filesystem", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mcpName = "Filesystem";
                        }
                    }

                    if (!string.IsNullOrEmpty(mcpName))
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[MCP MAP] PID={ev.ProcessID} Name={mcpName}");
                        Console.ResetColor();

                        // 이후 이벤트에서 식별할 수 있도록 저장
                        ProcessTracker.ProcCmdline[ev.ProcessID] = mcpName;
                    }

                    ProcessInspector.DumpProcessDetails(ev.ProcessID, "child-start");
                }
            };

            source.Kernel.ProcessStop += ev =>
            {
                if (ProcessTracker.TrackedPids.TryRemove(ev.ProcessID, out _))
                {
                    ProcessTracker.ProcCmdline.TryRemove(ev.ProcessID, out var lastCmd);
                    ProcessTracker.LastResolvedHostByPid.TryRemove(ev.ProcessID, out _);

                    if (ev.ProcessID == ProcessTracker.RootPid)
                        ProcessTracker.RootPid = -1;

                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[PROC STOP] PID={ev.ProcessID}");
                    Console.ResetColor();

                    string mcpName = McpHelper.MapCmdlineToMcp(lastCmd);
                    if (!string.IsNullOrEmpty(mcpName))
                    {
                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[MCP MAP] PID={ev.ProcessID} Name={mcpName} (stopped)");
                        Console.ResetColor();
                    }
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

            source.Kernel.FileIORead += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("READ", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            source.Kernel.FileIODirEnum += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("DIRENUM", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            source.Kernel.FileIORename += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("RENAME", ev.ProcessID, ev.FileName, ev.FileKey);
            };

            source.Kernel.FileIOClose += ev =>
            {
                if (ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID))
                    FileEventHandler.LogEvent("CLOSE", ev.ProcessID, ev.FileName, ev.FileKey);
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

                if (saddr == "127.0.0.1" || daddr == "127.0.0.1" || saddr == "::1" || daddr == "::1")
                    ProcessInspector.DumpProcessDetails(ev.ProcessID, "loopback-connect");
            };

            source.Kernel.TcpIpSend += ev =>
            {
                if (!ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID)) return;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NET-SEND] PID={ev.ProcessID} -> {ev.daddr}:{ev.dport} Bytes={ev.size}");
                Console.ResetColor();
            };

            source.Kernel.TcpIpRecv += ev =>
            {
                if (!ProcessTracker.TrackedPids.ContainsKey(ev.ProcessID)) return;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[NET-RECV] PID={ev.ProcessID} <- {ev.saddr}:{ev.sport} Bytes={ev.size}");
                Console.ResetColor();
            };

            // -------------------------------
            // 추가 프로바이더 (DNS/WinHTTP/WinINet)
            // -------------------------------
            try
            {
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-DNS-Client", "Query", ev =>
                {
                    try
                    {
                        int pid = GetEventProcessId(ev);
                        if (pid <= 0 || !ProcessTracker.TrackedPids.ContainsKey(pid)) return;
                        string query = SafePayload(ev, "QueryName");
                        string qtype = SafePayload(ev, "QueryType");

                        Console.ForegroundColor = ConsoleColor.Magenta;
                        Console.WriteLine($"[DNS] PID={pid} Query={query} Type={qtype}");
                        Console.ResetColor();

                        if (!string.IsNullOrEmpty(query))
                            ProcessTracker.LastResolvedHostByPid[pid] = query;
                    }
                    catch (Exception ex)
                    {
                        SafeLogError("DNS Query", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLogError("DNS provider attach", ex);
            }

            try
            {
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-WinHTTP", "WinHttpConnect", ev =>
                {
                    try
                    {
                        int pid = GetEventProcessId(ev);
                        if (pid <= 0 || !ProcessTracker.TrackedPids.ContainsKey(pid)) return;
                        string serverName = SafePayload(ev, "ServerName");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[WinHTTP-CONNECT] PID={pid} Server={serverName}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        SafeLogError("WinHTTP-CONNECT", ex);
                    }
                });

                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-WinHTTP", "WinHttpSendRequest", ev =>
                {
                    try
                    {
                        int pid = GetEventProcessId(ev);
                        if (pid <= 0 || !ProcessTracker.TrackedPids.ContainsKey(pid)) return;
                        string verb = SafePayload(ev, "Verb");
                        string objectName = SafePayload(ev, "ObjectName");
                        Console.ForegroundColor = ConsoleColor.DarkYellow;
                        Console.WriteLine($"[WinHTTP-REQ] PID={pid} {verb} {objectName}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        SafeLogError("WinHTTP-REQ", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLogError("WinHTTP provider attach", ex);
            }

            try
            {
                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-WinINet", "InternetConnect", ev =>
                {
                    try
                    {
                        int pid = GetEventProcessId(ev);
                        if (pid <= 0 || !ProcessTracker.TrackedPids.ContainsKey(pid)) return;
                        string serverName = SafePayload(ev, "ServerName");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"[WinINet-CONNECT] PID={pid} Server={serverName}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        SafeLogError("WinINet-CONNECT", ex);
                    }
                });

                source.Dynamic.AddCallbackForProviderEvent("Microsoft-Windows-WinINet", "HttpSendRequest", ev =>
                {
                    try
                    {
                        int pid = GetEventProcessId(ev);
                        if (pid <= 0 || !ProcessTracker.TrackedPids.ContainsKey(pid)) return;
                        string verb = SafePayload(ev, "Verb");
                        string url = SafePayload(ev, "Url");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"[WinINet-REQ] PID={pid} {verb} {url}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        SafeLogError("WinINet-REQ", ex);
                    }
                });
            }
            catch (Exception ex)
            {
                SafeLogError("WinINet provider attach", ex);
            }

            // -------------------------------
            // 지역 함수
            // -------------------------------
            int GetEventProcessId(TraceEvent ev)
            {
                try
                {
                    var prop = ev.GetType().GetProperty("ProcessID");
                    if (prop != null)
                    {
                        var v = prop.GetValue(ev);
                        if (v is int i && i > 0) return i;
                    }

                    object pld;
                    pld = ev.PayloadByName("ProcessId");
                    if (pld is int i1 && i1 > 0) return i1;
                    if (pld is uint u1 && u1 > 0) return (int)u1;
                    pld = ev.PayloadByName("ProcessID");
                    if (pld is int i2 && i2 > 0) return i2;
                    if (pld is uint u2 && u2 > 0) return (int)u2;
                    pld = ev.PayloadByName("PID");
                    if (pld is int i3 && i3 > 0) return i3;
                    if (pld is uint u3 && u3 > 0) return (int)u3;
                }
                catch (Exception ex)
                {
                    SafeLogError("GetEventProcessId", ex);
                }
                return -1;
            }

            string SafePayload(TraceEvent ev, string name)
            {
                try
                {
                    return ev.PayloadByName(name)?.ToString() ?? "";
                }
                catch
                {
                    return "";
                }
            }
        }

        // -------------------------------
        // 공통 에러 로거
        // -------------------------------
        private static void SafeLogError(string context, Exception ex)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {context}: {ex.GetType().Name} - {ex.Message}");
                Console.ResetColor();
            }
            catch
            {
                Console.WriteLine($"[ERROR] {context}: <unprintable exception>");
            }
        }
    }
}
