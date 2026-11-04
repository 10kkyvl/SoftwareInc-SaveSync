using System.Collections.Generic;

namespace SaveSync
{
    public static class SyncState
    {
        public const int Schema = 1;

        public static bool Pending;
        public static List<string> PendingMods;
        public static List<string> PendingChoices;
        public static List<string> PendingSkipped;
        public static List<string> Applied;
        public static List<string> Skipped;
        public static List<string> Resolved;
        public static bool LoadedFromRecord;
        public static string Failure;
        public static List<string> JustApplied;

        public static void Reset()
        {
            Pending = false;
            PendingMods = new List<string>();
            PendingChoices = new List<string>();
            PendingSkipped = new List<string>();
            Applied = new List<string>();
            Skipped = new List<string>();
            Resolved = new List<string>();
            Failure = null;
            JustApplied = new List<string>();
        }

        public static void Write(WriteDictionary data)
        {
            data["SchemaVersion"] = Schema;
            data["Pending"] = Pending;
            data["PendingMods"] = PendingMods.ToArray();
            data["PendingChoices"] = PendingChoices.ToArray();
            data["PendingSkipped"] = PendingSkipped.ToArray();
            data["Applied"] = Applied.ToArray();
            data["Skipped"] = Skipped.ToArray();
            data["Resolved"] = Resolved.ToArray();
        }

        public static void Read(WriteDictionary data)
        {
            Reset();
            Pending = data.Get("Pending", false);
            PendingMods = ToList(data.Get<string[]>("PendingMods", null));
            PendingChoices = ToList(data.Get<string[]>("PendingChoices", null));
            PendingSkipped = ToList(data.Get<string[]>("PendingSkipped", null));
            Applied = ToList(data.Get<string[]>("Applied", null));
            Skipped = ToList(data.Get<string[]>("Skipped", null));
            Resolved = ToList(data.Get<string[]>("Resolved", null));
        }

        private static List<string> ToList(string[] items)
        {
            List<string> list = new List<string>();
            if (items != null)
            {
                list.AddRange(items);
            }
            return list;
        }
    }
}
