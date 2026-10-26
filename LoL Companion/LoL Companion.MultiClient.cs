using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

namespace LoL_Companion
{
    public partial class Form1
    {
        private readonly MultiClientManager clients = new MultiClientManager();
        private readonly HashSet<string> initializedClients = new HashSet<string>();
        private bool refreshingClients;
        private bool closing;
        private LCU_Request[] draggedClientRequests;
        private CancellationTokenSource clientOperationCancellation;
        private bool clientPreferencesLoaded;
        private bool applyingClientSelection;

        internal ClientSelectionSettings CaptureClientSelection()
        {
            return new ClientSelectionSettings { Accounts = launchAccountsList.CheckedItems.Cast<string>().ToArray(),
                Locale = clientLocaleComboBox.SelectedIndex == 1 ? "en_AU" : "ko_KR" };
        }

        private void ReloadClientAccounts()
        {
            var selection = clientPreferencesLoaded ? CaptureClientSelection() : SettingSaver.LoadClientSelection();
            SettingSaver.LoadCredentials();
            applyingClientSelection = true;
            try
            {
                launchAccountsList.Items.Clear();
                foreach (string account in leagueID)
                    launchAccountsList.Items.Add(account, selection.Accounts.Contains(account, StringComparer.OrdinalIgnoreCase));
                clientLocaleComboBox.SelectedIndex = selection.Locale == "en_AU" ? 1 : 0;
                clientCountLabel.Text = "선택한 계정: " + launchAccountsList.CheckedItems.Count + "개";
            }
            finally { applyingClientSelection = false; clientPreferencesLoaded = true; }
        }

