using SDB;
using SDB.EntryTypes;
using System;
using System.Collections.Generic;
using System.Linq;
using static SDB.SdbFile.TagValue;

namespace ShimDBA
{
    class SdbView
    {
        public readonly SdbFile File;
        public readonly SdbEntryList Database;
        public readonly SdbEntryList Library;
        public Dictionary<Guid, SdbViewApp> Applications = new Dictionary<Guid, SdbViewApp>();
        public List<SdbViewFix> Fixes = new List<SdbViewFix>();

        public SdbView(SdbFile db)
        {
            File = db;
            Database = db.Child<SdbEntryList>(TAG_DATABASE);
            Library = Database.ChildOrDefault<SdbEntryList>(TAG_LIBRARY);
            var combinedByName = new Dictionary<string, Guid>();
            foreach (var exeTag in Database.Children.Where(c => c.TypeId == TAG_EXE).Cast<SdbEntryList>()) {
                var appName = (string)exeTag.Child<SdbEntryStringRef>(TAG_APP_NAME).Value;
                var appId = exeTag.ChildOrDefault<SdbEntryBinary>(TAG_APP_ID)?.AsGuid();
                var key = appId ?? combinedByName.Upsert(appName, () => Guid.NewGuid());
                if (!Applications.TryGetValue(key, out var app)) {
                    Applications.Add(key, app = new SdbViewApp { Id = appId, Name = appName });
                } else {
                    if (app.Name != appName)
                        throw new Exception($"App name changed {app.Name} → {appName}");
                }
                app.Exes.Add(new SdbViewExe(exeTag));
            }
            if (Library != null)
                foreach (var tag in Library.Children) {
                    if (tag is SdbEntryList list)
                        Fixes.Add(new SdbViewFix(list));
                }
            // patches can be located outside <LIBRARY>
            foreach (var tag in Database.Children.Where(c => c.TypeId == TAG_PATCH)) {
                if (tag is SdbEntryList list)
                    Fixes.Add(new SdbViewFix(list));
            }
        }
    }

    class SdbViewApp
    {
        public string Name;
        public Guid? Id;
        public List<SdbViewExe> Exes = new List<SdbViewExe>();
    }

    // anything inside <LIBRARY>
    class SdbViewFix
    {
        public SdbFile.TagValue Type;
        public string Name;
        public readonly SdbEntryList Tag;
        public SdbViewFix(SdbEntryList tag)
        {
            Tag = tag;
            Type = tag.TypeId;
            Name = tag.ChildOrDefault<SdbEntryStringRef>(TAG_NAME)?.Value as string;
            if (Type == TAG_INEXCLUDE)
                Name = tag.Child<SdbEntryStringRef>(TAG_MODULE).Value as string;
        }
    }

    class SdbViewExe
    {
        public string Name;
        public readonly SdbEntryList Tag;
        public SdbViewExe(SdbEntryList exeTag)
        {
            Tag = exeTag;
            Name = exeTag.Child<SdbEntryStringRef>(TAG_NAME).Value as string;
        }
    }
}
