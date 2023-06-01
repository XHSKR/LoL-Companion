using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LoL_Companion
{
    class LCU_Request
    {
        public LCU_Request() { } //Create a default constructor

        private void GetRiotCredentials(string plugIn)
        {
            var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();
            string CommandLine;

            using (var searcher = new ManagementObjectSearcher(
               $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
            using (var objects = searcher.Get())
                CommandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();

            Form1.Object.riotPass = Regex.Match(CommandLine, "(\"--remoting-auth-token=)([^\"]*)(\")").Groups[2].Value;
            Form1.Object.riotPort = Regex.Match(CommandLine, "(\"--app-port=)([^\"]*)(\")").Groups[2].Value;
            Form1.Object.completeURL = "https://127.0.0.1:" + Form1.Object.riotPort + plugIn;
        }

        public void GET(string plugIn)
        {
            GetRiotCredentials(plugIn);

            using (var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) })
            {
                var responseNotConverted = client.DownloadData(Form1.Object.completeURL);
                Form1.Object.response = Encoding.UTF8.GetString(responseNotConverted);
            }
        }

        private void SendRequest(string plugIn, string method)
        {
            GetRiotCredentials(plugIn);

            byte[] bytes = Encoding.UTF8.GetBytes(Form1.Object.json); //convert from json to byte

            using (var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) })
                client.UploadData(Form1.Object.completeURL, method, bytes);
        }

        public void POST(string plugIn)
        {
            SendRequest(plugIn, "POST");
        }

        public void PUT(string plugIn)
        {
            SendRequest(plugIn, "PUT");
        }

        public void PATCH(string plugIn)
        {
            SendRequest(plugIn, "PATCH");
        }
    }
}