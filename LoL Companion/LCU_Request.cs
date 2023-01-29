using System.Linq;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Net;
using System.Text;
using System.Management;

namespace LoL_Companion
{
    class LCU_Request
    {
        public LCU_Request() { } //Create a default constructor

        public void getriotCredentials(string plugIn)
        {
            var process = Process.GetProcessesByName("LeagueClientUx").FirstOrDefault();

            string CommandLine;

            using (var searcher = new ManagementObjectSearcher(
               $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {process.Id}"))
            using (var objects = searcher.Get())
            {
                CommandLine = objects.Cast<ManagementBaseObject>().SingleOrDefault()?["CommandLine"]?.ToString();
            }

            Form1.Object.riotPass = Regex.Match(CommandLine, "(\"--remoting-auth-token=)([^\"]*)(\")").Groups[2].Value;
            Form1.Object.riotPort = Regex.Match(CommandLine, "(\"--app-port=)([^\"]*)(\")").Groups[2].Value;
            Form1.Object.completeURL = Form1.Object.riotURL + Form1.Object.riotPort + plugIn;
        }

        public void GET(string plugIn)
        {
            getriotCredentials(plugIn);

            var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) };
            string completeURL = Form1.Object.riotURL + Form1.Object.riotPort + plugIn;

            var responseNotConverted = client.DownloadData(completeURL);
            Form1.Object.response = Encoding.UTF8.GetString(responseNotConverted);
        }

        public void POST(string plugIn)
        {
            getriotCredentials(plugIn);

            byte[] bytes = Encoding.UTF8.GetBytes(Form1.Object.json); //convert from json to byte
            using (var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) })
            {
                client.UploadData(Form1.Object.completeURL, "POST", bytes);
            }
        }

        public void PUT(string plugIn)
        {
            getriotCredentials(plugIn);

            byte[] bytes = Encoding.UTF8.GetBytes(Form1.Object.json); //convert from json to byte
            using (var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) })
            {
                client.UploadData(Form1.Object.completeURL, "PUT", bytes);
            }
        }

        public void PATCH(string plugIn)
        {
            getriotCredentials(plugIn);

            byte[] bytes = Encoding.UTF8.GetBytes(Form1.Object.json); //convert from json to byte
            using (var client = new WebClient { Credentials = new NetworkCredential("riot", Form1.Object.riotPass) })
            {
                client.UploadData(Form1.Object.completeURL, "PATCH", bytes);
            }
        }

        public void DELETE(string plugIn)
        {
            getriotCredentials(plugIn);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Form1.Object.completeURL);
            request.Credentials = new NetworkCredential("riot", Form1.Object.riotPass);
            request.Method = "DELETE";
            HttpWebResponse response = (HttpWebResponse)request.GetResponse();
        }
    }
}