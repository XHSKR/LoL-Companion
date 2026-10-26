// Port of login_fields.rs (MIT); field-selection rules match the reference.
using System;
using System.Collections.Generic;
using System.Linq;

namespace LoL_Companion
{
    internal sealed class RiotLoginField
    {
        public string Name;
        public bool IsPassword;
        public bool Offscreen;
        public bool Enabled = true;
    }

    internal static class RiotLoginFields
    {
        public static Tuple<int, int> Pick(IReadOnlyList<RiotLoginField> fields)
        {
            int[] candidates = Enumerable.Range(0, fields.Count).Where(i => fields[i].Enabled && !fields[i].Offscreen).ToArray();
            if (candidates.Length < 2) return null;
            Func<int, bool> password = i => fields[i].IsPassword || Contains(fields[i].Name,
                "password", "passwd", "mat khau", "mot de passe", "passwort", "senha");
            Func<int, bool> username = i => !password(i) && Contains(fields[i].Name,
                "username", "user name", "email", "e-mail", "riot id", "account", "ten dang nhap", "tai khoan");
            int pass = Array.FindIndex(candidates, i => password(i));
            if (pass >= 0)
            {
                int p = candidates[pass];
                int u = Array.FindIndex(candidates, i => i != p && username(i));
                if (u < 0) u = Array.FindIndex(candidates, i => i != p && !password(i));
                return u < 0 ? null : Tuple.Create(candidates[u], p);
            }
            int user = Array.FindIndex(candidates, i => username(i));
            if (user >= 0) return Tuple.Create(candidates[user], candidates.First(i => i != candidates[user]));
            return candidates.Length == 2 ? Tuple.Create(candidates[0], candidates[1]) : null;
        }

        private static bool Contains(string name, params string[] values)
        {
            string normalized = (name ?? string.Empty).ToLowerInvariant().Replace('đ', 'd').Replace('ă', 'a')
                .Replace('â', 'a').Replace('ê', 'e').Replace('ô', 'o').Replace('ơ', 'o').Replace('ư', 'u');
            return values.Any(normalized.Contains);
        }
    }
}
