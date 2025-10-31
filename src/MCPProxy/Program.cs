using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MCPProxy
{
    public class Program
    {
        private static TcpClient? collectorClient;
        private static StreamWriter? collectorWriter;
        private static Process? targetProcess;
        private static readonly object _collectorLock = new object();

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            InitCollector();
            StartProxy(args);
        }

        /// <summary>
        /// Collector TCP 연결 (재시도 로직 포함)
        /// </summary>
        private static void InitCollector()
        {
            const int MAX_RETRIES = 5;
            const int RETRY_DELAY_MS = 1000;

            for (int i = 0; i < MAX_RETRIES; i++)
            {
                try
                {
                    Console.Error.WriteLine($"[MCPProxy PID={Process.GetCurrentProcess().Id}] Connecting to Collector (attempt {i + 1}/{MAX_RETRIES})...");

                    collectorClient = new TcpClient();
                    collectorClient.Connect("127.0.0.1", 8888);

                    var stream = collectorClient.GetStream();
                    collectorWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    Console.Error.WriteLine($"[MCPProxy PID={Process.GetCurrentProcess().Id}] Connected to Collector ✓");
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCPProxy] Connection attempt {i + 1} failed: {ex.Message}");

                    if (i < MAX_RETRIES - 1)
                    {
                        Thread.Sleep(RETRY_DELAY_MS);
                    }
                }
            }

            Console.Error.WriteLine($"[MCPProxy] Failed to connect to Collector after {MAX_RETRIES} attempts");
            collectorWriter = null;
        }

        /// <summary>
        /// Collector 연결 확인 및 재연결
        /// </summary>
        private static bool EnsureCollectorConnection()
        {
            lock (_collectorLock)
            {
                // 이미 연결되어 있으면 OK
                if (collectorClient?.Connected == true && collectorWriter != null)
                    return true;

                // 재연결 시도
                try
                {
                    collectorClient?.Close();
                    collectorClient = new TcpClient();
                    collectorClient.Connect("127.0.0.1", 8888);

                    var stream = collectorClient.GetStream();
                    collectorWriter = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                    Console.Error.WriteLine($"[MCPProxy PID={Process.GetCurrentProcess().Id}] Reconnected to Collector");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCPProxy PID={Process.GetCurrentProcess().Id}] Reconnection failed: {ex.Message}");
                    collectorWriter = null;
                    return false;
                }
            }
        }

        /// <summary>
        /// 실제 MCP 서버 실행 및 STDIO 중계
        /// </summary>
        private static void StartProxy(string[] args)
        {
            string command = args[0];
            string arguments = args.Length > 1 ? string.Join(" ", args.Skip(1)) : "";

            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            targetProcess = new Process { StartInfo = startInfo };
            targetProcess.Start();

            // STDIO 중계 스레드
            new Thread(ForwardStdin) { IsBackground = true }.Start();
            new Thread(ForwardStdout) { IsBackground = true }.Start();
            new Thread(ForwardStderr) { IsBackground = true }.Start();

            targetProcess.WaitForExit();
            SendToCollector("proxy_exit", $"Process exited with code {targetProcess.ExitCode}");
        }

        /// <summary>
        /// Claude → Proxy → Target stdin
        /// </summary>
        private static void ForwardStdin()
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            using var writer = targetProcess!.StandardInput;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                writer.WriteLine(line);
                writer.Flush();

                // MCP 형식으로 전송 (SEND)
                try
                {
                    var message = JsonSerializer.Deserialize<JsonElement>(line);
                    SendMCPEvent("SEND", message);
                }
                catch
                {
                    // JSON이 아니면 무시
                }
            }
        }

        /// <summary>
        /// Target stdout → Proxy → Claude
        /// </summary>
        private static void ForwardStdout()
        {
            using var reader = targetProcess!.StandardOutput;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                Console.WriteLine(line);

                // MCP 형식으로 전송 (RECV)
                try
                {
                    var message = JsonSerializer.Deserialize<JsonElement>(line);
                    SendMCPEvent("RECV", message);
                }
                catch
                {
                    // JSON이 아니면 무시
                }
            }
        }

        /// <summary>
        /// Target stderr → Proxy stderr + Collector
        /// </summary>
        private static void ForwardStderr()
        {
            using var reader = targetProcess!.StandardError;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                Console.Error.WriteLine(line);
                SendToCollector("server_stderr", line);
            }
        }

        /// <summary>
        /// MCP 이벤트를 명세서 형식으로 Collector에 전송
        /// </summary>
        private static void SendMCPEvent(string task, JsonElement message)
        {
            try
            {
                if (targetProcess == null) return;

                // 연결 확인 및 재연결
                if (!EnsureCollectorConnection()) return;

                // 명세서에 맞는 형식
                var envelope = new
                {
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000, // nanoseconds
                    producer = "proxy",
                    pid = targetProcess.Id,
                    pname = targetProcess.ProcessName,
                    eventType = "MCP",
                    data = new
                    {
                        task,
                        transPort = "stdio",
                        src = task == "SEND" ? "client" : "server",
                        dst = task == "SEND" ? "server" : "client",
                        message
                    }
                };

                var json = JsonSerializer.Serialize(envelope);

                lock (_collectorLock)
                {
                    // 길이 헤더 추가
                    collectorWriter?.WriteLine($"{json.Length}");
                    collectorWriter?.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MCPProxy] SendMCPEvent failed: {ex.Message}");
                collectorWriter = null;
            }
        }

        /// <summary>
        /// 일반 이벤트를 Collector로 전송 (stderr 등)
        /// </summary>
        private static void SendToCollector(string type, string message)
        {
            try
            {
                // 연결 확인 및 재연결
                if (!EnsureCollectorConnection()) return;

                var envelope = new
                {
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000,
                    producer = "proxy",
                    pid = targetProcess?.Id ?? 0,
                    pname = targetProcess?.ProcessName ?? "unknown",
                    eventType = "ProxyLog",
                    data = new
                    {
                        type,
                        message
                    }
                };

                var json = JsonSerializer.Serialize(envelope);

                lock (_collectorLock)
                {
                    // 길이 헤더 추가
                    collectorWriter?.WriteLine($"{json.Length}");
                    collectorWriter?.WriteLine(json);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MCPProxy] SendToCollector failed: {ex.Message}");
                collectorWriter = null;
            }
        }
    }
}