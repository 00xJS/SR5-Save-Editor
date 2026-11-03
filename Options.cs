// Turns a record into a list of labelled options by walking every value in it, so nothing can be left out:
// a field nobody planned for still gets a row. Sections, labels, lists and notes only make the rows nicer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Sr5Save
{
    public enum OptionKind { Text, Check, Choice, Color }

    public sealed class Choice
    {
        public uint Value { get; set; }
        public string Text { get; set; }
    }

    public sealed class OptionRow
    {
        public string Section, Label, Note, Path;
        public OptionKind Kind;
        public Node Node;
        public List<Node> Nodes = new List<Node>();     // every value this row shows or sets
        public List<Choice> Choices;
        public Func<float[]> GetColor;
        public Action<float[]> SetColor;
        public Func<string> GetText;
        public Func<string, string> SetText;

        public string Value
        {
            get { return GetText != null ? GetText() : Node.Text; }
        }

        // Accepts what Value prints, or a choice's own label. Returns an error message, or null.
        public string SetValue(string text)
        {
            if (SetText != null) return SetText(text);
            if (Choices != null)
            {
                Choice c = Choices.FirstOrDefault(x => x.Text == text);
                if (c != null) { Node.SetRaw(c.Value); return null; }
            }
            return Node.TrySetText(text);
        }
    }

    public sealed class NamedSlot
    {
        public string Label;
        public string[] Options;
    }

    public sealed class VehicleAxle
    {
        public string Name, Rim, Tire;
        public string MinSize, MaxSize, DefaultSize, MinWidth, MaxWidth;
    }

    public sealed class VehicleOptions
    {
        public string Display = "", Underglow;
        public List<NamedSlot> Parts = new List<NamedSlot>(), Wraps = new List<NamedSlot>();
        public List<string> Links = new List<string>();
        public List<VehicleAxle> Axles = new List<VehicleAxle>();
    }

    // Each vehicle's real parts, wraps, wheels and underglow, and the game's sounds, from its own tables.
    public static class VehicleTables
    {
        static Dictionary<string, VehicleOptions> vehicles;
        static Dictionary<string, string[]> rims, tires, glows;
        static Dictionary<string, List<Choice>> sounds;

        static void Load()
        {
            if (vehicles != null) return;
            vehicles = new Dictionary<string, VehicleOptions>(StringComparer.OrdinalIgnoreCase);
            rims = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            tires = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            glows = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            sounds = new Dictionary<string, List<Choice>>();
            foreach (string line in Resources.Lines("vehicle_tables.txt"))
            {
                string[] p = line.Split('\t');
                switch (p[0])
                {
                    case "A": if (p.Length >= 3) Vehicle(p[1]).Display = p[2]; break;
                    case "P": if (p.Length == 5) Set(Vehicle(p[1]).Parts, p[2], p[3], p[4]); break;
                    case "T": if (p.Length == 5) Set(Vehicle(p[1]).Wraps, p[2], p[3], p[4]); break;
                    case "L":
                        if (p.Length == 4)
                        {
                            List<string> links = Vehicle(p[1]).Links;
                            int at = int.Parse(p[2]);
                            while (links.Count <= at) links.Add("");
                            links[at] = p[3];
                        }
                        break;
                    case "W":
                        if (p.Length == 11)
                            Vehicle(p[1]).Axles.Add(new VehicleAxle { Name = p[3], MinSize = p[4], MaxSize = p[5], DefaultSize = p[6], MinWidth = p[7], MaxWidth = p[8], Rim = p[9], Tire = p[10] });
                        break;
                    case "U": if (p.Length == 3) Vehicle(p[1]).Underglow = p[2]; break;
                    case "G": if (p.Length == 3) glows[p[1]] = p[2].Split('|'); break;
                    case "R": if (p.Length == 3) rims[p[1]] = p[2].Split('|'); break;
                    case "I": if (p.Length == 3) tires[p[1]] = p[2].Split('|'); break;
                    case "H":
                        if (p.Length == 4)
                        {
                            List<Choice> list;
                            if (!sounds.TryGetValue(p[1], out list)) sounds[p[1]] = list = new List<Choice>();
                            list.Add(new Choice { Value = uint.Parse(p[2], NumberStyles.HexNumber), Text = p[3] });
                        }
                        break;
                }
            }
        }

        static VehicleOptions Vehicle(string name)
        {
            VehicleOptions v;
            if (!vehicles.TryGetValue(name, out v)) vehicles[name] = v = new VehicleOptions();
            return v;
        }

        static void Set(List<NamedSlot> list, string index, string label, string options)
        {
            int at = int.Parse(index);
            while (list.Count <= at) list.Add(null);
            list[at] = new NamedSlot { Label = label, Options = options.Length == 0 ? new string[0] : options.Split('|') };
        }

        public static VehicleOptions Find(string info)
        {
            Load();
            VehicleOptions v;
            return info != null && vehicles.TryGetValue(info, out v) ? v : null;
        }

        public static string[] Glow(string set) { Load(); string[] v; return set != null && glows.TryGetValue(set, out v) ? v : null; }
        public static string[] Rim(string set) { Load(); string[] v; return set != null && rims.TryGetValue(set, out v) ? v : null; }
        public static string[] Tire(string set) { Load(); string[] v; return set != null && tires.TryGetValue(set, out v) ? v : null; }
        public static List<Choice> Sounds(string field) { Load(); List<Choice> v; return sounds.TryGetValue(field, out v) ? v : null; }
    }

    public static class Options
    {
        static Dictionary<string, string> notes;
        static Dictionary<string, List<Choice>> seenChoices;

        static void LoadData()
        {
            if (notes != null) return;
            notes = new Dictionary<string, string>();
            seenChoices = new Dictionary<string, List<Choice>>();
            foreach (string line in Resources.Lines("garage_options.txt"))
            {
                string[] p = line.Split('\t');
                if (p[0] == "N" && p.Length == 3) notes[p[1]] = p[2];
                else if (p[0] == "H" && p.Length == 4)
                {
                    List<Choice> list;
                    if (!seenChoices.TryGetValue(p[1], out list)) seenChoices[p[1]] = list = new List<Choice>();
                    list.Add(new Choice { Value = uint.Parse(p[2], NumberStyles.HexNumber), Text = p[3] });
                }
            }
        }

        // Lists for handle fields, from records in this save. Add the row's own value so it's always shown.
        static List<Choice> ChoicesFor(GameSave g, Dictionary<string, List<Choice>> cache, string field, uint current)
        {
            List<Choice> list;
            if (!cache.TryGetValue(field, out list))
            {
                list = new List<Choice>();
                string st = null;
                bool ownedOnly = false;
                switch (field)
                {
                    case "m_material": st = "vehicle_cust_mat_data"; break;
                    case "m_weapon_skin": st = "weapon_skin_state"; break;
                    case "m_wearable": st = "char_cust_wearable_data"; ownedOnly = true; break;
                    case "m_material_override": st = "char_cust_material_data"; break;
                    case "m_skin_shader_handle": st = "char_cust_skin_shader_info_data"; break;
                }
                if (st != null)
                    foreach (Node r in g.All(Crc.Of(st)))
                    {
                        if (ownedOnly && r.Child("m_purchased") != null && r.Child("m_purchased").Value == 0) continue;
                        list.Add(new Choice { Value = r.Key, Text = r.Id });
                    }
                List<Choice> known = VehicleTables.Sounds(field);
                if (known != null) list.AddRange(known);
                List<Choice> seen;
                if (seenChoices.TryGetValue(field, out seen)) list.AddRange(seen.Where(c => !list.Any(x => x.Value == c.Value)));
                list = list.OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase).ToList();
                list.Insert(0, new Choice { Value = 0, Text = "(none)" });
                cache[field] = list;
            }
            if (!list.Any(c => c.Value == current))
                list.Add(new Choice { Value = current, Text = Names.Find(current) ?? current.ToString("x8") });
            return list;
        }

        static readonly Dictionary<string, string> FriendlyFields = new Dictionary<string, string>
        {
            { "m_engine_audio", "Engine sound" }, { "m_tranny_audio", "Transmission" }, { "m_siren_audio", "Siren" }, { "m_horn_audio", "Horn" },
            { "m_rim_variant_idx", "Rim" }, { "m_tire_variant_idx", "Tire" }, { "m_tire_profile", "Tire profile" }, { "m_torque_level", "Engine level" },
            { "m_frame_level", "Frame level" }, { "m_bumper_level", "Bumper level" }, { "m_tire_level", "Tires level" }, { "m_nitro_level", "Nitro level" },
            { "m_color_choice", "Colour" }, { "m_vehicle_info", "Vehicle" }, { "m_variant_info", "Model" }, { "m_reward_variant", "Reward model" },
            { "m_plate_seed", "Licence plate" }, { "m_paint_selection", "Paint choice" }
        };

        static string Label(string field)
        {
            string s;
            return FriendlyFields.TryGetValue(field, out s) ? s : Names.Pretty(field);
        }

        static List<Choice> NumberChoices(int max)
        {
            List<Choice> list = new List<Choice>();
            for (int i = 0; i <= max; i++) list.Add(new Choice { Value = (uint)i, Text = i.ToString() });
            return list;
        }

        static OptionRow Scalar(GameSave g, Dictionary<string, List<Choice>> cache, Node n, string section, string label, string path, string notePath)
        {
            OptionRow row = new OptionRow { Section = section, Label = label, Path = path, Node = n, Kind = OptionKind.Text };
            row.Nodes.Add(n);
            string note;
            if (notePath != null && notes.TryGetValue(notePath, out note)) row.Note = note;
            if (n.Op == Node.OpBool) row.Kind = OptionKind.Check;
            else if (n.Field != null && n.Field.IsHandle)
            {
                row.Kind = OptionKind.Choice;
                row.Choices = ChoicesFor(g, cache, n.FieldName, n.Value);
                if (row.Note == null) row.Note = "Choose from the list.";
            }
            return row;
        }

        static float[] FloatsOf(List<Node> bytes)
        {
            byte[] raw = new byte[12];
            for (int i = 0; i < 12 && i < bytes.Count; i++) raw[i] = (byte)bytes[i].Value;
            return new[] { BitConverter.ToSingle(raw, 0), BitConverter.ToSingle(raw, 4), BitConverter.ToSingle(raw, 8) };
        }

        static void SetFloats(List<Node> bytes, float[] v)
        {
            for (int i = 0; i < 3; i++)
            {
                byte[] raw = BitConverter.GetBytes(v[i]);
                for (int k = 0; k < 4 && i * 4 + k < bytes.Count; k++) bytes[i * 4 + k].SetRaw(raw[k]);
            }
        }

        static string FloatsText(float[] v)
        {
            return string.Join(", ", v.Select(x => float.IsNaN(x) || Math.Abs(x) > 100000f ? "?" : x.ToString("0.###", CultureInfo.InvariantCulture)));
        }

        static string ParseFloats(string text, int count, Action<float[]> set, float[] keep)
        {
            string[] parts = (text ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > count) return "Enter up to " + count + " numbers, separated by commas.";
            float[] v = (float[])keep.Clone();
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != "?" && !float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) || float.IsNaN(v[i]) || float.IsInfinity(v[i]))
                    return "\"" + parts[i] + "\" isn't a number.";
            set(v);
            return null;
        }

        // A material parameter: 12 bytes holding 3 floats. Colours use all three; single values use the first.
        static OptionRow ParamRow(Node param, string section, string label, string path)
        {
            List<Node> bytes = param.Children;
            OptionRow row = new OptionRow { Section = section, Label = label, Path = path };
            row.Nodes.AddRange(bytes);
            row.GetColor = () => FloatsOf(bytes);
            row.SetColor = v => SetFloats(bytes, v);
            row.GetText = () => FloatsText(FloatsOf(bytes));
            row.SetText = text => ParseFloats(text, 3, v => SetFloats(bytes, v), FloatsOf(bytes));
            row.Kind = label.IndexOf("color", StringComparison.OrdinalIgnoreCase) >= 0 ? OptionKind.Color : OptionKind.Text;
            row.Note = row.Kind == OptionKind.Color ? "Red, green, blue. Double-click to pick." : "Often one number with unused values after it.";
            return row;
        }

        // Underglow and similar: x, y, z, w floats.
        static OptionRow TupleRow(Node tuple, string section, string label, string path)
        {
            List<Node> parts = tuple.Children;
            OptionRow row = new OptionRow { Section = section, Label = label, Path = path, Kind = OptionKind.Color, Note = "Red, green, blue, strength. Double-click to pick." };
            row.Nodes.AddRange(parts);
            Func<float[]> get = () => parts.Select(p => p.Float).ToArray();
            Action<float[]> set = v => { for (int i = 0; i < parts.Count && i < v.Length; i++) parts[i].SetFloat(v[i]); };
            row.GetColor = get;
            row.SetColor = set;
            row.GetText = () => FloatsText(get());
            row.SetText = text => ParseFloats(text, parts.Count, set, get());
            return row;
        }

        // The nickname: UTF-16 characters. Unset ones hold leftover memory, which reads as no name.
        static OptionRow NicknameRow(Node array, string section, string path)
        {
            List<Node> chars = array.Children;
            OptionRow row = new OptionRow { Section = section, Label = "Nickname", Path = path, Note = "Not confirmed in the game: no save seen has used one." };
            row.Nodes.AddRange(chars);
            row.GetText = () =>
            {
                StringBuilder b = new StringBuilder();
                foreach (Node c in chars)
                {
                    char ch = (char)(ushort)c.Value;
                    if (ch == 0) return b.ToString();
                    if (ch < 32 || ch > 126) return "";
                    b.Append(ch);
                }
                return "";
            };
            row.SetText = text =>
            {
                text = text ?? "";
                if (text.Length >= chars.Count) return "That's too long: " + (chars.Count - 1) + " characters at most.";
                if (text.Any(ch => ch < 32 || ch > 126)) return "Use plain letters, numbers and punctuation.";
                for (int i = 0; i < chars.Count; i++) chars[i].SetRaw(i < text.Length ? (uint)(ushort)text[i] : 0);
                return null;
            };
            return row;
        }

        // Offers a list where the value is a position in it. A value outside the list, such as -1 for unset, stays visible.
        static void NameIt(OptionRow row, string[] labels, string unset)
        {
            if (labels == null || labels.Length == 0) return;
            List<Choice> list = new List<Choice>();
            for (int i = 0; i < labels.Length; i++) list.Add(new Choice { Value = (uint)i, Text = labels[i] });
            uint current = row.Node.Value;
            if (current >= labels.Length)
                list.Insert(0, new Choice { Value = current, Text = current == uint.MaxValue ? unset : "Number " + current });
            row.Kind = OptionKind.Choice;
            row.Choices = list;
            row.Note = labels.Length + " to choose from.";
        }

        static string VehicleSection(string path)
        {
            if (path.Contains("m_material_data")) return null;     // named per material as it goes
            if (path.Contains("m_normal_axles")) return "Wheels";
            if (path.Contains("m_offroad_axles")) return "Offroad wheels";
            if (path.Contains("m_underglow_data")) return "Underglow";
            if (path.Contains("m_vid_slot_selection")) return "Parts";
            if (path.Contains("m_mat_tex_selection")) return "Wraps and decals";
            if (path.Contains("m_linked_data")) return "Linked parts";
            if (path.Contains("_audio")) return "Sound";
            if (path.Contains("m_wrap_save_data")) return "Paint and finish";
            if (path.Contains("m_external_save_data")) return "Part slots";
            if (path.StartsWith("m_customized.")) return "Performance";
            return "Vehicle";
        }

        public static List<OptionRow> ForVehicle(GameSave g, Node slot)
        {
            LoadData();
            List<OptionRow> rows = new List<OptionRow>();
            Dictionary<string, List<Choice>> cache = new Dictionary<string, List<Choice>>();
            Node info = slot.Child("m_vehicle_info");
            VehicleOptions named = info == null ? null : VehicleTables.Find(Names.Find(info.Value));
            WalkVehicle(g, cache, named, slot, "", "", rows);
            string[] order = { "Vehicle", "Performance", "Sound", "Paint and finish", "Wraps and decals", "Parts", "Part slots", "Linked parts", "Wheels", "Offroad wheels", "Underglow" };
            return rows.OrderBy(r =>
            {
                int i = Array.IndexOf(order, r.Section);
                return i < 0 ? order.Length : i;     // materials come last, in the order they appear
            }).ToList();
        }

        static void WalkVehicle(GameSave g, Dictionary<string, List<Choice>> cache, VehicleOptions named, Node n, string path, string notePath, List<OptionRow> rows)
        {
            foreach (Node c in n.Children)
            {
                string field = c.FieldName;
                string p = path.Length > 0 ? path + "." + field : field;
                string np = notePath.Length > 0 ? notePath + "." + field : field;
                if (c.Kind == NodeKind.Struct)
                {
                    if (field == "components") { rows.Add(TupleRow(c, VehicleSection(p), "Glow color", p)); continue; }
                    WalkVehicle(g, cache, named, c, p, np, rows);
                }
                else if (c.Kind == NodeKind.Array)
                {
                    if (field == "m_nickname") { rows.Add(NicknameRow(c, "Vehicle", p)); continue; }
                    if (field == "m_material_data") { foreach (Node m in c.Children) WalkMaterial(g, cache, m, p, np + "[]", rows); continue; }
                    for (int i = 0; i < c.Children.Count; i++)
                    {
                        Node e = c.Children[i];
                        string ep = p + "[" + i + "]";
                        if (e.Kind == NodeKind.Scalar) rows.Add(ListRow(g, cache, named, e, field, i, VehicleSection(p), ep, np + "[]"));
                        else if (e.Kind == NodeKind.Struct) WalkAxle(g, cache, named, e, field, p, np + "[]", i, rows);
                    }
                }
                else if (c.Kind == NodeKind.Scalar)
                {
                    OptionRow row = Scalar(g, cache, c, VehicleSection(p), Label(field), p, np);
                    if (IsLevel(field)) { row.Kind = OptionKind.Choice; row.Choices = NumberChoices(field == "m_nitro_level" ? 1 : 3); }
                    if (field == "m_color_choice" && named != null) NameIt(row, VehicleTables.Glow(named.Underglow), "(no colour set)");
                    rows.Add(row);
                }
            }
        }

        static bool IsLevel(string field)
        {
            return field == "m_torque_level" || field == "m_frame_level" || field == "m_bumper_level" || field == "m_tire_level" || field == "m_nitro_level";
        }

        // A part, wrap or link: named from this vehicle's own tables when they cover it.
        static OptionRow ListRow(GameSave g, Dictionary<string, List<Choice>> cache, VehicleOptions named, Node e, string field, int i, string section, string path, string notePath)
        {
            NamedSlot slot = null;
            if (named != null)
            {
                List<NamedSlot> list = field == "m_vid_slot_selection" ? named.Parts : field == "m_mat_tex_selection" ? named.Wraps : null;
                if (list != null && i < list.Count) slot = list[i];
            }
            string label = ArrayLabel(field, i);
            if (slot != null) label = slot.Label.StartsWith("vehcust_tether") ? "Tether" : slot.Label;
            else if (named != null && field == "m_linked_data" && i < named.Links.Count && named.Links[i].Length > 0) label = named.Links[i];
            OptionRow row = Scalar(g, cache, e, section, label, path, notePath);
            if (slot != null) NameIt(row, slot.Options, "(game's default)");
            return row;
        }

        static string ArrayLabel(string field, int i)
        {
            switch (field)
            {
                case "m_vid_slot_selection": return "Part slot " + (i + 1);
                case "m_mat_tex_selection": return "Wrap or decal " + (i + 1);
                case "m_linked_data": return "Linked part " + (i + 1);
                case "m_part_slot_choice": return "Part slot choice " + (i + 1);
            }
            return Label(field) + " " + (i + 1);
        }

        static void WalkAxle(GameSave g, Dictionary<string, List<Choice>> cache, VehicleOptions named, Node e, string arrayField, string path, string notePath, int index, List<OptionRow> rows)
        {
            VehicleAxle axle = named != null && index < named.Axles.Count ? named.Axles[index] : null;
            string prefix = index == 0 ? "Front: " : index == 1 ? "Rear: " : "Axle " + (index + 1) + ": ";
            foreach (Node f in e.Children)
            {
                if (f.Kind != NodeKind.Scalar) continue;
                OptionRow row = Scalar(g, cache, f, VehicleSection(path), prefix + Label(f.FieldName), path + "[" + index + "]." + f.FieldName, notePath + "." + f.FieldName);
                if (axle != null && arrayField == "m_normal_axles")
                {
                    if (f.FieldName == "m_wheel_size") row.Note = "The game's range: " + axle.MinSize + " to " + axle.MaxSize + ", normally " + axle.DefaultSize + ". 0 uses the normal size.";
                    else if (f.FieldName == "m_wheel_width") row.Note = "The game's range: " + axle.MinWidth + " to " + axle.MaxWidth + ". 0 uses the normal width.";
                }
                if (axle != null && f.FieldName == "m_rim_variant_idx") NameIt(row, VehicleTables.Rim(axle.Rim), "(game's default)");
                else if (axle != null && f.FieldName == "m_tire_variant_idx") NameIt(row, VehicleTables.Tire(axle.Tire), "(game's default)");
                rows.Add(row);
            }
        }

        // Materials are named by what they paint, so an option means the same thing on any car with that material.
        static void WalkMaterial(GameSave g, Dictionary<string, List<Choice>> cache, Node m, string path, string notePath, List<OptionRow> rows)
        {
            Node handle = m.Child("m_material");
            string name = handle == null ? "?" : Names.Find(handle.Value) ?? handle.Value.ToString("x8");
            string section = "Material: " + Names.Pretty(name);
            string key = path + "{" + (handle == null ? "" : handle.Value.ToString("x8")) + "}";
            foreach (Node c in m.Children)
            {
                string field = c.FieldName;
                string np = notePath + "." + field;
                if (c.Kind == NodeKind.Scalar) rows.Add(Scalar(g, cache, c, section, Label(field), key + "." + field, np));
                else if (c.Kind == NodeKind.Struct)
                {
                    foreach (Node f in c.Children)
                        if (f.Kind == NodeKind.Scalar) rows.Add(Scalar(g, cache, f, section, Label(f.FieldName), key + "." + field + "." + f.FieldName, np + "." + f.FieldName));
                }
                else if (c.Kind == NodeKind.Array && field == "m_mat_pal_selection")
                {
                    for (int i = 0; i < c.Children.Count; i++)
                        rows.Add(Scalar(g, cache, c.Children[i], section, "Palette choice " + (i + 1), key + "." + field + "[" + i + "]", np + "[]"));
                }
                else if (c.Kind == NodeKind.Array && field == "m_param_mappings")
                {
                    foreach (Node pm in c.Children)
                    {
                        Node dyn = pm.Child("m_dynamic_parameter"), param = pm.Child("m_param"), flags = pm.Child("m_flags");
                        string pname = dyn == null ? "?" : Names.Find(dyn.Value) ?? dyn.Value.ToString("x8");
                        string pkey = key + "." + (dyn == null ? "" : dyn.Value.ToString("x8"));
                        if (dyn != null) rows.Add(Scalar(g, cache, dyn, section, pname + ": parameter", pkey + ".param", np + "[].m_dynamic_parameter"));
                        if (param != null) rows.Add(ParamRow(param, section, pname, pkey));
                        if (flags != null)
                            foreach (Node f in flags.Children)
                                if (f.Kind == NodeKind.Scalar) rows.Add(Scalar(g, cache, f, section, pname + ": " + Label(f.FieldName), pkey + "." + f.FieldName, null));
                    }
                }
                else if (c.Kind == NodeKind.Array)
                {
                    for (int i = 0; i < c.Children.Count; i++)
                        if (c.Children[i].Kind == NodeKind.Scalar)
                            rows.Add(Scalar(g, cache, c.Children[i], section, Label(field) + " " + (i + 1), key + "." + field + "[" + i + "]", null));
                }
            }
        }

        // Any other record, by the same rule: every value gets a row. Sections come from its top-level lists.
        public static List<OptionRow> ForRecord(GameSave g, Node record, string firstSection)
        {
            LoadData();
            List<OptionRow> rows = new List<OptionRow>();
            Dictionary<string, List<Choice>> cache = new Dictionary<string, List<Choice>>();
            WalkGeneric(g, cache, record, firstSection, "", "", rows);
            return rows;
        }

        // Only the record's own lists make sections. Anything nested deeper stays with its parent, labelled by where it is.
        static void WalkGeneric(GameSave g, Dictionary<string, List<Choice>> cache, Node n, string section, string path, string prefix, List<OptionRow> rows)
        {
            bool top = n.Kind == NodeKind.Record;
            foreach (Node c in n.Children)
            {
                string field = c.FieldName;
                string p = path.Length > 0 ? path + "." + field : field;
                if (c.Kind == NodeKind.Scalar) rows.Add(Scalar(g, cache, c, section, prefix + Label(field), p, null));
                else if (c.Kind == NodeKind.Struct) WalkGeneric(g, cache, c, top ? WearSection(c, Label(field)) : section, p, top ? "" : prefix + Label(field) + ": ", rows);
                else if (c.Kind == NodeKind.Array)
                {
                    for (int i = 0; i < c.Children.Count; i++)
                    {
                        Node e = c.Children[i];
                        string ep = p + "[" + i + "]";
                        string name = Label(field) + " " + (i + 1);
                        if (e.Kind == NodeKind.Scalar) rows.Add(Scalar(g, cache, e, top ? Label(field) : section, prefix + name, ep, null));
                        else if (e.Kind == NodeKind.Struct)
                        {
                            if (top && e.Child("m_wearable") != null) WalkGeneric(g, cache, e, WearSection(e, "Slot " + (i + 1)), ep, "", rows);
                            else if (top) WalkGeneric(g, cache, e, Label(field), ep, name + ": ", rows);
                            else WalkGeneric(g, cache, e, section, ep, prefix + name + ": ", rows);
                        }
                    }
                }
            }
        }

        // A clothing slot reads better by what's in it.
        static string WearSection(Node e, string name)
        {
            Node worn = e.Child("m_wearable");
            if (worn == null) return name;
            return name + ": " + (worn.Value == 0 ? "empty" : Names.Pretty(Names.Find(worn.Value) ?? worn.Value.ToString("x8")));
        }

        // Sets the same option on every car that has it. Returns how many changed.
        public static int ApplyToAllVehicles(GameSave g, Node from, OptionRow row)
        {
            int changed = 0;
            string text = row.Value;
            foreach (Node slot in g.GarageSlots())
            {
                if (slot == from) continue;
                OptionRow target = ForVehicle(g, slot).FirstOrDefault(r => r.Path == row.Path);
                if (target == null || target.Value == text) continue;
                if (target.SetValue(text) == null)
                {
                    changed++;
                    g.MarkCustomized(slot);
                }
            }
            return changed;
        }
    }
}
