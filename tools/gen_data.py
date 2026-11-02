"""Regenerates the tables in data/ from your saves, the game files and the SR5-Tweaks research.

    python -I tools/gen_data.py SAVES_DIR GAME_DIR SR5_TWEAKS_DIR

Writes names.txt (hash to name), weapons.txt (which weapons are real and how
they upgrade) and vehicles.txt (display names). The editor still works on saves
with hashes that aren't listed: it shows them as hex.
"""
import glob
import os
import re
import struct
import sys
from collections import Counter

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from sr5save import crc, parse_blocks, u32  # noqa: E402

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'data')

# Sections whose names aren't in any game string. Named after what they hold.
HAND_NAMED = {
    0xBDD6F187: 'vehicle unlocks',
    0xAD182D1A: 'collectibles',
    0x67D36BE8: 'missions',
    0xB80F05C1: 'playlists',
    0xFF6E29CD: 'profile: settings 1',
    0xC7398B2F: 'profile: header',
    0xE0D15E28: 'profile: empty',
    0x126C0664: 'profile: saved presets',
    0x5E2106DA: 'profile: settings 2',
    0x647C6F0E: 'profile: unlocks',
    0x823E423F: 'two_frontz_reward',    # from the car patch; the game writes no name for it
}


def table():
    t = []
    for i in range(256):
        c = i
        for _ in range(8):
            c = (c >> 1) ^ (0xEDB88320 if c & 1 else 0)
        t.append(c)
    return t


T = table()


def fast_crc(bs):
    c = 0
    for x in bs:
        c = T[(c ^ x) & 255] ^ (c >> 8)
    return c


def collect_wanted(blocks, want):
    def visit(node, st):
        for c in node['children']:
            f = st['fields'][c['f']] if st and c['f'] < len(st['fields']) else None
            if c['kind'] == 'struct':
                visit(c, c['struct'])
            elif c['kind'] == 'array':
                for e in c['children']:
                    if e['kind'] == 'struct':
                        visit(e, e['struct'])
                    elif e['kind'] == 'val' and f and (f['type'] >> 8) & 0x30 and e['v']:
                        want.add(e['v'])
            elif c['kind'] == 'val' and f and (f['type'] >> 8) & 0x30 and c['v']:
                want.add(c['v'])
    for b in blocks:
        want.add(b['hash'])
        for st in b['schema']:
            want.add(st['hash'])
            for f in st['fields']:
                want.add(f['hash'])
        for r in b['records']:
            want.add(r['key'])
            visit(r, r['struct'])


