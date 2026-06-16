using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoL_Companion
{
    class SettingSaver
    {
        private const string CredentialsFile = "credentials.conf";
        private const string SettingsFile = "settings.ini";

        public void LoadCredentials()
        {
            try
            {
                if (!File.Exists(CredentialsFile))
                    WriteDefaultCredentials();

                var lines = File.ReadAllLines(CredentialsFile);
                Form1.leagueID = lines.First().Split(',').ToList();
                Form1.leaguePass = lines.ElementAtOrDefault(1) ?? string.Empty;
            }
            catch (IOException ex)
            {
                MessageBox.Show($"크리덴셜 로드 중 오류가 발생했습니다:\n{ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void WriteDefaultCredentials()
        {
            File.WriteAllLines(CredentialsFile, new[]
            {
                "username1,username2,username3",
                "password"
            });
        }

        public void SaveSettings()
        {
            try
            {
                using (var writer = new StreamWriter(SettingsFile))
                {
                    WriteSection(writer, "League Location", () =>
                        writer.WriteLine($"\"leagueLocation\" = {Form1.leagueLocation}"));

                    WriteSection(writer, "League Client Settings", () =>
                    {
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox8.Text}\" = {Form1.Object.materialCheckBox8.Checked}");
                    });

                    WriteSection(writer, "In-Game Settings", () =>
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox3.Text}\" = {Form1.Object.materialCheckBox3.Checked}"));

                    WriteSection(writer, "Lobby Settings", () =>
                    {
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox12.Text}\" = {Form1.Object.materialCheckBox12.Checked}");
                        var radios = new[] {
                            Form1.Object.materialRadioButton5,
                            Form1.Object.materialRadioButton6,
                            Form1.Object.materialRadioButton7,
                            Form1.Object.materialRadioButton8,
                            Form1.Object.materialRadioButton9
                        };
                        foreach (var rb in radios)
                            writer.WriteLine($"\"{rb.Text}\" = {rb.Checked}");

                        writer.WriteLine($"\"selectedChampion\" = {NonEmptyOrDefault(Form1.Object.comboBox3.Text, "Champion")}");
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox16.Text}\" = {Form1.Object.materialCheckBox16.Checked}");
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox14.Text}\" = {Form1.Object.materialCheckBox14.Checked}");
                        writer.WriteLine($"\"selectedBanChampionId\" = {NonEmptyOrDefault(Form1.Object.comboBox6.Text, "Champion")}");
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox20.Text}\" = {Form1.Object.materialCheckBox20.Checked}");
                    });

                    WriteSection(writer, "Chat Settings", () =>
                    {
                        writer.WriteLine($"\"Ban-Phase\" = {NonEmptyOrDefault(Form1.Object.materialSingleLineTextField1.Text, "Enter your phrase")}");
                        writer.WriteLine($"\"Ban-Phase CheckBox\" = {Form1.Object.materialCheckBox19.Checked}");
                        writer.WriteLine($"\"Finalization-Phase\" = {NonEmptyOrDefault(Form1.Object.materialSingleLineTextField2.Text, "Enter your phrase")}");
                        writer.WriteLine($"\"Finalization-Phase CheckBox\" = {Form1.Object.materialCheckBox21.Checked}");
                    });

                    WriteSection(writer, "Spell Tracker Settings", () =>
                    {
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox10.Text}\" = {Form1.Object.materialCheckBox10.Checked}");
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox1.Text}\" = {Form1.Object.materialCheckBox1.Checked}");
                        writer.WriteLine($"\"{Form1.Object.materialCheckBox2.Text}\" = {Form1.Object.materialCheckBox2.Checked}");
                    });
                }
            }
            catch (IOException ex)
            {
                MessageBox.Show($"설정 저장 중 오류가 발생했습니다:\n{ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static void WriteSection(StreamWriter writer, string header, Action writeContent)
        {
            writer.WriteLine($"[{header}]");
            writeContent();
            writer.WriteLine();
        }

        public void LoadSettings()
        {
            try
            {
                if (!File.Exists(SettingsFile))
                {
                    MessageBox.Show(
                        "Thanks for using LoL Companion !\n\nDISCLAIMER\n1. Visit credentials.conf file to change the details with yours.\n" +
                        "2. Multi Search, Type W/R in Chat, and 50 FOV in Replay do not work on Non-Riot Server (PBE, China, Garena).\n" +
                        "3. Some functions featured by this app may not work or be expunged in the future.\n" +
                        "4. The developer does not take any form of accountability for using the app. USE IT AT YOUR OWN RISK !",
                        "Welcome", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    SaveSettings();
                    return;
                }

                var states = File.ReadAllLines(SettingsFile)
                                 .Where(line => !string.IsNullOrWhiteSpace(line) && line.Contains('='))
                                 .Select(line => line.Substring(line.IndexOf('=') + 1).Trim())
                                 .ToList();

                int i = 0;
                Form1.leagueLocation = states[i++];
                Form1.Object.materialCheckBox8.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox3.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox12.Checked = bool.Parse(states[i++]);
                Form1.Object.materialRadioButton5.Checked = bool.Parse(states[i++]);
                Form1.Object.materialRadioButton6.Checked = bool.Parse(states[i++]);
                Form1.Object.materialRadioButton7.Checked = bool.Parse(states[i++]);
                Form1.Object.materialRadioButton8.Checked = bool.Parse(states[i++]);
                Form1.Object.materialRadioButton9.Checked = bool.Parse(states[i++]);
                Form1.Object.comboBox3.Text = states[i++];
                Form1.Object.materialCheckBox16.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox14.Checked = bool.Parse(states[i++]);
                Form1.Object.comboBox6.Text = states[i++];
                Form1.Object.materialCheckBox20.Checked = bool.Parse(states[i++]);
                Form1.Object.materialSingleLineTextField1.Text = states[i++];
                Form1.Object.materialCheckBox19.Checked = bool.Parse(states[i++]);
                Form1.Object.materialSingleLineTextField2.Text = states[i++];
                Form1.Object.materialCheckBox21.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox10.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox1.Checked = bool.Parse(states[i++]);
                Form1.Object.materialCheckBox2.Checked = bool.Parse(states[i++]);
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException)
            {
                MessageBox.Show($"설정 로드 중 오류가 발생했습니다:\n{ex.Message}",
                                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string NonEmptyOrDefault(string value, string defaultValue)
            => string.IsNullOrEmpty(value) ? defaultValue : value;
    }
}
