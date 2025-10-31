using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Collector
{
    class Program
    {
        private static StreamWriter? logWriter;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            // 로그 파일 초기화
            var logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"events_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
            logWriter = new StreamWriter(logFile, append: true) { AutoFlush = true };

            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine("MCP Observer - Event Collector");
            Console.WriteLine("=".PadRight(80, '='));
            Console.WriteLine();
            Console.WriteLine($"[*] Logging to: {logFile}");
            Console.WriteLine("[*] Starting TCP server on localhost:8888...");
            Console.WriteLine("[*] Waiting for MCPTrace and MCPProxy connections...");
            Console.WriteLine();

            var listener = new TcpListener(IPAddress.Loopback, 8888);
            listener.Start();

            int connectionId = 0;

            while (true)
            {
                var client = await listener.AcceptTcpClientAsync();
                int currentId = connectionId++;

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[Connection #{currentId}] Client connected from {client.Client.RemoteEndPoint}");
                Console.ResetColor();

                _ = Task.Run(() => HandleClient(client, currentId));
            }
        }

        static async Task HandleClient(TcpClient client, int connectionId)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    while (true)
                    {
                        // 1. 길이 읽기
                        string? lengthStr = await reader.ReadLineAsync();
                        if (lengthStr == null) break;

                        if (!int.TryParse(lengthStr, out int length))
                        {
                            Console.WriteLine($"[Connection #{connectionId}] Invalid length: {lengthStr}");
                            continue;
                        }

                        // 2. 정확한 길이만큼 읽기
                        char[] buffer = new char[length];
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int read = await reader.ReadAsync(buffer, totalRead, length - totalRead);
                            if (read == 0) break;
                            totalRead += read;
                        }

                        if (totalRead == length)
                        {
                            string json = new string(buffer);

                            // 3. 개행 문자 소비 (\n)
                            await reader.ReadLineAsync();

                            ProcessEvent(json, connectionId);
                        }
                    }
                }

                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Connection #{connectionId}] Client disconnected");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Connection #{connectionId} ERROR] {ex.Message}");
                Console.ResetColor();
            }
        }

        static void ProcessEvent(string json, int connectionId)
        {
            try
            {
                // 파일에 저장
                logWriter?.WriteLine(json);

                // 명세서 형식으로 파싱
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // producer 필드 읽기 (없으면 unknown)
                string producer = "unknown";
                if (root.TryGetProperty("producer", out var producerElement))
                {
                    producer = producerElement.GetString() ?? "unknown";
                }

                string eventType = root.GetProperty("eventType").GetString() ?? "";

                // ProxyLog는 간단하게 한 줄로 출력
                if (eventType == "ProxyLog")
                {
                    if (root.TryGetProperty("data", out var dataElement))
                    {
                        if (dataElement.TryGetProperty("message", out var msgElement))
                        {
                            string msg = msgElement.GetString() ?? "";
                            // 공백이 많거나 너무 짧은 건 스킵
                            string trimmed = msg.Trim();
                            if (trimmed.Length > 10)
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine($"[PROXY-LOG] {trimmed}");
                                Console.ResetColor();
                            }
                        }
                    }
                    return; // ProxyLog는 여기서 종료
                }

                // 나머지 이벤트는 기존 방식대로
                ConsoleColor color = producer switch
                {
                    "etw" => ConsoleColor.Green,
                    "proxy" => ConsoleColor.Cyan,
                    "mitm" => ConsoleColor.Blue,
                    _ => ConsoleColor.White
                };

                Console.ForegroundColor = color;
                Console.Write($"[{producer.ToUpper()}] ");
                Console.ResetColor();

                // eventType 출력
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.Write($"{eventType}: ");
                Console.ResetColor();

                // 전체 data를 보기 좋게 포맷팅해서 출력
                if (root.TryGetProperty("data", out var dataElement2))
                {
                    // JSON을 보기 좋게 포맷팅 (들여쓰기)
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    };
                    string formattedData = JsonSerializer.Serialize(dataElement2, options);

                    // 각 줄에 들여쓰기 추가
                    var lines = formattedData.Split('\n');
                    foreach (var line in lines)
                    {
                        Console.WriteLine(line);
                    }
                }
                else
                {
                    Console.WriteLine("(no data)");
                }

                Console.WriteLine(); // 이벤트 구분을 위한 빈 줄
            }
            catch
            {
                Console.WriteLine($"[RAW #{connectionId}] {json}");
                logWriter?.WriteLine(json);
            }
        }
    }
}