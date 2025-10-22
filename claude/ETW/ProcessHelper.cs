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

                // runtime 추정은 유지하되 출력에서는 제거
                string runtime = GuessRuntime(p.ProcessName + ".exe", cmd);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[INIT] Found running PID={p.Id} {p.ProcessName}.exe CMD={cmd}");
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
