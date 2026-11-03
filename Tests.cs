// Checks against real saves: tests\run.bat [saves folder]. Originals are only read.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sr5Save
{
    static class Tests
    {
        static int failures, checks;
        static string outDir;

        static void Check(bool ok, string what)
        {
            checks++;
            if (ok) return;
            failures++;
            Console.WriteLine("  FAIL " + what);
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static int FirstDiff(byte[] a, byte[] b)
        {
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) return i;
            return a.Length == b.Length ? -1 : Math.Min(a.Length, b.Length);
        }

        static int DiffCount(byte[] a, byte[] b)
        {
            int n = 0;
            for (int i = 0; i < Math.Min(a.Length, b.Length); i++) if (a[i] != b[i]) n++;
            return n + Math.Abs(a.Length - b.Length);
        }

        static void RoundTrip(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            SaveFile f = SaveFile.Parse(raw);
            byte[] back = f.Serialize();
            int d = FirstDiff(raw, back);
            Check(d < 0, Path.GetFileName(path) + " round trip differs at " + d);
            string met = GameSave.MetaPath(path);
            if (File.Exists(met))
            {
                byte[] m = File.ReadAllBytes(met);
                MetaFile mf = MetaFile.Parse(m);
                Check(Path.GetFileName(path).StartsWith("SR_PROFILE") ? mf == null : mf != null && mf.Has(MetaFile.Cash), Path.GetFileName(met) + " parse");
            }
        }

        static void CountBound(string path)
        {
            SaveFile f = SaveFile.Parse(File.ReadAllBytes(path));
            foreach (Block b in f.Blocks)
            {
                uint most = 0;
                foreach (Node r in b.Records)
                {
                    int s = 0, a = 0;
                    Measure(r, ref s, ref a);
                    most = Math.Max(most, (uint)(1 + s + a));
                }
                Check(b.Count <= most || (b.Count == 0 && most == 0), Path.GetFileName(path) + " " + b.Name + " count " + b.Count + " above bound " + most);
            }
        }

        static void Measure(Node n, ref int s, ref int a)
        {
            if (n.Children == null) return;
            foreach (Node c in n.Children)
            {
                if (c.Kind == NodeKind.Struct) s++;
                else if (c.Kind == NodeKind.Array) a++;
                Measure(c, ref s, ref a);
            }
        }

        // A value edit must change the value's own bytes and nothing else.
        static void ValueEdit(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            GameSave g = GameSave.Load(path);
            Node cash = g.Currency("cash");
            Check(cash != null, "cash found");
            uint old = cash.Value;
            uint target = old == 123456789u ? 987654321u : 123456789u;
            cash.SetRaw(target);
            byte[] edited = g.File.Serialize();
            int d = DiffCount(raw, edited);
            Check(d >= 1 && d <= 4, Path.GetFileName(path) + " cash edit changed " + d + " bytes");
            GameSave re = new GameSave { File = SaveFile.Parse(edited) };
            Check(re.Currency("cash").Value == target, "cash reads back");
        }

        static void MetaSync(string path)
        {
            string temp = Path.Combine(outDir, "metasync_" + Path.GetFileName(path));
            File.Copy(path, temp, true);
            File.Copy(GameSave.MetaPath(path), GameSave.MetaPath(temp), true);
            byte[] rawMeta = File.ReadAllBytes(GameSave.MetaPath(temp));
            GameSave g = GameSave.Load(temp);
            g.Currency("cash").SetRaw(424242);
            string backup = g.SaveTo(temp);
            if (backup != null) Directory.Delete(backup, true);
            byte[] after = File.ReadAllBytes(GameSave.MetaPath(temp));
            MetaFile m = MetaFile.Parse(after);
            Check(m.Get(MetaFile.Cash) == 424242, "met cash synced");
            Check(after.Length == rawMeta.Length, "met size kept");
            // Only the cash value may differ, unless the original was already out of step.
            Check(DiffCount(rawMeta, after) <= 8, "met changed " + DiffCount(rawMeta, after) + " bytes");
            Check(GameSave.Load(temp).Currency("cash").Value == 424242, "saved cash reads back");
            File.Delete(temp);
            File.Delete(GameSave.MetaPath(temp));
        }

        static void InsertRecords(string youngSave)
        {
            byte[] raw = File.ReadAllBytes(youngSave);
            GameSave g = GameSave.Load(youngSave);
            SaveFile before = SaveFile.Parse(raw);
            List<WeaponEntry> weapons = g.Weapons();
            int present = weapons.Count(w => w.Record != null);
            Check(weapons.Count >= present && weapons.Any(w => w.Record == null), "young save is missing weapons the catalog knows");
            WeaponEntry add = weapons.First(w => w.Record == null && w.Info.Kind == "shop");
            g.MaxWeapon(add, true);
            byte[] edited = g.File.Serialize();
            Check(edited.Length == raw.Length, "file size stays fixed");
            GameSave re = new GameSave { File = SaveFile.Parse(edited) };
            Node r = re.Find("inventory_weapon_data", add.Info.Name);
            Check(r != null, "added weapon reads back");
            Check(r != null && r.Child("m_player_unlocked").Value == 1, "added weapon is owned");
            Check(r != null && r.Child("m_stat_level_purchased").Value == 3, "added weapon is upgraded");
            Check(re.File.Blocks.Count == before.Blocks.Count, "section count unchanged");
            for (int i = 0; i < before.Blocks.Count; i++)
            {
                Block a = before.Blocks[i], b = re.File.Blocks[i];
                Check(a.Hash == b.Hash, "section order unchanged at " + i);
                if (a.Hash != add.Template.Owner.Hash)
                    Check(a.Records.Count == b.Records.Count, "other section " + a.Name + " unchanged");
                else
                    Check(b.Records.Count == a.Records.Count + 1, "one record added");
            }
            // The added record must round trip too, once it has come from disk.
            Check(Same(edited, re.File.Serialize()), "edited save round trips");
        }

        static void UnlockEverything(string path)
        {
            GameSave g = GameSave.Load(path);
            int added = 0, set = 0;
            foreach (Category c in Categories.All)
            {
                foreach (UnlockEntry e in g.Entries(c))
                {
                    if (e.Record == null) added++;
                    g.SetOwned(c, e, true);
                    set++;
                    if (e.Record != null) Check(c.IsOwned(e.Record), c.Title + " " + e.Id + " owned after unlock");
                }
            }
            foreach (WeaponEntry w in g.Weapons())
                if (w.Info.Kind == "shop") g.MaxWeapon(w, true);
            foreach (Node slot in g.GarageSlots()) g.MaxVehicle(slot);
            byte[] edited = g.File.Serialize();
            GameSave re = new GameSave { File = SaveFile.Parse(edited) };
            Check(Same(edited, re.File.Serialize()), Path.GetFileName(path) + " unlocked save round trips");
            foreach (Category c in Categories.All)
            {
                foreach (Node r in re.All(c.StructHash))
                    Check(c.IsOwned(r), c.Title + " " + r.Id + " owned after reload");
            }
            string outPath = Path.Combine(outDir, "unlocked_" + Path.GetFileName(path));
            File.WriteAllBytes(outPath, edited);
            Console.WriteLine("  " + Path.GetFileName(path) + ": set " + set + " items, added " + added + ", " + edited.Length + " bytes, data used to 0x"
                + DataEnd(edited).ToString("x"));
        }

        static int DataEnd(byte[] b)
        {
            int i = b.Length;
            while (i > 0 && b[i - 1] == 0) i--;
            return i;
        }

        // The rule the Level list relies on. Reports how well it holds rather than assuming it.
        static void WeaponLevelRule(string[] saves)
        {
            int held = 0;
            List<string> broke = new List<string>();
            foreach (string path in saves)
            {
                GameSave g = GameSave.Load(path);
                foreach (Node r in g.All(Crc.Of("inventory_weapon_data")))
                {
                    WeaponInfo w = WeaponTable.Get(r.Id);
                    Node mods = r.Child("m_equipped_mod_levels");
                    int level = r.Child("m_stat_level_purchased").Int;
                    if (mods == null || w.Tracks == 0 || level < 1 || level > 3) continue;
                    bool ok = true;
                    for (int i = 0; i < w.Tracks && i < mods.Children.Count; i++)
                        ok &= mods.Children[i].Value == GameSave.TrackLevels[level];
                    if (ok) held++;
                    else broke.Add(Path.GetFileNameWithoutExtension(path) + " " + r.Id + " level " + level + ": "
                        + string.Join(",", mods.Children.Select(c => c.Value.ToString())));
                }
            }
            Console.WriteLine("  track level rule held for " + held + " upgraded weapons, broke for " + broke.Count);
            foreach (string s in broke.Take(12)) Console.WriteLine("    " + s);
            Check(broke.Count == 0, "track level rule");
        }

        // Changing a level through the list has to leave the shape real saves have.
        static void WeaponLevelEdit(string path)
        {
            GameSave g = GameSave.Load(path);
            WeaponEntry e = g.Weapons().First(w => w.Record != null && w.Info.Kind == "shop" && w.Info.Tracks == 4);
            g.SetWeaponLevel(e, 2);
            Node mods = e.Record.Child("m_equipped_mod_levels");
            Check(mods.Children[0].Value == 3 && mods.Children[3].Value == 3, "level 2 sets tracks to 3");
            g.SetWeaponLevel(e, 3);
            Check(mods.Children[0].Value == 5 && mods.Children[3].Value == 5, "level 3 sets tracks to 5");
            uint last = mods.Children[4].Value;
            g.SetWeaponLevel(e, 1);
            Check(mods.Children[4].Value == last, "changing level leaves the ultimate's flag");
        }

        // Adding a car has to give what the game writes: the info hash real saves pair with that model.
        static void GarageTable(string[] saves)
        {
            int matched = 0, unknown = 0;
            List<string> wrong = new List<string>();
            foreach (string path in saves)
            {
                GameSave g = GameSave.Load(path);
                foreach (Node slot in g.GarageSlots())
                {
                    string variant = Names.Find(slot.Child("m_variant_info").Value);
                    VariantInfo v = VariantTable.Find(variant);
                    if (v == null) { unknown++; continue; }
                    if (Crc.Of(v.Vehicle) == slot.Child("m_vehicle_info").Value) matched++;
                    else wrong.Add(variant + " in " + Path.GetFileName(path));
                }
            }
            Console.WriteLine("  model table: " + matched + " garage slots match, " + unknown + " models not in the table, " + wrong.Count + " wrong");
            foreach (string s in wrong.Distinct().Take(10)) Console.WriteLine("    " + s);
            Check(wrong.Count == 0, "model table agrees with real garages");
        }

        static void GarageAddRemove(string path)
        {
            GameSave g = GameSave.Load(path);
            int before = g.GarageSlots().Count;
            uint rewarded = g.Find("vehicle_save_state").Child("m_rewarded_count").Value;
            VariantInfo v = VariantTable.Find("tank_01_preset_1");
            Node added = g.AddVehicle(v);
            Check(g.GarageSlots().Count == before + 1, "vehicle added");
            Check(g.Find("vehicle_save_state").Child("m_rewarded_count").Value == rewarded + 1, "reward count follows");
            byte[] bytes = g.File.Serialize();
            GameSave re = new GameSave { File = SaveFile.Parse(bytes) };
            List<Node> slots = re.GarageSlots();
            Check(slots.Count == before + 1, "added vehicle reads back");
            Node last = slots[slots.Count - 1];
            Check(last.Child("m_variant_info").Value == Crc.Of("tank_01_preset_1") && last.Child("m_vehicle_info").Value == Crc.Of(v.Vehicle), "added vehicle is that model");
            Check(last.ElementIndex == before, "added vehicle has the next element index");
            Check(Same(bytes, re.File.Serialize()), "save with added vehicle round trips");

            // Taking it out again has to give back the save we started with.
            g.RemoveVehicle(added);
            Check(Same(g.File.Serialize(), File.ReadAllBytes(path)) || DiffCount(g.File.Serialize(), File.ReadAllBytes(path)) == 0, "adding then removing restores the file");

            // Removing from the middle must renumber what follows.
            GameSave h = GameSave.Load(path);
            h.RemoveVehicle(h.GarageSlots()[0]);
            GameSave back = new GameSave { File = SaveFile.Parse(h.File.Serialize()) };
            Check(back.GarageSlots().Count == before - 1, "vehicle removed");
            for (int i = 0; i < back.GarageSlots().Count; i++) Check(back.GarageSlots()[i].ElementIndex == i, "slots renumbered");
        }

        // The layout rule adding and removing relies on, across every array in every save.
        static void ArrayIndexRule(string path)
        {
            SaveFile f = SaveFile.Parse(File.ReadAllBytes(path));
            foreach (Block b in f.Blocks)
                foreach (Node r in b.Records)
                    CheckArrays(r, path);
        }

        static void CheckArrays(Node n, string path)
        {
            if (n.Children == null) return;
            for (int i = 0; i < n.Children.Count; i++)
            {
                Node c = n.Children[i];
                if (n.Kind == NodeKind.Array)
                {
                    if (c.ElementIndex != i) Check(false, Path.GetFileName(path) + " " + n.FieldName + " element " + i + " says " + c.ElementIndex);
                    else if (c.FieldIndex != n.FieldIndex) Check(false, Path.GetFileName(path) + " " + n.FieldName + " element " + i + " has field " + c.FieldIndex);
                    else checks++;
                }
                CheckArrays(c, path);
            }
        }

        // Every value in a vehicle has to reach the option panel, or the panel can't claim to be complete.
        static void OptionsAreComplete(string path)
        {
            GameSave g = GameSave.Load(path);
            foreach (Node slot in g.GarageSlots())
            {
                List<OptionRow> rows = Options.ForVehicle(g, slot);
                HashSet<Node> covered = new HashSet<Node>();
                foreach (OptionRow row in rows) foreach (Node n in row.Nodes) covered.Add(n);
                List<Node> all = new List<Node>();
                Scalars(slot, all);
                foreach (Node n in all) Check(covered.Contains(n), slot.Id + " " + n.FieldName + " has an option row");
            }
        }

        static void Scalars(Node n, List<Node> into)
        {
            foreach (Node c in n.Children)
            {
                if (c.Kind == NodeKind.Scalar) into.Add(c);
                else if (c.Children != null) Scalars(c, into);
            }
        }

        static void OptionEdits(string path)
        {
            GameSave g = GameSave.Load(path);
            Node slot = g.GarageSlots().First(s => s.Child("m_customized").Path("m_wrap_save_data.m_material_data") != null && s.Child("m_has_customization").Value == 1);
            List<OptionRow> rows = Options.ForVehicle(g, slot);
            OptionRow color = rows.First(r => r.Kind == OptionKind.Color && r.Label.Contains("paint color"));
            color.SetColor(new[] { 0.25f, 0.5f, 0.75f });
            byte[] bytes = g.File.Serialize();
            GameSave re = new GameSave { File = SaveFile.Parse(bytes) };
            Node slot2 = re.GarageSlots()[slot.ElementIndex];
            OptionRow color2 = Options.ForVehicle(re, slot2).First(r => r.Path == color.Path);
            float[] back = color2.GetColor();
            Check(back[0] == 0.25f && back[1] == 0.5f && back[2] == 0.75f, "paint colour reads back");
            Check(Same(bytes, re.File.Serialize()), "save with a new colour round trips");

            OptionRow torque = rows.First(r => r.Path == "m_customized.m_torque_level");
            torque.SetValue("2");
            Check(GameSave.GarageField(slot, "m_torque_level").Value == 2, "option row sets a level");
            Check(torque.SetValue("nonsense") != null, "option row refuses bad text");

            // Set on every car, then copy a look between two of the same vehicle.
            int changed = Options.ApplyToAllVehicles(g, slot, torque);
            Check(changed > 0 && g.GarageSlots().All(s => GameSave.GarageField(s, "m_torque_level").Value == 2), "option set on every car");
            GameSave h = GameSave.Load(path);
            List<IGrouping<uint, Node>> same = h.GarageSlots().GroupBy(s => s.Child("m_vehicle_info").Value).Where(x => x.Count() >= 2).ToList();
            if (same.Count > 0)
            {
                Node a = same[0].ElementAt(0), b = same[0].ElementAt(1);
                GameSave.GarageField(a, "m_tire_level").SetRaw(3);
                Check(h.CopyLook(a, b), "look copied");
                Check(GameSave.GarageField(b, "m_tire_level").Value == 3, "copied look applies");
                GameSave reread = new GameSave { File = SaveFile.Parse(h.File.Serialize()) };
                Check(reread.GarageSlots().Count == h.GarageSlots().Count, "save with a copied look reads back");
            }
        }

        static void LoadoutRules(string path)
        {
            GameSave g = GameSave.Load(path);
            Check(g.DifficultyLevels.Count == 8, "8 difficulty sliders");
            Node slot = g.PerkSlots.First();
            uint old = slot.Child("m_perk_entry_h").Value;
            Node pick = g.OwnedPerks.First(p => p.Key != old && g.PerkSlots.All(s => s.Child("m_perk_entry_h").Value != p.Key));
            g.EquipPerk(slot, pick.Key);
            Check(slot.Child("m_perk_entry_h").Value == pick.Key, "slot holds the new perk");
            Check(pick.Child("m_equipped").Value == 1, "new perk is equipped");
            Node was = g.All(Crc.Of("perk_entry_state")).FirstOrDefault(p => p.Key == old);
            Check(was == null || was.Child("m_equipped").Value == 0, "old perk is unequipped");
        }

        // Every part and wrap chosen on every car in real saves has to be a real option in the game's tables.
        static void GarageOptionsAreReal(string[] saves)
        {
            int cars = 0, parts = 0, wraps = 0, unset = 0, off = 0, unnamed = 0, sounds = 0, soundsNamed = 0, glow = 0, wheels = 0, wheelsOff = 0, wheelsUnset = 0;
            foreach (string path in saves)
            {
                GameSave g = GameSave.Load(path);
                foreach (Node slot in g.GarageSlots())
                {
                    string info = Names.Find(slot.Child("m_vehicle_info").Value);
                    VehicleOptions named = VehicleTables.Find(info);
                    if (named == null) { unnamed++; continue; }
                    bool customized = slot.Child("m_has_customization").Value != 0;
                    if (customized) cars++;
                    foreach (OptionRow r in Options.ForVehicle(g, slot))
                    {
                        if (r.Path.Contains("_audio") && r.Node.Value != 0)
                        {
                            sounds++;
                            Choice c = r.Choices.First(x => x.Value == r.Node.Value);
                            if (!c.Text.StartsWith("As on") && c.Text != r.Node.Value.ToString("x8")) soundsNamed++;
                        }
                        if (r.Path.EndsWith("m_rim_variant_idx") || r.Path.EndsWith("m_tire_variant_idx"))
                        {
                            wheels++;
                            if (r.Node.Value == uint.MaxValue) wheelsUnset++;
                            else if (r.Kind != OptionKind.Choice || r.Choices.First(x => x.Value == r.Node.Value).Text.StartsWith("Number "))
                            {
                                wheelsOff++;
                                if (wheelsOff <= 8) Console.WriteLine("    wheel outside its list: " + info + " " + r.Path + " = " + r.Node.Text);
                            }
                        }
                        bool part = r.Path.Contains("m_vid_slot_selection"), wrap = r.Path.Contains("m_mat_tex_selection");
                        if (r.Path.Contains("m_color_choice") && customized && r.Kind == OptionKind.Choice) glow++;
                        if (!part && !wrap) continue;
                        if (part) parts++; else wraps++;
                        if (r.Node.Value == uint.MaxValue) { unset++; continue; }
                        if (r.Kind != OptionKind.Choice || r.Choices.First(x => x.Value == r.Node.Value).Text.StartsWith("Number ")) off++;
                    }
                }
            }
            Console.WriteLine("  named options: " + cars + " customized cars, " + parts + " parts and " + wraps + " wraps, " + off + " outside the game's lists, " + unset + " unset, "
                + sounds + " sounds of which " + soundsNamed + " named, " + glow + " underglow lists, " + unnamed + " slots with no table, "
                + wheels + " rim and tire values: " + wheelsOff + " outside the lists, " + wheelsUnset + " unset");
            Check(wheelsOff == 0, "every chosen rim and tire is a real option");
            Check(off == 0, "every chosen part and wrap is a real option");
            Check(soundsNamed == sounds, "every sound in real saves has a name");
            Check(unnamed == 0, "every garage vehicle has a table");
            VehicleOptions olive = VehicleTables.Find("sedan_01b");
            Check(olive != null && olive.Display == "Saints Olive" && olive.Parts.Count == 8 && olive.Parts[2].Label == "Hood", "Saints Olive's parts are named");
            Check(olive != null && olive.Axles.Count == 2 && olive.Axles[0].MinSize == "0.95" && VehicleTables.Rim(olive.Axles[0].Rim) != null && VehicleTables.Tire(olive.Axles[0].Tire) != null, "Saints Olive's wheels load, with limits and lists");
            Check(Names.Display("veh_horn_bike_01") == "Motocross", "the game's English names load");
        }

        // The button's scope is a decision: these lists have to stay opt-in.
        static void UnlockAllScope()
        {
            foreach (string title in new[] { "Models", "Upgrade tiers", "Features and menus", "Treasure found", "Map districts", "Districts", "Homies", "Boot Hill upgrades",
                "Neighborhoods", "Reward bundles", "Achievement progress", "Level rewards", "Challenge packs", "Stores bought from", "Tutorial popups seen",
                "Conversations", "Flashpoints done", "Bonus objectives done" })
                Check(!Categories.All.First(c => c.Title == title).InUnlockAll, title + " is left out of Unlock everything");
        }

        // Upgrading a car must follow what real saves do: flag set, the save-wide counter untouched.
        static void GarageRules(string path)
        {
            GameSave g = GameSave.Load(path);
            Node state = g.Find("vehicle_save_state");
            List<Node> slots = g.GarageSlots();
            if (state == null || slots.Count == 0) return;
            uint counter = state.Child("m_customized_count").Value;
            Node slot = slots[0];
            g.MaxVehicle(slot);
            Check(slot.Child("m_has_customization").Value == 1, "upgraded car is flagged");
            Check(GameSave.GarageField(slot, "m_torque_level").Value == 3, "upgraded car has engine 3");
            Check(GameSave.GarageField(slot, "m_nitro_purchased").Value == 1, "upgraded car has nitro");
            Check(state.Child("m_customized_count").Value == counter, "customized counter left alone");
        }

        static void RejectsGarbage()
        {
            Action<string, byte[]> expect = delegate(string what, byte[] data)
            {
                try { SaveFile.Parse(data); Check(false, what + " should be refused"); }
                catch (SaveFormatException) { checks++; }
            };
            expect("empty file", new byte[0]);
            expect("not a save", new byte[64]);
            byte[] header = new byte[12];
            header[0] = (byte)'e'; header[1] = (byte)'v'; header[2] = (byte)'a'; header[3] = (byte)'s';
            header[8] = 1;
            expect("truncated", header);
        }

        static void TooBig(string path)
        {
            byte[] raw = File.ReadAllBytes(path);
            SaveFile f = SaveFile.Parse(raw);
            f.FileSize = 4096;
            try { f.Serialize(); Check(false, "oversized save should be refused"); }
            catch (SaveFormatException) { checks++; }
        }

        static void BackupWorks(string path)
        {
            string temp = Path.Combine(outDir, "backup_" + Path.GetFileName(path));
            File.Copy(path, temp, true);
            File.Copy(GameSave.MetaPath(path), GameSave.MetaPath(temp), true);
            byte[] rawSave = File.ReadAllBytes(temp);
            GameSave g = GameSave.Load(temp);
            g.Currency("cash").SetRaw(777);
            string dir = g.SaveTo(temp);
            Check(dir != null && Directory.Exists(dir), "backup folder made");
            Check(Same(File.ReadAllBytes(Path.Combine(dir, Path.GetFileName(temp))), rawSave), "backup holds the original");
            Check(!Same(File.ReadAllBytes(temp), rawSave), "target was replaced");
            Check(!File.Exists(temp + ".sr5edit.tmp"), "temp file removed");
            Directory.Delete(dir, true);
            File.Delete(temp);
            File.Delete(GameSave.MetaPath(temp));
        }

        // Every record type in every save has a category and a real screen to edit it on.
        static void InventoryComplete(string[] saves)
        {
            HashSet<string> unknown = new HashSet<string>();
            int records = 0;
            foreach (string path in saves)
            {
                SaveFile f = SaveFile.Parse(File.ReadAllBytes(path));
                foreach (Block b in f.Blocks)
                    foreach (Node r in b.Records)
                    {
                        records++;
                        if (Inventory.Find(r.Def.Name) == null) unknown.Add(r.Def.Name + " in " + b.Name);
                    }
            }
            foreach (string u in unknown) Console.WriteLine("    no category: " + u);
            Check(unknown.Count == 0, "every record type has a category");
            foreach (RecordKind k in Inventory.All) Check(Inventory.Screens.Contains(k.Screen), k.Struct + " points at a real screen");
            foreach (Category c in Categories.All)
            {
                RecordKind k = Inventory.Find(c.Struct);
                Check(k != null && k.Screen != "Tables", c.Title + " is listed as on its screen");
            }
            Console.WriteLine("  inventory: " + records + " records, " + Inventory.All.Count() + " kinds, all categorized");
        }

        // Your own mods' items must not reach other saves.
        static void CustomItemsStayOut(string[] saves)
        {
            Check(CustomItems.IsCustom("two_frontz_reward") && CustomItems.IsCustom("sedan_01z_reward") && !CustomItems.IsCustom("sedan_01b_saints"), "custom items are recognised");
            Check(!CustomItems.IsCustom("two_fronts_preset_1") && !CustomItems.IsCustom("veh_spec_challenge_two_fronts"), "the game's own Two Fronts isn't taken for the patched car");
            GameSave g = GameSave.Load(saves.First(p => p.EndsWith("Save1693871473.sf_pc")));
            foreach (Category c in Categories.All)
                foreach (UnlockEntry e in g.Entries(c))
                    if (e.Record == null) Check(!CustomItems.IsCustom(e.Id), e.Id + " is a custom item the editor would add");
            Check(!VariantTable.All.Values.Any(v => v.How != "custom" && CustomItems.IsCustom(v.Variant)), "custom models are marked as custom");
        }

        // The game's own Truckster, against the same car made by Add vehicle.
        static void RealRewardCar(string[] saves)
        {
            GameSave real = GameSave.Load(saves.First(p => p.EndsWith("Save1793601432.sf_pc")));
            Node slot = real.GarageSlots().First(s => s.Child("m_variant_info").Value == Crc.Of("wagon_09_reward"));
            GameSave made = GameSave.Load(saves.First(p => p.EndsWith("Save1793508528.sf_pc")));
            Node mine = made.AddVehicle(VariantTable.Find("wagon_09_reward"));
            foreach (string f in new[] { "m_vehicle_info", "m_variant_info", "m_reward_variant", "m_sort_order", "m_has_customization", "m_favorite_slot", "m_from_garage", "m_is_reward", "m_is_permanent" })
            {
                Node a = slot.Child(f), b = mine.Child(f);
                bool same = a != null && b != null && a.Value == b.Value;
                Console.WriteLine("  Truckster " + f + ": game " + (a == null ? "absent" : a.Text) + ", Add vehicle " + (b == null ? "absent" : b.Text) + (same ? "" : "  DIFFERENT"));
                Check(same, "Add vehicle matches the game's reward car on " + f);
            }
        }

        // How the catalog is made: Tests.exe --build-catalog SAVES_DIR data\catalog.bin
        static int BuildCatalog(string dir, string output)
        {
            string[] saves = Directory.GetFiles(dir, "*.sf_pc")
                .Where(p => !Path.GetFileName(p).StartsWith("SR_PROFILE"))
                .OrderByDescending(p => File.GetLastWriteTimeUtc(p)).ToArray();
            byte[] gz = Catalog.Build(saves);
            File.WriteAllBytes(output, gz);
            Console.WriteLine("catalog from " + saves.Length + " saves: " + gz.Length + " bytes");
            return 0;
        }

        // Makes the saves for trying in the game: Tests.exe --make-ingame-test SAVE OUT_FOLDER
        // A changes a few things that are easy to read back; B is Unlock everything.
        static int MakeIngameTest(string source, string folder)
        {
            Directory.CreateDirectory(folder);
            long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;

            GameSave a = GameSave.Load(source);
            a.SetCurrency("cash", 123456789);
            List<WeaponEntry> weapons = a.Weapons();
            WeaponEntry existing = weapons.First(w => w.Record != null && w.Info.Kind == "shop" && w.Info.Tracks > 0 && w.Record.Child("m_player_unlocked").Value == 0);
            a.MaxWeapon(existing, false);
            WeaponEntry added = weapons.First(w => w.Record == null && w.Info.Kind == "shop");
            a.MaxWeapon(added, false);
            foreach (string[] pick in new[] { new[] { "Skills", "flashcracker" }, new[] { "Perks", "perk_ricochet_entry" }, new[] { "Clothing and body", "clo_upper_tshirt_pumpkin_happy_01" } })
            {
                Category c = Categories.All.First(x => x.Title == pick[0]);
                UnlockEntry e = a.Entries(c).First(x => x.Id == pick[1]);
                a.SetOwned(c, e, true);
            }
            Node car = a.GarageSlots().First(s => GameSave.GarageField(s, "m_torque_level").Value == 0);
            a.MaxVehicle(car);
            string pathA = Path.Combine(folder, "Save" + t + ".sf_pc");
            a.SaveTo(pathA);
            Console.WriteLine("A " + Path.GetFileName(pathA) + ": cash 123456789, weapon " + existing.Info.Name + " owned, weapon " + added.Info.Name
                + " added, flashcracker, perk_ricochet_entry, clo_upper_tshirt_pumpkin_happy_01, car " + Names.Get(car.Child("m_variant_info").Value));

            GameSave b = GameSave.Load(source);
            b.SetCurrency("cash", 987654321);
            int count = 0;
            foreach (Category c in Categories.All.Where(x => x.InUnlockAll))
                foreach (UnlockEntry e in b.Entries(c)) { b.SetOwned(c, e, true); count++; }
            foreach (WeaponEntry w in b.Weapons())
                if (w.Info.Kind == "shop") b.MaxWeapon(w, true);
            foreach (Node slot in b.GarageSlots()) b.MaxVehicle(slot);
            string pathB = Path.Combine(folder, "Save" + (t + 1) + ".sf_pc");
            b.SaveTo(pathB);
            Console.WriteLine("B " + Path.GetFileName(pathB) + ": cash 987654321, " + count + " unlocks, all shop weapons and garage upgraded");

            // C: the garage and loadout options, easy to see at a glance.
            GameSave third = GameSave.Load(source);
            third.SetCurrency("cash", 555555555);
            Node painted = third.GarageSlots().First(s => Options.ForVehicle(third, s).Any(r => r.Kind == OptionKind.Color && r.Label == "paint color 1"));
            foreach (OptionRow r in Options.ForVehicle(third, painted).Where(r => r.Kind == OptionKind.Color && r.Label == "paint color 1"))
                r.SetColor(new[] { 1f, 0f, 1f });
            third.MaxVehicle(painted);
            third.AddVehicle(VariantTable.Find("tank_01_preset_1"));
            foreach (Node level in third.DifficultyLevels) level.SetRaw(3);
            string pathC = Path.Combine(folder, "Save" + (t + 2) + ".sf_pc");
            third.SaveTo(pathC);
            Console.WriteLine("C " + Path.GetFileName(pathC) + ": cash 555555555, " + Names.Get(painted.Child("m_variant_info").Value)
                + " painted magenta and upgraded, tank_01_preset_1 added to the garage, difficulty sliders all 3");
            return 0;
        }

        static int Main(string[] args)
        {
            if (args.Length == 3 && args[0] == "--build-catalog") return BuildCatalog(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--make-ingame-test") return MakeIngameTest(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--inventory")
            {
                Inventory.Export(Directory.GetFiles(args[1], "*.sf_pc").Where(p => !Path.GetFileName(p).StartsWith("SR_PROFILE")), args[2]);
                return 0;
            }
            string dir = args.Length > 0 ? args[0] : "samples\\original";
            outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "sr5test");
            Directory.CreateDirectory(outDir);
            string[] all = Directory.GetFiles(dir, "*.sf_pc").OrderBy(p => p).ToArray();
            string[] games = all.Where(p => !Path.GetFileName(p).StartsWith("SR_PROFILE")).ToArray();
            Console.WriteLine(all.Length + " saves in " + dir);

            foreach (string p in all) { RoundTrip(p); CountBound(p); }
            Console.WriteLine("round trip and count bound: " + (failures == 0 ? "ok" : failures + " failures"));
            UnlockAllScope();
            GarageOptionsAreReal(games);
            InventoryComplete(games);
            CustomItemsStayOut(games);
            RealRewardCar(games);
            WeaponLevelRule(games);
            WeaponLevelEdit(games.Last());
            GarageTable(games);
            GarageAddRemove(games.Last());
            LoadoutRules(games.Last());
            foreach (string p in all) ArrayIndexRule(p);
            foreach (string p in games) OptionsAreComplete(p);
            OptionEdits(games.Last());
            foreach (string p in games) { ValueEdit(p); MetaSync(p); BackupWorks(p); GarageRules(p); }
            Console.WriteLine("value edits, met sync, backups: " + (failures == 0 ? "ok" : failures + " failures"));
            RejectsGarbage();
            TooBig(games[0]);
            Console.WriteLine("refusals: " + (failures == 0 ? "ok" : failures + " failures"));
            InsertRecords(games.First(p => Path.GetFileName(p) == "AUTOSAVE1.sf_pc"));
            Console.WriteLine("adding records: " + (failures == 0 ? "ok" : failures + " failures"));
            foreach (string p in games) UnlockEverything(p);
            Console.WriteLine("unlock everything on every save: " + (failures == 0 ? "ok" : failures + " failures"));

            Console.WriteLine(checks + " checks, " + failures + " failures");
            return failures == 0 ? 0 : 1;
        }
    }
}
