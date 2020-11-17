using SDB;
using SDB.EntryTypes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using TagValue = SDB.SdbFile.TagValue;

namespace ShimDBA
{
    public partial class FormMain : Form
    {
        List<TreeNode> NavHistory = new List<TreeNode>();
        int NavCurrent = -1;
        bool NavInProgress;

        public FormMain()
        {
            InitializeComponent();
            WireControl(this);
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location);
        }

        void WireControl(Control control)
        {
            control.MouseDown += Form1_MouseDown;
            foreach (Control c in control.Controls)
                WireControl(c);
        }

        void Form1_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Copy;
        }

        void Form1_DragDrop(object sender, DragEventArgs e)
        {
            foreach (var fn in (string[])e.Data.GetData("FileDrop"))
                LoadSDB(fn);
        }

        void LoadSDB(string fn)
        {
            var db = Sdb.LoadFile(fn);
            var sv = new SdbView(db);
            try {
                treeView1.BeginUpdate();
                var dbNode = new TreeNode(Path.GetFileName(fn)) { ImageIndex = 3, SelectedImageIndex = 3, Tag = sv };
                var tagsNode = new TreeNode("<raw tags>") { ImageIndex = 2, SelectedImageIndex = 2 };
                foreach (var ch in db.Children)
                    tagsNode.Nodes.Add(MakeNode(ch));
                dbNode.Nodes.Add(tagsNode);

                var appsNode = new TreeNode("Applications") { ImageIndex = 5, SelectedImageIndex = 5 };
                foreach (var app in sv.Applications.Values.OrderBy(app => app.Name)) {
                    var appNode = new TreeNode(app.Name) { ImageIndex = 8, SelectedImageIndex = 8, Tag = app };
                    foreach (var exe in app.Exes) {
                        var iindex = exe.Name.EndsWith(".exe") ? 9 : 1;
                        appNode.Nodes.Add(new TreeNode(exe.Name) { ImageIndex = iindex, SelectedImageIndex = iindex, Tag = exe });
                    }
                    appsNode.Nodes.Add(appNode);
                }
                dbNode.Nodes.Add(appsNode);

                var fixesNode = new TreeNode("Compatibility fixes") { ImageIndex = 7, SelectedImageIndex = 7 };
                fixesNode.Nodes.Add(new TreeNode("yyy") { ImageIndex = 6, SelectedImageIndex = 6 });
                dbNode.Nodes.Add(fixesNode);

                var modesNode = new TreeNode("Compatibility modes") { ImageIndex = 10, SelectedImageIndex = 10 };
                modesNode.Nodes.Add(new TreeNode("zzz") { ImageIndex = 10, SelectedImageIndex = 10 });
                dbNode.Nodes.Add(modesNode);

                treeView1.Nodes.Add(dbNode);
                NavigateTo(dbNode, false);
            } finally {
                treeView1.EndUpdate();
            }
        }

        TreeNode MakeNode(ISdbEntry child)
        {
            if (child.Children.Count > 0) {
                var dim = false;
                var list = (SdbEntryList)child;
                var text = child.TypeId.ToString().Substring(4);
                var name = list.ChildOrDefault<SdbEntryStringRef>(TagValue.TAG_NAME)?.Value as string;
                if (!string.IsNullOrEmpty(name))
                    text += " - " + name;
                if (child.TypeId == TagValue.TAG_INDEX) {
                    var tag = list.Child<SdbEntryWord>(TagValue.TAG_INDEX_TAG).AsTag();
                    var key = list.Child<SdbEntryWord>(TagValue.TAG_INDEX_KEY).AsTag();
                    var flags = list.ChildOrDefault<SdbEntryDWord>(TagValue.TAG_INDEX_FLAGS)?.NValue();
                    var uniq = flags.HasValue && flags.Value != 0 ? ", UNIQ" : "";
                    var bits = list.Child<SdbEntryBinary>(TagValue.TAG_INDEX_BITS);
                    var count = bits.Bytes.Length / 12 /* sizeof INDEX_RECORD */;
                    text += $" - ({tag}, {key}{uniq}) count = {count}, file offset = {child.Offset:X8}";
                    dim = count == 0;
                }
                var tNode = new TreeNode(text) { Tag = CreateLazy(child) };
                if (dim)
                    tNode.ForeColor = Color.Gray;
                tNode.Nodes.Add(new TreeNode());
                return tNode;
            } else {
                return new TreeNode(child.TypeId + " - " + child.Value) { Tag = child };
            }
        }

        void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            try {
                e.Node.TreeView.BeginUpdate();
                var tNode = e.Node;
                if (tNode.Tag is LazyTag<ISdbEntry> ltag) {
                    tNode.Nodes.Clear();
                    foreach (var ch in ltag.Value.Children)
                        tNode.Nodes.Add(MakeNode(ch));
                    tNode.Tag = ltag.Value;
                }
            } finally {
                e.Node.TreeView.EndUpdate();
            }
        }

        void Form1_Load(object sender, EventArgs e)
        {
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
                LoadSDB(arg);
        }

        void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            if (e.Node == null)
                richTextBox1.Text = "";

            if (!NavInProgress) {
                NavCurrent++;
                NavHistory.SetCount(NavCurrent);
                NavHistory.Add(e.Node);
            }

            var tag = e.Node.Tag;
            if (tag is ILazyTag lt)
                tag = lt.Data;
            if (tag is SdbEntryBinary bin && bin.TypeId == TagValue.TAG_INDEX_BITS && e.Node.Parent.Tag is SdbEntryList parent) {
                var keyType = parent.Child<SdbEntryWord>(TagValue.TAG_INDEX_KEY).AsTag();
                var bytes = bin.Bytes;
                richTextBox1.Text = " ";
                var sw = new StringWriter();
                sw.WriteLine(@"{\rtf1\ansi\deff0\nouicompat{\fonttbl{\f0\fnil Microsoft Sans Serif;}}");
                sw.Write(@"\pard\f0\fs17 ");
                for (var i = 0; i < bytes.Length; i += 12) {
                    string key;
                    switch (keyType.GetTagType()) {
                        case SdbFile.TagType.TAG_TYPE_STRING:
                        case SdbFile.TagType.TAG_TYPE_STRINGREF:
                            key = new string(Encoding.Default.GetString(bytes, i, 8).Reverse().TakeWhile(c => c != 0).ToArray());
                            break;
                        default:
                            key = BitConverter.ToUInt64(bytes, i).ToString("X16");
                            break;
                    }
                    var value = BitConverter.ToUInt32(bytes, i + 8);
                    sw.Write($"{key} -> ");
                    sw.Write(@"{{\field{\*\fldinst{HYPERLINK " + $"0x{value:X}" + @" }}{\fldrslt{" + $"0x{value:X}" + @"}}}}");
                    sw.WriteLine(@"\par");
                }
                sw.WriteLine("}");
                var myRtf = sw.ToString();
                richTextBox1.Rtf = myRtf;
            } else if (tag is ISdbEntry entry) {
                richTextBox1.Text = DumpTags(entry);
            } else if (tag is SdbView sv) {
                var metadata = new[] {
                    $"FileName: {sv.File.SourceFile}",
                    $"FileVersion: {sv.File.MajorVersion}.{sv.File.MinorVersion}",
                    $"",
                    }.Concat(
                    sv.Database.Children.TakeWhile(ch => ch.TypeId != TagValue.TAG_EXE && ch.TypeId != TagValue.TAG_LIBRARY)
                    .Select(en => $"{en.TypeId.ToString().Substring(4)}: {FormatEntry(en)}")
                );
                richTextBox1.Text = string.Join("\r\n", metadata);
            } else if (tag is SdbViewApp app) {
                richTextBox1.Text = "App";
            } else if (tag is SdbViewExe exe) {
                richTextBox1.Text = DumpTags(exe.Tag);
            } else {
                richTextBox1.Text = "";
            }
        }

        static string DumpTags(ISdbEntry entry)
        {
            var sw = new StringWriter();
            DumpEntry(entry, sw, 0);
            return sw.ToString();
        }
        static readonly char[] MAX_PADDING = new string(' ', 1024).ToArray();
        static void Pad(StringWriter sw, int indent) => sw.Write(MAX_PADDING, 0, indent * 3);
        static void DumpEntry(ISdbEntry entry, StringWriter sw, int indent)
        {
            Pad(sw, indent);
            sw.Write($"{entry.TypeId.ToString().Substring(4)}");
            if (entry is SdbEntryList list) {
                sw.WriteLine();
                foreach (var ch in list.Children)
                    DumpEntry(ch, sw, indent + 1);
            } else {
                sw.WriteLine($": {FormatEntry(entry)}");
            }
        }

        static string FormatEntry(ISdbEntry entry)
        {
            if (entry is SdbEntryBinary bin) {
                if (bin.Bytes.Length == 16)
                    return $"{entry.Value}";
                else
                    return $"{bin.Bytes.Length} bytes at offset 0x{bin.Offset:X}";
            } else if (entry.TypeId == TagValue.TAG_TIME) {
                var tmValue = (long)entry.Value;
                return $"{entry.Value} ({DateTime.FromFileTime(tmValue):s})";
            } else {
                return $"{entry.Value}";
            }
        }

        interface ILazyTag { object Data { get; } }
        class LazyTag<T> : ILazyTag { public T Value; public object Data => Value; }
        static LazyTag<T> CreateLazy<T>(T val) => new LazyTag<T> { Value = val };

        void richTextBox1_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            var node = FindNode(GetCurrentDb(), int.Parse(e.LinkText.Replace("0x", ""), System.Globalization.NumberStyles.HexNumber));
            if (node != null) {
                NavigateTo(node, false);
            } else {
                // show in status bar
            }
        }

        TreeNode GetCurrentDb()
        {
            var n = treeView1.SelectedNode;
            while (n != null) {
                if (n.Tag is SdbView)
                    return n;
                n = n.Parent;
            }
            return null;
        }

        TreeNode FindNode(TreeNode node, int offset)
        {
            if (node == null)
                return null;
            //if (node.Tag is ISdbEntry e && e.Offset == offset)
            //    return node;
            //if (node.Tag is LazyTag<ISdbEntry> lt && lt.Value.Offset == offset)
            //    return node;
            if (node.Tag is SdbViewExe ve && ve.Tag.Offset == offset)
                return node;
            foreach (TreeNode child in node.Nodes) {
                var result = FindNode(child, offset);
                if (result != null)
                    return result;
            }
            return null;
        }

        void Form1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.XButton1)
                NavigateTo(NavHistory[NavCurrent = Math.Max(0, NavCurrent - 1)], true);
            else if (e.Button == MouseButtons.XButton2)
                NavigateTo(NavHistory[NavCurrent = Math.Min(NavHistory.Count - 1, NavCurrent + 1)], true);
        }

        void NavigateTo(TreeNode node, bool ignoreHistory)
        {
            if (node == treeView1.SelectedNode)
                return;
            try {
                if (ignoreHistory)
                    NavInProgress = true;
                treeView1.SelectedNode = node;
                node.EnsureVisible();
                treeView1.Focus();
            } finally {
                if (ignoreHistory)
                    NavInProgress = false;
            }
        }
    }
}
