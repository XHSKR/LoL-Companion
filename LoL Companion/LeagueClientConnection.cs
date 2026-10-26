using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace LoL_Companion
{
    internal sealed class LeagueClientConnection
    {
        public int ProcessId { get; private set; }
        public int LeagueProcessId { get; private set; }
        public RiotClientConnection Api { get; private set; }
        public int RiotPort { get; private set; }
        public RiotClientConnection RiotApi { get; private set; }
        public string Key => ProcessId + ":" + Api.Port;

        internal static LeagueClientConnection Parse(int processId, string commandLine, int leagueProcessId = 0)
        {
            int port, riotPort;
            string token = RiotClientConnection.ReadArgument(commandLine, "remoting-auth-token");
            if (processId <= 0 || !int.TryParse(RiotClientConnection.ReadArgument(commandLine, "app-port"), out port) ||
                port < 1 || port > 65535 || string.IsNullOrWhiteSpace(token))
                throw new FormatException("League Client 연결 정보를 읽지 못했습니다.");
            int.TryParse(RiotClientConnection.ReadArgument(commandLine, "riotclient-app-port"), out riotPort);
            string riotToken = RiotClientConnection.ReadArgument(commandLine, "riotclient-auth-token");
            return new LeagueClientConnection { ProcessId = processId, LeagueProcessId = leagueProcessId,
                Api = RiotClientConnection.Create(port, token), RiotPort = riotPort,
                RiotApi = riotPort > 0 && riotPort <= 65535 && !string.IsNullOrEmpty(riotToken) ? RiotClientConnection.Create(riotPort, riotToken) : null };
        }

        public static IReadOnlyList<LeagueClientConnection> DiscoverAll()
        {
            var connections = new List<LeagueClientConnection>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name='LeagueClientUx.exe'"))
            using (var results = searcher.Get())
                foreach (ManagementBaseObject row in results)
                    try { connections.Add(Parse(Convert.ToInt32(row["ProcessId"]), row["CommandLine"] as string,
                        Convert.ToInt32(row["ParentProcessId"]))); }
                    catch (FormatException) { /* A client may still be starting. */ }
            return connections.OrderBy(item => item.ProcessId).GroupBy(item => item.Api.Port).Select(group => group.First()).ToArray();
        }
    }
}
