using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace LoL_Companion
{
    class SettingSaver
    {
        public void saveStates()
        {
            using (StreamWriter writer = new StreamWriter(@"settings.ini"))
            {
                writer.WriteLine("[League Location]");
                writer.WriteLine("leagueLocation" + "=" + Form1.Object.leagueLocation);

                writer.WriteLine("\n[League Client Settings]");
                writer.WriteLine(Form1.Object.materialCheckBox8.Text + "=" + Form1.Object.materialCheckBox8.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox18.Text + "=" + Form1.Object.materialCheckBox18.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox11.Text + "=" + Form1.Object.materialCheckBox11.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox4.Text + "=" + Form1.Object.materialCheckBox4.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox17.Text + "=" + Form1.Object.materialCheckBox17.Checked);
                writer.WriteLine("Log-in ID" + "=" + Form1.Object.comboBox2.Text);

                writer.WriteLine("\n[In-Game Settings]");
                writer.WriteLine(Form1.Object.materialCheckBox3.Text + "=" + Form1.Object.materialCheckBox3.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox5.Text + "=" + Form1.Object.materialCheckBox5.Checked);

                writer.WriteLine("\n[Chat Settings]");
                writer.WriteLine(Form1.Object.materialCheckBox12.Text + "=" + Form1.Object.materialCheckBox12.Checked);
                writer.WriteLine(Form1.Object.materialRadioButton5.Text + "=" + Form1.Object.materialRadioButton5.Checked);
                writer.WriteLine(Form1.Object.materialRadioButton6.Text + "=" + Form1.Object.materialRadioButton6.Checked);
                writer.WriteLine(Form1.Object.materialRadioButton7.Text + "=" + Form1.Object.materialRadioButton7.Checked);
                writer.WriteLine(Form1.Object.materialRadioButton8.Text + "=" + Form1.Object.materialRadioButton8.Checked);
                writer.WriteLine(Form1.Object.materialRadioButton9.Text + "=" + Form1.Object.materialRadioButton9.Checked);
                if (Form1.Object.comboBox3.Text == string.Empty)
                    writer.WriteLine("selectedChampion" + "=" + "Champion");
                else
                    writer.WriteLine("selectedChampion" + "=" + Form1.Object.comboBox3.Text);
                writer.WriteLine(Form1.Object.materialCheckBox16.Text + "=" + Form1.Object.materialCheckBox16.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox14.Text + "=" + Form1.Object.materialCheckBox14.Checked);
                if (Form1.Object.comboBox6.Text == string.Empty)
                    writer.WriteLine("selectedBanChampionId" + "=" + "Champion");
                else
                    writer.WriteLine("selectedBanChampionId" + "=" + Form1.Object.comboBox6.Text);
                writer.WriteLine(Form1.Object.materialCheckBox20.Text + "=" + Form1.Object.materialCheckBox20.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox6.Text + "=" + Form1.Object.materialCheckBox6.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox7.Text + "=" + Form1.Object.materialCheckBox7.Checked);
                if (Form1.Object.materialSingleLineTextField6.Text == string.Empty)
                    writer.WriteLine("Spamming Text" + "=" + "Enter text here to spam");
                else
                    writer.WriteLine("Spamming Text" + "=" + Form1.Object.materialSingleLineTextField6.Text);
                writer.WriteLine("Macro 1=" + Form1.Object.chatMacro1);
                writer.WriteLine("Macro 2=" + Form1.Object.chatMacro2);
                writer.WriteLine("Macro 3=" + Form1.Object.chatMacro3);
                writer.WriteLine("Macro 4=" + Form1.Object.chatMacro4);
                writer.WriteLine("Macro 5=" + Form1.Object.chatMacro5);

                writer.WriteLine("\n[Spell Tracker Settings]");
                writer.WriteLine(Form1.Object.materialCheckBox10.Text + "=" + Form1.Object.materialCheckBox10.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox1.Text + "=" + Form1.Object.materialCheckBox1.Checked);
                writer.WriteLine(Form1.Object.materialCheckBox2.Text + "=" + Form1.Object.materialCheckBox2.Checked);

                writer.Close();
            }
        }

        public void loadStates()
        {
            if (!File.Exists(@"settings.ini"))
            {
                Form1.Object.isNewUser = true;
                MessageBox.Show("Thanks for using LoL Companion !\n\nDISCLAIMER\n1. Multi Search, Type W/R in Chat, and 50 FOV in Replay do not work on Non-Riot Server (PBE, China, Garena).\n2. You MUST enable DX9 Legacy Mode in the settings of the League Client in order to properly use Mute All on Game Start and AFK Timer. (Settings -> GAME -> Prefer DX9 Legacy Mode)\n3. Some functions featured by this app may not work or be expunged in the future.\n4. The developer does not take any form of accountability for using the app. USE IT AT YOUR OWN RISK !\n\nThis message only appears once.\n\nFor any inquiries, please send me an email to mail@xhs.kr", "Welcome", MessageBoxButtons.OK, MessageBoxIcon.Information);
                saveStates();
            }
            else
            {
                List<String> states = new List<String>();
                using (var file_read = new StreamReader(@"settings.ini")) //text파일을 읽는다
                {
                    for (int i = 0; i < File.ReadLines(@"settings.ini").Count(); i++)
                    {
                        string str = file_read.ReadLine();
                        if (String.IsNullOrWhiteSpace(str) || str.Contains("[") || str.Contains("]")) { }
                        else
                        {
                            states.Add(str.Substring(str.IndexOf("=") + 1));
                        }
                    }
                    file_read.Close();
                }

                //League Location
                Form1.Object.leagueLocation = states[0];

                //League Client Settings
                Form1.Object.materialCheckBox8.Checked = bool.Parse(states[1]);
                Form1.Object.materialCheckBox18.Checked = bool.Parse(states[2]);
                Form1.Object.materialCheckBox11.Checked = bool.Parse(states[3]);
                Form1.Object.materialCheckBox4.Checked = bool.Parse(states[4]);
                Form1.Object.materialCheckBox17.Checked = bool.Parse(states[5]);
                Form1.Object.comboBox2.Text = states[6];

                //In-Game Settings
                Form1.Object.materialCheckBox3.Checked = bool.Parse(states[7]);
                Form1.Object.materialCheckBox5.Checked = bool.Parse(states[8]);

                //Chat Settings
                Form1.Object.materialCheckBox12.Checked = bool.Parse(states[9]);
                Form1.Object.materialRadioButton5.Checked = bool.Parse(states[10]);
                Form1.Object.materialRadioButton6.Checked = bool.Parse(states[11]);
                Form1.Object.materialRadioButton7.Checked = bool.Parse(states[12]);
                Form1.Object.materialRadioButton8.Checked = bool.Parse(states[13]);
                Form1.Object.materialRadioButton9.Checked = bool.Parse(states[14]);
                Form1.Object.comboBox3.Text = states[15];
                Form1.Object.materialCheckBox16.Checked = bool.Parse(states[16]);
                Form1.Object.materialCheckBox14.Checked = bool.Parse(states[17]);
                Form1.Object.comboBox6.Text = states[18];
                Form1.Object.materialCheckBox20.Checked = bool.Parse(states[19]);
                Form1.Object.materialCheckBox6.Checked = bool.Parse(states[20]);
                Form1.Object.materialCheckBox7.Checked = bool.Parse(states[21]);
                Form1.Object.materialSingleLineTextField6.Text = states[22];

                Form1.Object.chatMacro1 = states[23];
                Form1.Object.chatMacro2 = states[24];
                Form1.Object.chatMacro3 = states[25];
                Form1.Object.chatMacro4 = states[26];
                Form1.Object.chatMacro5 = states[27];


                //Spell Tracker Settings
                Form1.Object.materialCheckBox10.Checked = bool.Parse(states[28]);
                Form1.Object.materialCheckBox1.Checked = bool.Parse(states[29]);
                Form1.Object.materialCheckBox2.Checked = bool.Parse(states[30]);
            }
        }
    }
}