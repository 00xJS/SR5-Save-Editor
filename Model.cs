// What the save means: money, weapons, the garage and the lists of things you can own.
// The values used here are the ones the game itself writes, read off real saves. FORMAT.md lists the evidence.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace Sr5Save
{
    // One line of what "owned" means for a kind of record.
    public sealed class Category
    {
        public string Group, Title, Help, Struct;
        public string OwnedField;
        public bool OwnedWhenZero;      // for m_locked style flags
        public string[] Own, Lock;      // "field=value" sets it; "field>=value" raises it if lower
        public bool InUnlockAll = true;
        public bool Addable = true;     // false: records hold world state, so a blank copy isn't safe to add

        public uint StructHash { get { return Crc.Of(Struct); } }

        public bool IsOwned(Node r)
        {
            Node n = r.Path(OwnedField);
            if (n == null) return false;
            return OwnedWhenZero ? n.Value == 0 : n.Value != 0;
        }

        public void Apply(Node r, bool own)
        {
            foreach (string action in own ? Own : Lock)
            {
                bool raise = action.Contains(">=");
                string[] parts = action.Split(new[] { raise ? ">=" : "=" }, StringSplitOptions.None);
                Node n = r.Path(parts[0]);
                if (n == null) continue;
                uint v = uint.Parse(parts[1], CultureInfo.InvariantCulture);
                if (!raise || n.Value < v) n.SetRaw(v);
            }
        }
    }

    public static class Categories
    {
        static Category Collectible(string group, string title, string st, string help = null)
        {
            return new Category
            {
                Group = group, Title = title, Struct = st, Help = help, OwnedField = "m_available",
                Own = new[] { "m_available>=1" }, Lock = new[] { "m_available=0", "m_equipped=0" }
            };
        }

        static Category Linked(string group, string title, string st, string help = null)
        {
            return new Category
            {
                Group = group, Title = title, Struct = st, Help = help, OwnedField = "m_available",
                Own = new[] { "m_available>=1", "m_my_data.m_unlocked=1" }, Lock = new[] { "m_available=0", "m_my_data.m_unlocked=0" }
            };
        }

        static Category Flag(string group, string title, string st, string field, string help = null)
        {
            return new Category
            {
                Group = group, Title = title, Struct = st, Help = help, OwnedField = field,
                Own = new[] { field + "=1" }, Lock = new[] { field + "=0" }
            };
        }

        // Marks things as done, which can skip content, so it's left out of Unlock everything.
        static Category Progress(string title, string st, string field)
        {
            Category c = Flag("World", title, st, field, "This is progress, not an item: marking it done can skip what the game would have shown you.");
            c.InUnlockAll = false;
            return c;
        }

        // Story and progress lists: nothing here is in Unlock everything.
        static Category Story(string title, string st, string field, string help)
        {
            Category c = Flag("Story", title, st, field, help);
            c.InUnlockAll = false;
            return c;
        }

        static Category StoryCount(string title, string st, string help)
        {
            Category c = Collectible("Story", title, st, help);
            c.InUnlockAll = false;
            return c;
        }

        static readonly string[] GroupOrder = { "Character", "Weapons", "Vehicles", "Empire", "World", "Story" };

        public static readonly Category[] All = Unsorted().OrderBy(c => Array.IndexOf(GroupOrder, c.Group)).ToArray();

        static Category[] Unsorted()
        {
            return new[]
            {
            new Category { Group = "Character", Title = "Skills", Struct = "skill_info_data", OwnedField = "m_available",
                Own = new[] { "m_available>=1", "m_activated=1" }, Lock = new[] { "m_available=0", "m_activated=0", "m_equipped=0" } },
            Collectible("Character", "Perks", "perk_entry_state"),
            Collectible("Character", "Emotes", "collectible_emote_state"),
            Flag("Character", "Clothing and body", "char_cust_wearable_data", "m_purchased"),
            Collectible("Character", "Clothing rewards", "collectible_wearable_state"),
            Flag("Character", "Saved looks", "char_cust_outfit_data", "m_unlocked"),
            Flag("Character", "Skin colours", "char_cust_skin_shader_info_data", "m_unlocked"),
            Flag("Character", "Clothing materials", "char_cust_material_data", "m_unlocked"),
            Collectible("Weapons", "Weapon rewards", "collectible_weapon_state"),
            Collectible("Weapons", "Weapon skins", "weapon_skin_state"),
            Flag("Weapons", "Weapon materials", "inventory_weapon_material_data", "m_unlocked"),
            Flag("Weapons", "Decal sets", "inventory_weapon_texture_set_data", "m_unlocked"),
            new Category { Group = "Weapons", Title = "Decals", Struct = "inventory_weapon_texture_state", OwnedField = "m_locked", OwnedWhenZero = true,
                Own = new[] { "m_locked=0" }, Lock = new[] { "m_locked=1" } },
            Collectible("Vehicles", "Vehicle rewards", "collectible_vehicle_state"),
            Collectible("Vehicles", "Vehicle ultimates", "vehicle_ultimate_state"),
            Collectible("Vehicles", "Part rewards", "collectible_vehicle_customization_vid_state"),
            new Category { Group = "Vehicles", Title = "Models", Struct = "vehicle_variant_entry_state", OwnedField = "m_locked", OwnedWhenZero = true,
                Own = new[] { "m_locked=0" }, Lock = new[] { "m_locked=1" }, InUnlockAll = false,
                Help = "A finished game still has about half of these locked: many are mission and enemy versions. Pick the ones you want." },
            new Category { Group = "Vehicles", Title = "Parts", Struct = "vehicle_cust_vid_slot_option_name_state", OwnedField = "m_purchased",
                Own = new[] { "m_locked=0", "m_purchased=1" }, Lock = new[] { "m_purchased=0" } },
            new Category { Group = "Vehicles", Title = "Wheels", Struct = "vehicle_wheel_axle_name_state", OwnedField = "m_purchased",
                Own = new[] { "m_locked=0", "m_purchased=1" }, Lock = new[] { "m_purchased=0" } },
            new Category { Group = "Vehicles", Title = "Wraps and decals", Struct = "vehicle_cust_material_texture_set_state", OwnedField = "m_locked", OwnedWhenZero = true,
                Own = new[] { "m_locked=0" }, Lock = new[] { "m_locked=1" } },
            Flag("Vehicles", "Paint materials", "vehicle_cust_mat_data", "m_unlocked"),
            Linked("Empire", "HQ props", "building_customization_prop_info_state"),
            Linked("Empire", "Gang members", "gang_customization_data"),
            Linked("Empire", "Gang vehicles", "gang_vehicle_customization_data"),
            Collectible("Empire", "HQ themes", "hq_room_theme_info_state"),
            Collectible("Empire", "Homie outfits", "homie_outfit_state"),
            Collectible("World", "Radio songs", "radio_song_info_state"),
            Collectible("World", "Radio news", "radio_newscast_info_state"),
            Collectible("World", "Radio ads", "radio_commercial_info_state"),
            new Category { Group = "World", Title = "Fast travel", Struct = "city_warp_point_state", OwnedField = "m_my_data.m_unlocked",
                Own = new[] { "m_my_data.m_unlocked=1" }, Lock = new[] { "m_my_data.m_unlocked=0" } },
            Progress("Map districts", "map_district_state", "m_discovered"),
            Progress("Districts", "district_state", "m_discovered"),
            Progress("Treasure found", "treasure_data", "m_found"),
            new Category { Group = "Story", Title = "Features and menus", Struct = "gameplay_unlock_state", OwnedField = "m_available",
                Own = new[] { "m_available>=1", "m_my_data.m_unlocked=1" }, Lock = new[] { "m_available=0", "m_my_data.m_unlocked=0" },
                InUnlockAll = false, Help = "These open as the story moves on. Opening them early can put menus and phone apps ahead of the story." },
            Collectible("Character", "Perk slots", "perk_slot_state"),
            new Category { Group = "Character", Title = "Homies", Struct = "homie_state", OwnedField = "m_unlocked",
                Own = new[] { "m_available>=1", "m_unlocked=1" }, Lock = new[] { "m_unlocked=0" }, InUnlockAll = false,
                Help = "Homies join as the story moves on. Adding one early can get ahead of the story." },
            Rare(Collectible("Weapons", "Upgrade tiers", "collectible_upgrade_data", "None of these are owned even in a finished game, so what owning one does isn't known. Left out of Unlock everything.")),
            StoryCount("Boot Hill upgrades", "boothill_weapon_class_upgrade_data", "Weapon class upgrades from the Boot Hill content."),
            StoryCount("Neighborhoods", "neighborhood_state", "Marking one earned can skip what the game would have shown you there."),
            StoryCount("Reward bundles", "collectible_suite_state", "A bundle's items come when the game awards it, so marking it owned here may not give them, and may stop the game awarding them."),
            StoryCount("Achievement progress", "gml_achievement_state", "Numbers behind achievements. Reaching a target this way could award it on your Epic account, which can't be undone."),
            Story("Level rewards", "aspiration_unlock_state", "m_unlocked", "Marks a level's reward as reached. It may not give the reward."),
            Story("Challenge packs", "challenge_pack_info_data", "m_awarded", "Marks a pack's reward as given, which may mean you never get it."),
            Story("Stores bought from", "store_info_data", "m_item_purchased", "Counts toward buying something from every store."),
            Story("Tutorial popups seen", "tutorial_state", "m_viewed", "Stops these popups showing again. Your story tutorial missions are separate: see Missions in Tables."),
            Story("Conversations", "friend_conversation_data", "m_my_data.m_unlocked", "Conversations with your crew at HQ and the apartment."),
            NoAdd(Story("Flashpoints done", "flashpoint_info_data", "m_completed", "Marks a flashpoint as cleared, which can skip what the game would have shown you.")),
            Story("Bonus objectives done", "bonus_objective_data", "m_completed", "Marks a side objective as done. Its reward may not be given."),
            };
        }

        static Category Rare(Category c)
        {
            c.InUnlockAll = false;
            return c;
        }

        static Category NoAdd(Category c)
        {
            c.Addable = false;
            return c;
        }
    }

    public sealed class WeaponInfo
    {
        public string Name, Kind;
        public int Tracks;      // upgrade tracks seen in saves; 0 = not known
        public bool HasUltimate;
    }

    public static class WeaponTable
    {
        static Dictionary<string, WeaponInfo> table;

        public static Dictionary<string, WeaponInfo> All
        {
            get
            {
                if (table == null)
                {
                    table = new Dictionary<string, WeaponInfo>();
                    foreach (string line in Resources.Lines("weapons.txt"))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length == 4)
                            table[p[0]] = new WeaponInfo { Name = p[0], Kind = p[1], Tracks = int.Parse(p[2]), HasUltimate = p[3] == "1" };
                    }
                }
                return table;
            }
        }

        public static WeaponInfo Get(string name)
        {
            WeaponInfo w;
            return All.TryGetValue(name, out w) ? w : new WeaponInfo { Name = name, Kind = "internal" };
        }
    }

    public static class VehicleTable
    {
        static Dictionary<string, string> table;

        public static string Display(string info)
        {
            if (table == null)
            {
                table = new Dictionary<string, string>();
                foreach (string line in Resources.Lines("vehicles.txt"))
                {
                    string[] p = line.Split('\t');
                    if (p.Length == 2) table[p[0]] = p[1];
                }
            }
            VehicleOptions named = VehicleTables.Find(info);
            if (named != null && named.Display.Length > 0) return named.Display;
            string s;
            return info != null && table.TryGetValue(info, out s) ? s : Names.Pretty(info ?? "");
        }
    }

    public sealed class VariantInfo
    {
        public string Variant, How, Vehicle, Display;

        public string Label
        {
            get { return (Display.Length > 0 ? Display : Names.Pretty(Vehicle)) + "  (" + Variant + ")"; }
        }
    }

    // Every vehicle model, and how you normally meet it, from the SR5-Tweaks tables.
    public static class VariantTable
    {
        static Dictionary<string, VariantInfo> table;

        public static Dictionary<string, VariantInfo> All
        {
            get
            {
                if (table == null)
                {
                    table = new Dictionary<string, VariantInfo>();
                    foreach (string line in Resources.Lines("vehicle_variants.txt"))
                    {
                        string[] p = line.Split('\t');
                        if (p.Length == 4) table[p[0]] = new VariantInfo { Variant = p[0], How = CustomItems.IsCustom(p[0]) ? "custom" : p[1], Vehicle = p[2], Display = p[3] };
                    }
                }
                return table;
            }
        }

        public static VariantInfo Find(string variant)
        {
            VariantInfo v;
            return variant != null && All.TryGetValue(variant, out v) ? v : null;
        }
    }

    // Blank copies of records, so items a save hasn't met yet can be added to it. The game creates these
    // records the first time you earn something, so a young save has far fewer than a finished one.
    public static class Catalog
    {
        static SaveFile file;
        static Dictionary<ulong, Node> index;

        static ulong KeyOf(uint st, uint key) { return (ulong)st << 32 | key; }

        static void Ensure()
        {
            if (index != null) return;
            index = new Dictionary<ulong, Node>();
            Stream s = Resources.Open("catalog.bin");
            if (s == null) return;
            using (GZipStream gz = new GZipStream(s, CompressionMode.Decompress))
            using (MemoryStream ms = new MemoryStream())
            {
                gz.CopyTo(ms);
                file = SaveFile.Parse(ms.ToArray());
            }
            foreach (Block b in file.Blocks)
                foreach (Node r in b.Records)
                    index[KeyOf(r.Def.Hash, r.Key)] = r;
        }

        public static IEnumerable<Node> Of(uint structHash)
        {
            Ensure();
            if (file == null) yield break;
            foreach (Block b in file.Blocks)
                foreach (Node r in b.Records)
                    if (r.Def.Hash == structHash) yield return r;
        }

        // Builds data/catalog.bin from saves, newest first so the newest game version's layout wins.
        public static byte[] Build(IEnumerable<string> saveFiles)
        {
            HashSet<uint> wanted = new HashSet<uint>(Categories.All.Where(c => c.Addable).Select(c => c.StructHash));
            uint weapon = Crc.Of("inventory_weapon_data");
            wanted.Add(weapon);
            SaveFile cat = new SaveFile { Version = 1, FileSize = 0 };
            HashSet<ulong> seen = new HashSet<ulong>();
            foreach (string path in saveFiles)
            {
                SaveFile f = SaveFile.Parse(File.ReadAllBytes(path));
                foreach (Block b in f.Blocks)
                {
                    Block target = null;
                    foreach (Node r in b.Records)
                    {
                        // Your own mods' items stay out: a game without the mod has no such thing.
                        if (!wanted.Contains(r.Def.Hash) || CustomItems.IsCustom(r.Id) || !seen.Add(KeyOf(r.Def.Hash, r.Key))) continue;
                        if (target == null)
                        {
                            target = cat.Section(b.Hash);
                            if (target == null)
                            {
                                target = new Block
                                {
                                    File = cat, Hash = b.Hash, Version = b.Version, SchemaVersion = b.SchemaVersion,
                                    SchemaZ1 = b.SchemaZ1, SchemaZ2 = b.SchemaZ2, SchemaTag = b.SchemaTag
                                };
                                cat.Blocks.Add(target);
                            }
                        }
                        Node copy = target.AddRecord(r);
                        Blank(copy, r.Def.Hash == weapon);
                    }
                }
            }
            byte[] raw = cat.Serialize();
            using (MemoryStream ms = new MemoryStream())
            {
                using (GZipStream gz = new GZipStream(ms, CompressionMode.Compress))
                    gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        // Removes everything about the player from a template: it should say "never earned".
        static void Blank(Node n, bool isWeapon)
        {
            if (isWeapon)
            {
                Node shader = n.Child("m_shader_values");
                if (shader != null) n.Children.Remove(shader);
            }
            foreach (Node c in n.Children)
            {
                if (c.Kind == NodeKind.Struct || c.Kind == NodeKind.Array)
                {
                    Blank(c, false);
                    continue;
                }
                if (c.Kind != NodeKind.Scalar) continue;
                string f = c.FieldName;
                if (f == "m_is_new" || f == "m_new" || f == "m_bh_new" || f == "m_locked") c.Value = 1;
                else if (f == "m_stored_magazine_size" || f == "m_stored_reserve_size") c.Value = unchecked((uint)-2);
                else if (f == "m_reserve_size_multiplier") c.SetFloat(1f);
                else if (f == "m_slot" || f == "m_vlib_table_idx" || f == "m_weapon_skin" || f == "m_material" || f == "m_default_material") { }
                else if (c.Field.IsHandle) c.Value = 0;
                else if (c.Op == Node.OpFloat) c.SetFloat(0f);
                else c.Value = 0;
            }
        }
    }

    public sealed class WeaponEntry
    {
        public WeaponInfo Info;
        public Node Record;     // null while the save has no record for it
        public Node Template;

        // Some weapons come in versions that share a name, so the version is shown beside it.
        public string Label
        {
            get
            {
                string label = Names.Label(Info.Name), id = Info.Name.ToLowerInvariant();
                if (id.Contains("boothill")) return label + " (Boot Hill)";
                if (id.Contains("maingame")) return label + " (main game)";
                if (id.Contains("custom")) return label + " (custom)";
                return label;
            }
        }
    }

    public sealed class UnlockEntry
    {
        public Node Record;
        public Node Template;
        public string Id { get { return (Record ?? Template).Id; } }
    }

    public sealed class GameSave
    {
        public string SavePath;
        public SaveFile File;
        public MetaFile Meta;
        byte[] metaBytes;

        public static readonly string[] GameProcesses = { "SaintsRow", "SaintsRow_DX12", "SaintsRow_Vulkan" };

        public static bool GameRunning
        {
            get { return GameProcesses.Any(p => Process.GetProcessesByName(p).Length > 0); }
        }

        public static GameSave Load(string path)
        {
            GameSave g = new GameSave { SavePath = path, File = SaveFile.Parse(System.IO.File.ReadAllBytes(path)) };
            string met = MetaPath(path);
            if (System.IO.File.Exists(met))
            {
                g.metaBytes = System.IO.File.ReadAllBytes(met);
                g.Meta = MetaFile.Parse(g.metaBytes);
            }
            return g;
        }

        public static string MetaPath(string savePath) { return Path.ChangeExtension(savePath, ".met_pc"); }

        public bool IsProfile { get { return File.Section("stats") == null; } }

        public int Changes { get { return File.Changes; } }

        public Node Find(string structName, string name = null)
        {
            uint st = Crc.Of(structName);
            uint key = name == null ? 0 : Crc.Of(name);
            foreach (Block b in File.Blocks)
                foreach (Node r in b.Records)
                    if (r.Def.Hash == st && (name == null || r.Key == key)) return r;
            return null;
        }

        public IEnumerable<Node> All(uint structHash)
        {
            foreach (Block b in File.Blocks)
                foreach (Node r in b.Records)
                    if (r.Def.Hash == structHash) yield return r;
        }

        public Node Currency(string name) { Node r = Find("currency_state", name); return r == null ? null : r.Child("m_available"); }
        public Node Level { get { Node r = Find("avatar_progression_state"); return r == null ? null : r.Child("m_level"); } }
        public Node Tier { get { Node r = Find("criminal_tier_state"); return r == null ? null : r.Path("m_my_data.m_current_tier"); } }
        public Node WaitingCash { get { Node r = Find("money_cache_state"); return r == null ? null : r.Child("m_bucket"); } }
        public Node CheatFlag { get { Node r = Find("cheat_savegame_state"); return r == null ? null : r.Child("m_cheat_previously_enabled"); } }
        public Node StartPoint { get { Node r = Find("savegame_city_warp_entry_state"); return r == null ? null : r.Child("m_warp_point"); } }

        public Node Stat(string name)
        {
            Block b = File.Section("stats");
            if (b == null) return null;
            uint key = Crc.Of(name);
            foreach (Node r in b.Records) if (r.Key == key) return r.Child("m_value");
            return null;
        }

        public IEnumerable<Node> Stats
        {
            get
            {
                Block b = File.Section("stats");
                if (b == null) yield break;
                foreach (Node r in b.Records.OrderBy(x => x.Id)) yield return r;
            }
        }

        // Weapons -----------------------------------------------------------------------------------

        public List<WeaponEntry> Weapons()
        {
            uint st = Crc.Of("inventory_weapon_data");
            Dictionary<string, WeaponEntry> map = new Dictionary<string, WeaponEntry>();
            foreach (Node r in All(st))
                map[r.Id] = new WeaponEntry { Info = WeaponTable.Get(r.Id), Record = r };
            foreach (Node t in Catalog.Of(st))
            {
                WeaponEntry e;
                if (map.TryGetValue(t.Id, out e)) e.Template = t;
                else map[t.Id] = new WeaponEntry { Info = WeaponTable.Get(t.Id), Template = t };
            }
            return map.Values.OrderBy(e => e.Info.Name).ToList();
        }

        public Node EnsureWeapon(WeaponEntry e)
        {
            if (e.Record == null && e.Template != null)
            {
                Block b = File.Section(e.Template.Owner.Hash);
                if (b != null) e.Record = b.AddRecord(e.Template);
            }
            return e.Record;
        }

        public void SetWeaponOwned(WeaponEntry e, bool owned)
        {
            Node r = owned ? EnsureWeapon(e) : e.Record;
            if (r == null) return;
            Node n = r.Child("m_player_unlocked");
            if (n != null) n.SetRaw(owned ? 1u : 0u);
        }

        public const int MaxStatLevel = 3;

        // What each track shows at each weapon level, in every save seen.
        public static readonly uint[] TrackLevels = { 0, 1, 3, 5 };

        public void SetWeaponLevel(WeaponEntry e, int level)
        {
            Node r = EnsureWeapon(e);
            if (r == null) return;
            foreach (string f in new[] { "m_stat_level_purchased", "m_stat_level_enabled" })
            {
                Node n = r.Child(f);
                if (n != null) n.SetRaw((uint)level);
            }
            Node mods = r.Child("m_equipped_mod_levels");
            if (mods == null) return;
            for (int i = 0; i < e.Info.Tracks && i < mods.Children.Count; i++)
                mods.Children[i].SetRaw(TrackLevels[level]);
        }

        // m_equipped_mod_levels is [one level per upgrade track..., 1 if the ultimate is on].
        public void SetWeaponUltimate(WeaponEntry e, bool on)
        {
            Node r = EnsureWeapon(e);
            if (r == null) return;
            foreach (string f in new[] { "m_ultimate_purchased", "m_ultimate_enabled" })
            {
                Node n = r.Child(f);
                if (n != null) n.SetRaw(on ? 1u : 0u);
            }
            Node mods = r.Child("m_equipped_mod_levels");
            if (mods == null) return;
            if (e.Info.Tracks > 0 && e.Info.Tracks < mods.Children.Count)
                mods.Children[e.Info.Tracks].SetRaw(on ? 1u : 0u);
            else if (e.Info.Tracks == 0 && mods.Children.Count > 0 && e.Info.HasUltimate)
                mods.Children[mods.Children.Count - 1].SetRaw(on ? 1u : 0u);
        }

        // Fully upgrades it, using the number of tracks real saves show for this weapon.
        public void MaxWeapon(WeaponEntry e, bool ultimate)
        {
            SetWeaponOwned(e, true);
            SetWeaponLevel(e, MaxStatLevel);
            if (ultimate && e.Info.HasUltimate) SetWeaponUltimate(e, true);
        }

        // Garage ------------------------------------------------------------------------------------

        public List<Node> GarageSlots()
        {
            Node state = Find("vehicle_save_state");
            Node slots = state == null ? null : state.Child("m_save_slots");
            return slots == null ? new List<Node>() : slots.Children;
        }

        public static readonly string[] UpgradeFields = { "m_torque_level", "m_frame_level", "m_bumper_level", "m_tire_level" };

        public static Node GarageField(Node slot, string field) { return slot.Path("m_customized." + field); }

        public void MaxVehicle(Node slot)
        {
            foreach (string f in UpgradeFields)
            {
                Node n = GarageField(slot, f);
                if (n != null) n.SetRaw(3);
            }
            Node nitro = GarageField(slot, "m_nitro_level");
            if (nitro != null) nitro.SetRaw(1);
            nitro = GarageField(slot, "m_nitro_purchased");
            if (nitro != null) nitro.SetRaw(1);
            MarkCustomized(slot);
        }

        Node GarageList
        {
            get
            {
                Node state = Find("vehicle_save_state");
                return state == null ? null : state.Child("m_save_slots");
            }
        }

        public bool CanAddVehicle
        {
            get { Node list = GarageList; return list != null && list.Children.Any(s => s.Child("m_has_customization") != null && s.Child("m_has_customization").Value == 0); }
        }

        // Copies a stock car's slot and makes it this model, set up as the game sets up a reward it has just given:
        // checked against the Truckster the game added in a real save. Older saves have no m_is_permanent.
        public Node AddVehicle(VariantInfo v)
        {
            Node list = GarageList;
            Node stock = list == null ? null : list.Children.FirstOrDefault(s => s.Child("m_has_customization") != null && s.Child("m_has_customization").Value == 0);
            if (stock == null) throw new SaveFormatException("This save has no stock car to copy yet. Earn one in the game first.");
            Node slot = stock.Owner.AppendCopy(list, stock);
            uint variant = Crc.Of(v.Variant);
            slot.Child("m_vehicle_info").SetRaw(Crc.Of(v.Vehicle));
            slot.Child("m_variant_info").SetRaw(variant);
            slot.Child("m_reward_variant").SetRaw(variant);
            slot.Child("m_sort_order").SetRaw(0);
            Node favorite = slot.Child("m_favorite_slot");
            if (favorite != null) favorite.SetRaw(0);
            Node permanent = slot.Child("m_is_permanent");
            if (permanent != null) permanent.SetRaw(1);
            Node reward = slot.Child("m_is_reward");
            if (reward != null) reward.SetRaw(1);
            Node state = Find("vehicle_save_state");
            foreach (string counter in new[] { "m_rewarded_count", "m_permanent_count" })
            {
                Node count = state.Child(counter);
                if (count != null) count.SetRaw(count.Value + 1);
            }
            return slot;
        }

        // Gives one car another's customization. Only between the same vehicle: parts and materials are per vehicle.
        public bool CopyLook(Node from, Node to)
        {
            if (from == to || from.Child("m_vehicle_info").Value != to.Child("m_vehicle_info").Value) return false;
            Node source = from.Child("m_customized"), target = to.Child("m_customized");
            if (source == null || target == null) return false;
            to.Owner.ReplaceWithCopy(target, source);
            MarkCustomized(to);
            return true;
        }

        public void RemoveVehicle(Node slot)
        {
            Node list = GarageList;
            if (list == null) return;
            Node reward = slot.Child("m_is_reward"), permanent = slot.Child("m_is_permanent");
            bool wasReward = reward != null && reward.Value != 0, wasPermanent = permanent != null && permanent.Value != 0;
            slot.Owner.RemoveElement(list, slot);
            Node state = Find("vehicle_save_state");
            Node rewarded = state.Child("m_rewarded_count"), kept = state.Child("m_permanent_count");
            if (wasReward && rewarded != null && rewarded.Value > 0) rewarded.SetRaw(rewarded.Value - 1);
            if (wasPermanent && kept != null && kept.Value > 0) kept.SetRaw(kept.Value - 1);
        }

        // Loadout -----------------------------------------------------------------------------------

        // The pause menu's custom difficulty sliders, 0 to 10.
        public List<Node> DifficultyLevels
        {
            get
            {
                Node r = Find("difficulty_state");
                Node a = r == null ? null : r.Child("m_difficulty_levels");
                return a == null ? new List<Node>() : a.Children;
            }
        }

        public List<Node> PerkSlots { get { return All(Crc.Of("perk_slot_state")).OrderBy(r => r.Id).ToList(); } }

        public List<Node> OwnedPerks
        {
            get
            {
                return All(Crc.Of("perk_entry_state")).Where(r => r.Child("m_available") != null && r.Child("m_available").Value != 0)
                    .OrderBy(r => r.Id).ToList();
            }
        }

        // Moves a perk into a slot, keeping each perk's own equipped flag in step.
        public void EquipPerk(Node slot, uint perkKey)
        {
            Node handle = slot.Child("m_perk_entry_h");
            if (handle == null || handle.Value == perkKey) return;
            foreach (Node p in All(Crc.Of("perk_entry_state")))
            {
                Node equipped = p.Child("m_equipped");
                if (equipped == null) continue;
                if (p.Key == handle.Value) equipped.SetRaw(0);
                if (p.Key == perkKey) equipped.SetRaw(1);
            }
            handle.SetRaw(perkKey);
        }

        // Every upgraded car in real saves has this flag. m_customized_count doesn't track it, so it's left alone.
        public void MarkCustomized(Node slot)
        {
            Node flag = slot.Child("m_has_customization");
            if (flag != null) flag.SetRaw(1);
        }

        // The game marks every held currency as seen; a save with 0 cash can have it clear.
        public void SetCurrency(string name, uint value)
        {
            Node r = Find("currency_state", name);
            if (r == null) return;
            r.Child("m_available").SetRaw(value);
            Node seen = r.Child("m_is_new");
            if (seen != null && value != 0) seen.SetRaw(1);
        }

        // Unlocks -----------------------------------------------------------------------------------

        public List<UnlockEntry> Entries(Category c)
        {
            Dictionary<uint, UnlockEntry> map = new Dictionary<uint, UnlockEntry>();
            foreach (Node r in All(c.StructHash)) map[r.Key] = new UnlockEntry { Record = r };
            foreach (Node t in Catalog.Of(c.StructHash))
            {
                UnlockEntry e;
                if (map.TryGetValue(t.Key, out e)) e.Template = t;
                else if (File.Section(t.Owner.Hash) != null) map[t.Key] = new UnlockEntry { Template = t };
            }
            return map.Values.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public bool IsOwned(Category c, UnlockEntry e)
        {
            return e.Record != null && c.IsOwned(e.Record);
        }

        public void SetOwned(Category c, UnlockEntry e, bool own)
        {
            if (e.Record == null)
            {
                if (!own || e.Template == null) return;
                Block b = File.Section(e.Template.Owner.Hash);
                if (b == null) return;
                e.Record = b.AddRecord(e.Template);
            }
            c.Apply(e.Record, own);
        }

        // Saving ------------------------------------------------------------------------------------

        // Copies what the load menu shows into the .met_pc, so the menu agrees with the save.
        void SyncMeta()
        {
            if (Meta == null) return;
            Node cash = Currency("cash");
            if (cash != null) Meta.Set(MetaFile.Cash, cash.Value);
            Node time = Stat("stat_play_time");
            if (time != null && Meta.Has(MetaFile.Playtime)) Meta.Set(MetaFile.Playtime, time.Value);
            Node done = Stat("stat_game_progress");
            if (done != null && Meta.Has(MetaFile.Completion))
                Meta.Set(MetaFile.Completion, Meta.OpOf(MetaFile.Completion) == Node.OpFloat ? done.Value : (uint)Math.Round(Math.Max(0f, done.Float)));
        }

        public static string BackupRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SR5SaveEditor", "backups");
            }
        }

        // Copies the files as they are on disk now. Returns the folder.
        public static string Backup(string savePath)
        {
            string name = Path.GetFileNameWithoutExtension(savePath) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            string dir = Path.Combine(BackupRoot, name);
            Directory.CreateDirectory(dir);
            System.IO.File.Copy(savePath, Path.Combine(dir, Path.GetFileName(savePath)), false);
            string met = MetaPath(savePath);
            if (System.IO.File.Exists(met)) System.IO.File.Copy(met, Path.Combine(dir, Path.GetFileName(met)), false);
            return dir;
        }

        // Writes to a temporary file, reads it back, and only then replaces the target.
        static void WriteVerified(string path, byte[] bytes, Action<byte[]> verify)
        {
            string temp = path + ".sr5edit.tmp";
            System.IO.File.WriteAllBytes(temp, bytes);
            try
            {
                byte[] back = System.IO.File.ReadAllBytes(temp);
                if (back.Length != bytes.Length) throw new IOException("The file didn't write completely.");
                if (verify != null) verify(back);
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null);
                else System.IO.File.Move(temp, path);
            }
            finally
            {
                if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp);
            }
        }

        // Writes this save to a path. Returns the backup folder, or null if there was nothing to back up.
        public string SaveTo(string path)
        {
            SyncMeta();
            byte[] save = File.Serialize();
            // Reads the result with the strict parser before anything on disk changes.
            SaveFile check = SaveFile.Parse(save);
            if (check.Blocks.Count != File.Blocks.Count) throw new SaveFormatException("The written save didn't read back correctly.");
            string backup = System.IO.File.Exists(path) ? Backup(path) : null;
            WriteVerified(path, save, null);
            if (metaBytes != null) WriteVerified(MetaPath(path), metaBytes, null);
            SavePath = path;
            File.Changes = 0;
            return backup;
        }

        // The name the game gives a new manual save, with the next free time.
        public static string NewSlotPath(string folder)
        {
            long t = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            string path;
            while (System.IO.File.Exists(path = Path.Combine(folder, "Save" + t + ".sf_pc"))) t++;
            return path;
        }

        public static IEnumerable<string> FindSaveFolders()
        {
            List<string> found = new List<string>();
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            found.Add(Path.Combine(pf, "Epic Games", "SaintsRow", "sr5", "_cloudfolder", "saves", "SR"));
            try
            {
                string manifest = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
                if (System.IO.File.Exists(manifest))
                    foreach (Match m in Regex.Matches(System.IO.File.ReadAllText(manifest), "\"InstallLocation\"\\s*:\\s*\"([^\"]*SaintsRow[^\"]*)\""))
                        found.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "sr5", "_cloudfolder", "saves", "SR"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            try
            {
                string steam = Path.Combine(pf86, "Steam", "userdata");
                if (Directory.Exists(steam))
                    foreach (string user in Directory.GetDirectories(steam))
                        found.Add(Path.Combine(user, "742420", "remote"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return found.Distinct(StringComparer.OrdinalIgnoreCase).Where(d =>
            {
                try { return Directory.Exists(d) && Directory.GetFiles(d, "*.sf_pc").Length > 0; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            });
        }
    }
}
