// Where everything in a save belongs. Every record type has a category and a place to edit it, and a test fails
// if a save holds a type this table doesn't know, so nothing can quietly stay uncategorized.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sr5Save
{
    public sealed class RecordKind
    {
        public string Struct, Group, Title, Screen;
        public bool Risky;
    }

    // Items your own mods added to the game, such as the car patch. They're listed like anything else, but a game
    // without the mod has no such thing, so the editor never adds them to a save.
    public static class CustomItems
    {
        static List<string> marks;

        public static bool IsCustom(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (marks == null) marks = Resources.Lines("custom_items.txt").Select(l => l.Trim().ToLowerInvariant()).Where(l => l.Length > 0).ToList();
            string s = id.ToLowerInvariant();
            return marks.Any(m => s.Contains(m));
        }
    }

    public static class Inventory
    {
        public const string Money = "Money and progress", Char = "Character", Weap = "Weapons", Veh = "Vehicles", Emp = "Empire and HQ",
            World = "World", Story = "Story and missions", Radio = "Radio", Phone = "Phone and menus", Sys = "Game settings";

        public static readonly string[] Screens = { "Overview", "Weapons", "Garage", "Character", "Loadout", "Unlocks", "Stats", "Tables", "All data" };

        static readonly Dictionary<string, RecordKind> Table = Build();
        static Dictionary<uint, Category> byStruct;

        static Dictionary<string, RecordKind> Build()
        {
            Dictionary<string, RecordKind> t = new Dictionary<string, RecordKind>(StringComparer.OrdinalIgnoreCase);
            Action<string, string, string, string, bool> add = delegate(string st, string group, string title, string screen, bool risky)
            {
                t[st] = new RecordKind { Struct = st, Group = group, Title = title, Screen = screen, Risky = risky };
            };
            add("currency_state", Money, "Money and XP", "Overview", false);
            add("avatar_progression_state", Money, "Level", "Overview", false);
            add("criminal_tier_state", Money, "Empire tier", "Overview", false);
            add("money_cache_state", Money, "Cash waiting to collect", "Overview", false);
            add("gml_stat_integer_data", Money, "Whole-number stats", "Stats", false);
            add("gml_stat_float_data", Money, "Decimal stats", "Stats", false);
            add("stunt_info_data", Money, "Stunt counters", "Tables", false);
            add("gml_achievement_state", Money, "Achievement progress", "Unlocks", true);
            add("aspiration_unlock_state", Money, "Level rewards", "Unlocks", false);
            add("gameplay_unlock_state", Money, "Features, DLC ownership and level unlocks", "Unlocks", true);
            add("collectible_suite_state", Money, "Reward bundles", "Unlocks", true);
            add("collectible_upgrade_data", Money, "Upgrade tiers", "Unlocks", false);
            add("perks_config_info_data", Money, "Perk challenge count", "Tables", false);

            add("character_customization_state", Char, "What you're wearing", "Character", false);
            add("avatar_persona_state", Char, "Voice and persona", "Character", false);
            add("mission_character_customization_backup", Char, "Outfit kept during missions", "Tables", true);
            add("prank_character_customization_backup", Char, "Outfit kept during pranks", "Tables", true);
            add("char_cust_wearable_data", Char, "Clothing and body", "Unlocks", false);
            add("collectible_wearable_state", Char, "Clothing rewards", "Unlocks", false);
            add("char_cust_outfit_data", Char, "Saved looks", "Unlocks", false);
            add("char_cust_skin_shader_info_data", Char, "Skin colours", "Unlocks", false);
            add("char_cust_material_data", Char, "Clothing materials", "Unlocks", false);
            add("skill_info_data", Char, "Skills", "Unlocks", false);
            add("perk_entry_state", Char, "Perks", "Unlocks", false);
            add("perk_slot_state", Char, "Perk slots", "Loadout", false);
            add("collectible_emote_state", Char, "Emotes", "Unlocks", false);
            add("emote_state", Char, "Emotes equipped", "Loadout", false);
            add("homie_state", Char, "Homies", "Unlocks", true);
            add("homie_outfit_state", Char, "Homie outfits", "Unlocks", false);
            add("friend_conversation_data", Char, "Crew conversations", "Unlocks", false);

            add("inventory_weapon_data", Weap, "Weapons", "Weapons", false);
            add("inventory_slot_default_data", Weap, "Weapon wheel and ammo", "Tables", false);
            add("collectible_weapon_state", Weap, "Weapon rewards", "Unlocks", false);
            add("weapon_skin_state", Weap, "Weapon skins", "Unlocks", false);
            add("inventory_weapon_material_data", Weap, "Weapon materials", "Unlocks", false);
            add("inventory_weapon_material_list_data", Weap, "Weapon material lists", "All data", false);
            add("inventory_weapon_texture_set_data", Weap, "Decal sets", "Unlocks", false);
            add("inventory_weapon_texture_state", Weap, "Decals", "Unlocks", false);
            add("boothill_weapon_class_upgrade_data", Weap, "Boot Hill weapon upgrades", "Unlocks", false);

            add("vehicle_save_state", Veh, "Your garage", "Garage", false);
            add("collectible_vehicle_state", Veh, "Vehicle rewards", "Unlocks", false);
            add("collectible_vehicle_customization_vid_state", Veh, "Part rewards", "Unlocks", false);
            add("vehicle_ultimate_state", Veh, "Vehicle ultimates", "Unlocks", false);
            add("vehicle_variant_entry_state", Veh, "Models", "Unlocks", false);
            add("vehicle_cust_vid_slot_option_name_state", Veh, "Parts", "Unlocks", false);
            add("vehicle_wheel_axle_name_state", Veh, "Wheels", "Unlocks", false);
            add("vehicle_cust_material_texture_set_state", Veh, "Wraps and decals", "Unlocks", false);
            add("vehicle_cust_mat_data", Veh, "Paint materials", "Unlocks", false);

            add("building_customization_prop_info_state", Emp, "HQ props", "Unlocks", false);
            add("hq_prefab_anchor_info_state", Emp, "HQ prop placement", "Tables", false);
            add("hq_room_info_state", Emp, "HQ theme in use", "Loadout", false);
            add("hq_room_theme_info_state", Emp, "HQ themes", "Unlocks", false);
            add("gang_customization_data", Emp, "Gang members", "Unlocks", false);
            add("gang_vehicle_customization_data", Emp, "Gang vehicles", "Unlocks", false);
            add("real_estate_blueprint_state", Emp, "Venture blueprints", "Tables", true);
            add("real_estate_lot_state", Emp, "Venture lots", "Tables", true);
            add("real_estate_goal_state", Emp, "Venture goals", "Tables", true);
            add("neighborhood_state", Emp, "Neighborhoods", "Unlocks", true);

            add("city_warp_point_state", World, "Fast travel", "Unlocks", false);
            add("savegame_city_warp_entry_state", World, "Where you start", "Overview", false);
            add("map_district_state", World, "Map districts", "Unlocks", false);
            add("district_state", World, "Districts", "Unlocks", false);
            add("treasure_data", World, "Treasure found", "Unlocks", false);
            add("city_state", World, "Cities", "Tables", true);
            add("city_enforced_zone_set_exclusion_group_state", World, "World zone sets", "Tables", true);
            add("open_world_collectible_data", World, "Collectibles in the world", "Tables", true);
            add("discoverable_info_data", World, "Discoverable places", "Tables", false);
            add("loot_system_state", World, "Loot categories", "Tables", false);
            add("enemy_type_data", World, "Enemy types", "Tables", true);

            add("mission_info_state", Story, "Missions", "Tables", true);
            add("mission_group_state", Story, "Mission groups", "All data", true);
            add("challenge_info_data", Story, "Challenges", "Tables", true);
            add("challenge_pack_info_data", Story, "Challenge packs", "Unlocks", false);
            add("diversion_info_data", Story, "Side hustles and diversions", "Tables", true);
            add("flashpoint_info_data", Story, "Flashpoints", "Unlocks", false);
            add("bonus_objective_data", Story, "Bonus objectives", "Unlocks", false);
            add("atcha_survival_instance_state", Story, "Survival ratings", "Tables", false);
            add("boothill_contestant_data", Story, "Boot Hill contestants", "Tables", false);
            add("store_info_data", Story, "Stores bought from", "Unlocks", false);
            add("tutorial_state", Story, "Tutorial popups", "Unlocks", false);

            add("radio_song_info_state", Radio, "Songs", "Unlocks", false);
            add("radio_newscast_info_state", Radio, "News", "Unlocks", false);
            add("radio_commercial_info_state", Radio, "Ads", "Unlocks", false);
            add("radio_state", Radio, "Station override", "Tables", false);
            add("playlist_info_state", Radio, "Playlists", "Tables", false);

            add("saintsbook_post_data", Phone, "Saintsbook posts", "Tables", true);
            add("saintsbook_order", Phone, "Saintsbook order", "Tables", false);
            add("cell_phone_app_state", Phone, "Phone apps", "Tables", false);
            add("unlocked_shortcut_data", Phone, "Unlocked shortcuts", "All data", false);

            add("difficulty_state", Sys, "Difficulty", "Loadout", false);
            add("cheat_savegame_state", Sys, "Used-cheats mark", "Overview", false);
            add("gameplay_variables_state", Sys, "Gameplay variables", "Tables", true);
            add("savegame_version_data", Sys, "Save version", "Tables", true);
            return t;
        }

        public static IEnumerable<RecordKind> All { get { return Table.Values; } }

        public static RecordKind Find(string structName)
        {
            RecordKind k;
            return Table.TryGetValue(structName, out k) ? k : null;
        }

        // A type no table knows, say from a new game version, is still shown and editable, under its own name.
        public static RecordKind Of(StructDef d)
        {
            return Find(d.Name) ?? new RecordKind { Struct = d.Name, Group = "Other", Title = Names.Words(d.Name), Screen = "Tables" };
        }

        // Owned, not owned, or nothing where the idea doesn't apply.
        public static string StateOf(Node record)
        {
            if (record == null) return "Not in this save yet";
            if (byStruct == null)
            {
                byStruct = new Dictionary<uint, Category>();
                foreach (Category x in Categories.All) byStruct[x.StructHash] = x;
            }
            Category c;
            if (byStruct.TryGetValue(record.Def.Hash, out c)) return c.IsOwned(record) ? "Owned" : "Not owned";
            Node unlocked = record.Child("m_player_unlocked");
            if (unlocked != null) return unlocked.Value != 0 ? "Owned" : "Not owned";
            Node done = record.Path("m_my_data.m_completed") ?? record.Child("m_completed");
            if (done != null) return done.Value != 0 ? "Done" : "Not done";
            return "";
        }

        static string Csv(string s)
        {
            s = s ?? "";
            return s.IndexOfAny(new[] { ',', '"', '\n' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
        }

        // Writes every record type and every item seen across a folder of saves. It lists what exists in the game,
        // never how far anyone has got, so these lists are safe to keep with the code.
        public static void Export(IEnumerable<string> saveFiles, string folder)
        {
            Dictionary<string, int> typeCounts = new Dictionary<string, int>();
            Dictionary<string, string[]> items = new Dictionary<string, string[]>();
            foreach (string path in saveFiles)
            {
                SaveFile f = SaveFile.Parse(File.ReadAllBytes(path));
                foreach (Block b in f.Blocks)
                    foreach (Node r in b.Records)
                    {
                        RecordKind k = Of(r.Def);
                        string key = r.Def.Name + "|" + r.Key.ToString("x8");
                        if (items.ContainsKey(key)) continue;
                        int n;
                        typeCounts.TryGetValue(k.Struct, out n);
                        typeCounts[k.Struct] = n + 1;
                        string id = r.Id;
                        items[key] = new[] { k.Group, k.Title, id, k.Screen, CustomItems.IsCustom(id) ? "Custom" : "Game", k.Risky ? "Yes" : "", b.Name, r.Def.Name };
                    }
            }
            Directory.CreateDirectory(folder);
            StringBuilder s = new StringBuilder("Group,Kind,Item,Edit on screen,Source,Can affect the story,Save section,Record type\n");
            foreach (string[] row in items.Values.OrderBy(x => x[0]).ThenBy(x => x[1]).ThenBy(x => x[2], StringComparer.OrdinalIgnoreCase))
                s.Append(string.Join(",", row.Select(Csv))).Append('\n');
            File.WriteAllText(Path.Combine(folder, "inventory_items.csv"), s.ToString(), new UTF8Encoding(false));
            StringBuilder t = new StringBuilder("Group,Kind,Edit on screen,Can affect the story,Record type,Different items\n");
            foreach (KeyValuePair<string, int> e in typeCounts.OrderBy(x => (Find(x.Key) ?? new RecordKind { Group = "Other" }).Group).ThenBy(x => x.Key))
            {
                RecordKind k = Find(e.Key) ?? new RecordKind { Group = "Other", Title = e.Key, Screen = "Tables" };
                t.Append(string.Join(",", new[] { k.Group, k.Title, k.Screen, k.Risky ? "Yes" : "", e.Key }.Select(Csv))).Append(',').Append(e.Value).Append('\n');
            }
            File.WriteAllText(Path.Combine(folder, "inventory_types.csv"), t.ToString(), new UTF8Encoding(false));
        }
    }
}
