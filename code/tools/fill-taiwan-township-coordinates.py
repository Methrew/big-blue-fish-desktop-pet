#!/usr/bin/env python3
# Added by Methrew on 2026-11-09 for the taiwan-edition merge with Bai-Cute/big-blue-fish-desktop-pet 1.1.2 (Apache-2.0).
"""Fill township and county centres into taiwan-regions.json for the Open-Meteo fallback.

Usage (after import-taiwan-regions.py): python3 code/tools/fill-taiwan-township-coordinates.py <TOWN_MOI_yyymmdd.shp>

Source: 鄉(鎮、市、區)界線（TWD97 經緯度）by 內政部國土測繪中心, data.gov.tw dataset 7441,
Open Government Data License, version 1.0. TWD97 coordinates are used as WGS84 (they differ by
under a metre). Codes, names and order stay exactly as import-taiwan-regions.py wrote them; each
township gets the centre of its largest land part, and each county the mean of its townships.
Taiwan locations use these points only when no CWA authorization code is set.
"""
import hashlib, json, struct, sys
from pathlib import Path

def records(dbf: bytes):
    count, header, size = struct.unpack("<IHH", dbf[4:12])
    fields = [(dbf[o:o + 11].split(b"\0")[0].decode(), dbf[o + 16]) for o in range(32, header - 1, 32)]
    for index in range(count):
        row, position, values = dbf[header + index * size + 1: header + (index + 1) * size], 0, {}
        for name, width in fields:
            values[name] = row[position:position + width].decode("utf-8").strip(); position += width
        yield values

def polygons(shp: bytes):
    offset = 100
    while offset < len(shp):
        length = struct.unpack(">i", shp[offset + 4:offset + 8])[0] * 2
        content = shp[offset + 8:offset + 8 + length]; offset += 8 + length
        if struct.unpack("<i", content[:4])[0] != 5: raise ValueError("expected polygon records")
        parts, points = struct.unpack("<ii", content[36:44])
        starts = list(struct.unpack(f"<{parts}i", content[44:44 + 4 * parts])) + [points]
        base = 44 + 4 * parts
        coordinates = [struct.unpack("<2d", content[base + 16 * i: base + 16 * i + 16]) for i in range(points)]
        yield [coordinates[starts[i]:starts[i + 1]] for i in range(parts)]

def centre(ring):
    area = x = y = 0.0
    for (x0, y0), (x1, y1) in zip(ring, ring[1:] + ring[:1]):
        cross = x0 * y1 - x1 * y0
        area += cross; x += (x0 + x1) * cross; y += (y0 + y1) * cross
    return abs(area / 2), x / (3 * area), y / (3 * area)

def same(name: str) -> str:
    return name.replace("台", "臺")

def main(path: Path):
    shp, dbf = path.read_bytes(), path.with_suffix(".dbf").read_bytes()
    points = {}
    for values, rings in zip(records(dbf), polygons(shp)):
        _, longitude, latitude = max(centre(ring) for ring in rings)
        points[(same(values["COUNTYNAME"]), same(values["TOWNNAME"]))] = (round(latitude, 5), round(longitude, 5))
    regions = Path(__file__).resolve().parents[1] / "BigBlueFish.Windows/assets/regions"
    rows = json.loads((regions / "taiwan-regions.json").read_text(encoding="utf-8"))
    names = {row["Code"]: row["Name"] for row in rows}
    missing = []
    for row in rows:
        if row["Level"] == 2:
            point = points.get((same(names[row["ParentCode"]]), same(row["Name"])))
            if point is None: missing.append(names[row["ParentCode"]] + row["Name"]); continue
            row["Latitude"], row["Longitude"] = point
    if missing: raise SystemExit("no boundary for: " + "、".join(missing))
    for row in rows:
        if row["Level"] == 1:
            own = [r for r in rows if r["ParentCode"] == row["Code"]]
            row["Latitude"] = round(sum(r["Latitude"] for r in own) / len(own), 5)
            row["Longitude"] = round(sum(r["Longitude"] for r in own) / len(own), 5)
    (regions / "taiwan-regions.json").write_text(json.dumps(rows, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    source = dict(
        dataset="鄉(鎮、市、區)界線(TWD97經緯度)", provider="內政部國土測繪中心", catalog="https://data.gov.tw/dataset/7441",
        release=path.stem.rsplit("_", 1)[-1], license="政府資料開放授權條款－第1版 (Open Government Data License, version 1.0)",
        licenseUrl="https://data.gov.tw/license", townships=sum(r["Level"] == 2 for r in rows),
        inputSha256={p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in (path, path.with_suffix(".dbf"))},
        coordinateNote="Township centres of the largest land part; county centres average their townships. "
            "Used for Open-Meteo only when no CWA authorization code is set; not a street-level forecast.")
    (regions / "taiwan-source.json").write_text(json.dumps(source, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"{source['townships']} townships filled from {len(points)} boundaries")

if __name__ == "__main__":
    main(Path(sys.argv[1]))
