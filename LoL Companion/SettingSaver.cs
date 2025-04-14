using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoL_Companion
{
    class SettingSaver
    {
        public void loadCredentials()
        {
            if (!File.Exists(@"credentials.conf"))
            {
                using (StreamWriter writer = new StreamWriter(@"credentials.conf"))
                {
                    writer.WriteLine("username1,username2,username3");
                    writer.WriteLine("password");
                    writer.Close();
                }
            }
            using (StreamReader reader = new StreamReader(@"credentials.conf"))
            {
                Form1.Object.leagueID = new List<string>(reader.ReadLine().Split(','));
                Form1.Object.leaguePass = reader.ReadLine();
                reader.Close();
            }
        }

        public void saveStates()
        {
            using (StreamWriter writer = new StreamWriter(@"settings.ini"))
            {
                // [League Location] 섹션
                writer.WriteLine("[League Location]");
                writer.WriteLine("\"leagueLocation\"" + " = " + Form1.Object.leagueLocation);

                // [League Client Settings] 섹션
                writer.WriteLine("\n[League Client Settings]");
                // materialCheckBox8
                writer.WriteLine("\"" + Form1.Object.materialCheckBox8.Text + "\" = " + Form1.Object.materialCheckBox8.Checked);
                // materialCheckBox18, materialCheckBox11, materialCheckBox4, materialCheckBox17 항목은 제외
                writer.WriteLine("\"Log-in ID\"" + " = " + Form1.Object.comboBox2.Text);

                // [In-Game Settings] 섹션
                writer.WriteLine("\n[In-Game Settings]");
                writer.WriteLine("\"" + Form1.Object.materialCheckBox3.Text + "\" = " + Form1.Object.materialCheckBox3.Checked);
                // materialCheckBox5 항목은 제외

                // [Lobby Settings] 섹션
                writer.WriteLine("\n[Lobby Settings]");
                writer.WriteLine("\"" + Form1.Object.materialCheckBox12.Text + "\" = " + Form1.Object.materialCheckBox12.Checked);
                writer.WriteLine("\"" + Form1.Object.materialRadioButton5.Text + "\" = " + Form1.Object.materialRadioButton5.Checked);
                writer.WriteLine("\"" + Form1.Object.materialRadioButton6.Text + "\" = " + Form1.Object.materialRadioButton6.Checked);
                writer.WriteLine("\"" + Form1.Object.materialRadioButton7.Text + "\" = " + Form1.Object.materialRadioButton7.Checked);
                writer.WriteLine("\"" + Form1.Object.materialRadioButton8.Text + "\" = " + Form1.Object.materialRadioButton8.Checked);
                writer.WriteLine("\"" + Form1.Object.materialRadioButton9.Text + "\" = " + Form1.Object.materialRadioButton9.Checked);
                if (Form1.Object.comboBox3.Text == string.Empty)
                    writer.WriteLine("\"selectedChampion\"" + " = " + "Champion");
                else
                    writer.WriteLine("\"selectedChampion\"" + " = " + Form1.Object.comboBox3.Text);
                writer.WriteLine("\"" + Form1.Object.materialCheckBox16.Text + "\" = " + Form1.Object.materialCheckBox16.Checked);
                writer.WriteLine("\"" + Form1.Object.materialCheckBox14.Text + "\" = " + Form1.Object.materialCheckBox14.Checked);
                if (Form1.Object.comboBox6.Text == string.Empty)
                    writer.WriteLine("\"selectedBanChampionId\"" + " = " + "Champion");
                else
                    writer.WriteLine("\"selectedBanChampionId\"" + " = " + Form1.Object.comboBox6.Text);
                writer.WriteLine("\"" + Form1.Object.materialCheckBox20.Text + "\" = " + Form1.Object.materialCheckBox20.Checked);

                // [Chat Settings] 섹션
                writer.WriteLine("\n[Chat Settings]");
                if (Form1.Object.materialSingleLineTextField1.Text == string.Empty)
                    writer.WriteLine("\"Ban-Phase\"" + " = " + "Enter your phrase");
                else
                    writer.WriteLine("\"Ban-Phase\"" + " = " + Form1.Object.materialSingleLineTextField1.Text);
                writer.WriteLine("\"Ban-Phase CheckBox\"" + " = " + Form1.Object.materialCheckBox19.Checked);
                if (Form1.Object.materialSingleLineTextField2.Text == string.Empty)
                    writer.WriteLine("\"Finalization-Phase\"" + " = " + "Enter your phrase");
                else
                    writer.WriteLine("\"Finalization-Phase\"" + " = " + Form1.Object.materialSingleLineTextField2.Text);
                writer.WriteLine("\"Finalization-Phase CheckBox\"" + " = " + Form1.Object.materialCheckBox21.Checked);
                // materialCheckBox6, materialCheckBox7, Spamming Text 항목은 제외

                // [Spell Tracker Settings] 섹션
                writer.WriteLine("\n[Spell Tracker Settings]");
                writer.WriteLine("\"" + Form1.Object.materialCheckBox10.Text + "\" = " + Form1.Object.materialCheckBox10.Checked);
                writer.WriteLine("\"" + Form1.Object.materialCheckBox1.Text + "\" = " + Form1.Object.materialCheckBox1.Checked);
                writer.WriteLine("\"" + Form1.Object.materialCheckBox2.Text + "\" = " + Form1.Object.materialCheckBox2.Checked);

                writer.Close();
            }
        }

        public void loadStates()
        {
            if (!File.Exists(@"settings.ini"))
            {
                MessageBox.Show("Thanks for using LoL Companion !\n\nDISCLAIMER\n1. Visit credentials.conf file to change the details with yours.\n2. Multi Search, Type W/R in Chat, and 50 FOV in Replay do not work on Non-Riot Server (PBE, China, Garena).\n3. Some functions featured by this app may not work or be expunged in the future.\n4. The developer does not take any form of accountability for using the app. USE IT AT YOUR OWN RISK !\n\nThis message only appears once.\n\nFor any inquiries, please send me an email to mail@xhs.kr", "Welcome", MessageBoxButtons.OK, MessageBoxIcon.Information);
                saveStates();
            }
            else
            {
                List<String> states = new List<String>();
                using (var file_read = new StreamReader(@"settings.ini"))
                {
                    int totalLines = File.ReadLines(@"settings.ini").Count();
                    for (int i = 0; i < totalLines; i++)
                    {
                        string str = file_read.ReadLine();
                        if (String.IsNullOrWhiteSpace(str) || str.Contains("[") || str.Contains("]"))
                        {
                            continue;
                        }
                        else
                        {
                            states.Add(str.Substring(str.IndexOf("=") + 1).Trim());
                        }
                    }
                    file_read.Close();
                }

                // [League Location] 섹션
                Form1.Object.leagueLocation = states[0];

                // [League Client Settings] 섹션
                Form1.Object.materialCheckBox8.Checked = bool.Parse(states[1]);
                // materialCheckBox18, materialCheckBox11, materialCheckBox4, materialCheckBox17 항목은 제외
                Form1.Object.comboBox2.Text = states[2];

                // [In-Game Settings] 섹션
                Form1.Object.materialCheckBox3.Checked = bool.Parse(states[3]);
                // materialCheckBox5 항목은 제외

                // [Lobby Settings] 섹션
                Form1.Object.materialCheckBox12.Checked = bool.Parse(states[4]);
                Form1.Object.materialRadioButton5.Checked = bool.Parse(states[5]);
                Form1.Object.materialRadioButton6.Checked = bool.Parse(states[6]);
                Form1.Object.materialRadioButton7.Checked = bool.Parse(states[7]);
                Form1.Object.materialRadioButton8.Checked = bool.Parse(states[8]);
                Form1.Object.materialRadioButton9.Checked = bool.Parse(states[9]);
                Form1.Object.comboBox3.Text = states[10];
                Form1.Object.materialCheckBox16.Checked = bool.Parse(states[11]);
                Form1.Object.materialCheckBox14.Checked = bool.Parse(states[12]);
                Form1.Object.comboBox6.Text = states[13];
                Form1.Object.materialCheckBox20.Checked = bool.Parse(states[14]);

                // [Chat Settings] 섹션
                Form1.Object.materialSingleLineTextField1.Text = states[15];
                Form1.Object.materialCheckBox19.Checked = bool.Parse(states[16]);
                Form1.Object.materialSingleLineTextField2.Text = states[17];
                Form1.Object.materialCheckBox21.Checked = bool.Parse(states[18]);
                // materialCheckBox6, materialCheckBox7, Spamming Text 항목은 제외

                // [Spell Tracker Settings] 섹션
                Form1.Object.materialCheckBox10.Checked = bool.Parse(states[19]);
                Form1.Object.materialCheckBox1.Checked = bool.Parse(states[20]);
                Form1.Object.materialCheckBox2.Checked = bool.Parse(states[21]);
            }
        }
    }
}
