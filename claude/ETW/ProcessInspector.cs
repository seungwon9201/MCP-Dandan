using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace ETW
{
    public static class ProcessInspector
    {
        // 같은 PID는 한 번만 출력 (중복 방지용)
        private static readonly ConcurrentDictionary<int, byte> Dumped = new();

        public static void DumpProcessDetails(int pid, string reasonTag = "")
        {
            if (!Dumped.TryAdd(pid, 1)) return; // 이미 덤프했으면 스킵

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("────────────────────────────────────────────");
                sb.AppendLine($"[PROC DETAIL] PID={pid} Reason={reasonTag}");

                // 1) WMI로 기본 정보
                string image = null, cmdline = null, exePath = null, startTime = null, user = null, domain = null;
                int ppid = -1;

                try
                {
                    using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_Process WHERE ProcessId={pid}");
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        image = (mo["Name"] as string) ?? "";
                        cmdline = (mo["CommandLine"] as string) ?? "";
                        exePath = (mo["ExecutablePath"] as string) ?? "";
                        ppid = mo["ParentProcessId"] is uint p ? (int)p : -1;

                        var dmtf = mo["CreationDate"] as string;
                        if (!string.IsNullOrEmpty(dmtf))
                            startTime = ManagementDateTimeConverter.ToDateTime(dmtf).ToString("yyyy-MM-dd HH:mm:ss");

                        // 실행 계정
                        try
                        {
                            var ownerArgs = new string[] { string.Empty, string.Empty };
                            var ret = Convert.ToInt32(mo.InvokeMethod("GetOwner", ownerArgs));
                            if (ret == 0) { user = ownerArgs[0]; domain = ownerArgs[1]; }
                        }
                        catch { }
                    }
                }
                catch { }

                sb.AppendLine($" Image:        {image ?? "<n/a>"}");
                sb.AppendLine($" Executable:   {exePath ?? "<n/a>"}");
                sb.AppendLine($" CommandLine:  {cmdline ?? "<n/a>"}");
                sb.AppendLine($" Parent PID:   {ppid}");
                sb.AppendLine($" Started:      {startTime ?? "<n/a>"}");
                sb.AppendLine($" User:         {(string.IsNullOrEmpty(domain) ? user : $"{domain}\\{user}")}");

                // 2) 런타임 추정
                string runtime = ProcessHelper.GuessRuntime(image ?? exePath, cmdline);
                sb.AppendLine($" RuntimeGuess: {runtime}");

                // 3) 부모 체인
                sb.Append(" Ancestry:     ");
                sb.AppendLine(BuildAncestry(ppid, depth: 5));

                // 4) 모듈 일부
                try
                {
                    var proc = Process.GetProcessById(pid);
                    sb.AppendLine(" Modules (top 8):");
                    int count = 0;
                    foreach (ProcessModule m in proc.Modules)
                    {
                        sb.AppendLine($"   - {m.ModuleName} @ {m.FileName}");
                        if (++count >= 8) break;
                    }
                }
                catch { sb.AppendLine(" Modules:      <access denied / different bitness>"); }

                // 5) TCP 연결 (IPv4만)
                try
                {
                    sb.AppendLine(" TCP (IPv4) connections:");
                    foreach (var row in NetTable.GetTcp4ByPid(pid))
                    {
                        string state = row.State.ToString();
                        string local = $"{row.LocalAddress}:{row.LocalPort}";
                        string remote = $"{row.RemoteAddress}:{row.RemotePort}";
                        sb.AppendLine($"   - {local} ⇄ {remote} [{state}]");

                        // 루프백 상대방 PID 역매핑
                        if (row.RemoteAddress == "127.0.0.1")
                        {
                            int remotePid = NetTable.FindPidByLocalPort(row.RemotePort);
                            if (remotePid > 0)
                            {
                                sb.AppendLine($"     ↳ Remote PID={remotePid}");
                            }
                        }
                    }
                }
                catch { sb.AppendLine(" TCP (IPv4):   <unavailable>"); }

                Console.ForegroundColor = ConsoleColor.White;
                Console.Write(sb.ToString());
                Console.ResetColor();
            }
            catch { /* ignore errors */ }
        }

        private static string BuildAncestry(int pid, int depth)
        {
            if (pid <= 0 || depth <= 0) return "<end>";
            try
            {
                using var s = new ManagementObjectSearcher($"SELECT ProcessId, ParentProcessId, Name FROM Win32_Process WHERE ProcessId={pid}");
                foreach (ManagementObject mo in s.Get())
                {
                    var name = mo["Name"] as string ?? "?";
                    int parent = mo["ParentProcessId"] is uint p ? (int)p : -1;
                    return $"{name}({pid}) <- " + BuildAncestry(parent, depth - 1);
                }
            }
            catch { }
            return $"{pid} <- <end>";
        }

        // ---------- IPv4 TCP table by PID ----------
        private static class NetTable
        {
            public static System.Collections.Generic.IEnumerable<TcpRow> GetTcp4ByPid(int pid)
            {
                int AF_INET = 2; // IPv4
                int buffSize = 0;
                uint res = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, AF_INET,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

                IntPtr ptr = Marshal.AllocHGlobal(buffSize);
                try
                {
                    res = GetExtendedTcpTable(ptr, ref buffSize, true, AF_INET,
                        TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (res != 0) yield break;

                    int numEntries = Marshal.ReadInt32(ptr);
                    IntPtr rowPtr = IntPtr.Add(ptr, 4);
                    int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        if ((int)row.dwOwningPid == pid)
                        {
                            yield return new TcpRow
                            {
                                LocalAddress = new IPAddress(row.dwLocalAddr).ToString(),
                                LocalPort = ntohs((ushort)row.dwLocalPort),
                                RemoteAddress = new IPAddress(row.dwRemoteAddr).ToString(),
                                RemotePort = ntohs((ushort)row.dwRemotePort),
                                State = (MIB_TCP_STATE)row.dwState
                            };
                        }
                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            public static int FindPidByLocalPort(int port)
            {
                int AF_INET = 2;
                int buffSize = 0;
                uint res = GetExtendedTcpTable(IntPtr.Zero, ref buffSize, true, AF_INET,
                    TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);

                IntPtr ptr = Marshal.AllocHGlobal(buffSize);
                try
                {
                    res = GetExtendedTcpTable(ptr, ref buffSize, true, AF_INET,
                        TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
                    if (res != 0) return -1;

                    int numEntries = Marshal.ReadInt32(ptr);
                    IntPtr rowPtr = IntPtr.Add(ptr, 4);
                    int rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();

                    for (int i = 0; i < numEntries; i++)
                    {
                        var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                        int localPort = ntohs((ushort)row.dwLocalPort);

                        if (localPort == port && row.dwState == (uint)MIB_TCP_STATE.LISTEN)
                            return (int)row.dwOwningPid;

                        rowPtr = IntPtr.Add(rowPtr, rowSize);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
                return -1;
            }

            public class TcpRow
            {
                public string LocalAddress { get; set; }
                public int LocalPort { get; set; }
                public string RemoteAddress { get; set; }
                public int RemotePort { get; set; }
                public MIB_TCP_STATE State { get; set; }
            }

            [DllImport("iphlpapi.dll", SetLastError = true)]
            private static extern uint GetExtendedTcpTable(
                IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion,
                TCP_TABLE_CLASS tblClass, uint reserved);

            private static ushort ntohs(ushort net)
            {
                return (ushort)((net >> 8) | (net << 8));
            }

            private enum TCP_TABLE_CLASS
            {
                TCP_TABLE_BASIC_LISTENER,
                TCP_TABLE_BASIC_CONNECTIONS,
                TCP_TABLE_BASIC_ALL,
                TCP_TABLE_OWNER_PID_LISTENER,
                TCP_TABLE_OWNER_PID_CONNECTIONS,
                TCP_TABLE_OWNER_PID_ALL,
                TCP_TABLE_OWNER_MODULE_LISTENER,
                TCP_TABLE_OWNER_MODULE_CONNECTIONS,
                TCP_TABLE_OWNER_MODULE_ALL
            }

            public enum MIB_TCP_STATE
            {
                CLOSED = 1,
                LISTEN = 2,
                SYN_SENT = 3,
                SYN_RCVD = 4,
                ESTABLISHED = 5,
                FIN_WAIT1 = 6,
                FIN_WAIT2 = 7,
                CLOSE_WAIT = 8,
                CLOSING = 9,
                LAST_ACK = 10,
                TIME_WAIT = 11,
                DELETE_TCB = 12
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MIB_TCPROW_OWNER_PID
            {
                public uint dwState;
                public uint dwLocalAddr;
                public uint dwLocalPort;
                public uint dwRemoteAddr;
                public uint dwRemotePort;
                public uint dwOwningPid;
            }
        }
    }
}
