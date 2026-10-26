using System;
using System.Threading;
using System.Windows.Forms;

namespace LoL_Companion
{
    public partial class Form1
    {
        private async void closeRiotClientButton_Click(object sender, EventArgs e)
        {
            if (clientOperationCancellation != null || clientStatusList.SelectedItems.Count != 1) return;
            string key = clientStatusList.SelectedItems[0].Tag as string;
            RiotClientInstance target;
            if (key == null || !riotClientRows.TryGetValue(key, out target)) return;
            string account = clientStatusList.SelectedItems[0].Text;
            var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            clientOperationCancellation = cancellation;
            SetClientOperationBusy(true);
            multiClientStatusLabel.Text = account + " Riot Client 종료 중...";
            try
            {
                await new RiotClientCloseService().CloseAsync(target, cancellation.Token);
                if (!closing && !IsDisposed) multiClientStatusLabel.Text = account + " Riot Client 종료 완료";
            }
            catch (OperationCanceledException)
            { if (!closing && !IsDisposed) multiClientStatusLabel.Text = "Riot Client 종료 확인 취소 / 시간 초과"; }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                {
                    multiClientStatusLabel.Text = "Riot Client 종료 실패: " + ex.Message;
                    MessageBox.Show(this, ex.Message, "Riot Client 종료", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                clientOperationCancellation = null;
                cancellation.Dispose();
                if (!closing && !IsDisposed)
                {
                    SetClientOperationBusy(false);
                    await RefreshClientsAsync();
                }
            }
        }
    }
}