        private void SaveClientSelection(string[] pendingAccounts = null)
        {
            if (!clientPreferencesLoaded || applyingClientSelection) return;
            var selection = CaptureClientSelection();
            if (pendingAccounts != null) selection.Accounts = pendingAccounts;
            try { SettingSaver.SaveClientSelection(selection); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
            { multiClientStatusLabel.Text = "선택 저장 실패: " + ex.Message; }
        }

        private void clientLocaleComboBox_SelectedIndexChanged(object sender, EventArgs e) { SaveClientSelection(); }
        private void cancelMultiLoginButton_Click(object sender, EventArgs e) { clientOperationCancellation?.Cancel(); }

        private void SetClientOperationBusy(bool busy)
        {
            launchClientsButton.Enabled = launchAccountsList.Enabled = clientLocaleComboBox.Enabled =
                reloadAccountsButton.Enabled = startLeagueButton.Enabled = !busy;
            cancelMultiLoginButton.Enabled = busy;
        }

        private async Task RunClientActionAsync(Func<LCU_Request, Task> action, params string[] phases)
        {
            try
            {
                await clients.RefreshAsync(false);
                int completed = await clients.ApplyAsync(client => action(client.Request), phases);
                multiClientStatusLabel.Text = completed + "개 클라이언트에 적용했습니다.";
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                    MessageBox.Show(this, ex.Message, "Client action", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private Task JoinQueueAsync(int queueId)
        {
            return RunClientActionAsync(async request =>
            {
                await request.POST("/lol-lobby/v2/lobby", "{\"queueId\":" + queueId + "}");
                await request.PUT("/lol-lobby/v2/lobby/members/localMember/position-preferences",
                    "{\"firstPreference\":\"JUNGLE\",\"secondPreference\":\"MIDDLE\"}");
                await request.POST("/lol-lobby/v2/lobby/matchmaking/search", "0");
            }, "None", "Lobby");
        }

        private void reloadAccountsButton_Click(object sender, EventArgs e) { ReloadClientAccounts(); }

        private void launchAccountsList_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            int count = launchAccountsList.CheckedItems.Count;
            if (e.CurrentValue != CheckState.Unchecked) count--;
            if (e.NewValue != CheckState.Unchecked) count++;
            clientCountLabel.Text = "선택한 계정: " + count + "개";
            var selected = launchAccountsList.CheckedItems.Cast<string>().ToList();
            string account = (string)launchAccountsList.Items[e.Index];
            selected.RemoveAll(value => string.Equals(value, account, StringComparison.OrdinalIgnoreCase));
            if (e.NewValue != CheckState.Unchecked) selected.Add(account);
            SaveClientSelection(selected.ToArray());
        }

        private async Task RefreshClientsAsync()
        {
            if (refreshingClients || closing) return;
            refreshingClients = true;
            try
            {
                await clients.RefreshAsync(materialCheckBox8.Checked);
                if (closing || IsDisposed || Disposing) return;
                var snapshot = clients.Clients;
                initializedClients.RemoveWhere(key => !snapshot.Any(client => client.Connection.Key == key && client.Phase != "Unavailable"));
                UpdateAccountConnectionStatus(snapshot);
                var riotInstances = await Task.Run(() => RiotClientDiscovery.DiscoverAll());
                var riotRows = await Task.WhenAll(riotInstances.Select(async instance =>
                {
                    var league = snapshot.FirstOrDefault(client => client.Connection.RiotPort == instance.Connection.Port);
                    string account = league?.Name ?? "Riot PID " + instance.ProcessId;
                    string state = "로그인 대기";
                    string error = "";
                    string username = null;
                    try
                    {
                        using (var api = new RiotClientApi(instance.Connection))
                        using (var poll = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                        {
                            username = await MultiClientLaunchService.ReadLoggedInUsernameAsync(api, poll.Token);
                            if (username != null) { account = username; state = "Riot 로그인 완료"; }
                        }
                    }
                    catch (Exception ex) { error = ex.GetType().Name; }
                    if (league != null)
                    {
                        state += " / " + (league.Phase == "Unavailable" ? "League 연결 실패" : league.Phase);
                        if (!string.IsNullOrEmpty(league.Error))
                            error = string.IsNullOrEmpty(error) ? league.Error : error + " / " + league.Error;
                    }
                    return Tuple.Create(new ListViewItem(new[] { account, state, instance.ProcessId.ToString(), error })
                        { Tag = instance.Key }, instance, username);
                }));
                if (closing || IsDisposed || Disposing) return;
                riotClientRows.Clear();
                signedInRiotAccounts.Clear();
                foreach (var row in riotRows)
                {
                    riotClientRows[row.Item2.Key] = row.Item2;
                    if (row.Item3 != null) signedInRiotAccounts[row.Item2.Key] = row.Item3;
                }
                var displayRows = riotRows.Select(row => row.Item1).ToList();
                foreach (var client in snapshot.Where(client => !riotInstances.Any(instance => instance.Connection.Port == client.Connection.RiotPort)))
                    displayRows.Add(new ListViewItem(new[] { client.Name, client.Phase,
                        client.Connection.ProcessId.ToString(), client.Error ?? "" }) { Tag = "league:" + client.Connection.Key });
                UpdateClientRows(displayRows);
                foreach (var client in snapshot.Where(client => client.Phase != "Unavailable" &&
                    (client.Events.LCU == null || !client.Events.LCU.IsAlive)))
                {
                    try { await Task.Run(() => client.Events.ConnectToLCU()); }
                    catch (Exception ex) { System.Diagnostics.Trace.WriteLine("LCU subscription failed: " + ex.GetType().Name); }
                    if (closing) return;
                }
                if (materialCheckBox13.Checked)
                    foreach (var client in snapshot.Where(client => client.Phase != "Unavailable" && client.Events.LCU_Debug == null))
                        try { await Task.Run(() => client.Events.Debug()); } catch (Exception) { }
                isConnectedtoWebsocket = snapshot.Any(client => client.Events.LCU != null && client.Events.LCU.IsAlive);
                foreach (var client in snapshot.Where(client => client.Phase != "Unavailable" && !initializedClients.Contains(client.Connection.Key)))
                {
                    try { await RetrieveAccountInfo(client.Request); initializedClients.Add(client.Connection.Key); }
                    catch (Exception ex)
                    {
                        if (!closing && !IsDisposed && snapshot.Length == 1)
                            materialLabel14.Text = "League 계정 정보 조회 실패: " + LeagueClientSession.DescribeError(ex);
                        System.Diagnostics.Trace.WriteLine("Account information failed: " + ex.GetType().Name);
                    }
                }
                if (snapshot.Length > 1) UpdateAccountConnectionStatus(snapshot);
                runningClientsLabel.Text = "Riot " + riotInstances.Count + "개 · League " + snapshot.Length + "개";
            }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed) runningClientsLabel.Text = "클라이언트 검색 실패: " + ex.GetType().Name;
            }
            finally { refreshingClients = false; }
        }

        private async void launchClientsButton_Click(object sender, EventArgs e)
        {
            if (clientOperationCancellation != null) return;
            string[] accounts = launchAccountsList.CheckedItems.Cast<string>().ToArray();
            int count = accounts.Length;
            string locale = clientLocaleComboBox.SelectedIndex == 1 ? "en_AU" : "ko_KR";
            var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(Math.Max(1, count) * 8));
            clientOperationCancellation = cancellation;
            SetClientOperationBusy(true);
            try
            {
                await new MultiClientLaunchService().LaunchAsync(accounts, count, leaguePass, locale, leagueLocation,
                    message => { if (!closing && !IsDisposed) multiClientStatusLabel.Text = message; }, cancellation.Token);
            }
            catch (OperationCanceledException)
            { if (!closing && !IsDisposed) multiClientStatusLabel.Text = "추가 로그인 취소 / 시간 초과. 로그인한 Riot Client는 유지됩니다."; }
            catch (Exception ex)
            {
                if (!closing && !IsDisposed)
                {
                    multiClientStatusLabel.Text = "Riot 다중 로그인 중단";
                    MessageBox.Show(this, ex.Message, "Riot Client 로그인", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                clientOperationCancellation = null;
                cancellation.Dispose();
                if (!closing && !IsDisposed) SetClientOperationBusy(false);
            }
        }

        private async Task DodgeClientsAsync()
        {
            await clients.RefreshAsync(false);
            int dodged = await clients.ApplyToPhaseAsync("ChampSelect", async client =>
            {
                using (var server = new LeagueEdgeClient(client.Request))
                    await new QuickDodgeService(client.Request, server).DodgeAsync();
            });
            string result = "닷지: " + dodged + "개 클라이언트";
            multiClientStatusLabel.Text = result;
        }
    }
}
