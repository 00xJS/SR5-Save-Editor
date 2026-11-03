"""Builds data/vehicle_tables.txt and data/display_names.txt from the game's own tables and English text.

    python -I tools/gen_tables.py SR5_TWEAKS_TOOLS_DIR SAVES_DIR [GAME_ROOT]

The game's tables use the same record format as saves. They sit in .tfe_pad files inside the DLC archives and inside the
*_precache.vpp_pc archives nested in the main ones. The user's own car patch is left out: it isn't part of the game.
"""
import glob
import os
import struct
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sr5save import crc, parse_blocks, parse_records, parse_schema, u32, load_names  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data')
SOUND_CLASSES = {'m_engine_audio': 'vehicle_engine_audio_info', 'm_tranny_audio': 'vehicle_transmission_info',
                 'm_siren_audio': 'vehicle_siren_info', 'm_horn_audio': 'vehicle_horn_info'}
# Classes whose records stand for something a save lists, with the field that holds their English text key.
NAME_FIELDS = ['m_display_name', 'm_ui_display_name', 'm_title', 'm_name_tag', 'm_text']
LINK_FIELDS = ['m_skill_h', 'm_inventory_weapon_info', 'm_weapon_info']


def inner_files(raw, pb):
    """Files inside a small archive held in memory."""
    return pb.read_pack(raw)


def scan_archive(path, pb, tables, text, get=None, depth=0):
    if get is None:
        f = open(path, 'rb')

        def get(o, n):
            f.seek(o)
            return f.read(n)
    head = get(0, 0x78)
    if head[:4] != b'\xce\x0a\x89\x51':
        return
    nfiles, npaths, namesz = struct.unpack_from('<III', head, 0x10)
    dataoff = struct.unpack_from('<Q', head, 0x40)[0]
    ents = get(0x78, nfiles * 0x30)
    names = get(0x78 + nfiles * 0x30 + npaths * 8, namesz)

    def name_at(o):
        return names[o:names.find(b'\0', o)].decode('latin1')

    def unpack(data):
        return pb.lz4_unframe(data) if data[:4] == b'\x04"M\x18' else data

    for k in range(nfiles):
        no, _po, do, sz, csz, _fl = struct.unpack_from('<QQQQqI', ents, k * 0x30)
        name = name_at(no)
        stored = sz if csz < 0 else csz
        if name.endswith('.vpp_pc') and depth < 2 and 'patch' not in name and 'zz_probe' not in name:
            raw = get(dataoff + do, stored)
            if csz >= 0:
                raw = pb.lz4_unframe(raw)
            scan_archive(None, pb, tables, text, lambda o, n, r=raw: r[o:o + n], depth + 1)
        elif name.endswith('.tfe_pad.str2_pc') or name.endswith('.en.scribe_pad.str2_pc'):
            for entry in inner_files(get(dataoff + do, stored), pb):
                if name.endswith('.tfe_pad.str2_pc'):
                    tables.append(unpack(entry[2]))
                else:
                    text.append(unpack(entry[2]))


def read_records(raw):
    i = raw.find(b'\xd7\xbb\xc7\xe4')
    if i < 0:
        return []
    ds = u32(raw, i + 8)
    end = i + 8 + ds
    schema, _ = parse_schema(raw, end)
    return parse_records(raw, i + 16, end, schema)


class Db:
    def __init__(self, names):
        self.names = names
        self.by_class = {}

    def name(self, h):
        return self.names.get(h, '%08x' % h)

    def add(self, rec):
        self.by_class.setdefault(self.name(rec['struct']['hash']), {}).setdefault(rec['name'].lower(), rec)

    def get(self, cls, name):
        return self.by_class.get(cls, {}).get((name or '').lower())

    def fields(self, node):
        st = node['struct']
        out = {}
        for c in node['children']:
            if c['f'] >= len(st['fields']):
                continue
            key = self.name(st['fields'][c['f']]['hash'])
            if c['kind'] == 'blob':
                out[key] = c['data'].split(b'\0')[0].decode('latin1')
            elif c['kind'] == 'val':
                out[key] = c['v']
            else:
                out[key] = c
        return out

    def items(self, arr):
        out = []
        for e in arr['children'] if isinstance(arr, dict) else []:
            if e['kind'] == 'struct':
                out.append(self.fields(e))
            elif e['kind'] == 'blob':
                out.append(e['data'].split(b'\0')[0].decode('latin1'))
            elif e['kind'] == 'val':
                out.append(e['v'])
        return out


