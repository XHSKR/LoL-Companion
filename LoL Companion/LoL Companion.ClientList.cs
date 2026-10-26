using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace LoL_Companion
{
    public partial class Form1
    {
        private readonly Dictionary<string, RiotClientInstance> riotClientRows = new Dictionary<string, RiotClientInstance>();
        private readonly Dictionary<string, string> signedInRiotAccounts = new Dictionary<string, string>();

        private void UpdateAccountConnectionStatus(LeagueClientSession[] snapshot)
        {
            if (snapshot.Length == 0) materialLabel14.Text = "실행 중인 League 클라이언트가 없습니다.";
            else if (snapshot.Length == 1 && snapshot[0].Phase == "Unavailable")
                materialLabel14.Text = "League 클라이언트 연결 실패\n" + snapshot[0].Error;
            else if (snapshot.Length > 1)
                materialLabel14.Text = string.Join("\n", snapshot.Select(client => client.Name + " | " + client.Phase));
            else if (!initializedClients.Contains(snapshot[0].Connection.Key))
                materialLabel14.Text = "League 계정 정보를 불러오는 중...";
        }

        private void UpdateClientRows(IEnumerable<ListViewItem> updated)
        {
            var incoming = updated.ToArray();
            var keys = new HashSet<string>(incoming.Select(item => (string)item.Tag));
            clientStatusList.BeginUpdate();
            try
            {
                foreach (ListViewItem item in clientStatusList.Items.Cast<ListViewItem>().ToArray())
                    if (item.Tag == null || !keys.Contains((string)item.Tag)) clientStatusList.Items.Remove(item);
                var existing = clientStatusList.Items.Cast<ListViewItem>().ToDictionary(item => (string)item.Tag);
                foreach (var item in incoming)
                {
                    ListViewItem current;
                    if (!existing.TryGetValue((string)item.Tag, out current)) { clientStatusList.Items.Add(item); continue; }
                    for (int i = 0; i < item.SubItems.Count; i++)
                        if (current.SubItems[i].Text != item.SubItems[i].Text) current.SubItems[i].Text = item.SubItems[i].Text;
                }
            }
            finally { clientStatusList.EndUpdate(); }
        }

        private void clientStatusList_SelectedIndexChanged(object sender, EventArgs e) { UpdateClientActionButtons(); }

        private void UpdateClientActionButtons()
        {
            RiotClientInstance target = null;
            string key = clientStatusList.SelectedItems.Count == 1 ? clientStatusList.SelectedItems[0].Tag as string : null;
            bool selected = key != null && riotClientRows.TryGetValue(key, out target);
            switchAccountButton.Enabled = clientOperationCancellation == null && selected && signedInRiotAccounts.ContainsKey(key) &&
                !clients.Clients.Any(client => client.Connection.RiotPort == target.Connection.Port) &&
                clients.Clients.Length == 1 && clients.Clients[0].Phase == "ChampSelect";
        }
    }
}
