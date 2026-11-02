// The window. Each screen edits the tree in Model.cs; nothing reaches the disk until Save.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Sr5Save
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 3 && args[0] == "--ui-check") return UiCheck.Run(args[1], args[2]);
            Application.Run(new MainForm());
            return 0;
        }
    }

    static class Ui
    {
        public static readonly Color Purple = Color.FromArgb(82, 39, 134);
        public static readonly Color PurpleLight = Color.FromArgb(240, 234, 248);
        public static readonly Color Warning = Color.FromArgb(255, 241, 204);
        public static readonly Color Muted = Color.FromArgb(105, 105, 115);
        public static readonly Color Ink = Color.FromArgb(45, 28, 74);
        public static readonly Color Selected = Color.FromArgb(222, 210, 240);
        public static readonly Color Line = Color.FromArgb(225, 225, 230);

        // Dark text on a light button everywhere, on the purple header too.
        public static Button MakeButton(string text, EventHandler click)
        {
            Button b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                Margin = new Padding(3),
                Height = 28,
                Padding = new Padding(6, 0, 6, 0),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                BackColor = Color.White,
                ForeColor = Ink
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(200, 190, 220);
            b.FlatAppearance.MouseOverBackColor = PurpleLight;
            b.FlatAppearance.MouseDownBackColor = Selected;
            b.EnabledChanged += delegate { b.BackColor = b.Enabled ? Color.White : Color.FromArgb(226, 222, 234); };
            b.Click += click;
            return b;
        }

        // Starts editing the current list cell with its list open, so one click is enough.
        public static bool OpenList(DataGridView g)
        {
            if (!(g.CurrentCell is DataGridViewComboBoxCell)) return false;
            g.BeginEdit(true);
            ComboBox box = g.EditingControl as ComboBox;
            if (box == null) return false;
            box.DroppedDown = true;
            return true;
        }

        public static DataGridView MakeGrid()
        {
            DataGridView g = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.None,
                CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
                GridColor = Color.FromArgb(236, 236, 240),
                EnableHeadersVisualStyles = false,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
                ColumnHeadersHeight = 28,
                ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            g.ColumnHeadersDefaultCellStyle.BackColor = Ui.PurpleLight;
            g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Ui.PurpleLight;
            g.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
            g.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
            g.DefaultCellStyle.SelectionBackColor = Selected;
            g.DefaultCellStyle.SelectionForeColor = Color.Black;
            g.RowTemplate.Height = 26;
            // List cells show their number with a small arrow, rather than a grey box per cell.
            g.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0 || g.VirtualMode || !(g.Rows[e.RowIndex].Cells[e.ColumnIndex] is DataGridViewComboBoxCell)) return;
                e.Paint(e.CellBounds, DataGridViewPaintParts.All);
                int x = e.CellBounds.Right - 18, y = e.CellBounds.Top + e.CellBounds.Height / 2 - 1;
                System.Drawing.Drawing2D.SmoothingMode mode = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (SolidBrush arrow = new SolidBrush(Muted))
                    e.Graphics.FillPolygon(arrow, new[] { new Point(x, y), new Point(x + 8, y), new Point(x + 4, y + 4) });
                e.Graphics.SmoothingMode = mode;
                e.Handled = true;
            };
            g.CellClick += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex >= 0) OpenList(g);
            };
            // A checkbox or list change counts as soon as it's clicked, without leaving the cell.
            g.CurrentCellDirtyStateChanged += delegate
            {
                if (g.IsCurrentCellDirty && !(g.CurrentCell is DataGridViewTextBoxCell))
                    g.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            g.DataError += delegate(object s, DataGridViewDataErrorEventArgs e) { e.ThrowException = false; };
            return g;
        }

        public static DataGridViewTextBoxColumn Text(string title, int width, bool fill = false, bool readOnly = true)
        {
            return new DataGridViewTextBoxColumn
            {
                HeaderText = title,
                Width = width,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None
            };
        }

        public static DataGridViewCheckBoxColumn Check(string title, int width = 60)
        {
            return new DataGridViewCheckBoxColumn { HeaderText = title, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
        }

        public static DataGridViewComboBoxColumn Levels(string title, int max, int width = 62)
        {
            DataGridViewComboBoxColumn c = new DataGridViewComboBoxColumn
            {
                HeaderText = title,
                Width = width,
                FlatStyle = FlatStyle.Flat,
                DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing,
                SortMode = DataGridViewColumnSortMode.NotSortable
            };
            for (int i = 0; i <= max; i++) c.Items.Add(i.ToString());
            return c;
        }

        public static FlowLayoutPanel Bar()
        {
            return new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(6, 6, 6, 4), WrapContents = true };
        }

        public static Label Help(string text)
        {
            return new Label { Text = text, ForeColor = Muted, AutoSize = false, Dock = DockStyle.Top, Padding = new Padding(9, 4, 9, 4), Height = 28, AutoEllipsis = true };
        }
    }

    interface IScreen
    {
        Control View { get; }
        void Bind(GameSave g);
    }

    sealed class OverviewScreen : IScreen
    {
        sealed class Row
        {
            public Func<GameSave, Node> Get;
            public Action<GameSave, uint> Set;
            public NumericUpDown Box;
        }

        readonly Panel view = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        readonly Label headline = new Label { AutoSize = true, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = Ui.Purple, Margin = new Padding(0, 0, 0, 2) };
        readonly Label detail = new Label { AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(0, 0, 0, 12) };
        readonly List<Row> rows = new List<Row>();
        readonly ComboBox startAt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        readonly CheckBox cheated = new CheckBox { AutoSize = true, Text = "This save is marked as having used cheats" };
        GameSave save;
        bool binding;

        public Control View { get { return view; } }

        public OverviewScreen()
        {
            TableLayoutPanel t = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Location = new Point(18, 14), Padding = new Padding(0) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.Controls.Add(headline, 0, 0);
            t.SetColumnSpan(headline, 3);
            t.Controls.Add(detail, 0, 1);
            t.SetColumnSpan(detail, 3);
            int y = 2;
            AddRow(t, ref y, "Cash", g => g.Currency("cash"), (g, v) => g.SetCurrency("cash", v), 2000000000, "What you can spend. The load menu's figure is kept in step.");
            AddRow(t, ref y, "XP", g => g.Currency("xp"), (g, v) => g.SetCurrency("xp", v), 2000000000, "The game levels you up from this on your next XP gain, with each level's rewards.");
            AddRow(t, ref y, "Level", g => g.Level, (g, v) => g.Level.SetRaw(v), 100, "Sets the number only. Rewards for skipped levels aren't given: adding XP is better.");
            AddRow(t, ref y, "Criminal empire tier", g => g.Tier, (g, v) => g.Tier.SetRaw(v), 5, "0 to 5.");
            AddRow(t, ref y, "Cash waiting to collect", g => g.WaitingCash, (g, v) => g.WaitingCash.SetRaw(v), 2000000000, "Believed to be venture income waiting in the phone's Cash Transfer app.");
            AddRow(t, ref y, "Boot Hill coins", g => g.Currency("boot_hill_coins"), (g, v) => g.SetCurrency("boot_hill_coins", v), 2000000000, "Only in saves that have played the Boot Hill content.");
            AddRow(t, ref y, "Boot Hill XP", g => g.Currency("boot_hill_xp"), (g, v) => g.SetCurrency("boot_hill_xp", v), 2000000000, "");
            t.Controls.Add(new Label { Text = "Start at", AutoSize = true, Margin = new Padding(0, 9, 14, 6) }, 0, y);
            t.Controls.Add(startAt, 1, y);
            t.Controls.Add(new Label { Text = "Where you appear when this save loads.", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(10, 9, 0, 6) }, 2, y);
            y++;
            t.Controls.Add(cheated, 1, y);
            t.SetColumnSpan(cheated, 2);
            cheated.Margin = new Padding(3, 8, 0, 0);
            view.Controls.Add(t);
            startAt.SelectedIndexChanged += delegate
            {
                if (binding || save == null || save.StartPoint == null || startAt.SelectedItem == null) return;
                save.StartPoint.SetRaw(((Choice)startAt.SelectedItem).Hash);
            };
            cheated.CheckedChanged += delegate
            {
                if (!binding && save != null && save.CheatFlag != null) save.CheatFlag.SetRaw(cheated.Checked ? 1u : 0u);
            };
        }

        sealed class Choice
        {
            public uint Hash;
            public string Text;
            public override string ToString() { return Text; }
        }

        void AddRow(TableLayoutPanel t, ref int y, string label, Func<GameSave, Node> get, Action<GameSave, uint> set, decimal max, string help)
        {
            NumericUpDown box = new NumericUpDown { Maximum = max, Width = 150, ThousandsSeparator = true, Margin = new Padding(3, 5, 0, 5) };
            Row row = new Row { Get = get, Set = set, Box = box };
            box.ValueChanged += delegate
            {
                if (!binding && save != null && row.Get(save) != null) row.Set(save, (uint)box.Value);
            };
            rows.Add(row);
            t.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 9, 14, 6) }, 0, y);
            t.Controls.Add(box, 1, y);
            t.Controls.Add(new Label { Text = help, AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(10, 9, 0, 6) }, 2, y);
            y++;
        }

        public void Bind(GameSave g)
        {
            save = g;
            binding = true;
            string name = Path.GetFileNameWithoutExtension(g.SavePath);
            Node level = g.Level, time = g.Stat("stat_play_time"), done = g.Stat("stat_game_progress");
            headline.Text = name + (level != null ? "  ·  Level " + level.Value : "");
            detail.Text = (time != null ? TimeSpan.FromSeconds(time.Float).TotalHours.ToString("N1") + " hours played" : "")
                + (done != null ? "  ·  " + done.Float.ToString("0") + "% complete" : "")
                + "  ·  saved " + File.GetLastWriteTime(g.SavePath).ToString("d MMM yyyy, HH:mm");
            foreach (Row r in rows)
            {
                Node n = r.Get(g);
                r.Box.Enabled = n != null;
                r.Box.Value = n == null ? 0 : Math.Min(r.Box.Maximum, Math.Max(0, (decimal)n.Value));
            }
            startAt.Items.Clear();
            Node start = g.StartPoint;
            startAt.Enabled = start != null;
            if (start != null)
            {
                List<Choice> choices = g.All(Crc.Of("city_warp_point_state"))
                    .Select(r => new Choice { Hash = r.Key, Text = Names.Pretty(r.Id) })
                    .OrderBy(c => c.Text).ToList();
                if (!choices.Any(c => c.Hash == start.Value))
                    choices.Insert(0, new Choice { Hash = start.Value, Text = Names.Pretty(Names.Get(start.Value)) });
                foreach (Choice c in choices) startAt.Items.Add(c);
                startAt.SelectedItem = choices.First(c => c.Hash == start.Value);
            }
            cheated.Enabled = g.CheatFlag != null;
            cheated.Checked = g.CheatFlag != null && g.CheatFlag.Value != 0;
            binding = false;
        }
    }

    sealed class WeaponsScreen : IScreen
    {
        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly OptionPanel options = new OptionPanel();
        readonly TextBox filter = new TextBox { Width = 180, Margin = new Padding(3, 6, 3, 3) };
        readonly CheckBox showAll = new CheckBox { Text = "Show enemy and internal entries", AutoSize = true, Margin = new Padding(10, 8, 3, 3) };
        GameSave save;
        List<WeaponEntry> entries = new List<WeaponEntry>();
        bool binding;

        public Control View { get { return view; } }

        public WeaponsScreen()
        {
            grid.Columns.Add(Ui.Check("Owned", 58));
            grid.Columns.Add(Ui.Text("Weapon", 240, true));
            grid.Columns.Add(Ui.Levels("Level", 3));
            grid.Columns.Add(Ui.Text("Upgrades", 90));
            grid.Columns.Add(Ui.Check("Ultimate", 62));
            grid.Columns.Add(Ui.Text("In this save", 120));
            grid.CellValueChanged += CellChanged;
            FlowLayoutPanel bar = Ui.Bar();
            bar.Controls.Add(new Label { Text = "Find", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(filter);
            bar.Controls.Add(showAll);
            bar.Controls.Add(Ui.MakeButton("Own all", delegate { Bulk(e => save.SetWeaponOwned(e, true)); }));
            bar.Controls.Add(Ui.MakeButton("Fully upgrade all", delegate { Bulk(e => save.MaxWeapon(e, false)); }));
            bar.Controls.Add(Ui.MakeButton("Also buy ultimates", delegate { Bulk(e => { if (e.Info.HasUltimate) save.MaxWeapon(e, true); }); }));
            filter.TextChanged += delegate { Fill(); };
            showAll.CheckedChanged += delegate { Fill(); };
            grid.CurrentCellChanged += delegate { ShowOptions(); };
            options.Edited = delegate { if (grid.CurrentRow != null) { binding = true; Show(grid.CurrentRow); binding = false; } };
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Line, SplitterWidth = 5 };
            split.Panel1.BackColor = Color.White;
            split.Panel2.BackColor = Color.White;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(options);
            view.Controls.Add(split);
            view.Controls.Add(Ui.Help("The buttons act on the weapons listed. Pick a weapon in this save to see its skin, material, ammo and other options below."));
            view.Controls.Add(bar);
            split.SizeChanged += delegate { if (!split.Tag.Equals(true) && split.Height > 300) { split.Tag = true; split.SplitterDistance = split.Height / 2; } };
            split.Tag = false;
        }

        void ShowOptions()
        {
            if (binding || save == null) return;
            WeaponEntry e = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as WeaponEntry;
            options.ShowRows(e == null ? "No weapon picked" : Names.Pretty(e.Info.Name),
                e == null || e.Record == null ? new List<OptionRow>() : Options.ForRecord(save, e.Record, "Weapon"));
        }

        public void Bind(GameSave g)
        {
            save = g;
            entries = g.Weapons();
            Fill();
        }

        void Bulk(Action<WeaponEntry> act)
        {
            foreach (DataGridViewRow r in grid.Rows) act((WeaponEntry)r.Tag);
            Fill();
        }

        void Fill()
        {
            binding = true;
            int top = grid.FirstDisplayedScrollingRowIndex;
            grid.Rows.Clear();
            string f = filter.Text.Trim();
            foreach (WeaponEntry e in entries)
            {
                if (!showAll.Checked && e.Info.Kind != "shop") continue;
                string label = Names.Pretty(e.Info.Name);
                if (f.Length > 0 && label.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0 && e.Info.Name.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                int i = grid.Rows.Add();
                grid.Rows[i].Tag = e;
                Show(grid.Rows[i]);
            }
            if (top >= 0 && top < grid.Rows.Count) grid.FirstDisplayedScrollingRowIndex = top;
            binding = false;
            ShowOptions();
        }

        void Show(DataGridViewRow row)
        {
            WeaponEntry e = (WeaponEntry)row.Tag;
            Node r = e.Record;
            row.Cells[0].Value = r != null && Val(r, "m_player_unlocked") != 0;
            row.Cells[1].Value = Names.Pretty(e.Info.Name);
            row.Cells[2].Value = Math.Min(3, r == null ? 0 : Val(r, "m_stat_level_purchased")).ToString();
            Node mods = r == null ? null : r.Child("m_equipped_mod_levels");
            row.Cells[3].Value = mods == null ? "" : string.Join(",", mods.Children.Select(c => c.Value.ToString()));
            row.Cells[4].Value = r != null && Val(r, "m_ultimate_purchased") != 0;
            row.Cells[5].Value = r != null ? "Yes" : "Added when owned";
            row.Cells[5].Style.ForeColor = r != null ? Color.Black : Ui.Muted;
        }

        static int Val(Node r, string field)
        {
            Node n = r.Child(field);
            return n == null ? 0 : n.Int;
        }

        void CellChanged(object sender, DataGridViewCellEventArgs a)
        {
            if (binding || a.RowIndex < 0) return;
            DataGridViewRow row = grid.Rows[a.RowIndex];
            WeaponEntry e = (WeaponEntry)row.Tag;
            object v = row.Cells[a.ColumnIndex].Value;
            if (a.ColumnIndex == 0) save.SetWeaponOwned(e, Convert.ToBoolean(v));
            else if (a.ColumnIndex == 2) save.SetWeaponLevel(e, int.Parse((string)v));
            else if (a.ColumnIndex == 4) save.SetWeaponUltimate(e, Convert.ToBoolean(v));
            else return;
            binding = true;
            Show(row);
            binding = false;
        }

        // For the UI check.
        public void SetFirstListedOwned(bool own)
        {
            grid.Rows[0].Cells[0].Value = own;
            CellChanged(grid, new DataGridViewCellEventArgs(0, 0));
        }
        public WeaponEntry FirstListed { get { return (WeaponEntry)grid.Rows[0].Tag; } }
        public bool OpenFirstLevelList()
        {
            grid.CurrentCell = grid.Rows[0].Cells[2];
            bool opened = Ui.OpenList(grid);
            if (opened) grid.CancelEdit();
            grid.EndEdit();
            return opened;
        }
    }

    sealed class GarageScreen : IScreen
    {
        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly OptionPanel options = new OptionPanel();
        Node selected;
        GameSave save;
        bool binding;

        // For the UI check.
        public OptionPanel OptionList { get { return options; } }
        public void PickFirstCustomized()
        {
            foreach (DataGridViewRow r in grid.Rows)
                if (((Node)r.Tag).Child("m_has_customization").Value == 1) { grid.CurrentCell = r.Cells[0]; break; }
        }

        public Control View { get { return view; } }

        public GarageScreen()
        {
            grid.Columns.Add(Ui.Text("Vehicle", 200, true));
            grid.Columns.Add(Ui.Text("Model", 200));
            grid.Columns[1].DefaultCellStyle.ForeColor = Ui.Muted;
            grid.Columns.Add(Ui.Levels("Engine", 3));
            grid.Columns.Add(Ui.Levels("Frame", 3));
            grid.Columns.Add(Ui.Levels("Bumper", 3));
            grid.Columns.Add(Ui.Levels("Tires", 3));
            grid.Columns.Add(Ui.Check("Nitro", 50));
            grid.Columns.Add(Ui.Check("Offroad", 60));
            grid.Columns.Add(Ui.Check("Tether", 54));
            grid.CellValueChanged += CellChanged;
            FlowLayoutPanel bar = Ui.Bar();
            bar.Controls.Add(Ui.MakeButton("Upgrade selected", delegate
            {
                foreach (DataGridViewRow r in grid.SelectedRows) { save.MaxVehicle((Node)r.Tag); Show(r); }
            }));
            bar.Controls.Add(Ui.MakeButton("Upgrade all", delegate
            {
                foreach (DataGridViewRow r in grid.Rows) { save.MaxVehicle((Node)r.Tag); Show(r); }
            }));
            bar.Controls.Add(Ui.MakeButton("Add vehicle...", delegate { AddVehicle(); }));
            bar.Controls.Add(Ui.MakeButton("Remove selected", delegate { RemoveSelected(); }));
            bar.Controls.Add(Ui.MakeButton("Copy look to selected", delegate { CopyLook(); }));
            options.Bar.Controls.Add(Ui.MakeButton("Set this option on every car", delegate { ApplyToAll(); }));
            options.Edited = delegate(OptionRow r)
            {
                if (selected == null) return;
                if (r.Path.StartsWith("m_customized.")) save.MarkCustomized(selected);
                if (grid.CurrentRow != null) Show(grid.CurrentRow);
            };
            grid.CurrentCellChanged += delegate { ShowOptions(); };
            SplitContainer split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, BackColor = Ui.Line, SplitterWidth = 5 };
            split.Panel1.BackColor = Color.White;
            split.Panel2.BackColor = Color.White;
            split.Panel1.Controls.Add(grid);
            split.Panel2.Controls.Add(options);
            view.Controls.Add(split);
            view.Controls.Add(Ui.Help("Pick a car to see every option it has. Upgrade buttons set engine, frame, bumper and tires to 3 and buy nitro."));
            view.Controls.Add(bar);
            split.SizeChanged += delegate { if (!split.Tag.Equals(true) && split.Height > 300) { split.Tag = true; split.SplitterDistance = split.Height * 2 / 5; } };
            split.Tag = false;
        }

        void ShowOptions()
        {
            if (binding || save == null) return;
            Node slot = grid.CurrentRow == null ? null : grid.CurrentRow.Tag as Node;
            if (slot == selected) return;
            selected = slot;
            bool stock = slot != null && slot.Child("m_has_customization") != null && slot.Child("m_has_customization").Value == 0;
            options.ShowRows(slot == null ? "No car picked" : Convert.ToString(grid.CurrentRow.Cells[0].Value) + (stock ? " (stock: a ? is an unset value)" : ""),
                slot == null ? new List<OptionRow>() : Options.ForVehicle(save, slot));
        }

        void ApplyToAll()
        {
            OptionRow r = options.Current;
            if (r == null || selected == null) return;
            if (MessageBox.Show(view, "Set " + r.Label + " to " + r.Value + " on every car that has this option?", "Every car", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            int n = Options.ApplyToAllVehicles(save, selected, r);
            foreach (DataGridViewRow row in grid.Rows) Show(row);
            MessageBox.Show(view, n + " other cars changed.", "Every car", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // The look of the car picked in the list goes to the other selected cars of the same vehicle.
        void CopyLook()
        {
            if (selected == null) return;
            int done = 0, skipped = 0;
            foreach (DataGridViewRow r in grid.SelectedRows)
            {
                Node to = (Node)r.Tag;
                if (to == selected) continue;
                if (save.CopyLook(selected, to)) { done++; Show(r); }
                else skipped++;
            }
            MessageBox.Show(view, done == 0 && skipped == 0 ? "Select other cars of the same vehicle as well, holding Ctrl, then press this."
                : done + " cars changed." + (skipped > 0 ? " " + skipped + " skipped: a look only fits the same vehicle." : ""), "Copy look", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public void Bind(GameSave g)
        {
            save = g;
            binding = true;
            grid.Rows.Clear();
            foreach (Node slot in g.GarageSlots())
            {
                int i = grid.Rows.Add();
                grid.Rows[i].Tag = slot;
                Show(grid.Rows[i]);
            }
            binding = false;
            selected = null;
            ShowOptions();
        }

        void AddVehicle()
        {
            if (save == null) return;
            if (!save.CanAddVehicle)
            {
                MessageBox.Show(view, "This save has no stock car to copy yet. Earn one in the game first.", "Add a vehicle", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            using (AddVehicleForm f = new AddVehicleForm())
            {
                if (f.ShowDialog(view) != DialogResult.OK || f.Picked == null) return;
                save.AddVehicle(f.Picked);
                Bind(save);
                if (grid.Rows.Count > 0) grid.FirstDisplayedScrollingRowIndex = grid.Rows.Count - 1;
            }
        }

        void RemoveSelected()
        {
            if (save == null || grid.SelectedRows.Count == 0) return;
            if (MessageBox.Show(view, "Remove the " + grid.SelectedRows.Count + " selected vehicles from this save's garage?", "Remove vehicles", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            List<Node> slots = new List<Node>();
            foreach (DataGridViewRow r in grid.SelectedRows) slots.Add((Node)r.Tag);
            foreach (Node s in slots) save.RemoveVehicle(s);
            Bind(save);
        }

        static int Level(Node slot, string field)
        {
            Node n = GameSave.GarageField(slot, field);
            return n == null ? 0 : Math.Min(3, Math.Max(0, n.Int));
        }

        void Show(DataGridViewRow row)
        {
            bool was = binding;
            binding = true;
            Node slot = (Node)row.Tag;
            Node info = slot.Child("m_vehicle_info"), variant = slot.Child("m_variant_info");
            row.Cells[0].Value = info == null ? "" : VehicleTable.Display(Names.Find(info.Value) ?? info.Value.ToString("x8"));
            row.Cells[1].Value = variant == null ? "" : Names.Get(variant.Value);
            for (int i = 0; i < 4; i++) row.Cells[2 + i].Value = Level(slot, GameSave.UpgradeFields[i]).ToString();
            row.Cells[6].Value = Level(slot, "m_nitro_purchased") != 0;
            row.Cells[7].Value = Level(slot, "m_offroad_purchased") != 0;
            row.Cells[8].Value = Level(slot, "m_tether_purchased") != 0;
            binding = was;
        }

        void CellChanged(object sender, DataGridViewCellEventArgs a)
        {
            if (binding || a.RowIndex < 0) return;
            DataGridViewRow row = grid.Rows[a.RowIndex];
            Node slot = (Node)row.Tag;
            object v = row.Cells[a.ColumnIndex].Value;
            if (a.ColumnIndex >= 2 && a.ColumnIndex <= 5)
            {
                Node n = GameSave.GarageField(slot, GameSave.UpgradeFields[a.ColumnIndex - 2]);
                if (n != null) n.SetRaw(uint.Parse((string)v));
            }
            else if (a.ColumnIndex == 6)
            {
                uint on = Convert.ToBoolean(v) ? 1u : 0u;
                foreach (string f in new[] { "m_nitro_purchased", "m_nitro_level" })
                {
                    Node n = GameSave.GarageField(slot, f);
                    if (n != null) n.SetRaw(on);
                }
            }
            else if (a.ColumnIndex == 7 || a.ColumnIndex == 8)
            {
                Node n = GameSave.GarageField(slot, a.ColumnIndex == 7 ? "m_offroad_purchased" : "m_tether_purchased");
                if (n != null) n.SetRaw(Convert.ToBoolean(v) ? 1u : 0u);
            }
            else return;
            save.MarkCustomized(slot);
        }
    }

    sealed class UnlocksScreen : IScreen
    {
        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly ListBox list = new ListBox { Dock = DockStyle.Left, Width = 290, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 26 };
        readonly DataGridView grid = Ui.MakeGrid();
        readonly TextBox filter = new TextBox { Width = 170, Margin = new Padding(3, 6, 3, 3) };
        readonly Label help = Ui.Help("");
        GameSave save;
        List<UnlockEntry> all = new List<UnlockEntry>();
        List<UnlockEntry> shown = new List<UnlockEntry>();

        public Control View { get { return view; } }

        Category Current { get { return list.SelectedIndex < 0 ? null : Categories.All[list.SelectedIndex]; } }

        public UnlocksScreen()
        {
            foreach (Category c in Categories.All) list.Items.Add(c);
            list.DrawItem += DrawCategory;
            list.SelectedIndexChanged += delegate { Pick(); };
            grid.VirtualMode = true;
            grid.Columns.Add(Ui.Check("Owned", 58));
            grid.Columns.Add(Ui.Text("Item", 300, true));
            grid.Columns.Add(Ui.Text("Normally met as", 110));
            grid.Columns.Add(Ui.Text("In this save", 120));
            grid.CellValueNeeded += delegate(object s, DataGridViewCellValueEventArgs a)
            {
                if (a.RowIndex >= shown.Count) return;
                UnlockEntry e = shown[a.RowIndex];
                VariantInfo v = VariantTable.Find(e.Id);
                if (a.ColumnIndex == 0) a.Value = save.IsOwned(Current, e);
                else if (a.ColumnIndex == 1) a.Value = (v != null ? v.Label : Names.Pretty(e.Id)) + (CustomItems.IsCustom(e.Id) ? "  (your mod)" : "");
                else if (a.ColumnIndex == 2) a.Value = v != null ? v.How : "";
                else a.Value = e.Record != null ? "Yes" : "Added when owned";
            };
            grid.CellValuePushed += delegate(object s, DataGridViewCellValueEventArgs a)
            {
                if (a.ColumnIndex != 0 || a.RowIndex >= shown.Count) return;
                save.SetOwned(Current, shown[a.RowIndex], Convert.ToBoolean(a.Value));
                list.Invalidate();
            };
            filter.TextChanged += delegate { Filter(); };
            FlowLayoutPanel bar = Ui.Bar();
            bar.Controls.Add(new Label { Text = "Find", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            bar.Controls.Add(filter);
            bar.Controls.Add(Ui.MakeButton("Own all listed", delegate { Act(true); }));
            bar.Controls.Add(Ui.MakeButton("Remove all listed", delegate
            {
                if (MessageBox.Show(view, "Mark the " + shown.Count + " items listed as not owned?", "Remove items", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) Act(false);
            }));
            bar.Controls.Add(Ui.MakeButton("Unlock everything", delegate
            {
                if (MessageBox.Show(view, "Own everything except story features, vehicle models, treasure and map progress? Those lists are left for you to choose from.\n\nNothing is written until you press Save.", "Unlock everything", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK)
                    UnlockEverything();
            }));
            Panel right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(grid);
            right.Controls.Add(help);
            right.Controls.Add(bar);
            view.Controls.Add(right);
            view.Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Color.FromArgb(225, 225, 230) });
            view.Controls.Add(list);
        }

        void DrawCategory(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || save == null) return;
            Category c = Categories.All[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(sel ? Ui.Selected : SystemColors.Window)) e.Graphics.FillRectangle(back, e.Bounds);
            string counts;
            if (!counts_.TryGetValue(c, out counts))
            {
                List<UnlockEntry> entries = save.Entries(c);
                counts = entries.Count(x => save.IsOwned(c, x)) + "/" + entries.Count;
                counts_[c] = counts;
            }
            Rectangle r = e.Bounds;
            TextRenderer.DrawText(e.Graphics, c.Group + ": " + c.Title, list.Font, new Rectangle(r.X + 8, r.Y, r.Width - 80, r.Height), Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, counts, list.Font, new Rectangle(r.Right - 80, r.Y, 74, r.Height), Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        readonly Dictionary<Category, string> counts_ = new Dictionary<Category, string>();

        public void Bind(GameSave g)
        {
            save = g;
            counts_.Clear();
            if (list.SelectedIndex < 0) list.SelectedIndex = 0;
            Pick();
            list.Invalidate();
        }

        void Pick()
        {
            if (save == null || Current == null) return;
            all = save.Entries(Current);
            help.Text = Current.Help ?? ("\"Added when owned\" items aren't in this save yet. The editor adds them, as the game does when you earn one.");
            Filter();
        }

        void Filter()
        {
            string f = filter.Text.Trim();
            shown = f.Length == 0 ? all : all.Where(e =>
            {
                VariantInfo v = VariantTable.Find(e.Id);
                return e.Id.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || Names.Pretty(e.Id).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0
                    || (v != null && (v.Label.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 || v.How.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0));
            }).ToList();
            grid.Columns[2].Visible = Current != null && Current.Struct == "vehicle_variant_entry_state";
            grid.RowCount = 0;
            grid.RowCount = shown.Count;
            grid.Invalidate();
        }

        void Act(bool own)
        {
            foreach (UnlockEntry e in shown) save.SetOwned(Current, e, own);
            counts_.Remove(Current);
            grid.Invalidate();
            list.Invalidate();
        }

        public void UnlockEverything()
        {
            foreach (Category c in Categories.All)
            {
                if (!c.InUnlockAll) continue;
                foreach (UnlockEntry e in save.Entries(c)) save.SetOwned(c, e, true);
            }
            counts_.Clear();
            Pick();
            list.Invalidate();
        }
    }

    sealed class StatsScreen : IScreen
    {
        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly DataGridView grid = Ui.MakeGrid();
        GameSave save;
        bool binding;

        public Control View { get { return view; } }

        public StatsScreen()
        {
            grid.Columns.Add(Ui.Text("Stat", 300, true));
            grid.Columns.Add(Ui.Text("Value", 180, false, false));
            grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs a)
            {
                if (binding || a.RowIndex < 0) return;
                Node v = (Node)grid.Rows[a.RowIndex].Tag;
                string error = v.TrySetText(Convert.ToString(grid.Rows[a.RowIndex].Cells[1].Value));
                binding = true;
                grid.Rows[a.RowIndex].Cells[1].Value = v.Text;
                binding = false;
                if (error != null) MessageBox.Show(view, error, "Stats", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            view.Controls.Add(grid);
            view.Controls.Add(Ui.Help("Lifetime numbers. Play time is in seconds and progress is 0 to 100. Both also show on the load menu."));
        }

        public void Bind(GameSave g)
        {
            save = g;
            binding = true;
            grid.Rows.Clear();
            foreach (Node r in g.Stats)
            {
                Node v = r.Child("m_value");
                if (v == null) continue;
                int i = grid.Rows.Add(Names.Pretty(r.Id.Replace("stat_", "")), v.Text);
                grid.Rows[i].Tag = v;
            }
            binding = false;
        }
    }

    sealed class DataScreen : IScreen
    {
        readonly Panel view = new Panel { Dock = DockStyle.Fill };
        readonly TreeView tree = new TreeView { Dock = DockStyle.Fill, HideSelection = false, BorderStyle = BorderStyle.None };
        readonly TextBox find = new TextBox { Width = 240, Margin = new Padding(3, 6, 3, 3) };
        readonly TextBox value = new TextBox { Width = 240, Margin = new Padding(3, 6, 3, 3), Enabled = false };
        readonly Label what = new Label { AutoSize = true, Margin = new Padding(10, 9, 3, 3), ForeColor = Ui.Muted };
        readonly Button apply;
        GameSave save;
        Node selected;

        public Control View { get { return view; } }

        public DataScreen()
        {
            tree.BeforeExpand += delegate(object s, TreeViewCancelEventArgs a) { Populate(a.Node); };
            tree.AfterSelect += delegate(object s, TreeViewEventArgs a) { Select(a.Node.Tag as Node); };
            FlowLayoutPanel top = Ui.Bar();
            top.Controls.Add(new Label { Text = "Find record", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            top.Controls.Add(find);
            top.Controls.Add(Ui.MakeButton("Next", delegate { FindNext(); }));
            find.KeyDown += delegate(object s, KeyEventArgs a) { if (a.KeyCode == Keys.Enter) { FindNext(); a.SuppressKeyPress = true; } };
            FlowLayoutPanel edit = Ui.Bar();
            edit.Dock = DockStyle.Bottom;
            edit.Controls.Add(new Label { Text = "Value", AutoSize = true, Margin = new Padding(3, 9, 3, 3) });
            edit.Controls.Add(value);
            apply = Ui.MakeButton("Set", delegate { Apply(); });
            apply.Enabled = false;
            edit.Controls.Add(apply);
            edit.Controls.Add(what);
            value.KeyDown += delegate(object s, KeyEventArgs a) { if (a.KeyCode == Keys.Enter) { Apply(); a.SuppressKeyPress = true; } };
            view.Controls.Add(tree);
            view.Controls.Add(edit);
            view.Controls.Add(Ui.Help("Every value in the save, by section. Nothing here is checked against the game, so change what you understand; missions and story flags can break a playthrough."));
            view.Controls.Add(top);
        }

        public void Bind(GameSave g)
        {
            save = g;
            selected = null;
            Select(null);
            tree.Nodes.Clear();
            foreach (Block b in g.File.Blocks.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                TreeNode n = new TreeNode(Names.Pretty(b.Name.ToLowerInvariant()) + "  (" + b.Records.Count + ")") { Tag = b };
                if (b.Records.Count > 0) n.Nodes.Add(new TreeNode());
                tree.Nodes.Add(n);
            }
        }

        static string Label(Node n)
        {
            if (n.Kind == NodeKind.Record) return (n.Id.Length > 0 ? n.Id : n.Def.Name);
            string name = n.Parent != null && n.Parent.Kind == NodeKind.Array ? "[" + n.ElementIndex + "]" : n.FieldName;
            switch (n.Kind)
            {
                case NodeKind.Struct: return name;
                case NodeKind.Array: return name + "  [" + n.Children.Count + "]";
                case NodeKind.Bytes: return name + ": " + n.Bytes.Length + " bytes";
            }
            return name + ": " + ValueText(n);
        }

        static string ValueText(Node n)
        {
            string t = n.Text;
            if (n.Field != null && n.Field.IsHandle && n.Value != 0)
            {
                string found = Names.Find(n.Value);
                if (found != null) t += " = " + found;
            }
            return t;
        }

        void Populate(TreeNode node)
        {
            if (node.Nodes.Count != 1 || node.Nodes[0].Tag != null || node.Nodes[0].Text != "") return;
            node.Nodes.Clear();
            IEnumerable<Node> kids = node.Tag is Block
                ? ((Block)node.Tag).Records.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
                : (IEnumerable<Node>)((Node)node.Tag).Children;
            tree.BeginUpdate();
            foreach (Node c in kids) node.Nodes.Add(Make(c));
            tree.EndUpdate();
        }

        static TreeNode Make(Node c)
        {
            TreeNode t = new TreeNode(Label(c)) { Tag = c };
            if (c.Children != null && c.Children.Count > 0) t.Nodes.Add(new TreeNode());
            return t;
        }

        void Select(Node n)
        {
            selected = n != null && n.Kind == NodeKind.Scalar ? n : null;
            value.Enabled = apply.Enabled = selected != null;
            value.Text = selected != null ? selected.Text : "";
            what.Text = selected != null ? selected.FieldName + " (" + selected.TypeName + ")" + (selected.Field.IsHandle ? ": a number, or the name it stands for" : "") : "";
        }

        void Apply()
        {
            if (selected == null) return;
            string error = selected.TrySetText(value.Text);
            if (error != null)
            {
                MessageBox.Show(view, error, "Value", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            value.Text = selected.Text;
            if (tree.SelectedNode != null) tree.SelectedNode.Text = Label(selected);
        }

        void FindNext()
        {
            string f = find.Text.Trim();
            if (f.Length == 0 || save == null) return;
            List<TreeNode> sections = tree.Nodes.Cast<TreeNode>().ToList();
            TreeNode cur = tree.SelectedNode;
            while (cur != null && cur.Parent != null && !(cur.Tag is Node && ((Node)cur.Tag).Kind == NodeKind.Record)) cur = cur.Parent;
            int startSection = 0, startRecord = 0;
            if (cur != null)
            {
                TreeNode sec = cur.Parent ?? cur;
                startSection = sections.IndexOf(sec);
                if (cur.Parent != null) startRecord = cur.Index + 1;
            }
            for (int k = 0; k <= sections.Count; k++)
            {
                TreeNode sec = sections[(startSection + k) % sections.Count];
                if (!(sec.Tag is Block) || ((Block)sec.Tag).Records.Count == 0) continue;
                Populate(sec);
                for (int i = k == 0 ? startRecord : 0; i < sec.Nodes.Count; i++)
                {
                    if (sec.Nodes[i].Text.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sec.Expand();
                    tree.SelectedNode = sec.Nodes[i];
                    sec.Nodes[i].EnsureVisible();
                    tree.Focus();
                    return;
                }
            }
            MessageBox.Show(view, "No record's name contains \"" + f + "\".", "Find", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    public sealed class MainForm : Form
    {
        readonly ComboBox picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 340, Margin = new Padding(3, 8, 3, 3) };
        readonly ListBox nav = new ListBox { Dock = DockStyle.Left, Width = 130, IntegralHeight = false, BorderStyle = BorderStyle.None, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 34, BackColor = Color.FromArgb(248, 246, 251) };
        readonly Panel host = new Panel { Dock = DockStyle.Fill };
        readonly Label banner = new Label { Dock = DockStyle.Top, Height = 28, BackColor = Ui.Warning, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0), Visible = false };
        readonly ToolStripStatusLabel statusText = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel changesText = new ToolStripStatusLabel();
        readonly Timer timer = new Timer { Interval = 500 };
        readonly List<IScreen> screens = new List<IScreen>();
        readonly HashSet<IScreen> bound = new HashSet<IScreen>();
        readonly Button saveButton, newButton;
        int gameCheck;
        bool gameRunning;
        string folder;
        bool changingPick;
        internal GameSave Current;
        internal OverviewScreen Overview;
        internal WeaponsScreen WeaponList;
        internal UnlocksScreen Unlocks;
        internal TablesScreen Tables;
        internal GarageScreen Garage;
        internal InventoryScreen InventoryList;

        void OpenByName(string screen)
        {
            int at = nav.Items.IndexOf(screen);
            if (at >= 0) nav.SelectedIndex = at;
        }

        sealed class SaveItem
        {
            public string Path;
            public string Text;
            public override string ToString() { return Text; }
        }

        static string SettingsPath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SR5SaveEditor", "folder.txt"); }
        }

        public MainForm()
        {
            Text = "SR5-Save-Editor";
            Font = new Font("Segoe UI", 9f);
            BackColor = Color.White;    // panels, bars and labels all take this
            ClientSize = new Size(1120, 780);
            MinimumSize = new Size(820, 480);
            StartPosition = FormStartPosition.CenterScreen;

            Panel head = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Ui.Purple };
            FlowLayoutPanel headFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(8, 2, 8, 0) };
            headFlow.Controls.Add(new Label { Text = "Save", ForeColor = Color.White, AutoSize = true, Margin = new Padding(3, 11, 3, 3) });
            headFlow.Controls.Add(picker);
            Button openButton = Ui.MakeButton("Folder...", delegate { PickFolder(); });
            saveButton = Ui.MakeButton("Save", delegate { SaveOver(); });
            newButton = Ui.MakeButton("Save as new", delegate { SaveNew(); });
            Button exportButton = Ui.MakeButton("Export copy...", delegate { Export(); });
            Button restoreButton = Ui.MakeButton("Restore backup...", delegate { Restore(); });
            foreach (Button b in new[] { openButton, saveButton, newButton, exportButton, restoreButton })
            {
                b.Margin = new Padding(3, 8, 3, 3);
                b.FlatAppearance.BorderSize = 0;
                headFlow.Controls.Add(b);
            }
            saveButton.Font = new Font(Font, FontStyle.Bold);
            head.Controls.Add(headFlow);

            Overview = new OverviewScreen();
            WeaponList = new WeaponsScreen();
            Unlocks = new UnlocksScreen();
            screens.Add(Overview);
            screens.Add(WeaponList);
            Tables = new TablesScreen();
            Garage = new GarageScreen();
            screens.Add(Garage);
            screens.Add(new CharacterScreen());
            screens.Add(new LoadoutScreen());
            screens.Add(Unlocks);
            screens.Add(new StatsScreen());
            screens.Add(Tables);
            screens.Add(new DataScreen());
            InventoryList = new InventoryScreen { OpenScreen = OpenByName };
            screens.Add(InventoryList);
            foreach (string s in Inventory.Screens) nav.Items.Add(s);
            nav.Items.Add("Inventory");
            nav.DrawItem += DrawNav;
            nav.SelectedIndexChanged += delegate { ShowScreen(); };

            StatusStrip strip = new StatusStrip { SizingGrip = true, BackColor = Color.FromArgb(248, 246, 251) };
            statusText.ForeColor = Ui.Muted;
            changesText.ForeColor = Ui.Ink;
            strip.Items.Add(statusText);
            strip.Items.Add(changesText);

            Controls.Add(host);
            Controls.Add(new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Color.FromArgb(225, 225, 230) });
            Controls.Add(nav);
            Controls.Add(banner);
            Controls.Add(head);
            Controls.Add(strip);

            picker.SelectedIndexChanged += delegate { PickSave(); };
            timer.Tick += delegate { Tick(); };
            FormClosing += delegate(object s, FormClosingEventArgs a) { if (!ConfirmDiscard()) a.Cancel = true; };
            Load += delegate { Start(); };
        }

        void DrawNav(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(sel ? Ui.Selected : nav.BackColor)) e.Graphics.FillRectangle(back, e.Bounds);
            if (sel) using (SolidBrush mark = new SolidBrush(Ui.Purple)) e.Graphics.FillRectangle(mark, e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, (string)nav.Items[e.Index], nav.Font, new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height),
                Color.Black, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        }

        void Start()
        {
            gameRunning = GameSave.GameRunning;
            timer.Start();
            string remembered = null;
            try { if (File.Exists(SettingsPath)) remembered = File.ReadAllText(SettingsPath).Trim(); }
            catch (IOException) { }
            if (remembered != null && Directory.Exists(remembered)) folder = remembered;
            else folder = GameSave.FindSaveFolders().FirstOrDefault();
            nav.SelectedIndex = 0;
            if (folder != null) LoadFolder(null);
            else SetStatus("Press Folder... and choose where your saves are: the folder with the .sf_pc files.");
            UpdateState();
        }

        void Tick()
        {
            if (++gameCheck >= 4)
            {
                gameCheck = 0;
                gameRunning = GameSave.GameRunning;
            }
            UpdateState();
        }

        internal void LoadFolder(string select)
        {
            changingPick = true;
            picker.Items.Clear();
            List<SaveItem> items = new List<SaveItem>();
            try
            {
                foreach (string p in Directory.GetFiles(folder, "*.sf_pc"))
                {
                    string n = System.IO.Path.GetFileNameWithoutExtension(p);
                    if (n.StartsWith("SR_PROFILE", StringComparison.OrdinalIgnoreCase)) continue;
                    items.Add(new SaveItem { Path = p, Text = n + "  -  " + File.GetLastWriteTime(p).ToString("d MMM yyyy HH:mm") });
                }
            }
            catch (IOException ex) { SetStatus("Couldn't read the folder: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { SetStatus("Couldn't read the folder: " + ex.Message); }
            items = items.OrderByDescending(i => File.GetLastWriteTime(i.Path)).ToList();
            foreach (SaveItem i in items) picker.Items.Add(i);
            changingPick = false;
            if (items.Count == 0)
            {
                Current = null;
                SetStatus("No saves in " + folder);
                return;
            }
            SaveItem pick = items.FirstOrDefault(i => string.Equals(i.Path, select, StringComparison.OrdinalIgnoreCase)) ?? items[0];
            picker.SelectedItem = pick;
        }

        string loadedPath;

        void PickSave()
        {
            if (changingPick || picker.SelectedItem == null) return;
            SaveItem item = (SaveItem)picker.SelectedItem;
            if (string.Equals(item.Path, loadedPath, StringComparison.OrdinalIgnoreCase) && Current != null && Current.Changes == 0) return;
            if (!ConfirmDiscard())
            {
                changingPick = true;
                foreach (SaveItem i in picker.Items)
                    if (string.Equals(i.Path, loadedPath, StringComparison.OrdinalIgnoreCase)) picker.SelectedItem = i;
                changingPick = false;
                return;
            }
            OpenSave(item.Path);
        }

        internal bool OpenSave(string path)
        {
            try
            {
                GameSave g = GameSave.Load(path);
                Current = g;
                loadedPath = path;
                bound.Clear();
                ShowScreen();
                SetStatus(path);
                return true;
            }
            catch (SaveFormatException ex)
            {
                MessageBox.Show(this, "This save can't be opened safely, so it hasn't been touched.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "The save couldn't be read.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, "The save couldn't be read.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        internal void ShowScreen()
        {
            int i = nav.SelectedIndex;
            if (i < 0) return;
            IScreen s = screens[i];
            host.SuspendLayout();
            host.Controls.Clear();
            if (Current != null)
            {
                if (bound.Add(s)) s.Bind(Current);
                host.Controls.Add(s.View);
            }
            host.ResumeLayout();
        }

        internal void SelectScreen(int i) { nav.SelectedIndex = i; }

        bool ConfirmDiscard()
        {
            if (Current == null || Current.Changes == 0) return true;
            return MessageBox.Show(this, "You have changes that aren't saved. Discard them?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        void SetStatus(string text) { statusText.Text = text; }

        void UpdateState()
        {
            int n = Current == null ? 0 : Current.Changes;
            changesText.Text = n == 0 ? "No changes" : "Changes not saved";
            banner.Text = "Saints Row is running. Close it before saving, so it can't overwrite your edits or read a half-written file.";
            if (banner.Visible != gameRunning) banner.Visible = gameRunning;
            saveButton.Enabled = Current != null && !gameRunning;
            newButton.Enabled = Current != null && !gameRunning;
        }

        void PickFolder()
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Choose the folder with your Saints Row saves (the .sf_pc files).\nEpic: sr5\\_cloudfolder\\saves\\SR in the game's folder.";
                if (folder != null) d.SelectedPath = folder;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!ConfirmDiscard()) return;
                folder = d.SelectedPath;
                try
                {
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsPath));
                    File.WriteAllText(SettingsPath, folder);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                Current = null;
                loadedPath = null;
                host.Controls.Clear();
                LoadFolder(null);
            }
        }

        bool Write(string path)
        {
            try
            {
                string backup = Current.SaveTo(path);
                loadedPath = path;
                SetStatus(backup == null ? "Saved " + path : "Saved. The previous version is backed up in " + backup);
                bound.Clear();
                ShowScreen();
                return true;
            }
            catch (SaveFormatException ex)
            {
                MessageBox.Show(this, "Nothing was written.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (IOException ex)
            {
                MessageBox.Show(this, "The save couldn't be written.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            catch (UnauthorizedAccessException ex)
            {
                MessageBox.Show(this, "The save couldn't be written. If it's under Program Files, try running the editor as administrator.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return false;
        }

        bool BlockedByGame()
        {
            gameRunning = GameSave.GameRunning;
            if (!gameRunning) return false;
            MessageBox.Show(this, "Saints Row is running. Close it first: it can overwrite saves while it runs.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        void SaveOver()
        {
            if (Current == null || BlockedByGame()) return;
            if (Write(Current.SavePath)) LoadFolder(Current.SavePath);
        }

        void SaveNew()
        {
            if (Current == null || BlockedByGame()) return;
            string path = GameSave.NewSlotPath(System.IO.Path.GetDirectoryName(Current.SavePath));
            if (Write(path))
            {
                LoadFolder(path);
                SetStatus("Saved as " + System.IO.Path.GetFileNameWithoutExtension(path) + ". The game reads its save list when it starts, so restart it to see this one.");
            }
        }

        // Saves a copy elsewhere and keeps editing the original.
        void Export()
        {
            if (Current == null) return;
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = "Export a copy of this save";
                d.Filter = "Saints Row saves (*.sf_pc)|*.sf_pc";
                d.FileName = System.IO.Path.GetFileName(Current.SavePath);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (folder != null && string.Equals(System.IO.Path.GetDirectoryName(d.FileName), folder, StringComparison.OrdinalIgnoreCase) && BlockedByGame()) return;
                string keep = Current.SavePath;
                int changes = Current.Changes;
                if (Write(d.FileName))
                {
                    Current.SavePath = keep;
                    loadedPath = keep;
                    Current.File.Changes = changes;
                    SetStatus("Exported to " + d.FileName);
                }
            }
        }

        void Restore()
        {
            if (Current == null && folder == null) return;
            if (BlockedByGame() || !ConfirmDiscard()) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a backup to put back";
                d.Filter = "Saints Row saves (*.sf_pc)|*.sf_pc";
                if (Directory.Exists(GameSave.BackupRoot)) d.InitialDirectory = GameSave.BackupRoot;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    SaveFile.Parse(File.ReadAllBytes(d.FileName));
                    string target = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(d.FileName));
                    if (string.Equals(System.IO.Path.GetFullPath(target), System.IO.Path.GetFullPath(d.FileName), StringComparison.OrdinalIgnoreCase)) return;
                    string kept = File.Exists(target) ? GameSave.Backup(target) : null;
                    File.Copy(d.FileName, target, true);
                    string met = GameSave.MetaPath(d.FileName);
                    if (File.Exists(met)) File.Copy(met, GameSave.MetaPath(target), true);
                    Current = null;
                    LoadFolder(target);
                    SetStatus("Restored. " + (kept == null ? "" : "What was there is backed up in " + kept));
                }
                catch (SaveFormatException ex)
                {
                    MessageBox.Show(this, "That file isn't a save the editor can read.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (IOException ex)
                {
                    MessageBox.Show(this, "The backup couldn't be restored.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (UnauthorizedAccessException ex)
                {
                    MessageBox.Show(this, "The backup couldn't be restored.\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        // For the UI check.
        internal void SnapTo(string path)
        {
            using (Bitmap bmp = new Bitmap(Width, Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                bmp.Save(path, ImageFormat.Png);
            }
        }
    }

    // Opens the real window off-screen, goes through every screen with a copy of a save, makes edits through
    // the controls, saves, and reads the result back. Writes only into the output folder.
    static class UiCheck
    {
        public static int Run(string sourceSave, string outDir)
        {
            Directory.CreateDirectory(outDir);
            string path = Path.Combine(outDir, Path.GetFileName(sourceSave));
            File.Copy(sourceSave, path, true);
            File.Copy(GameSave.MetaPath(sourceSave), GameSave.MetaPath(path), true);

            MainForm f = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            int failures = 0;
            Action<bool, string> check = delegate(bool ok, string what)
            {
                Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
                if (!ok) failures++;
            };
            Application.ThreadException += delegate(object s, System.Threading.ThreadExceptionEventArgs a)
            {
                check(false, "exception: " + a.Exception);
            };
            f.Show();
            Application.DoEvents();
            check(f.OpenSave(path), "opens " + Path.GetFileName(path));

            string[] screens = { "overview", "weapons", "garage", "character", "loadout", "unlocks", "stats", "tables", "data", "inventory" };
            for (int i = 0; i < screens.Length; i++)
            {
                f.SelectScreen(i);
                Application.DoEvents();
                f.SnapTo(Path.Combine(outDir, screens[i] + ".png"));
                check(File.Exists(Path.Combine(outDir, screens[i] + ".png")), "screen " + screens[i] + " draws");
            }

            f.SelectScreen(1);
            Application.DoEvents();
            WeaponEntry first = f.WeaponList.FirstListed;
            bool wasOwned = first.Record != null && first.Record.Child("m_player_unlocked").Value != 0;
            f.WeaponList.SetFirstListedOwned(!wasOwned);
            bool nowOwned = first.Record != null && first.Record.Child("m_player_unlocked").Value != 0;
            check(nowOwned == !wasOwned, "weapon checkbox edits " + first.Info.Name);
            check(f.WeaponList.OpenFirstLevelList(), "level list opens");

            f.Current.SetCurrency("cash", 31337);
            f.Unlocks.Bind(f.Current);
            f.Unlocks.UnlockEverything();
            f.SelectScreen(5);
            Application.DoEvents();
            f.SnapTo(Path.Combine(outDir, "unlocks_after.png"));

            f.SelectScreen(2);
            Application.DoEvents();
            f.Garage.PickFirstCustomized();
            Application.DoEvents();
            check(f.Garage.OptionList.SectionCount >= 8, "garage shows option sections for a car");
            f.SnapTo(Path.Combine(outDir, "garage_vehicle.png"));
            foreach (string section in new[] { "Performance", "Paint and finish", "Wheels", "Underglow", "Sound", "Parts" })
            {
                check(f.Garage.OptionList.SelectSection(section), "garage has " + section);
                Application.DoEvents();
                f.SnapTo(Path.Combine(outDir, "garage_" + section.Split(' ')[0].ToLowerInvariant() + ".png"));
            }
            check(f.Garage.OptionList.SelectFirst("Material:"), "garage has a material section");
            Application.DoEvents();
            f.SnapTo(Path.Combine(outDir, "garage_material.png"));

            f.SelectScreen(7);
            Application.DoEvents();
            check(f.Tables.SelectKind("Missions"), "tables lists missions");
            Application.DoEvents();
            check(f.Tables.RowCount > 0 && f.Tables.ColumnCount >= 5, "missions table has rows and columns");
            f.SnapTo(Path.Combine(outDir, "tables_missions.png"));
            f.SelectScreen(9);
            Application.DoEvents();
            check(f.InventoryList.RowCount > 5000, "inventory lists everything");
            f.InventoryList.Search("wagon_09");
            Application.DoEvents();
            check(f.InventoryList.RowCount >= 2, "inventory search finds the Truckster");
            f.SnapTo(Path.Combine(outDir, "inventory_search.png"));
            f.InventoryList.Search("");
            f.SelectScreen(7);
            Application.DoEvents();
            check(f.Tables.SelectKind("Stunt counters"), "tables lists stunts");
            f.Tables.SetColumnForAll("m_count", "7");
            Application.DoEvents();
            check(f.Current.All(Crc.Of("stunt_info_data")).All(r => r.Child("m_count").Float == 7f), "set for all listed changes every row");
            string backup = f.Current.SaveTo(path);
            check(backup != null, "saves with a backup");

            GameSave re = GameSave.Load(path);
            check(re.Currency("cash").Value == 31337, "cash survives saving");
            check(re.Meta.Get(MetaFile.Cash) == 31337, "load menu cash follows");
            Category perks = Categories.All.First(c => c.Title == "Perks");
            check(re.All(perks.StructHash).All(r => perks.IsOwned(r)), "perks all owned after unlock");
            if (Directory.Exists(backup)) Directory.Delete(backup, true);

            f.Current.File.Changes = 0;
            f.Close();
            Console.WriteLine(failures == 0 ? "UI check passed" : failures + " UI failures");
            return failures == 0 ? 0 : 1;
        }
    }
}
