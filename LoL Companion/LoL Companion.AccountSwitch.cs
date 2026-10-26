using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace LoL_Companion
{
    public partial class Form1
    {
        private async void switchAccountButton_Click(object sender, EventArgs e)
        {
            if (clientOperationCancellation != null || clientStatusList.SelectedItems.Count != 1) return;
            string key = clientStatusList.SelectedItems[0].Tag as string;
            RiotClientInstance target;
            string username;
            if (key == null || !riotClientRows.TryGetValue(key, out target) || !signedInRiotAccounts.TryGetValue(key, out username)) return;
            var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            clientOperationCancellation = cancellation;
            SetClientOperationBusy(true);
            var elapsed = Stopwatch.StartNew();
            try
            {
                var current = clients.Clients;
                if (current.Length != 1 || current[0].Phase != "ChampSelect")
                    throw new InvalidOperationException("닷지할 League 클라이언트 하나가 챔피언 선택 중이어야 합니다.");
                await new LeagueAccountSwitchService().RunAsync(current[0], target, username,
                    message => { if (!closing && !IsDisposed) multiClientStatusLabel.Text = message; }, cancellation.Token);
                if (!closing && !IsDisposed) multiClientStatusLabel.Text = username + " 실행 완료 · " + elapsed.Elapsed.TotalSeconds.ToString("F1") + "초";
            }
            catch (OperationCanceledException)
            { if (!closing && !IsDisposed) multiClientStatusLabel.Text = "계정 전환 취소 / 시간 초과"; }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                {
                    multiClientStatusLabel.Text = "계정 전환 실패: " + ex.Message;
                    MessageBox.Show(this, ex.Message, "닷지 후 계정 실행", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                clientOperationCancellation = null;
                cancellation.Dispose();
                if (!closing && !IsDisposed) SetClientOperationBusy(false);
            }
        }
    }
}
