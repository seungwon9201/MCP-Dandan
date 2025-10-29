using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public partial class Program
{
    public static string TargetProcName = "";
    public static HashSet<int> TrackedPids = new();
    private static readonly HashSet<int> RootPids = new();

    static async Task Main()
    {
        Console.OutputEncoding = Encoding.UTF8;

        var cts = new CancellationTokenSource();

        // 종료 시 세션 정리 & 정상 종료 처리
        AppDomain.CurrentDomain.ProcessExit += (s, e) => CleanupETWSessions();
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("\n[*] Ctrl+C detected. Stopping ...");
            cts.Cancel();
            CleanupETWSessions();
            e.Cancel = false;
            Environment.Exit(0);
        };

        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine("MCPTrace Agent (ETW Collector)");
        Console.WriteLine("=".PadRight(80, '='));
        Console.WriteLine();

        Console.WriteLine("모니터링할 프로세스를 선택하세요:");
        Console.WriteLine("1. Claude");
        Console.WriteLine("2. Cursor");
        Console.Write("\n선택 (1 또는 2): ");

        string choice = Console.ReadLine();
        TargetProcName = choice == "2" ? "cursor" : "claude";

        Console.WriteLine($"\n[+] 선택된 프로세스: {TargetProcName}");
        Console.WriteLine("[+] ETW 수집 및 Proxy 통신 대기 중...");
        Console.WriteLine("Note: 반드시 관리자 권한으로 실행해야 합니다.\n");

        StartPipeServer(cts.Token);
        await Task.Run(() => StartETWMonitoring(cts.Token));

        Console.WriteLine("[*] MCPTrace Agent 종료됨.");
    }


    private static void StartETWMonitoring(CancellationToken cancellationToken)
    {
        using var session = new TraceEventSession("MCPTraceSession_" + Process.GetCurrentProcess().Id);

        try
        {
            session.EnableKernelProvider(
                KernelTraceEventParser.Keywords.FileIOInit |
                KernelTraceEventParser.Keywords.FileIO |
                KernelTraceEventParser.Keywords.Process);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Kernel provider attach failed: {ex.Message}");
        }

        // 이벤트 핸들러 등록
        session.Source.Kernel.ProcessStart += HandleProcessStart;
        session.Source.Kernel.ProcessStop += HandleProcessStop;
        session.Source.Kernel.FileIORead += HandleFileIORead;
        session.Source.Kernel.FileIOWrite += HandleFileIOWrite;
        session.Source.Kernel.FileIOCreate += HandleFileIOCreate;

        // File Rename (Dynamic)
        var dynamicParser = new DynamicTraceEventParser(session.Source);
        dynamicParser.All += traceEvent =>
        {
            if (traceEvent.ProviderName.Equals("Microsoft-Windows-Kernel-File", StringComparison.OrdinalIgnoreCase) &&
                traceEvent.EventName.Equals("FileIORename", StringComparison.OrdinalIgnoreCase))
            {
                HandleFileIORenameDynamic(traceEvent);
            }
        };

        Task.Run(() =>
        {
            try
            {
                session.Source.Process();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ETW ERROR] {ex.GetType().Name}: {ex.Message}");
            }
        });

        cancellationToken.WaitHandle.WaitOne();
        Console.WriteLine("[*] ETW session stopped.");
    }


    // Proxy로부터 로그 받는 NamedPipe 서버 (보안 ACL 적용)
    private static void StartPipeServer(CancellationToken token)
    {
        Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var pipeSecurity = new PipeSecurity();
                    pipeSecurity.AddAccessRule(new PipeAccessRule(
                        new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                        PipeAccessRights.ReadWrite,
                        AccessControlType.Allow));

                    using var server = NamedPipeServerStreamAcl.Create(
                        "MCPTracePipe",
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.Asynchronous,
                        0, 0,
                        pipeSecurity
                    );

                    Console.WriteLine("[Agent] Waiting for Proxy connection...");
                    server.WaitForConnection();
                    Console.WriteLine("[Agent] Proxy connected.");

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var buffer = new char[8192];

                    while (server.IsConnected && !token.IsCancellationRequested)
                    {
                        int read = reader.Read(buffer, 0, buffer.Length);
                        if (read > 0)
                        {
                            string message = new string(buffer, 0, read).Trim();
                            if (!string.IsNullOrWhiteSpace(message))
                                PrintPipeMessage(message);
                        }
                        else
                        {
                            Thread.Sleep(50);
                        }
                    }

                    Console.WriteLine("[Agent] Proxy disconnected.");
                }
                catch (IOException)
                {
                    Console.WriteLine("[Agent] Proxy disconnected unexpectedly. Re-listening...");
                    Thread.Sleep(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Agent ERROR] {ex.Message}");
                    Thread.Sleep(1000);
                }
            }
        });
    }


    private static void PrintPipeMessage(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            string type = doc.RootElement.GetProperty("type").GetString() ?? "";
            string direction = doc.RootElement.TryGetProperty("direction", out var dirProp)
                ? dirProp.GetString() ?? ""
                : "";
            string data = doc.RootElement.TryGetProperty("data", out var dataProp)
                ? dataProp.GetString() ?? ""
                : "";

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[PIPE] Type: {type}, Dir: {direction}");
            Console.ResetColor();
            Console.WriteLine($"        Data: {TruncateForDisplay(data)}");
        }
        catch
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.WriteLine($"[PIPE RAW] {TruncateForDisplay(raw)}");
            Console.ResetColor();
        }
    }

    private static string TruncateForDisplay(string text, int maxLength = 200)
    {
        if (text == null) return "";
        if (text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "...";
    }


    private static void CleanupETWSessions()
    {
        try
        {
            using var s1 = new TraceEventSession("MCPTraceSession_" + Process.GetCurrentProcess().Id, null);
            s1.Stop();
        }
        catch { }

        try
        {
            using var s2 = new TraceEventSession("MCPMonitorSession_" + Process.GetCurrentProcess().Id, null);
            s2.Stop();
        }
        catch { }

        Console.WriteLine("[Cleanup] ETW sessions cleaned up.");
    }

}
