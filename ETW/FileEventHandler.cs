using System;
using System.IO;
using System.Text;
using System.Threading;

namespace ETW
{
    public static class FileEventHandler
    {
        private static readonly string[] IgnorePathPatterns = new[]
        {
            @"\cache\cache_data\",
            @"\gpucache\",
            @"\code cache\"
        };

        private const int MAX_READ_BYTES = 64 * 1024;

        public static void LogEvent(string kind, int pid, string path, ulong fileKey = 0)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            // 경로 정제
            path = SanitizePath(path);

            // 무시 패턴 필터링
            foreach (var ignore in IgnorePathPatterns)
            {
                if (path.ToLowerInvariant().Contains(ignore)) return;
            }

            // 확장자 체크 (log/json은 내부 내용 출력 제외)
            string ext = string.Empty;
            try { ext = Path.GetExtension(path)?.ToLowerInvariant(); }
            catch { ext = string.Empty; }

            // MCP 여부 확인
            string mcpName = "";
            if (ProcessTracker.ProcCmdline.TryGetValue(pid, out var tag))
            {
                if (!string.IsNullOrEmpty(tag) &&
                    tag.IndexOf("claude.exe", StringComparison.OrdinalIgnoreCase) < 0 &&
                    tag.IndexOf(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    mcpName = tag; //기존 MCP 태그
                }
            }

            // --- Filesystem MCP 감지 로직 ---
            if (string.IsNullOrEmpty(mcpName) &&
                path.IndexOf("anthropic.filesystem", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mcpName = "Filesystem";
                ProcessTracker.ProcCmdline[pid] = mcpName; // PID → Filesystem MCP로 등록
            }

            // 색상 결정
            ConsoleColor color;
            if (!string.IsNullOrEmpty(mcpName))
            {
                // MCP 프로세스 → 항상 Magenta
                color = ConsoleColor.Magenta;
            }
            else
            {
                // 일반 이벤트 색상
                switch (kind.ToUpperInvariant())
                {
                    case "CREATE": color = ConsoleColor.Cyan; break;
                    case "WRITE": color = ConsoleColor.Cyan; break;
                    case "READ": color = ConsoleColor.Green; break;
                    case "DIRENUM":
                    case "RENAME": color = ConsoleColor.DarkYellow; break;
                    case "DELETE": color = ConsoleColor.Red; break;
                    case "CLOSE": color = ConsoleColor.DarkGray; break;
                    default: color = ConsoleColor.Gray; break;
                }
            }

            // 출력
            Console.ForegroundColor = color;
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] [{kind}] PID={pid} PATH={path}");
            if (!string.IsNullOrEmpty(mcpName))
                Console.WriteLine($"   └─ [MCP] {mcpName}");
            Console.ResetColor();

            // 내용 덤프 (WRITE/READ만)
            if ((kind.Equals("WRITE", StringComparison.OrdinalIgnoreCase) ||
                 kind.Equals("READ", StringComparison.OrdinalIgnoreCase)) &&
                ShouldDumpContents(ext))
            {
                ThreadPool.QueueUserWorkItem(_ => TryReadFileContents(path));
            }
        }

        /// <summary>
        /// log/json 파일은 내용 덤프하지 않도록 필터링
        /// </summary>
        private static bool ShouldDumpContents(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return false;

            if (ext == ".log" || ext == ".json")
                return false;

            if (ext == ".tmp")
                return true;

            return false;
        }

        /// <summary>
        /// 경로 문자열에서 잘못된 문자 제거
        /// </summary>
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
                long fileLen = fs.Length;
                if (fileLen == 0) return;

                long toReadFrom = Math.Max(0, fileLen - MAX_READ_BYTES);
                fs.Seek(toReadFrom, SeekOrigin.Begin);

                byte[] buf = new byte[fileLen - toReadFrom];
                int read = fs.Read(buf, 0, buf.Length);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[FILE READ] {Path.GetFileName(path)} +{read} bytes:");
                Console.ResetColor();

                string text = Encoding.UTF8.GetString(buf, 0, read);
                Console.WriteLine(text.TrimEnd('\r', '\n'));
            }
            catch
            {
                // 무시 (동시에 쓰는 중이라 잠길 수 있음)
            }
        }
    }
}
