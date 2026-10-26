using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace LoL_Companion
{
    internal sealed class RiotClientInstance
    {
        public int ProcessId { get; private set; }
        public RiotClientConnection Connection { get; private set; }
        public string Key => "riot:" + ProcessId + ":" + Connection.Port;

        internal static RiotClientInstance Parse(int processId, string commandLine)
        {
            int port;
            string secret = RiotClientConnection.ReadArgument(commandLine, "remoting-auth-token");
            if (!int.TryParse(RiotClientConnection.ReadArgument(commandLine, "app-port"), out port) ||
                port < 1 || port > 65535 || string.IsNullOrWhiteSpace(secret)) return null;
            int serviceProcessId;
            if (int.TryParse(RiotClientConnection.ReadArgument(commandLine, "app-pid"), out serviceProcessId) && serviceProcessId > 0)
                processId = serviceProcessId;
            return new RiotClientInstance { ProcessId = processId, Connection = RiotClientConnection.Create(port, secret) };
        }
    }

    internal static class RiotClientDiscovery
    {
        public static IReadOnlyList<RiotClientInstance> DiscoverAll()
        {
            var instances = new List<RiotClientInstance>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process WHERE Name='RiotClientServices.exe' OR Name='Riot Client.exe' OR Name='RiotClientUx.exe'"))
            using (var results = searcher.Get())
                foreach (ManagementBaseObject row in results)
                {
                    var instance = RiotClientInstance.Parse(Convert.ToInt32(row["ProcessId"]), row["CommandLine"] as string);
                    if (instance != null) instances.Add(instance);
                }
            return instances.GroupBy(instance => instance.Connection.Port).Select(group => group.First()).ToArray();
        }
    }
}
