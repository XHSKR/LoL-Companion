using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LoL_Companion
{
    internal sealed class ClientSelectionSettings
    {
        public string[] Accounts = new string[0];
        public string Locale = "ko_KR";

        public static ClientSelectionSettings Read(IEnumerable<string> lines)
        {
            var result = new ClientSelectionSettings();
            bool clients = false;
            foreach (string original in lines)
            {
                string line = original.Trim();
                if (line.StartsWith("[") && line.EndsWith("]")) { clients = line == "[Clients]"; continue; }
                int separator = line.IndexOf('=');
                if (!clients || separator < 0) continue;
                string key = line.Substring(0, separator).Trim().Trim('"');
                string value = line.Substring(separator + 1).Trim();
                if (key == "Locale") result.Locale = value == "en_AU" ? "en_AU" : "ko_KR";
                if (key == "CheckedAccounts")
                    try { result.Accounts = JArray.Parse(value).Values<string>().Where(account => !string.IsNullOrWhiteSpace(account))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
                    catch (JsonException) { }
            }
            return result;
        }

        public void Write(TextWriter writer)
        {
            writer.WriteLine("[Clients]");
            writer.WriteLine("\"CheckedAccounts\" = " + new JArray(Accounts).ToString(Formatting.None));
            writer.WriteLine("\"Locale\" = " + Locale);
        }

        public static IEnumerable<string> WithoutClientSection(IEnumerable<string> lines)
        {
            bool clients = false;
            foreach (string line in lines)
            {
                string trimmed = line.Trim();
                if (trimmed.StartsWith("[") && trimmed.EndsWith("]")) clients = trimmed == "[Clients]";
                if (!clients) yield return line;
            }
        }

        public void Save(string path)
        {
            string[] previous = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var writer = new StreamWriter(temporary))
                {
                    foreach (string line in WithoutClientSection(previous)) writer.WriteLine(line);
                    writer.WriteLine();
                    Write(writer);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
