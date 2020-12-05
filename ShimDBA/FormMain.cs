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
using static SDB.SdbFile.TagValue;

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
            statusLabelPath.Text = "";
            statusLabelTag.Text = "";
            WireControl(this);
            Icon = Icon.ExtractAssociatedIcon(Assembly.GetEntryAssembly().Location);
        }

        void WireControl(Control control)
        {
            control.MouseDown += Form1_MouseDown;
            foreach (Control c in control.Controls)
                WireControl(c);
        }

        void Form1_DragEnter(object sender, DragEventArgs e) => e.Effect = DragDropEffects.Copy;

        void Form1_DragDrop(object sender, DragEventArgs e)
        {
            foreach (var fn in (string[])e.Data.GetData("FileDrop"))
                LoadSDB(fn);
        }

        void LoadSDB(string fn)
        {
            foreach (TreeNode node in treeView1.Nodes)
                if (node.Tag is SdbView sv1 && sv1.File.SourceFile == fn) {
                    NavigateTo(node, false);
                    return;
                }

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
                foreach (var fix in sv.Fixes) {
                    var iindex = 6;
                    switch (fix.Type) {
                        case TAG_PATCH: iindex = 13; break;
                        case TAG_FILE: iindex = 4; break;
                        case TAG_MSI_TRANSFORM: iindex = 11; break;
                        case TAG_FLAG: iindex = 12; break;
                        case TAG_INEXCLUDE: iindex = 14; break;
                    }
                    fixesNode.Nodes.Add(new TreeNode(fix.Name + " - " + fix.Tag.TagName()) { ImageIndex = iindex, SelectedImageIndex = iindex, Tag = fix });
                }
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
                var text = child.TagName();
                var name = list.ChildOrDefault<SdbEntryStringRef>(TAG_NAME)?.Value as string;
                if (!string.IsNullOrEmpty(name))
                    text += " - " + name;
                if (child.TypeId == TAG_INDEX) {
                    var tag = list.Child<SdbEntryWord>(TAG_INDEX_TAG).AsTag();
                    var key = list.Child<SdbEntryWord>(TAG_INDEX_KEY).AsTag();
                    var flags = list.ChildOrDefault<SdbEntryDWord>(TAG_INDEX_FLAGS)?.NValue();
                    var uniq = flags.HasValue && flags.Value != 0 ? ", UNIQ" : "";
                    var bits = list.Child<SdbEntryBinary>(TAG_INDEX_BITS);
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
                return new TreeNode(child.TagName() + " - " + child.Value) { Tag = child };
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

            var db = GetCurrentDb();
            if (db != null && db.Tag is SdbView dsv)
                statusLabelPath.Text = dsv.File.SourceFile;
            else
                statusLabelPath.Text = "";

            statusLabelTag.Text = "";
            if (tag is SdbEntryBinary bin) {
                if (bin.TypeId == TAG_INDEX_BITS && e.Node.Parent.Tag is SdbEntryList parent) {
                    var keyType = parent.Child<SdbEntryWord>(TAG_INDEX_KEY).AsTag();
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
                    statusLabelTag.Text = $"0x{bin.Offset:X}";
                    return;
                } else if (bin.TypeId == TAG_PATCH_BITS) {
                    var bytes = bin.Bytes;
                    var sw = new StringWriter();
                    for (var i = 0; i < bytes.Length - 8;) {
                        // see: https://github.com/evil-e/sdb-explorer/blob/5fa024ac180a40be2cb360410bf0cab94ef10a1c/sdb.h#L267
                        var opcode = BitConverter.ToUInt32(bytes, i);
                        var actionSize = BitConverter.ToInt32(bytes, i + 4);
                        var opName = opcode == 0 ? " (end)" : opcode == 2 ? " (replace)" : opcode == 4 ? " (match)" : " (unknown)";
                        sw.WriteLine($"opcode 0x{opcode:X}{opName} size={actionSize}");
                        if (opcode == 2 || opcode == 4) {
                            var patternSize = BitConverter.ToInt32(bytes, i + 8);
                            var rva = BitConverter.ToUInt32(bytes, i + 12);
                            var unknown = BitConverter.ToUInt32(bytes, i + 16);
                            var name = Encoding.Unicode.GetString(bytes, i + 20, 64);
                            var trimAt = name.IndexOf('\0');
                            if (trimAt != -1)
                                name = name.Substring(0, trimAt);
                            sw.WriteLine($"   patternSize={patternSize}, rva=0x{rva:X8}, unknown={unknown:X8}, name={name}");
                            DumpHex(sw, bytes, i + 20 + 64, patternSize);
                        } else {
                            DumpHex(sw, bytes, i + 8, actionSize);
                        }
                        if (opcode == 0 || actionSize <= 0)
                            break;
                        i += actionSize;
                    }
                    richTextBox1.Text = sw.ToString();
                    statusLabelTag.Text = $"0x{bin.Offset:X}";
                    return;
                }
            } else if (tag is ISdbEntry entry) {
                if (entry.TypeId == TAG_DATABASE)
                    richTextBox1.Text = "";
                else
                    richTextBox1.Text = DumpTags(entry);
                statusLabelTag.Text = $"0x{entry.Offset:X}";
            } else if (tag is SdbView sv) {
                var metadata = new[] {
                    $"FileName: {sv.File.SourceFile}",
                    $"FileVersion: {sv.File.MajorVersion}.{sv.File.MinorVersion}",
                    $"",
                    }.Concat(
                    sv.Database.Children.TakeWhile(ch => ch.TypeId != TAG_EXE && ch.TypeId != TAG_LIBRARY)
                    .Select(en => $"{en.TagName()}: {FormatEntry(en)}")
                );
                richTextBox1.Text = string.Join("\r\n", metadata);
            } else if (tag is SdbViewApp app) {
                richTextBox1.Text = $"AppName: {app.Name}\r\nAppID: {app.Id}";
            } else if (tag is SdbViewExe exe) {
                richTextBox1.Text = DumpTags(exe.Tag);
                statusLabelTag.Text = $"0x{exe.Tag.Offset:X}";
            } else if (tag is SdbViewFix fix) {
                richTextBox1.Text = DumpTags(fix.Tag);
                statusLabelTag.Text = $"0x{fix.Tag.Offset:X}";
            } else {
                richTextBox1.Text = "";
            }
        }

        static void DumpHex(StringWriter sw, byte[] bytes, int offest, int count)
        {
            while (count > 0) {
                sw.Write("  ");
                var perLine = Math.Min(16, count);
                for (var i = 0; i < perLine; i++) {
                    sw.Write(' ');
                    sw.Write(bytes[offest + i].ToString("X2"));
                }
                sw.WriteLine();
                count -= 16;
                offest += 16;
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
            sw.Write($"{entry.TagName()}");
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
            } else if (entry.TypeId == TAG_TIME) {
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

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            System.Diagnostics.Process.Start("https://github.com/omgtehlion/ShimDBA");
        }

        private void openToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (openFileDialog1.ShowDialog(this) == DialogResult.OK)
                LoadSDB(openFileDialog1.FileName);
        }

        private void closeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var db = GetCurrentDb();
            if (db != null && db.Tag is SdbView)
                db.Remove();
        }
    }
}
