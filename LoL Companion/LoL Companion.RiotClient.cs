using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace LoL_Companion
{
    public partial class Form1
    {
        private void startLeagueButton_Click(object sender, EventArgs e)
        {
            try
            {
                IntPtr window = RiotLoginWindow.Find();
                if (RiotLoginWindow.IsRunning() && window != IntPtr.Zero)
                {
                    if (!RiotLoginWindow.ForceForeground(window))
                        throw new InvalidOperationException("Riot Client 창을 활성화하지 못했습니다.");
                }
                else
                    using (Process process = Process.Start(new ProcessStartInfo
                    { FileName = RiotClientLauncher.ResolveExecutable(leagueLocation), UseShellExecute = false, CreateNoWindow = true })) { }
            }
            catch (Exception ex)
            { MessageBox.Show(this, ex.Message, "Riot Client", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
