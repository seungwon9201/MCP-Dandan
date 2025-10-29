using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace MCPProxy
{
    public class Program
    {
        private static StreamWriter? pipeWriter;
        private static Process? targetProcess;

        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            InitPipe();
            StartProxy(args);
        }

        /// <summary>
        /// ETW Agent(MCPTrace.exe)와 NamedPipe 연결 시도
        /// </summary>
        private static void InitPipe()
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", "MCPTracePipe", PipeDirection.Out);
                pipe.Connect(3000); // 타임아웃 3초
                pipeWriter = new StreamWriter(pipe) { AutoFlush = true };
            }
            catch
            {
                // Pipe 연결 실패해도 MCPProxy는 독립적으로 동작해야 함
                pipeWriter = null;
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
            SendPipeEvent("proxy_exit", $"Process exited with code {targetProcess.ExitCode}");
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
                SendPipeEvent("client_to_server", line);
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
                SendPipeEvent("server_to_client", line);
            }
        }

        /// <summary>
        /// Target stderr → Proxy stderr + Pipe 이벤트
        /// </summary>
        private static void ForwardStderr()
        {
            using var reader = targetProcess!.StandardError;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                Console.Error.WriteLine(line);
                SendPipeEvent("server_stderr", line);
            }
        }

        /// <summary>
        /// Pipe로 ETW Agent에 JSON 메시지 전송
        /// </summary>
        private static void SendPipeEvent(string dir, string data)
        {
            try
            {
                var json = JsonSerializer.Serialize(new
                {
                    type = "rpc_message",
                    direction = dir,
                    pid = targetProcess?.Id ?? 0,
                    data
                });
                pipeWriter?.WriteLine(json);
            }
            catch
            {
                // Pipe 끊겨도 조용히 무시
            }
        }
    }
}
