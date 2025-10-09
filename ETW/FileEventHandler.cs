using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ETW
{
    public static class FileEventHandler
    {
        private const int MAX_READ_BYTES = 64 * 1024;

        public static void LogEvent(string kind, int pid, string path, ulong fileKey = 0)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            path = SanitizePath(path);

            int repeatCount;
            if (EtwFilters.ShouldIgnore(kind, pid, path, out repeatCount))
                return;

            string ext = string.Empty;
            try { ext = Path.GetExtension(path)?.ToLowerInvariant(); }
            catch { ext = string.Empty; }

            // MCP 확인
            string mcpName = "";
            if (ProcessTracker.ProcCmdline.TryGetValue(pid, out var tag))
            {
                if (!string.IsNullOrEmpty(tag) &&
                    tag.IndexOf("claude.exe", StringComparison.OrdinalIgnoreCase) < 0 &&
                    tag.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    mcpName = tag;
                }
            }

            if (string.IsNullOrEmpty(mcpName) &&
                path.IndexOf("anthropic.filesystem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mcpName = "Filesystem";
                ProcessTracker.ProcCmdline[pid] = mcpName;
            }

            // 콘솔 색상
            ConsoleColor color = !string.IsNullOrEmpty(mcpName) ? ConsoleColor.Magenta : kind.ToUpperInvariant() switch
            {
                "CREATE" => ConsoleColor.Cyan,
                "WRITE" => ConsoleColor.Cyan,
                "READ" => ConsoleColor.Green,
                "DIRENUM" => ConsoleColor.DarkYellow,
                "RENAME" => ConsoleColor.DarkYellow,
                "DELETE" => ConsoleColor.Red,
                "CLOSE" => ConsoleColor.DarkGray,
                _ => ConsoleColor.Gray
            };

            // ────────────────────────────────
            // 콘솔 로그 출력
            // ────────────────────────────────
            Console.ForegroundColor = color;
            Console.Write($"[{DateTime.Now:HH:mm:ss.fff}] [{kind}] PID={pid} PATH={path}");
            if (repeatCount > 0)
                Console.Write($" (x{repeatCount})");
            Console.WriteLine();
            if (!string.IsNullOrEmpty(mcpName))
                Console.WriteLine($"   └─ [MCP] {mcpName}");
            Console.ResetColor();

            // ────────────────────────────────
            // JSON 형식 출력
            // ────────────────────────────────
            try
            {
                var json = new
                {
                    ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1_000_000,
                    producer = "agent-core",
                    pid = pid,
                    pname = ProcessTracker.TrackedPids.TryGetValue(pid, out var pName) ? pName : "<unknown>",
                    eventType = "File",
                    data = new
                    {
                        task = kind,
                        pid = pid,
                        filePath = path,
                        mcpTag = mcpName
                    }
                };

                string jsonString = System.Text.Json.JsonSerializer.Serialize(json, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                Console.WriteLine(jsonString);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[JSON ERROR] {ex.Message}");
                Console.ResetColor();
            }

            // ────────────────────────────────
            // 파일 내용 덤프
            // ────────────────────────────────
            string fileName = Path.GetFileName(path).ToLowerInvariant();
            if (fileName.StartsWith("data_"))
                return;

            if ((kind.Equals("WRITE", StringComparison.OrdinalIgnoreCase) ||
                 kind.Equals("READ", StringComparison.OrdinalIgnoreCase)) &&
                ShouldDumpContents(ext))
            {
                ThreadPool.QueueUserWorkItem(_ => TryReadFileContents(path));
            }
        }

        private static bool ShouldDumpContents(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;
            if (ext == ".log" || ext == ".json") return false;
            if (ext == ".tmp") return true;
            return false;
        }

        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var sb = new StringBuilder(path.Length);
            foreach (char c in path)
            {
                if (!char.IsControl(c))
                    sb.Append(c);
            }
            string cleaned = sb.ToString();
            foreach (var bad in Path.GetInvalidPathChars())
                cleaned = cleaned.Replace(bad, '?');
            return cleaned;
        }

        private static void TryReadFileContents(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                long len = fs.Length;
                if (len == 0) return;

                long from = Math.Max(0, len - MAX_READ_BYTES);
                fs.Seek(from, SeekOrigin.Begin);

                byte[] buf = new byte[len - from];
                int read = fs.Read(buf, 0, buf.Length);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[FILE READ] {Path.GetFileName(path)} +{read} bytes:");
                Console.ResetColor();

                string text = Encoding.UTF8.GetString(buf, 0, read);
                Console.WriteLine(text.TrimEnd('\r', '\n'));
            }
            catch
            {
                // 무시
            }
        }
    }
}
