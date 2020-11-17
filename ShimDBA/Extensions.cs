using SDB;
using SDB.EntryTypes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShimDBA
{
    static class Extensions
    {
        public static T Child<T>(this SdbEntryList list, SdbFile.TagValue tag) where T : ISdbEntry => (T)list.Children.First(ch => ch.TypeId == tag);
        public static T ChildOrDefault<T>(this SdbEntryList list, SdbFile.TagValue tag) where T : ISdbEntry => (T)list.Children.FirstOrDefault(ch => ch.TypeId == tag);
        public static T Child<T>(this SdbFile file, SdbFile.TagValue tag) where T : ISdbEntry => (T)file.Children.First(ch => ch.TypeId == tag);
        public static T ChildOrDefault<T>(this SdbFile file, SdbFile.TagValue tag) where T : ISdbEntry => (T)file.Children.FirstOrDefault(ch => ch.TypeId == tag);
        public static SdbFile.TagValue AsTag(this SdbEntryWord e) => (SdbFile.TagValue)BitConverter.ToUInt16(e.Bytes, 0);
        public static uint NValue(this SdbEntryDWord e) => BitConverter.ToUInt32(e.Bytes, 0);
        public static Guid AsGuid(this SdbEntryBinary e) => new Guid(e.Bytes);
        public static V Upsert<K, V>(this Dictionary<K, V> dict, K key, Func<V> generator)
        {
            if (!dict.TryGetValue(key, out var result))
                dict.Add(key, result = generator());
            return result;
        }
        public static void SetCount<T>(this List<T> lst, int count)
        {
            if (lst.Count > count)
                lst.RemoveRange(count, lst.Count - count);
        }
        public static SdbFile.TagType GetTagType(this SdbFile.TagValue tag) => (SdbFile.TagType)((uint)tag & 0xF000);
        public static string TagName(this ISdbEntry tag) => tag.TypeId.ToString().Substring(4);
    }
}
