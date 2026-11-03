// Loadout, Tables and the Add vehicle box. Tables is what makes the editor complete: every kind of record in a
// save gets a table, so nothing depends on someone having written a screen for it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Sr5Save
{
    sealed class LoadoutScreen : IScreen
    {
        static readonly string[] DifficultyNames = { "Player health", "Player damage", "Enemies", "Vehicles", "Notoriety", "Ammo reserve", "Timer speed", "Driving" };

        sealed class Choice
        {
            public uint Hash;
            public string Text;
            public override string ToString() { return Text; }
        }

        readonly Panel view = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly TableLayoutPanel table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Location = new Point(18, 14) };
        GameSave save;
        bool binding;
        int row;

        public Control View { get { return view; } }

        public LoadoutScreen()
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            view.Controls.Add(table);
        }

        void Heading(string text)
        {
            Label l = new Label { Text = text, AutoSize = true, Font = new Font(view.Font ?? SystemFonts.DefaultFont, FontStyle.Bold), ForeColor = Ui.Purple, Margin = new Padding(0, row == 0 ? 0 : 16, 0, 4) };
            table.Controls.Add(l, 0, row);
            table.SetColumnSpan(l, 3);
            row++;
        }

        void Row(string label, Control c, string help)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 8, 14, 5) }, 0, row);
            c.Margin = new Padding(3, 4, 0, 4);
            table.Controls.Add(c, 1, row);
            table.Controls.Add(new Label { Text = help ?? "", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(10, 8, 0, 5) }, 2, row);
            row++;
        }

        ComboBox Picker(IEnumerable<Node> records, uint current, Action<uint> set)
        {
            ComboBox box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300 };
            List<Choice> choices = records.Select(r => new Choice { Hash = r.Key, Text = Names.Pretty(r.Id) }).OrderBy(c => c.Text).ToList();
            if (!choices.Any(c => c.Hash == current))
                choices.Insert(0, new Choice { Hash = current, Text = current == 0 ? "(nothing)" : Names.Pretty(Names.Get(current)) });
            foreach (Choice c in choices) box.Items.Add(c);
            box.SelectedItem = choices.First(c => c.Hash == current);
            box.SelectedIndexChanged += delegate { if (!binding && box.SelectedItem != null) set(((Choice)box.SelectedItem).Hash); };
            return box;
        }

        public void Bind(GameSave g)
        {
            save = g;
            binding = true;
            table.SuspendLayout();
            table.Controls.Clear();
            table.RowStyles.Clear();
            row = 0;

            List<Node> levels = g.DifficultyLevels;
            if (levels.Count > 0)
            {
                Heading("Difficulty");
                for (int i = 0; i < levels.Count; i++)
                {
                    Node level = levels[i];
                    NumericUpDown n = new NumericUpDown { Maximum = 10, Width = 80, Value = Math.Min(10, Math.Max(0, level.Int)) };
                    n.ValueChanged += delegate { if (!binding) level.SetRaw((uint)n.Value); };
                    Row(i < DifficultyNames.Length ? DifficultyNames[i] : "Slider " + (i + 1), n,
                        i == 0 ? "Custom sliders, 0 to 10. The names' order is from notes, not confirmed in the game." : null);
                }
            }

            List<Node> slots = g.PerkSlots;
            if (slots.Count > 0)
            {
                Heading("Perks equipped");
                List<Node> perks = g.OwnedPerks;
                foreach (Node slot in slots)
                {
                    Node handle = slot.Child("m_perk_entry_h");
                    if (handle == null) continue;
                    Node s = slot;
                    Row(Names.Pretty(slot.Id), Picker(perks, handle.Value, h => save.EquipPerk(s, h)),
                        slot == slots[0] ? "Lists the perks you own. Own more on the Unlocks screen, then reopen this one." : null);
                }
            }

            Node emotes = g.Find("emote_state");
            if (emotes != null)
            {
                Heading("Emotes");
                List<Node> owned = g.All(Crc.Of("collectible_emote_state")).Where(r => r.Child("m_available") != null && r.Child("m_available").Value != 0).ToList();
                foreach (string f in new[] { "m_primary_handle", "m_secondary_handle" })
                {
                    Node handle = emotes.Child(f);
                    if (handle != null) Row(f == "m_primary_handle" ? "First emote" : "Second emote", Picker(owned, handle.Value, h => handle.SetRaw(h)), null);
                }
            }

            Node room = g.Find("hq_room_info_state");
            Node theme = room == null ? null : room.Path("m_my_data.m_theme_info");
            Node voice = g.Find("avatar_persona_state");
            Node pitch = voice == null ? null : voice.Child("m_voice_pitch");
            if (theme != null || pitch != null)
            {
                Heading("Base and character");
                if (theme != null) Row("HQ theme", Picker(g.All(Crc.Of("hq_room_theme_info_state")), theme.Value, h => theme.SetRaw(h)), "Only themes this save has a record for are listed.");
                if (pitch != null)
                {
                    TextBox box = new TextBox { Width = 80, Text = pitch.Text };
                    box.Leave += delegate
                    {
                        if (binding) return;
                        string error = pitch.TrySetText(box.Text);
                        if (error != null) MessageBox.Show(view, error, "Voice pitch", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        box.Text = pitch.Text;
                    };
                    Row("Voice pitch", box, "A decimal number. The save's own value is the safe starting point.");
                }
            }
            table.ResumeLayout();
            binding = false;
        }
    }

    sealed class TablesScreen : IScreen
    {
        sealed class Kind
        {
            public uint Struct;
            public string Group, Title;
            public bool Risky;
            public int Count;
            public override string ToString() { return Group + ": " + Title + "  (" + Count + ")"; }
        }

        sealed class Row
        {
            public Node Record;
            public string Label;
            public Dictionary<string, Node> Values;
        }

        const int MaxColumns = 40, MaxSmallArray = 8;

        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly ListBox kinds = new ListBox { Dock = DockStyle.Left, Width = 330, IntegralHeight = false, BorderStyle = BorderStyle.None, ItemHeight = 22, DrawMode = DrawMode.OwnerDrawFixed };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly TextBox filter = new TextBox { Width = 150, Margin = new Padding(3, 6, 3, 3) };
        readonly ComboBox column = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(3, 6, 3, 3) };
        readonly TextBox bulkValue = new TextBox { Width = 90, Margin = new Padding(3, 6, 3, 3) };
        readonly Label help = Ui.Help("");
        GameSave save;
        List<Row> all = new List<Row>();
        List<Row> shown = new List<Row>();
        List<string> paths = new List<string>();

        public Control View { get { return view; } }

        public TablesScreen()
        {
            grid.VirtualMode = true;
            grid.SelectionMode = DataGridViewSelectionMode.CellSelect;
            grid.ColumnHeadersHeight = 44;
            grid.CellValueNeeded += delegate(object s, DataGridViewCellValueEventArgs a)
            {
                if (a.RowIndex >= shown.Count) return;
                Row r = shown[a.RowIndex];
                if (a.ColumnIndex == 0) { a.Value = r.Label; return; }
                Node n;
                a.Value = r.Values.TryGetValue(paths[a.ColumnIndex - 1], out n) ? Display(n) : "";
            };
            grid.CellValuePushed += delegate(object s, DataGridViewCellValueEventArgs a)
            {
                if (a.ColumnIndex == 0 || a.RowIndex >= shown.Count) return;
                Node n;
                if (!shown[a.RowIndex].Values.TryGetValue(paths[a.ColumnIndex - 1], out n)) return;
                string error = n.TrySetText(Convert.ToString(a.Value));
                if (error != null) MessageBox.Show(view, error, "Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            kinds.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush back = new SolidBrush(sel ? Ui.Selected : SystemColors.Window)) e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, kinds.Items[e.Index].ToString(), kinds.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 10, e.Bounds.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            kinds.SelectedIndexChanged += delegate { Pick(); };
            filter.TextChanged += delegate { Filter(); };
            FlowLayoutPanel bar = Ui.Bar();
            bar.Controls.Add(new Label { Text = "Find", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(filter);
            bar.Controls.Add(new Label { Text = "Set", AutoSize = true, Margin = new Padding(12, 9, 3, 3) });
            bar.Controls.Add(column);
            bar.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(bulkValue);
            bar.Controls.Add(Ui.MakeButton("For all listed", delegate { SetAll(); }));
            Panel right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(grid);
            right.Controls.Add(help);
            right.Controls.Add(bar);
            view.Controls.Add(right);
            view.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Ui.Line });
            view.Controls.Add(kinds);
        }

        static string Display(Node n)
        {
            if (n.Field != null && n.Field.IsHandle && n.Value != 0)
            {
                string found = Names.Find(n.Value);
                if (found != null) return found;
            }
            return n.Text;
        }

        // Scalars of a record by path. Long arrays stay in All data: a column per element is no use to anyone.
        static void Flatten(Node n, string prefix, Dictionary<string, Node> into, List<string> order)
        {
            foreach (Node c in n.Children)
            {
                string name = prefix + c.FieldName;
                if (c.Kind == NodeKind.Scalar) Put(name, c, into, order);
                else if (c.Kind == NodeKind.Struct) Flatten(c, name + ".", into, order);
                else if (c.Kind == NodeKind.Array && c.Children.Count <= MaxSmallArray && c.Children.All(x => x.Kind == NodeKind.Scalar))
                    for (int i = 0; i < c.Children.Count; i++) Put(name + "[" + i + "]", c.Children[i], into, order);
            }
        }

        static void Put(string name, Node c, Dictionary<string, Node> into, List<string> order)
        {
            into[name] = c;
            if (order != null && !order.Contains(name)) order.Add(name);
        }

        static string Heading(string path)
        {
            return string.Join(" ", path.Split('.').Select(p => Names.Pretty(p)));
        }

        public void Bind(GameSave g)
        {
            save = g;
            kinds.Items.Clear();
            Dictionary<uint, Kind> found = new Dictionary<uint, Kind>();
            foreach (Block b in g.File.Blocks)
                foreach (Node r in b.Records)
                {
                    Kind k;
                    if (!found.TryGetValue(r.Def.Hash, out k))
                    {
                        RecordKind info = Inventory.Of(r.Def);
                        found[r.Def.Hash] = k = new Kind { Struct = r.Def.Hash, Group = info.Group, Title = info.Title, Risky = info.Risky };
                    }
                    k.Count++;
                }
            foreach (Kind k in found.Values.OrderBy(x => x.Group).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)) kinds.Items.Add(k);
            if (kinds.Items.Count > 0) kinds.SelectedIndex = 0;
        }

        // For the UI check.
        public bool SelectKind(string title)
        {
            for (int i = 0; i < kinds.Items.Count; i++)
                if (((Kind)kinds.Items[i]).Title == title) { kinds.SelectedIndex = i; return true; }
            return false;
        }
        public int ColumnCount { get { return paths.Count; } }
        public int RowCount { get { return shown.Count; } }
        public void SetColumnForAll(string path, string value)
        {
            for (int i = 0; i < column.Items.Count; i++)
                if ((string)column.Items[i] == Heading(path)) column.SelectedIndex = i;
            bulkValue.Text = value;
            SetAll(false);
        }

        void Pick()
        {
            Kind k = kinds.SelectedItem as Kind;
            if (k == null || save == null) return;
            all = new List<Row>();
            paths = new List<string>();
            foreach (Node r in save.All(k.Struct))
            {
                Row row = new Row { Record = r, Label = r.Id.Length > 0 ? r.Id : k.Title, Values = new Dictionary<string, Node>() };
                Flatten(r, "", row.Values, paths);
                all.Add(row);
            }
            if (paths.Count > MaxColumns) paths = paths.Take(MaxColumns).ToList();
            grid.RowCount = 0;
            grid.Columns.Clear();
            grid.Columns.Add(Ui.Text("Item", 260));
            foreach (string p in paths)
            {
                DataGridViewTextBoxColumn c = Ui.Text(Heading(p), 110, false, false);
                grid.Columns.Add(c);
            }
            column.Items.Clear();
            foreach (string p in paths) column.Items.Add(Heading(p));
            if (column.Items.Count > 0) column.SelectedIndex = 0;
            bool risky = k.Risky;
            help.ForeColor = risky ? Color.FromArgb(160, 70, 0) : Ui.Muted;
            help.Text = risky ? "Story and world state: changing these can skip content or break a playthrough. Use Save as new."
                : paths.Count == 0 ? "Records of this kind hold nothing but lists. See All data."
                : "Type in any cell. A list that's too long for columns is in All data.";
            Filter();
        }

        void Filter()
        {
            string f = filter.Text.Trim();
            shown = f.Length == 0 ? all : all.Where(r => r.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            grid.RowCount = 0;
            grid.RowCount = shown.Count;
            grid.Invalidate();
        }

        void SetAll(bool ask = true)
        {
            if (column.SelectedIndex < 0 || column.SelectedIndex >= paths.Count) return;
            string path = paths[column.SelectedIndex];
            List<Node> targets = shown.Where(r => r.Values.ContainsKey(path)).Select(r => r.Values[path]).ToList();
            if (targets.Count == 0) return;
            if (ask && MessageBox.Show(view, "Set " + Heading(path) + " to " + bulkValue.Text + " on " + targets.Count + " items?", "Set for all listed",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            foreach (Node n in targets)
            {
                string error = n.TrySetText(bulkValue.Text);
                if (error != null)
                {
                    MessageBox.Show(view, error, "Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                }
            }
            grid.Invalidate();
        }
    }

    // Sections on the left, one row per option on the right.
    sealed class OptionPanel : Panel
    {
        readonly ListBox sections = new ListBox { Dock = DockStyle.Left, Width = 200, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24 };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly TextBox find = new TextBox { Width = 170, Margin = new Padding(3, 6, 3, 3) };
        readonly Label title = new Label { AutoSize = true, Margin = new Padding(6, 9, 12, 3), Font = new Font("Segoe UI", 9f, FontStyle.Bold), ForeColor = Ui.Purple };
        readonly FlowLayoutPanel bar = Ui.Bar();
        List<OptionRow> rows = new List<OptionRow>();
        bool binding;

        public Action<OptionRow> Edited;
        public FlowLayoutPanel Bar { get { return bar; } }

        public OptionRow Current
        {
            get { return grid.CurrentRow == null ? null : grid.CurrentRow.Tag as OptionRow; }
        }

        public OptionPanel()
        {
            Dock = DockStyle.Fill;
            grid.MultiSelect = false;
            grid.Columns.Add(Ui.Text("Option", 270));
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", Width = 240, SortMode = DataGridViewColumnSortMode.NotSortable });
            DataGridViewTextBoxColumn note = Ui.Text("About", 200, true);
            note.DefaultCellStyle.ForeColor = Ui.Muted;
            grid.Columns.Add(note);
            grid.CellValueChanged += ValueChanged;
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e) { if (e.RowIndex >= 0 && e.ColumnIndex == 1) PickColor(e.RowIndex); };
            grid.CellPainting += PaintSwatch;
            sections.SelectedIndexChanged += delegate { Fill(); };
            sections.DrawItem += delegate(object s, DrawItemEventArgs e)
            {
                if (e.Index < 0) return;
                bool sel = (e.State & DrawItemState.Selected) != 0;
                using (SolidBrush back = new SolidBrush(sel ? Ui.Selected : SystemColors.Window)) e.Graphics.FillRectangle(back, e.Bounds);
                TextRenderer.DrawText(e.Graphics, (string)sections.Items[e.Index], sections.Font, new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                    Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            find.TextChanged += delegate { Fill(); };
            bar.Controls.Add(title);
            bar.Controls.Add(new Label { Text = "Find option", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(find);
            Panel right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(grid);
            Controls.Add(right);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Ui.Line });
            Controls.Add(sections);
            Controls.Add(bar);
        }

        public void ShowRows(string heading, List<OptionRow> list)
        {
            string keep = sections.SelectedItem as string;
            rows = list;
            title.Text = heading;
            sections.BeginUpdate();
            sections.Items.Clear();
            foreach (string s in list.Select(r => r.Section).Distinct()) sections.Items.Add(s);
            sections.EndUpdate();
            if (sections.Items.Count == 0) { Fill(); return; }
            int at = keep == null ? 0 : sections.Items.IndexOf(keep);
            sections.SelectedIndex = at < 0 ? 0 : at;
            Fill();
        }

        void Fill()
        {
            string f = find.Text.Trim();
            string section = sections.SelectedItem as string;
            IEnumerable<OptionRow> pick = f.Length > 0
                ? rows.Where(r => r.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || r.Section.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                : rows.Where(r => r.Section == section);
            binding = true;
            grid.Rows.Clear();
            foreach (OptionRow r in pick)
            {
                int i = grid.Rows.Add();
                DataGridViewRow row = grid.Rows[i];
                row.Tag = r;
                row.Cells[0].Value = f.Length > 0 ? r.Section + ": " + r.Label : r.Label;
                row.Cells[2].Value = r.Note ?? "";
                row.Cells[1] = MakeCell(r);
                ShowValue(row, r);
            }
            binding = false;
        }

        static DataGridViewCell MakeCell(OptionRow r)
        {
            switch (r.Kind)
            {
                case OptionKind.Check: return new DataGridViewCheckBoxCell();
                case OptionKind.Choice:
                    return new DataGridViewComboBoxCell
                    {
                        DataSource = r.Choices,
                        DisplayMember = "Text",
                        ValueMember = "Value",
                        DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                        FlatStyle = FlatStyle.Flat
                    };
            }
            return new DataGridViewTextBoxCell();
        }

        static void ShowValue(DataGridViewRow row, OptionRow r)
        {
            DataGridViewCell cell = row.Cells[1];
            if (r.Kind == OptionKind.Check) cell.Value = r.Node.Value != 0;
            else if (r.Kind == OptionKind.Choice) cell.Value = r.Node.Value;
            else cell.Value = r.Value;
        }

        void ValueChanged(object sender, DataGridViewCellEventArgs a)
        {
            if (binding || a.RowIndex < 0 || a.ColumnIndex != 1) return;
            DataGridViewRow row = grid.Rows[a.RowIndex];
            OptionRow r = (OptionRow)row.Tag;
            object v = row.Cells[1].Value;
            string error = null;
            if (r.Kind == OptionKind.Check) r.Node.SetRaw(Convert.ToBoolean(v) ? 1u : 0u);
            else if (r.Kind == OptionKind.Choice) { if (v is uint) r.Node.SetRaw((uint)v); }
            else error = r.SetValue(Convert.ToString(v));
            binding = true;
            ShowValue(row, r);
            binding = false;
            if (error != null) MessageBox.Show(this, error, "Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (Edited != null) Edited(r);
        }

        void PickColor(int index)
        {
            OptionRow r = grid.Rows[index].Tag as OptionRow;
            if (r == null || r.Kind != OptionKind.Color) return;
            float[] v = r.GetColor();
            using (ColorDialog d = new ColorDialog { FullOpen = true, Color = Swatch(v) })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                float[] next = (float[])v.Clone();
                next[0] = d.Color.R / 255f;
                next[1] = d.Color.G / 255f;
                next[2] = d.Color.B / 255f;
                r.SetColor(next);
            }
            binding = true;
            ShowValue(grid.Rows[index], r);
            binding = false;
            if (Edited != null) Edited(r);
        }

        static Color Swatch(float[] v)
        {
            Func<float, int> c = x => float.IsNaN(x) ? 0 : (int)Math.Max(0, Math.Min(255, x * 255));
            return Color.FromArgb(c(v[0]), c(v[1]), c(v[2]));
        }

        void PaintSwatch(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 1) return;
            OptionRow r = grid.Rows[e.RowIndex].Tag as OptionRow;
            if (r == null || r.Kind != OptionKind.Color) return;
            e.Paint(e.CellBounds, DataGridViewPaintParts.All);
            Rectangle box = new Rectangle(e.CellBounds.Right - 34, e.CellBounds.Top + 5, 26, e.CellBounds.Height - 11);
            using (SolidBrush b = new SolidBrush(Swatch(r.GetColor()))) e.Graphics.FillRectangle(b, box);
            using (Pen p = new Pen(Color.FromArgb(150, 150, 160))) e.Graphics.DrawRectangle(p, box);
            e.Handled = true;
        }

        // For the UI check.
        public int SectionCount { get { return sections.Items.Count; } }
        public bool SelectFirst(string prefix)
        {
            foreach (string s in sections.Items)
                if (s.StartsWith(prefix, StringComparison.Ordinal)) { sections.SelectedItem = s; return true; }
            return false;
        }
        public bool SelectSection(string name)
        {
            int at = sections.Items.IndexOf(name);
            if (at < 0) return false;
            sections.SelectedIndex = at;
            return true;
        }
    }

    // Everything in the save and everything the editor could add, in one list you can search.
    sealed class InventoryScreen : IScreen
    {
        sealed class Row
        {
            public string Group, Kind, Item, State, Screen, Source;
        }

        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly TextBox find = new TextBox { Width = 220, Margin = new Padding(3, 6, 3, 3) };
        readonly ComboBox group = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190, Margin = new Padding(3, 6, 3, 3) };
        readonly ComboBox state = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 6, 3, 3) };
        readonly Label summary = Ui.Help("");
        List<Row> all = new List<Row>();
        List<Row> shown = new List<Row>();
        bool binding;

        public Action<string> OpenScreen;
        public Control View { get { return view; } }
        public int RowCount { get { return shown.Count; } }
        public void Search(string text) { find.Text = text; }

        public InventoryScreen()
        {
            grid.VirtualMode = true;
            grid.ReadOnly = true;
            grid.MultiSelect = false;
            grid.Columns.Add(Ui.Text("Category", 150));
            grid.Columns.Add(Ui.Text("Kind", 190));
            grid.Columns.Add(Ui.Text("Item", 300, true));
            grid.Columns.Add(Ui.Text("State", 130));
            grid.Columns.Add(Ui.Text("Edit on", 80));
            grid.Columns.Add(Ui.Text("From", 70));
            grid.CellValueNeeded += delegate(object s, DataGridViewCellValueEventArgs a)
            {
                if (a.RowIndex >= shown.Count) return;
                Row r = shown[a.RowIndex];
                switch (a.ColumnIndex)
                {
                    case 0: a.Value = r.Group; break;
                    case 1: a.Value = r.Kind; break;
                    case 2: a.Value = r.Item; break;
                    case 3: a.Value = r.State; break;
                    case 4: a.Value = r.Screen; break;
                    default: a.Value = r.Source; break;
                }
            };
            grid.CellDoubleClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.RowIndex < shown.Count && OpenScreen != null) OpenScreen(shown[e.RowIndex].Screen);
            };
            find.TextChanged += delegate { Filter(); };
            group.SelectedIndexChanged += delegate { if (!binding) Filter(); };
            state.SelectedIndexChanged += delegate { if (!binding) Filter(); };
            FlowLayoutPanel bar = Ui.Bar();
            bar.Controls.Add(new Label { Text = "Find", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(find);
            bar.Controls.Add(new Label { Text = "Category", AutoSize = true, Margin = new Padding(10, 9, 3, 3) });
            bar.Controls.Add(group);
            bar.Controls.Add(new Label { Text = "State", AutoSize = true, Margin = new Padding(10, 9, 3, 3) });
            bar.Controls.Add(state);
            view.Controls.Add(grid);
            view.Controls.Add(summary);
            view.Controls.Add(bar);
        }

        public void Bind(GameSave g)
        {
            all = new List<Row>();
            foreach (Block b in g.File.Blocks)
                foreach (Node r in b.Records)
                {
                    RecordKind k = Inventory.Of(r.Def);
                    all.Add(new Row
                    {
                        Group = k.Group, Kind = k.Title, Screen = k.Screen, State = Inventory.StateOf(r), Item = Label(r.Id, k.Title),
                        Source = CustomItems.IsCustom(r.Id) ? "Your mod" : "Game"
                    });
                }
            foreach (Category c in Categories.All)
            {
                RecordKind k = Inventory.Find(c.Struct);
                foreach (UnlockEntry e in g.Entries(c))
                    if (e.Record == null)
                        all.Add(new Row { Group = k.Group, Kind = k.Title, Screen = k.Screen, State = Inventory.StateOf(null), Item = Label(e.Id, k.Title), Source = "Game" });
            }
            all = all.OrderBy(r => r.Group).ThenBy(r => r.Kind).ThenBy(r => r.Item, StringComparer.OrdinalIgnoreCase).ToList();
            binding = true;
            group.Items.Clear();
            group.Items.Add("All categories");
            foreach (string s in all.Select(r => r.Group).Distinct()) group.Items.Add(s);
            group.SelectedIndex = 0;
            state.Items.Clear();
            state.Items.Add("Any state");
            foreach (string s in new[] { "Owned", "Not owned", "Not in this save yet", "Done", "Not done" }) state.Items.Add(s);
            state.SelectedIndex = 0;
            binding = false;
            Filter();
        }

        static string Label(string id, string kind)
        {
            if (string.IsNullOrEmpty(id)) return kind;
            VariantInfo v = VariantTable.Find(id);
            if (v != null) return v.Label;
            string shown = Names.Display(id);
            return shown != null ? shown + "  (" + id + ")" : id;
        }

        void Filter()
        {
            string f = find.Text.Trim();
            string gr = group.SelectedIndex > 0 ? (string)group.SelectedItem : null;
            string st = state.SelectedIndex > 0 ? (string)state.SelectedItem : null;
            shown = all.Where(r => (gr == null || r.Group == gr) && (st == null || r.State == st)
                && (f.Length == 0 || r.Item.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || r.Kind.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            grid.RowCount = 0;
            grid.RowCount = shown.Count;
            grid.Invalidate();
            summary.Text = shown.Count.ToString("N0") + " of " + all.Count.ToString("N0") + " items. "
                + all.Count(r => r.State == "Not in this save yet").ToString("N0") + " aren't in this save yet, "
                + all.Count(r => r.Source == "Your mod") + " came from your mods. Double-click an item to open its screen.";
        }
    }

    // Who you are: every outfit slot, skin and body value, in the same option panel.
    sealed class CharacterScreen : IScreen
    {
        readonly OptionPanel panel = new OptionPanel();

        public Control View { get { return panel; } }

        public void Bind(GameSave g)
        {
            List<OptionRow> rows = new List<OptionRow>();
            Node state = g.Find("character_customization_state");
            if (state != null) rows.AddRange(Options.ForRecord(g, state, "Appearance"));
            Node persona = g.Find("avatar_persona_state");
            if (persona != null) rows.AddRange(Options.ForRecord(g, persona, "Voice and persona"));
            panel.ShowRows("Your character", rows);
        }
    }

    sealed class AddVehicleForm : Form
    {
        readonly ListBox list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None };
        readonly TextBox filter = new TextBox { Dock = DockStyle.Top };
        readonly List<VariantInfo> items;

        public VariantInfo Picked { get { return list.SelectedItem as VariantInfoRow == null ? null : ((VariantInfoRow)list.SelectedItem).Info; } }

        sealed class VariantInfoRow
        {
            public VariantInfo Info;
            public override string ToString() { return Info.Label + "  -  " + Info.How; }
        }

        public AddVehicleForm()
        {
            Text = "Add a vehicle";
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;
            ClientSize = new Size(560, 480);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            items = VariantTable.All.Values.Where(v => v.How != "custom").OrderBy(v => v.Label, StringComparer.OrdinalIgnoreCase).ToList();
            FlowLayoutPanel buttons = Ui.Bar();
            buttons.Dock = DockStyle.Bottom;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            Button ok = Ui.MakeButton("Add", delegate { if (Picked != null) DialogResult = DialogResult.OK; });
            buttons.Controls.Add(ok);
            buttons.Controls.Add(Ui.MakeButton("Cancel", delegate { DialogResult = DialogResult.Cancel; }));
            AcceptButton = ok;
            list.DoubleClick += delegate { if (Picked != null) DialogResult = DialogResult.OK; };
            filter.TextChanged += delegate { Fill(); };
            Controls.Add(list);
            Controls.Add(filter);
            Controls.Add(buttons);
            Fill();
        }

        void Fill()
        {
            string f = filter.Text.Trim();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (VariantInfo v in items)
                if (f.Length == 0 || v.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || v.How.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0)
                    list.Items.Add(new VariantInfoRow { Info = v });
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            list.EndUpdate();
        }
    }
}
