using System;
using System.Diagnostics;
using System.IO;
using System.Management;

namespace ETW
{
    public static class ProcessHelper
    {
        public static void InitializeTargetProcesses(string targetProcName)
        {
            string baseName = Path.GetFileNameWithoutExtension(targetProcName);
            foreach (var p in Process.GetProcessesByName(baseName))
            {
                ProcessTracker.TrackedPids[p.Id] = p.ProcessName + ".exe";
                string cmd = TryGetCommandLineForPid(p.Id);
                ProcessTracker.ProcCmdline[p.Id] = McpHelper.TagFromCommandLine(cmd);

                // 런타임 추정 추가
                string runtime = GuessRuntime(p.ProcessName + ".exe", cmd);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[INIT] Found running PID={p.Id} {p.ProcessName}.exe Runtime={runtime} CMD={cmd}");
                Console.ResetColor();
            }
        }

        public static string TryGetCommandLineForPid(int pid)
        {
            try
            {
                string query = $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}";
                using var searcher = new ManagementObjectSearcher(query);
                foreach (ManagementObject mo in searcher.Get())
                    return mo["CommandLine"] as string;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 실행 파일명/명령줄 기반으로 런타임(Node.js, Python, Go, Rust 등) 추정
        /// </summary>
        public static string GuessRuntime(string imageFileName, string cmdline)
        {
            string lowerImg = imageFileName?.ToLowerInvariant() ?? "";
            string lowerCmd = cmdline?.ToLowerInvariant() ?? "";

            if (lowerImg.EndsWith("node.exe") || lowerCmd.Contains("node"))
                return "Node.js";
            if (lowerImg.EndsWith("python.exe") || lowerImg.EndsWith("python") || lowerCmd.Contains("python"))
                return "Python";
            if (lowerImg.Contains("go") || lowerCmd.Contains("go run"))
                return "Go";
            if (lowerImg.Contains("rust") || lowerCmd.Contains("cargo"))
                return "Rust";

            return "Unknown";
        }
    }
}