def to_float(v):
    return round(struct.unpack('<f', struct.pack('<I', v))[0], 3)


def clean(s):
    return (s or '').replace('\t', ' ').replace('\n', ' ').replace('|', '/').strip()


def main(tools, saves, root):
    sys.path.insert(0, tools)
    import packbuild as pb

    names = load_names()
    refl = open(os.path.join(root, 'sr5', 'data', 'reflection', 'reflection.vpp_pc'), 'rb').read()
    import re
    for m in re.finditer(rb'[A-Za-z_][A-Za-z0-9_:<> .\-]{2,80}', refl):
        s = m.group().decode('latin1')
        names.setdefault(crc(s), s)

    tables, text = [], []
    for path in sorted(glob.glob(os.path.join(root, 'sr5', 'data', '*', '*.vpp_pc')) + glob.glob(os.path.join(root, 'ctg', 'data', '*', '*.vpp_pc'))):
        if 'patch' in os.path.basename(path) or 'zz_probe' in path:
            continue
        scan_archive(path, pb, tables, text)
    db = Db(names)
    failed = 0
    for raw in tables:
        try:
            for rec in read_records(raw):
                db.add(rec)
        except (AssertionError, struct.error, IndexError, UnicodeDecodeError):
            failed += 1
    english = {}
    for raw in text:
        try:
            for key, value in pb.scribe_rows(raw):
                if value:
                    english[key] = value
        except (AssertionError, struct.error, IndexError, UnicodeDecodeError):
            pass
    print('tables', len(tables), 'unreadable', failed, 'classes', len(db.by_class), 'english lines', len(english))

    def en(key, fallback=''):
        return clean(english.get(key, fallback))

    def option_label(option):
        rec = db.get('vehicle_cust_vid_slot_option_name', option)
        if rec:
            text_key = db.fields(rec).get('m_ui_display_name')
            if text_key and en(text_key):
                return en(text_key)
        return clean(option).replace('_', ' ').capitalize()

    lines = ['# The game\'s own lists for vehicle customization. Rebuild with tools/gen_tables.py.']
    for info, rec in sorted(db.by_class.get('gml_vehicle_info', {}).items()):
        v = db.fields(rec)
        lines.append('A\t%s\t%s' % (info, en(v.get('m_display_name', ''))))
        wrap = db.get('vehicle_cust_wrap_info', v.get('m_wrap_handle'))
        if wrap:
            w = db.fields(wrap)
            for i, slot_name in enumerate(db.items(w.get('m_vid_entries'))):
                slot = db.get('vehicle_cust_vid_slot', slot_name)
                if not slot:
                    continue
                s = db.fields(slot)
                options = [c.get('m_name', '') for c in db.items(s.get('m_choice_entries'))]
                lines.append('P\t%s\t%d\t%s\t%s' % (info, i, en(s.get('m_ui_display_name', ''), slot_name), '|'.join(option_label(o) for o in options)))
            for i, group_name in enumerate(db.items(w.get('m_texture_entries'))):
                group = db.get('vehicle_cust_material_texture_group', group_name)
                if not group:
                    continue
                g = db.fields(group)
                options = [c.get('m_texture_choice', '') for c in db.items(g.get('m_texture_entries'))]
                lines.append('T\t%s\t%d\t%s\t%s' % (info, i, en(g.get('m_ui_display_name', ''), group_name), '|'.join(clean(o) for o in options)))
            for i, link in enumerate(db.items(w.get('m_link_entries'))):
                name = link.get('m_linked_palettes', '') if isinstance(link, dict) else ''
                lines.append('L\t%s\t%d\t%s' % (info, i, clean(name).replace('_', ' ').capitalize()))
        ext = db.get('vehicle_cust_external_info', v.get('m_external_parts_handle'))
        if ext:
            e = db.fields(ext)
            wheels = e.get('m_wheels')
            if isinstance(wheels, dict):
                for i, axle in enumerate(db.items(db.fields(wheels).get('m_axles'))):
                    lines.append('W\t%s\t%d\t%s\t%s\t%s\t%s\t%s\t%s\t%s\t%s' % (
                        info, i, axle.get('m_name', ''), to_float(axle.get('m_min_size', 0)), to_float(axle.get('m_max_size', 0)), to_float(axle.get('m_default_size', 0)),
                        to_float(axle.get('m_min_width', 0)), to_float(axle.get('m_max_width', 0)), axle.get('m_rim_slot', ''), axle.get('m_tire_slot', '')))
            if e.get('m_underglow'):
                lines.append('U\t%s\t%s' % (info, e['m_underglow']))

    for name, rec in sorted(db.by_class.get('vehicle_underglow_color_set', {}).items()):
        colors = []
        for c in db.items(db.fields(rec).get('m_color_choices')):
            choice = c.get('m_color_choice', '') if isinstance(c, dict) else ''
            r = db.get('vehicle_underglow_color_choice', choice)
            label = en(db.fields(r).get('m_ui_display_name', '')) if r else ''
            colors.append(label or clean(choice).capitalize())
        lines.append('G\t%s\t%s' % (name, '|'.join(colors)))

    for cls, tag in (('vehicle_rim_slot_info', 'R'), ('vehicle_tire_slot_info', 'I')):
        for name, rec in sorted(db.by_class.get(cls, {}).items()):
            f = db.fields(rec)
            arr = next((x for x in f.values() if isinstance(x, dict) and x['kind'] == 'array'), None)
            labels = []
            for c in db.items(arr):
                key = next((c[k] for k in c if isinstance(c[k], str) and k.endswith('name')), None) if isinstance(c, dict) else None
                row = db.get('vehicle_wheel_axle_name', key) if key else None
                text_key = db.fields(row).get('m_ui_display_name') if row else None
                labels.append(en(text_key or '') or clean(key or '').capitalize())
            lines.append('%s\t%s\t%s' % (tag, name, '|'.join(labels)))

    for field, cls in sorted(SOUND_CLASSES.items()):
        for name, rec in sorted(db.by_class.get(cls, {}).items()):
            f = db.fields(rec)
            label = en(f.get('m_ui_display_name', '')) or clean(name)
            if label.lower() == 'unused':
                label = clean(name)
            lines.append('H\t%s\t%08x\t%s' % (field, crc(rec['name']), label))

    with open(os.path.join(OUT, 'vehicle_tables.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('\n'.join(lines) + '\n')

    # English names for everything a save can list, by record name, and hashes the tables can name.
    wanted = {}
    for path in glob.glob(os.path.join(saves, '*.sf_pc')):
        for x in parse_blocks(open(path, 'rb').read())[0]:
            for r in x['records']:
                wanted[r['key']] = r['name']
    display = {}
    extra = {}
    for cls, recs in db.by_class.items():
        for name, rec in recs.items():
            h = crc(rec['name'])
            if h not in wanted and h not in names:
                extra[h] = rec['name']
            f = db.fields(rec)
            label = next((en(f[k]) for k in NAME_FIELDS if isinstance(f.get(k), str) and en(f[k])), '')
            if not label and isinstance(f.get('m_skill_h'), str):
                skill = db.get('skill_info', f['m_skill_h'])
                if skill:
                    sf = db.fields(skill)
                    label = next((en(sf[k]) for k in NAME_FIELDS if isinstance(sf.get(k), str) and en(sf[k])), '')
            if label and label.lower() != 'unused' and h not in display:
                display[h] = label
    covered = sum(1 for h in wanted if h in display)
    with open(os.path.join(OUT, 'display_names.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('# English names from the game, by the hash of a record\'s name. Rebuild with tools/gen_tables.py.\n')
        for h in sorted(display):
            if h in wanted or display[h]:
                f.write('%08x\t%s\n' % (h, display[h]))
    # Parameter names and other handles the tables can name, for names.txt to pick up.
    with open(os.path.join(OUT, 'table_names.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('# Names of records in the game\'s tables that saves refer to by hash. Rebuild with tools/gen_tables.py.\n')
        for cls in ('vehicle_cust_material_params', 'vehicle_shared_params_info', 'vehicle_rim_slot_info', 'vehicle_tire_slot_info'):
            for name, rec in sorted(db.by_class.get(cls, {}).items()):
                f.write('%08x\t%s\n' % (crc(rec['name']), rec['name']))
    print('vehicle table lines', len(lines), 'display names', len(display), 'covers', covered, 'of', len(wanted), 'save records')


if __name__ == '__main__':
    if len(sys.argv) < 3:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1], sys.argv[2], sys.argv[3] if len(sys.argv) > 3 else r'C:\Program Files\Epic Games\SaintsRow')