def write_garage_options(files, resolved):
    """What real garages hold, for the notes beside each option, and the sounds to choose from."""
    fnames = {}
    seen = {}
    sounds = {}

    def name(h):
        return resolved.get(h, '%08x' % h)

    def walk(node, st, path, variant):
        for c in node['children']:
            f = st['fields'][c['f']] if st and c['f'] < len(st['fields']) else None
            if not f:
                continue
            p = (path + '.' if path else '') + name(f['hash'])
            if c['kind'] == 'struct':
                walk(c, c['struct'], p, variant)
            elif c['kind'] == 'array':
                for e in c['children']:
                    if e['kind'] == 'struct':
                        walk(e, e['struct'], p + '[]', variant)
                    elif e['kind'] == 'val' and name(f['hash']) not in ('m_nickname', 'm_param'):
                        seen.setdefault(p + '[]', []).append(struct.unpack('<i', struct.pack('<I', e['v']))[0])
            elif c['kind'] == 'val':
                if (f['type'] >> 8) & 0x30 == 0x30:
                    if c['v']:
                        sounds.setdefault(name(f['hash']), {}).setdefault(c['v'], Counter())[variant] += 1
                elif c['op'] == 0x0B:
                    v = struct.unpack('<f', struct.pack('<I', c['v']))[0]
                    if abs(v) < 100000:     # unset values are junk
                        seen.setdefault(p, []).append(round(v, 3))
                elif c['op'] == 0x07:
                    seen.setdefault(p, []).append(struct.unpack('<i', struct.pack('<I', c['v']))[0])
                else:
                    seen.setdefault(p, []).append(c['v'])

    for path in files:
        for x in parse_blocks(open(path, 'rb').read())[0]:
            for r in x['records']:
                if r['struct']['hash'] != crc('vehicle_save_state'):
                    continue
                fields = r['struct']['fields']
                slots = {fields[c['f']]['hash']: c for c in r['children']}.get(crc('m_save_slots'))
                for s in (slots['children'] if slots else []):
                    sf = s['struct']['fields']
                    vals = {sf[c['f']]['hash']: c.get('v') for c in s['children'] if c['kind'] == 'val'}
                    walk(s, s['struct'], '', name(vals[crc('m_variant_info')]))

    with open(os.path.join(OUT, 'garage_options.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('# N path note: what saves hold. H field hash label: a handle choice.\n')
        for p in sorted(seen):
            vals = seen[p]
            distinct = sorted(set(vals))
            if len(distinct) <= 6:
                note = 'Seen: ' + ', '.join(str(v) for v in distinct)
            else:
                note = 'Seen: %s to %s' % (distinct[0], distinct[-1])
            f.write('N\t%s\t%s\n' % (p, note))
        for field in sorted(sounds):
            if field not in ('m_engine_audio', 'm_tranny_audio', 'm_siren_audio', 'm_horn_audio'):
                continue
            for h, variants in sorted(sounds[field].items()):
                label = resolved.get(h) or 'As on ' + variants.most_common(1)[0][0]
                f.write('H\t%s\t%08x\t%s\n' % (field, h, label))


def main(saves, game, tweaks):
    want = set()
    resolved = {}
    files = sorted(glob.glob(os.path.join(saves, '*.sf_pc')))
    weapon_tracks = {}
    weapon_ult = set()
    for path in files:
        b = open(path, 'rb').read()
        blocks, _ = parse_blocks(b)
        collect_wanted(blocks, want)
        for x in blocks:
            for r in x['records']:
                if r['name'] and crc(r['name']) == r['key']:
                    resolved[r['key']] = r['name']
                # Weapon upgrade shape: upgraded weapons show how many tracks they have.
                if r['name'].startswith(('weapon_', 'inv_', 'inventory_')) and any(
                        f['hash'] == crc('m_equipped_mod_levels') for f in r['struct']['fields']):
                    fields = r['struct']['fields']
                    vals = {fields[c['f']]['hash']: c for c in r['children']}
                    level = vals.get(crc('m_stat_level_purchased'))
                    mods = vals.get(crc('m_equipped_mod_levels'))
                    ult = vals.get(crc('m_ultimate_purchased'))
                    if ult and ult['v']:
                        weapon_ult.add(r['name'])
                    if level and mods and level['v'] in (2, 3):
                        # Level 1 shows 1 per track, which can't be told from the ultimate's flag.
                        shown = {2: 3, 3: 5}[level['v']]
                        tracks = sum(1 for e in mods['children'] if e['v'] == shown)
                        if tracks:
                            weapon_tracks[r['name']] = max(tracks, weapon_tracks.get(r['name'], 0))

    # Met-file field hashes too.
    want.update({0x29D47E3B, 0x7980EC77, 0x86E71B0C, 0xCA512F1C, 0xEAB89C92, 0x9F393319, 0xC5E4AA3A, 0xD0C617D7})

    sources = [
        os.path.join(game, 'data', 'reflection', 'reflection.vpp_pc'),
        os.path.join(tweaks, 'tools', 'sr5.img'),
    ]
    seen = set()
    for src in sources:
        if not os.path.exists(src):
            print('missing', src)
            continue
        d = open(src, 'rb').read()
        for m in re.finditer(rb'[A-Za-z_][A-Za-z0-9_:<> .\-]{2,80}', d):
            s = m.group()
            if s in seen:
                continue
            seen.add(s)
            low = s.lower()
            for v in (low, low[2:] if low.startswith(b'm_') else low):
                h = fast_crc(v)
                if h in want and h not in resolved:
                    resolved[h] = s.decode('latin1') if v is low else low.decode('latin1')

    research = open(os.path.join(tweaks, 'research_hidden.md'), encoding='utf8').read()
    for section, stop in (('## Appendix C', '## Appendix D'), ('## Appendix D', '## Appendix E'), ('## Appendix F', '## Appendix G')):
        for line in research.split(section)[1].split(stop)[0].split('\n'):
            m = re.match(r'([0-9A-F]{8}) (\S+)', line)
            if m and int(m.group(1), 16) in want:
                resolved.setdefault(int(m.group(1), 16), m.group(2))

    # gml_weapon_info: what the weapon wheel's slots point at, a plain list of names.
    for token in research.split('## Appendix E')[1].split('## Appendix F')[0].split():
        if crc(token) in want:
            resolved.setdefault(crc(token), token)

    real_pairs = {}
    for path in files:
        for x in parse_blocks(open(path, 'rb').read())[0]:
            for r in x['records']:
                if r['struct']['hash'] != crc('vehicle_save_state'):
                    continue
                fields = r['struct']['fields']
                slots = {fields[c['f']]['hash']: c for c in r['children']}.get(crc('m_save_slots'))
                for s in (slots['children'] if slots else []):
                    sf = s['struct']['fields']
                    vals = {sf[c['f']]['hash']: c.get('v') for c in s['children'] if c['kind'] == 'val'}
                    variant, info = resolved.get(vals[crc('m_variant_info')]), resolved.get(vals[crc('m_vehicle_info')])
                    if variant and info:
                        real_pairs[variant] = info

    vehicles = []
    variant_rows = {}
    vfile = open(os.path.join(tweaks, 'SRTweakMenu', 'vehicles.lua'), encoding='utf8', errors='replace').read()
    for display, info, variants in re.findall(r'\{ "([^"]+)", "([^"]+)", \{((?:[^{}]|\{[^{}]*\})*)\}', vfile):
        pretty = display.split(' (')[0] if ' (' in display and not display.startswith('also named') else ''
        if crc(info) in want:
            resolved.setdefault(crc(info), info)
        for variant, how in re.findall(r'\{ "([^"]+)", "([^"]+)"', variants):
            if crc(variant) in want:
                resolved.setdefault(crc(variant), variant)
            variant_rows.setdefault(variant, []).append((how, info, pretty))
        if pretty:
            vehicles.append((info, pretty))
    # A model can be listed under several vehicles. What a real garage pairs it with wins, then the closest name.
    display_of = {}
    for rows in variant_rows.values():
        for how, info, pretty in rows:
            if pretty:
                display_of[info] = pretty
    def best_row(variant, rows):
        how = rows[0][0]
        if variant in real_pairs:
            info = real_pairs[variant]
        else:
            info = max(rows, key=lambda r: (len(os.path.commonprefix([variant, r[1]])), -rows.index(r)))[1]
        return how, info, display_of.get(info, '')
    variant_rows = {v: best_row(v, rows) for v, rows in variant_rows.items()}
    variant_rows['two_frontz_reward'] = ('custom', 'two_frontz', 'Two Frontz')

    for h, name in HAND_NAMED.items():
        resolved[h] = name

    weapons = []
    for line in research.split('## Appendix D')[1].split('## Appendix E')[0].split('\n'):
        m = re.match(r'([0-9A-F]{8}) (\S+) (.*)', line)
        if m:
            tag = m.group(3).strip()
            kind = 'shop' if tag.startswith('SHOP') else 'parent' if tag.startswith('PARENT') else 'internal'
            weapons.append((m.group(2), kind, weapon_tracks.get(m.group(2), 0), int(m.group(2) in weapon_ult)))

    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'names.txt'), 'w', encoding='utf8', newline='\n') as f:
        for h in sorted(resolved):
            if h in want and '\t' not in resolved[h]:
                f.write('%08x\t%s\n' % (h, resolved[h]))
    with open(os.path.join(OUT, 'weapons.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('# name\tkind (shop, internal or parent)\tupgrade tracks seen in saves (0 = unknown)\thas an ultimate\n')
        for row in sorted(weapons):
            f.write('%s\t%s\t%d\t%d\n' % row)
    with open(os.path.join(OUT, 'vehicles.txt'), 'w', encoding='utf8', newline='\n') as f:
        for info, pretty in sorted(set(vehicles)):
            f.write('%s\t%s\n' % (info, pretty))
    with open(os.path.join(OUT, 'vehicle_variants.txt'), 'w', encoding='utf8', newline='\n') as f:
        f.write('# variant\thow you normally meet it\tvehicle info\tdisplay name\n')
        for variant in sorted(variant_rows):
            f.write('%s\t%s\t%s\t%s\n' % ((variant,) + variant_rows[variant]))
    write_garage_options(files, resolved)
    print('hashes wanted', len(want), 'named', sum(1 for h in want if h in resolved))
    print('weapons', len(weapons), 'with track data', sum(1 for w in weapons if w[2]), 'with ultimates', sum(1 for w in weapons if w[3]))
    print('vehicles', len(set(vehicles)))


if __name__ == '__main__':
    if len(sys.argv) != 4:
        print(__doc__)
        sys.exit(2)
    main(sys.argv[1], sys.argv[2], sys.argv[3])
