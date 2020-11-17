using SDB;
using SDB.EntryTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using TagValue = SDB.SdbFile.TagValue;

namespace ShimDBA
{
    class SdbView
    {
        public readonly SdbFile File;
        public readonly SdbEntryList Database;
        public Dictionary<Guid, SdbViewApp> Applications = new Dictionary<Guid, SdbViewApp>();

        public SdbView(SdbFile db)
        {
            File = db;
            Database = db.Child<SdbEntryList>(TagValue.TAG_DATABASE);
            var combinedByName = new Dictionary<string, Guid>();
            foreach (var exeTag in Database.Children.Where(c => c.TypeId == TagValue.TAG_EXE).Cast<SdbEntryList>()) {
                var appName = (string)exeTag.Child<SdbEntryStringRef>(TagValue.TAG_APP_NAME).Value;
                var appId = exeTag.ChildOrDefault<SdbEntryBinary>(TagValue.TAG_APP_ID)?.AsGuid();
                var key = appId ?? combinedByName.Upsert(appName, () => Guid.NewGuid());
                if (!Applications.TryGetValue(key, out var app)) {
                    Applications.Add(key, app = new SdbViewApp { Id = appId, Name = appName });
                } else {
                    if (app.Name != appName)
                        throw new Exception($"App name changed {app.Name} → {appName}");
                }
                app.Exes.Add(new SdbViewExe(exeTag));
            }
        }
    }

    class SdbViewApp
    {
        public string Name;
        public Guid? Id;
        public List<SdbViewExe> Exes = new List<SdbViewExe>();
    }

    class SdbViewExe
    {
        public string Name;
        public readonly SdbEntryList Tag;

        public SdbViewExe(SdbEntryList exeTag)
        {
            Tag = exeTag;
            Name = exeTag.Child<SdbEntryStringRef>(TagValue.TAG_NAME).Value as string;
        }
    }
}
