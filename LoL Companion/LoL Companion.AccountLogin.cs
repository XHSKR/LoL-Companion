using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LoL_Companion
{
    public partial class Form1
    {
        private CancellationTokenSource accountLoginCancellation;

        private void ReloadLoginAccounts()
        {
            string selected = comboBox2.Text;
            SettingSaver.LoadCredentials();
            comboBox2.Items.Clear();
            comboBox2.Items.AddRange(leagueID.Cast<object>().ToArray());
            if (comboBox2.Items.Contains(selected)) comboBox2.SelectedItem = selected;
            else if (comboBox2.Items.Count > 0) comboBox2.SelectedIndex = 0;
        }

        private async void materialRaisedButton38_Click(object sender, EventArgs e)
        {
            await LoginSelectedAccountAsync("en_AU");
        }

        private async void materialRaisedButton30_Click(object sender, EventArgs e)
        {
            await LoginSelectedAccountAsync("ko_KR");
        }

        private void cancelLoginButton_Click(object sender, EventArgs e)
        {
            accountLoginCancellation?.Cancel();
        }

        private void startLeagueButton_Click(object sender, EventArgs e)
        {
            try
            {
                RiotClientLauncher.Start(RiotClientLauncher.ResolveExecutable(leagueLocation), "ko_KR", true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Start League", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task LoginSelectedAccountAsync(string locale)
        {
            if (accountLoginCancellation != null) return;
            ReloadLoginAccounts();
            string username = comboBox2.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(leaguePass) ||
                (username.StartsWith("username", StringComparison.Ordinal) && leaguePass == "password"))
            {
                MessageBox.Show(this, "실행 폴더의 credentials.conf를 수정해 주세요.\n첫 줄: 쉼표로 구분한 로그인 ID 목록\n둘째 줄: 공통 비밀번호",
                    "Auto Login", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            accountLoginCancellation = cancellation;
            comboBox2.Enabled = materialRaisedButton38.Enabled = materialRaisedButton30.Enabled = startLeagueButton.Enabled = false;
            cancelLoginButton.Enabled = true;
            try
            {
                RiotClientLauncher.EnsureGameIsNotRunning();
                string executable = await Task.Run(() => RiotClientLauncher.ResolveExecutable(leagueLocation), cancellation.Token);
                loginStatusLabel.Text = "Riot Client 연결 중...";
                RiotClientLauncher.Start(executable, locale, false);
                RiotClientConnection connection = await RiotClientLauncher.WaitForConnectionAsync(cancellation.Token);
                await RiotClientLauncher.CloseLeagueClientAsync(cancellation.Token);
                using (var api = new RiotClientApi(connection))
                    await new AccountLoginService(api, RiotClientLauncher.EnsureGameIsNotRunning,
                        submitThroughUi: RiotLoginUi.SubmitAsync).LoginAsync(username, leaguePass, locale,
                        message => { if (!IsDisposed && !Disposing) loginStatusLabel.Text = message; }, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                RiotClientLauncher.Start(executable, locale, true);
                loginStatusLabel.Text = "로그인 완료 · League 실행 중";
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed && !Disposing) loginStatusLabel.Text = "로그인 취소됨 (또는 대기 시간 초과)";
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !Disposing)
                {
                    loginStatusLabel.Text = "로그인 실패";
                    MessageBox.Show(this, ex.Message, "Auto Login", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                accountLoginCancellation = null;
                cancellation.Dispose();
                if (!IsDisposed && !Disposing)
                {
                    comboBox2.Enabled = materialRaisedButton38.Enabled = materialRaisedButton30.Enabled = startLeagueButton.Enabled = true;
                    cancelLoginButton.Enabled = false;
                }
            }
        }
    }
}
