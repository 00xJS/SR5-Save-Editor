// The save container: parses a .sf_pc into a tree and writes it back, byte for byte.
// FORMAT.md describes the layout. Nothing here knows about the game's meaning; see Model.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;

namespace Sr5Save
{
    public class SaveFormatException : Exception
    {
        public SaveFormatException(string message) : base(message) { }
    }

    public static class Crc
    {
        static readonly uint[] Table = MakeTable();

        static uint[] MakeTable()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c >> 1) ^ ((c & 1) != 0 ? 0xEDB88320u : 0);
                t[i] = c;
            }
            return t;
        }

        // The game's name hash: CRC-32 of the lowercased name, init 0, no final xor.
        public static uint Of(string name)
        {
            uint c = 0;
            foreach (char ch in name.ToLowerInvariant()) c = Table[(c ^ (byte)ch) & 255] ^ (c >> 8);
            return c;
        }
    }

    public static class Resources
    {
        public static Stream Open(string name)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
        }

        public static IEnumerable<string> Lines(string name)
        {
            Stream s = Open(name);
            if (s == null) yield break;
            using (StreamReader r = new StreamReader(s, Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                    if (line.Length > 0 && line[0] != '#') yield return line;
            }
        }
    }

    public static class Names
    {
        static Dictionary<uint, string> table;

        static Dictionary<uint, string> Table
        {
            get
            {
                if (table == null)
                {
                    table = new Dictionary<uint, string>();
                    foreach (string file in new[] { "table_names.txt", "names.txt" })
                        foreach (string line in Resources.Lines(file))
                        {
                            int tab = line.IndexOf('\t');
                            if (tab == 8) table[uint.Parse(line.Substring(0, 8), NumberStyles.HexNumber)] = line.Substring(9);
                        }
                }
                return table;
            }
        }

        public static string Find(uint hash)
        {
            string s;
            return Table.TryGetValue(hash, out s) ? s : null;
        }

        public static string Get(uint hash)
        {
            return Find(hash) ?? hash.ToString("x8");
        }

        static Dictionary<uint, string> display;

        // The game's own English name for a record, if it has one.
        public static string Display(string id)
        {
            if (display == null)
            {
                display = new Dictionary<uint, string>();
                foreach (string line in Resources.Lines("display_names.txt"))
                    if (line.Length > 9 && line[8] == '\t') display[uint.Parse(line.Substring(0, 8), NumberStyles.HexNumber)] = line.Substring(9);
            }
            string s;
            return !string.IsNullOrEmpty(id) && display.TryGetValue(Crc.Of(id), out s) ? s : null;
        }

        // What to call an item on screen: the game's name, or a tidied id.
        public static string Label(string id)
        {
            return Display(id) ?? Pretty(id);
        }

        // weapon_melee_2hand_axe -> Melee 2hand Axe
        public static string Pretty(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            string s = id;
            foreach (string prefix in new[] { "weapon_", "inventory_weapon_", "inv_weapon_", "inv_dlc_", "inv_", "clo_", "perk_", "m_" })
                if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { s = s.Substring(prefix.Length); break; }
            return Words(s);
        }

        // perk_entry -> Perk Entry
        public static string Words(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            StringBuilder b = new StringBuilder();
            bool up = true;
            foreach (char c in s)
            {
                if (c == '_' || c == '-') { b.Append(' '); up = true; }
                else { b.Append(up ? char.ToUpperInvariant(c) : c); up = c == ' '; }
            }
            return b.ToString();
        }
    }

    public sealed class FieldDef
    {
        public byte Pre;
        public uint Hash, Z1, Type, Bits, Ref, Offset, Bits2;

        public string Name { get { return Names.Get(Hash); } }
        public char BaseType { get { return (char)(Type & 0xFF); } }
        // Handles are CRCs of another record's name.
        public bool IsHandle { get { return ((Type >> 8) & 0x30) == 0x30; } }

        public FieldDef Copy() { return (FieldDef)MemberwiseClone(); }
    }

    public sealed class StructDef
    {
        public uint Hash, Z1, Size, Align;
        public List<FieldDef> Fields = new List<FieldDef>();
        public byte[] Tail = new byte[3];

        public string Name { get { return Names.Get(Hash); } }

        public int IndexOf(uint fieldHash)
        {
            for (int i = 0; i < Fields.Count; i++) if (Fields[i].Hash == fieldHash) return i;
            return -1;
        }

        public StructDef Copy()
        {
            StructDef d = new StructDef { Hash = Hash, Z1 = Z1, Size = Size, Align = Align, Tail = (byte[])Tail.Clone() };
            foreach (FieldDef f in Fields) d.Fields.Add(f.Copy());
            return d;
        }
    }

    public enum NodeKind { Record, Struct, Array, Scalar, Bytes }

    public sealed class Node
    {
        public const byte OpRecord = 0x01, OpBool = 0x02, OpS8 = 0x03, OpU8 = 0x04, OpS16 = 0x05, OpU16 = 0x06, OpS32 = 0x07,
            OpU32 = 0x08, OpU64 = 0x0A, OpFloat = 0x0B, OpString = 0x0E, OpStruct = 0x10, OpStructEnd = 0x11,
            OpArray = 0x12, OpArrayEnd = 0x13, OpBlob = 0x15;

        public NodeKind Kind;
        public uint Value;           // scalar value, struct index, record size or byte count
        public uint Tag;             // op << 24 | field << 12 | low
        public ulong Long;           // the extra 8 bytes of a 64-bit value
        public byte[] Bytes;         // string or blob data
        public uint EndValue, EndTag;
        public uint Key;             // record: CRC of its name (or of a name that isn't stored)
        public byte[] Reserved;      // record: 12 bytes, zero in every save seen
        public byte[] NameBytes;     // record: name with its NUL, or empty
        public StructDef Def;        // record, struct
        public FieldDef Field;       // the field this value belongs to
        public Node Parent;
        public Block Owner;
        public List<Node> Children;

        public byte Op { get { return (byte)(Tag >> 24); } }
        public int FieldIndex { get { return (int)((Tag >> 12) & 0xFFF); } }
        // Inside an array: the element's position. The field bits still name the array's own field.
        public int ElementIndex { get { return (int)(Tag & 0xFFF); } }

        public string Name
        {
            get
            {
                if (NameBytes == null || NameBytes.Length == 0) return "";
                return Encoding.GetEncoding(28591).GetString(NameBytes, 0, NameBytes.Length - 1);
            }
        }

        // The name to show. Missions store "Mission System" as the name and hash the real one into Key.
        public string Id
        {
            get
            {
                string found = Names.Find(Key);
                if (found != null) return found;
                string n = Name;
                return n.Length > 0 ? n : (Key == 0 ? "" : Key.ToString("x8"));
            }
        }

        public string FieldName { get { return Field != null ? Field.Name : "?"; } }

        public Node Child(string field)
        {
            if (Children == null) return null;
            uint h = Crc.Of(field);
            foreach (Node c in Children) if (c.Field != null && c.Field.Hash == h) return c;
            return null;
        }

        // "m_my_data.m_unlocked"
        public Node Path(string path)
        {
            Node n = this;
            foreach (string part in path.Split('.'))
            {
                if (n == null) return null;
                n = n.Child(part);
            }
            return n;
        }

        public void Touch() { if (Owner != null) Owner.File.Changes++; }

        public int Int { get { return (int)Value; } }

        public float Float
        {
            get { return BitConverter.ToSingle(BitConverter.GetBytes(Value), 0); }
        }

        public void SetRaw(uint value)
        {
            if (value == Value) return;
            Value = value;
            Touch();
        }

        public void SetFloat(float f) { SetRaw(BitConverter.ToUInt32(BitConverter.GetBytes(f), 0)); }

        public bool IsEditableScalar { get { return Kind == NodeKind.Scalar; } }

        public string Text
        {
            get
            {
                switch (Op)
                {
                    case OpBool: case OpU8: case OpU16: return Value.ToString(CultureInfo.InvariantCulture);
                    case OpS8: return ((sbyte)Value).ToString(CultureInfo.InvariantCulture);
                    case OpS16: return ((short)Value).ToString(CultureInfo.InvariantCulture);
                    case OpS32: return ((int)Value).ToString(CultureInfo.InvariantCulture);
                    case OpU32:
                        if (Field != null && Field.IsHandle) return "0x" + Value.ToString("x8");
                        return Value.ToString(CultureInfo.InvariantCulture);
                    case OpFloat: return Float.ToString("R", CultureInfo.InvariantCulture);
                    case OpU64: return "0x" + Long.ToString("x16");
                }
                return "";
            }
        }

        public string TypeName
        {
            get
            {
                switch (Op)
                {
                    case OpBool: return "bool";
                    case OpS8: return "int8";
                    case OpU8: return "byte";
                    case OpS16: return "int16";
                    case OpU16: return "uint16";
                    case OpS32: return "int32";
                    case OpU32: return Field != null && Field.IsHandle ? "handle" : "uint32";
                    case OpFloat: return "float";
                    case OpU64: return "uint64";
                }
                return "";
            }
        }

        // Accepts what Text prints. Returns an error message, or null if the value was set.
        public string TrySetText(string text)
        {
            text = (text ?? "").Trim();
            CultureInfo inv = CultureInfo.InvariantCulture;
            long n;
            bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            bool isNum = hex ? long.TryParse(text.Substring(2), NumberStyles.HexNumber, inv, out n)
                             : long.TryParse(text, NumberStyles.Integer, inv, out n);
            switch (Op)
            {
                case OpFloat:
                    float f;
                    if (!float.TryParse(text, NumberStyles.Float, inv, out f) || float.IsNaN(f) || float.IsInfinity(f)) return "Enter a number.";
                    SetFloat(f);
                    return null;
                case OpU64:
                    ulong u;
                    if (!(hex ? ulong.TryParse(text.Substring(2), NumberStyles.HexNumber, inv, out u) : ulong.TryParse(text, NumberStyles.Integer, inv, out u)))
                        return "Enter a whole number.";
                    if (u != Long) { Long = u; Touch(); }
                    return null;
                case OpU32:
                    // A handle can also be typed as the name it stands for.
                    if (Field != null && Field.IsHandle && !isNum) { SetRaw(text.Length == 0 ? 0 : Crc.Of(text)); return null; }
                    if (!isNum || n < 0 || n > uint.MaxValue) return "Enter a whole number from 0 to 4294967295.";
                    SetRaw((uint)n);
                    return null;
                case OpBool:
                    if (!isNum || n < 0 || n > 1) return "Enter 0 or 1.";
                    break;
                case OpS8:
                    if (!isNum || n < sbyte.MinValue || n > sbyte.MaxValue) return "Enter a whole number from -128 to 127.";
                    break;
                case OpU8:
                    if (!isNum || n < 0 || n > 255) return "Enter a whole number from 0 to 255.";
                    break;
                case OpS16:
                    if (!isNum || n < short.MinValue || n > short.MaxValue) return "Enter a whole number from -32768 to 32767.";
                    break;
                case OpU16:
                    if (!isNum || n < 0 || n > 65535) return "Enter a whole number from 0 to 65535.";
                    break;
                case OpS32:
                    if (!isNum || n < int.MinValue || n > int.MaxValue) return "Enter a whole number from -2147483648 to 2147483647.";
                    break;
                default:
                    return "This value can't be edited.";
            }
            SetRaw(unchecked((uint)n));
            return null;
        }
    }

    public sealed class Block
    {
        public SaveFile File;
        public uint Hash;
        public uint Version;
        public uint Count;           // a capacity hint: see FORMAT.md
        public uint SchemaVersion, SchemaZ1, SchemaZ2;
        public byte SchemaTag;
        public List<StructDef> Structs = new List<StructDef>();
        public List<Node> Records = new List<Node>();

        public string Name { get { return Names.Get(Hash); } }

        public int ImportStruct(StructDef src)
        {
            for (int i = 0; i < Structs.Count; i++) if (Structs[i].Hash == src.Hash) return i;
            Structs.Add(src.Copy());
            return Structs.Count - 1;
        }

        // Adds a copy of a record taken from another save, or from the catalog. The two may come from
        // different game versions, so fields are matched by hash and any this schema lacks are dropped.
        public Node AddRecord(Node src)
        {
            int idx = ImportStruct(src.Def);
            Node r = new Node
            {
                Kind = NodeKind.Record,
                Tag = (uint)Node.OpRecord << 24 | (uint)idx << 12 | (src.Tag & 0xFFF),
                Key = src.Key,
                Reserved = new byte[12],
                NameBytes = (byte[])src.NameBytes.Clone(),
                Def = Structs[idx],
                Owner = this,
                Children = new List<Node>()
            };
            CopyChildren(src, r);
            Records.Add(r);
            int structs = 0, arrays = 0;
            Measure(r, ref structs, ref arrays);
            uint need = (uint)(1 + structs + arrays);
            if (need > Count) Count = need;
            File.Changes++;
            return r;
        }

        static void Measure(Node n, ref int structs, ref int arrays)
        {
            if (n.Children == null) return;
            foreach (Node c in n.Children)
            {
                if (c.Kind == NodeKind.Struct) structs++;
                else if (c.Kind == NodeKind.Array) arrays++;
                Measure(c, ref structs, ref arrays);
            }
        }

        void CopyChildren(Node from, Node to)
        {
            foreach (Node c in from.Children)
            {
                int index;
                FieldDef field;
                if (to.Kind == NodeKind.Array)
                {
                    index = to.FieldIndex;  // elements carry the array's field, and their position in the low bits
                    field = to.Field;
                }
                else
                {
                    index = to.Def.IndexOf(c.Field.Hash);
                    if (index < 0 || to.Def.Fields[index].Type != c.Field.Type) continue;
                    field = to.Def.Fields[index];
                }
                uint low = c.Tag & 0xFFF;
                Node n = new Node
                {
                    Kind = c.Kind,
                    Value = c.Value,
                    Long = c.Long,
                    Bytes = c.Bytes == null ? null : (byte[])c.Bytes.Clone(),
                    Tag = (uint)c.Op << 24 | (uint)index << 12 | low,
                    EndValue = c.EndValue,
                    EndTag = (c.EndTag & 0xFF000000) | (uint)index << 12 | (c.EndTag & 0xFFF),
                    Field = field,
                    Parent = to,
                    Owner = this
                };
                if (c.Kind == NodeKind.Struct)
                {
                    int s = ImportStruct(c.Def);
                    n.Value = (uint)s;
                    n.Def = Structs[s];
                    n.Children = new List<Node>();
                    CopyChildren(c, n);
                }
                else if (c.Kind == NodeKind.Array)
                {
                    if (c.Value != uint.MaxValue) n.Value = (uint)ImportStruct(c.Owner.Structs[(int)c.Value]);
                    n.Children = new List<Node>();
                    CopyChildren(c, n);
                }
                to.Children.Add(n);
            }
        }

        static Node Clone(Node src, Node parent)
        {
            Node n = new Node
            {
                Kind = src.Kind, Value = src.Value, Tag = src.Tag, Long = src.Long,
                Bytes = src.Bytes == null ? null : (byte[])src.Bytes.Clone(),
                EndValue = src.EndValue, EndTag = src.EndTag,
                Def = src.Def, Field = src.Field, Parent = parent, Owner = src.Owner
            };
            if (src.Children != null)
            {
                n.Children = new List<Node>(src.Children.Count);
                foreach (Node c in src.Children) n.Children.Add(Clone(c, n));
            }
            return n;
        }

        static void SetElementIndex(Node n, int index)
        {
            n.Tag = (n.Tag & 0xFFFFF000) | (uint)index;
        }

        // Puts a copy of a struct from another record in place of this one: same field, same place.
        public Node ReplaceWithCopy(Node old, Node source)
        {
            Node copy = Clone(source, old.Parent);
            copy.Tag = old.Tag;
            copy.EndTag = old.EndTag;
            copy.Value = old.Value;
            int at = old.Parent.Children.IndexOf(old);
            old.Parent.Children[at] = copy;
            uint before = ElementWeight(old), after = ElementWeight(copy);
            if (after > before) Count += after - before;
            File.Changes++;
            return copy;
        }

        // Adds a copy of an array's element to the end of that array.
        public Node AppendCopy(Node array, Node element)
        {
            if (array.Children.Count >= 0xFFF) throw new SaveFormatException("That list is full.");
            Node copy = Clone(element, array);
            SetElementIndex(copy, array.Children.Count);
            array.Children.Add(copy);
            Count += ElementWeight(copy);
            File.Changes++;
            return copy;
        }

        public void RemoveElement(Node array, Node element)
        {
            if (!array.Children.Remove(element)) return;
            for (int i = 0; i < array.Children.Count; i++) SetElementIndex(array.Children[i], i);
            uint weight = ElementWeight(element);
            Count = Count > weight ? Count - weight : Count;
            File.Changes++;
        }

        // How much an array element adds to the section's capacity hint: count grows with what a record holds.
        static uint ElementWeight(Node element)
        {
            int structs = 0, arrays = 0;
            Measure(element, ref structs, ref arrays);
            return (uint)(1 + structs + arrays);
        }

        public void RemoveRecord(Node r)
        {
            if (Records.Remove(r)) File.Changes++;
        }

        // Drops a value from a record, for fields the game itself leaves out when they're empty.
        public void RemoveChild(Node n)
        {
            if (n.Parent != null && n.Parent.Children.Remove(n)) File.Changes++;
        }
    }

    public sealed class SaveFile
    {
        public const uint MagicData = 0xE4C7BBD7, MagicSchema = 0xAE1F7795;
        public const int StandardSize = 0x300000;

        public uint Version;
        public int FileSize;         // files are padded with zeros to this; 0 means no padding
        public List<Block> Blocks = new List<Block>();
        public int Changes;

        public Block Section(uint hash)
        {
            foreach (Block b in Blocks) if (b.Hash == hash) return b;
            return null;
        }

        public Block Section(string name) { return Section(Crc.Of(name)); }

        public static SaveFile Parse(byte[] b)
        {
            return new Reader(b).Run();
        }

        sealed class Reader
        {
            readonly byte[] b;
            public Reader(byte[] bytes) { b = bytes; }

            uint U(int o)
            {
                if (o < 0 || o + 4 > b.Length) throw new SaveFormatException("The file ends too early, at " + o + ".");
                return BitConverter.ToUInt32(b, o);
            }

            byte[] Slice(int o, int n)
            {
                if (n < 0 || o + n > b.Length) throw new SaveFormatException("The file ends too early, at " + o + ".");
                byte[] r = new byte[n];
                Buffer.BlockCopy(b, o, r, 0, n);
                return r;
            }

            static int Align8(int o) { return (o + 7) & ~7; }

            public SaveFile Run()
            {
                if (b.Length < 12 || b[0] != 'e' || b[1] != 'v' || b[2] != 'a' || b[3] != 's')
                    throw new SaveFormatException("This isn't a Saints Row save: it doesn't start with \"evas\".");
                SaveFile f = new SaveFile { Version = U(4), FileSize = b.Length };
                uint n = U(8);
                if (n > 4096) throw new SaveFormatException("The save claims " + n + " sections.");
                int o = 12;
                for (uint i = 0; i < n; i++) o = ReadBlock(f, o);
                for (int i = o; i < b.Length; i++)
                    if (b[i] != 0) throw new SaveFormatException("There is unknown data after the last section, at " + i + ".");
                return f;
            }

            int ReadBlock(SaveFile f, int o)
            {
                Block k = new Block { File = f, Hash = U(o) };
                uint size = U(o + 4);
                int p = o + 8;
                if (U(p) != MagicData) throw new SaveFormatException("Section " + k.Name + " has no data header, at " + p + ".");
                k.Version = U(p + 4);
                int ds = (int)U(p + 8);
                k.Count = U(p + 12);
                int end = p + 8 + ds;
                int blockEnd = p + (int)size;
                if (ds < 8 || blockEnd > b.Length || end > blockEnd) throw new SaveFormatException("Section " + k.Name + " has impossible sizes.");
                int se = ReadSchema(k, end);
                for (int i = se; i < blockEnd; i++)
                    if (b[i] != 0) throw new SaveFormatException("Section " + k.Name + " has unknown data after its schema.");
                if (blockEnd % 0x40 != 0) throw new SaveFormatException("Section " + k.Name + " doesn't end on a 64-byte boundary.");
                int pos = ds == 8 ? p + 16 : Align8(p + 16);
                while (pos < end) pos = ReadRecord(k, pos, end);
                if (pos != end) throw new SaveFormatException("Section " + k.Name + " records end in the wrong place.");
                f.Blocks.Add(k);
                return blockEnd;
            }

            int ReadSchema(Block k, int o)
            {
                if (U(o) != MagicSchema) throw new SaveFormatException("Section " + k.Name + " has no schema, at " + o + ".");
                k.SchemaVersion = U(o + 4);
                k.SchemaZ1 = U(o + 8);
                k.SchemaZ2 = U(o + 12);
                o += 16;
                k.SchemaTag = Slice(o, 1)[0];
                uint n = U(o + 1);
                o += 5;
                if (n > 100000) throw new SaveFormatException("Section " + k.Name + " claims " + n + " types.");
                for (uint i = 0; i < n; i++)
                {
                    StructDef d = new StructDef { Hash = U(o), Z1 = U(o + 4), Size = U(o + 12), Align = U(o + 16) };
                    uint nf = U(o + 8);
                    o += 20;
                    if (nf > 100000) throw new SaveFormatException("Type " + d.Name + " claims " + nf + " fields.");
                    for (uint j = 0; j < nf; j++)
                    {
                        FieldDef fd = new FieldDef { Pre = Slice(o, 1)[0] };
                        o += 1;
                        fd.Hash = U(o);
                        fd.Z1 = U(o + 4);
                        fd.Type = U(o + 8);
                        fd.Bits = U(o + 12);
                        fd.Ref = U(o + 16);
                        fd.Offset = U(o + 20);
                        fd.Bits2 = U(o + 24);
                        o += 28;
                        d.Fields.Add(fd);
                    }
                    d.Tail = Slice(o, 3);
                    o += 3;
                    k.Structs.Add(d);
                }
                return o;
            }

            StructDef StructAt(Block k, uint index, int pos)
            {
                if (index >= k.Structs.Count)
                    throw new SaveFormatException("Section " + k.Name + " uses type " + index + " of " + k.Structs.Count + ", at " + pos + ".");
                return k.Structs[(int)index];
            }

            FieldDef FieldOf(Node parent, int index)
            {
                if (parent.Kind == NodeKind.Array) return parent.Field;
                return index < parent.Def.Fields.Count ? parent.Def.Fields[index] : null;
            }

            int ReadRecord(Block k, int pos, int end)
            {
                uint v = U(pos), t = U(pos + 4);
                if (t >> 24 != Node.OpRecord) throw new SaveFormatException("Expected a record at " + pos + " in section " + k.Name + ".");
                int recEnd = pos + (int)v;
                if (v < 28 || recEnd > end) throw new SaveFormatException("A record at " + pos + " overruns section " + k.Name + ".");
                Node r = new Node
                {
                    Kind = NodeKind.Record,
                    Tag = t,
                    Key = U(pos + 8),
                    Reserved = Slice(pos + 12, 12),
                    Owner = k,
                    Children = new List<Node>(),
                    Def = StructAt(k, (t >> 12) & 0xFFF, pos)
                };
                int len = (int)U(pos + 24);
                r.NameBytes = Slice(pos + 28, len);
                int p = Align8(pos + 28 + len);
                Node cur = r;
                while (p < recEnd)
                {
                    uint val = U(p), tag = U(p + 4);
                    byte op = (byte)(tag >> 24);
                    int fi = (int)((tag >> 12) & 0xFFF);
                    Node n = null;
                    switch (op)
                    {
                        case Node.OpStruct:
                            n = new Node { Kind = NodeKind.Struct, Children = new List<Node>(), Def = StructAt(k, val, p) };
                            p += 8;
                            break;
                        case Node.OpArray:
                            n = new Node { Kind = NodeKind.Array, Children = new List<Node>() };
                            p += 8;
                            break;
                        case Node.OpStructEnd:
                        case Node.OpArrayEnd:
                            if (cur == r || cur.Kind != (op == Node.OpStructEnd ? NodeKind.Struct : NodeKind.Array))
                                throw new SaveFormatException("Unbalanced end marker at " + p + " in section " + k.Name + ".");
                            if (cur.Kind == NodeKind.Array && (cur.Tag & 0xFFF) != cur.Children.Count)
                                throw new SaveFormatException("An array at " + p + " has the wrong length in section " + k.Name + ".");
                            cur.EndValue = val;
                            cur.EndTag = tag;
                            cur = cur.Parent;
                            p += 8;
                            continue;
                        case Node.OpBlob:
                        case Node.OpString:
                            n = new Node { Kind = NodeKind.Bytes, Bytes = Slice(p + 8, (int)val) };
                            p = Align8(p + 8 + (int)val);
                            break;
                        case Node.OpU64:
                            n = new Node { Kind = NodeKind.Scalar, Long = BitConverter.ToUInt64(Slice(p + 8, 8), 0) };
                            p += 16;
                            break;
                        case Node.OpBool:
                        case Node.OpS8:
                        case Node.OpU8:
                        case Node.OpS16:
                        case Node.OpU16:
                        case Node.OpS32:
                        case Node.OpU32:
                        case Node.OpFloat:
                            n = new Node { Kind = NodeKind.Scalar };
                            p += 8;
                            break;
                        default:
                            throw new SaveFormatException("Unknown value kind 0x" + op.ToString("x2") + " at " + p + " in section " + k.Name + ". The editor won't guess.");
                    }
                    n.Value = val;
                    n.Tag = tag;
                    n.Parent = cur;
                    n.Owner = k;
                    n.Field = FieldOf(cur, fi);
                    if (n.Field == null) throw new SaveFormatException("A value at " + p + " uses a field the schema doesn't list, in section " + k.Name + ".");
                    cur.Children.Add(n);
                    if (n.Children != null) cur = n;
                }
                if (p != recEnd || cur != r) throw new SaveFormatException("A record at " + pos + " doesn't end where it says, in section " + k.Name + ".");
                k.Records.Add(r);
                return recEnd;
            }
        }

        sealed class Writer
        {
            public byte[] B = new byte[StandardSize];
            public int P;

            void Grow(int n)
            {
                if (P + n > B.Length) Array.Resize(ref B, Math.Max(B.Length * 2, P + n));
            }

            public void U(uint v)
            {
                Grow(4);
                B[P] = (byte)v; B[P + 1] = (byte)(v >> 8); B[P + 2] = (byte)(v >> 16); B[P + 3] = (byte)(v >> 24);
                P += 4;
            }

            public void Patch(int at, uint v)
            {
                B[at] = (byte)v; B[at + 1] = (byte)(v >> 8); B[at + 2] = (byte)(v >> 16); B[at + 3] = (byte)(v >> 24);
            }

            public void Raw(byte[] x)
            {
                Grow(x.Length);
                Buffer.BlockCopy(x, 0, B, P, x.Length);
                P += x.Length;
            }

            public void Byte(byte x) { Grow(1); B[P++] = x; }

            public void Pad(int boundary)
            {
                int n = (boundary - P % boundary) % boundary;
                Grow(n);
                P += n;     // the buffer is already zero
            }
        }

        // Throws if the result won't fit: the game's saves are a fixed size.
        public byte[] Serialize()
        {
            Writer w = new Writer();
            w.U(0x73617665);    // "evas"
            w.U(Version);
            w.U((uint)Blocks.Count);
            foreach (Block k in Blocks) WriteBlock(w, k);
            if (FileSize == 0)
            {
                byte[] exact = new byte[w.P];
                Buffer.BlockCopy(w.B, 0, exact, 0, w.P);
                return exact;
            }
            if (w.P > FileSize)
                throw new SaveFormatException("The save would be " + w.P + " bytes, and the game's saves hold " + FileSize + ". Add fewer items.");
            byte[] result = new byte[FileSize];
            Buffer.BlockCopy(w.B, 0, result, 0, w.P);
            return result;
        }

        static void WriteBlock(Writer w, Block k)
        {
            w.U(k.Hash);
            int sizeAt = w.P;
            w.U(0);
            int p = w.P;
            w.U(MagicData);
            w.U(k.Version);
            int dsAt = w.P;
            w.U(0);
            w.U(k.Count);
            w.Pad(8);
            foreach (Node r in k.Records) WriteNode(w, r);
            w.Patch(dsAt, (uint)(w.P - dsAt));
            w.U(MagicSchema);
            w.U(k.SchemaVersion);
            w.U(k.SchemaZ1);
            w.U(k.SchemaZ2);
            w.Byte(k.SchemaTag);
            w.U((uint)k.Structs.Count);
            foreach (StructDef d in k.Structs)
            {
                w.U(d.Hash);
                w.U(d.Z1);
                w.U((uint)d.Fields.Count);
                w.U(d.Size);
                w.U(d.Align);
                foreach (FieldDef f in d.Fields)
                {
                    w.Byte(f.Pre);
                    w.U(f.Hash);
                    w.U(f.Z1);
                    w.U(f.Type);
                    w.U(f.Bits);
                    w.U(f.Ref);
                    w.U(f.Offset);
                    w.U(f.Bits2);
                }
                w.Raw(d.Tail);
            }
            w.Pad(0x40);
            w.Patch(sizeAt, (uint)(w.P - p));
        }

        static void WriteNode(Writer w, Node n)
        {
            switch (n.Kind)
            {
                case NodeKind.Record:
                    int start = w.P;
                    w.U(0);
                    w.U(n.Tag);
                    w.U(n.Key);
                    w.Raw(n.Reserved);
                    w.U((uint)n.NameBytes.Length);
                    w.Raw(n.NameBytes);
                    w.Pad(8);
                    foreach (Node c in n.Children) WriteNode(w, c);
                    w.Patch(start, (uint)(w.P - start));
                    break;
                case NodeKind.Struct:
                    w.U(n.Value);
                    w.U(n.Tag);
                    foreach (Node c in n.Children) WriteNode(w, c);
                    w.U(n.EndValue);
                    w.U(n.EndTag);
                    break;
                case NodeKind.Array:
                    w.U(n.Value);
                    w.U((n.Tag & 0xFFFFF000) | (uint)n.Children.Count);
                    foreach (Node c in n.Children) WriteNode(w, c);
                    w.U(n.EndValue);
                    w.U(n.EndTag);
                    break;
                case NodeKind.Bytes:
                    w.U((uint)n.Bytes.Length);
                    w.U(n.Tag);
                    w.Raw(n.Bytes);
                    w.Pad(8);
                    break;
                default:
                    w.U(n.Value);
                    w.U(n.Tag);
                    if (n.Op == Node.OpU64) { w.U((uint)n.Long); w.U((uint)(n.Long >> 32)); }
                    break;
            }
        }
    }

    // A .met_pc: the preview the load menu reads. Same encoding, with no section or record wrapper,
    // and always the same shape, so values are patched where they lie.
    public sealed class MetaFile
    {
        public static readonly uint Cash = Crc.Of("m_cash"), Playtime = Crc.Of("m_total_playtime"), Completion = Crc.Of("m_completion");

        public byte[] Bytes;
        readonly Dictionary<uint, int> positions = new Dictionary<uint, int>();

        public static MetaFile Parse(byte[] b)
        {
            MetaFile m = new MetaFile { Bytes = b };
            if (b.Length < 16 || BitConverter.ToUInt32(b, 0) != SaveFile.MagicData) return null;   // SR_PROFILE's is blank
            int end = 8 + BitConverter.ToInt32(b, 8);
            if (end < 16 || end + 21 > b.Length || BitConverter.ToUInt32(b, end) != SaveFile.MagicSchema) return null;
            int o = end + 16 + 1;
            if (BitConverter.ToUInt32(b, o) == 0) return null;
            o += 4;
            int nf = BitConverter.ToInt32(b, o + 8);
            o += 20;
            List<uint> fields = new List<uint>();
            for (int i = 0; i < nf; i++)
            {
                fields.Add(BitConverter.ToUInt32(b, o + 1));
                o += 29;
            }
            int p = 16;
            int depth = 0;
            while (p < end)
            {
                uint tag = BitConverter.ToUInt32(b, p + 4);
                byte op = (byte)(tag >> 24);
                int fi = (int)((tag >> 12) & 0xFFF);
                if (op == Node.OpArray) depth++;
                else if (op == Node.OpArrayEnd) depth--;
                else if (depth == 0 && fi < fields.Count) m.positions[fields[fi]] = p;
                p += 8;
            }
            return m;
        }

        public bool Has(uint field) { return positions.ContainsKey(field); }

        public uint Get(uint field) { return BitConverter.ToUInt32(Bytes, positions[field]); }

        public byte OpOf(uint field) { return (byte)(BitConverter.ToUInt32(Bytes, positions[field] + 4) >> 24); }

        public void Set(uint field, uint value)
        {
            int at;
            if (!positions.TryGetValue(field, out at)) return;
            Buffer.BlockCopy(BitConverter.GetBytes(value), 0, Bytes, at, 4);
        }
    }
}
