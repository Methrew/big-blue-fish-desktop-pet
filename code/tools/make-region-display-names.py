# Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
"""Build region-display-names.json: the Traditional script names the location pickers show.

Usage: python3 tools/make-region-display-names.py [--review]   (Python 3.9+, no packages needed)
The region files keep their Simplified names, codes and order (china-regions.json, us-regions.json,
world-regions.json, world-countries.json); only the names shown change script. The conversion is
OpenCC 1.4.2 s2tw done by hand with the bundled dictionaries (assets/opencc): maximum-match
segmentation by STPhrases, STPhrases or else the first STCharacters candidate, then TWVariants.
TWPhrases is left out on purpose, so names change script only, never vocabulary (乍得 stays 乍得,
新泽西州 becomes 新澤西州). OVERRIDES holds the reviewed corrections. taiwan-regions.json is already
Traditional and is not listed.
Each entry keeps the source name next to the shown one: CompanionRegions only uses an entry while the
data file still has that name, so a region file updated without running this script shows the data's
own name rather than a stale one. --review prints every name in which a character that is also valid
Traditional changed, for checking against OVERRIDES.
"""
import json
import sys
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "BigBlueFish.Windows/assets"
regions, opencc = root / "regions", root / "opencc"


def table(name):
    result = {}
    for line in (opencc / name).read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("#"):
            continue
        key, values = line.split("\t", 1)
        result.setdefault(key, values.split(" "))
    return result


characters = table("STCharacters.txt")
phrases = table("STPhrases.txt")
for key, values in table("STPhrases_GeneratedFromRegionalPhrases.txt").items():
    phrases.setdefault(key, values)  # STPhrases wins on equal keys, as in OpenCC.
variant_phrases, variants = table("TWVariantsPhrases.txt"), table("TWVariants.txt")
longest = max(map(len, phrases))
longest_variant = max(map(len, variant_phrases))

# Reviewed by hand (--review lists the candidates). Keys are the Simplified names as the data has them;
# a value is what the picker shows. Each keeps a character that is Traditional too and that OpenCC
# changes, through a phrase entry or its first character candidate, although the place keeps it.
OVERRIDES = {
    "湖里区": "湖里區",        # 廈門市 Huli; OpenCC's phrase gives 湖裏 (裡)
    "巨鹿县": "巨鹿縣",        # 邢臺市 Julu; OpenCC's phrase gives 鉅鹿
    "巨野县": "巨野縣",        # 菏澤市 Juye; OpenCC's phrase gives 鉅野
    "石拐区": "石拐區",        # 包頭市 Shiguai; OpenCC's phrase gives 石柺
    "新干县": "新干縣",        # 吉安市 Xingan; OpenCC's phrase gives 新幹
    "凌河区": "凌河區",        # 錦州市 Linghe, as 凌海市 and 凌源市 nearby; OpenCC's phrase gives 淩河
    "云城区": "雲城區",        # 雲浮市 Yuncheng, as 雲浮市 and 雲安區; OpenCC's phrase keeps 云
    "南庄乡": "南庄鄉",        # 苗栗縣 Nanzhuang, the official 庄 (as in taiwan-regions.json); OpenCC gives 莊
    "岳西县": "岳西縣",        # 安慶市 Yuexi; 岳's first candidate 嶽 is no reviewed choice here
    "岳塘区": "岳塘區",        # 湘潭市 Yuetang; likewise
    "岳池县": "岳池縣",        # 廣安市 Yuechi; likewise
    "岳普湖县": "岳普湖縣",    # 喀什地區 Yopurga, a transliteration; likewise
    "马里": "馬里",            # Mali; 里's first candidate 裏 (TWVariants 裡)
}


def convert(text):
    # OpenCC segments once by STPhrases (unmatched characters gather into one segment), converts each
    # segment by STPhrases or STCharacters and then the segment by TWVariantsPhrases or TWVariants.
    segments, pending, i = [], "", 0
    while i < len(text):
        for size in range(min(longest, len(text) - i), 1, -1):
            piece = text[i:i + size]
            if piece in phrases:
                if pending:
                    segments.append(pending)
                    pending = ""
                segments.append(phrases[piece][0])
                i += size
                break
        else:
            pending += characters.get(text[i], [text[i]])[0]
            i += 1
    if pending:
        segments.append(pending)
    return "".join(variant(segment) for segment in segments)


def variant(text):
    out, i = [], 0
    while i < len(text):
        for size in range(min(longest_variant, len(text) - i), 1, -1):
            if text[i:i + size] in variant_phrases:
                out.append(variant_phrases[text[i:i + size]][0])
                i += size
                break
        else:
            out.append(variants.get(text[i], [text[i]])[0])
            i += 1
    return "".join(out)


def shown(name):
    if name in OVERRIDES:
        return OVERRIDES[name]
    # Country rows are "日本 Japan"; US states "加利福尼亚州 CA": only the Chinese part converts.
    return convert(name)


def read(name):
    return json.loads((regions / name).read_text(encoding="utf-8"))


rows = read("china-regions.json") + read("us-regions.json") + read("world-regions.json")
countries = read("world-countries.json")
result = {"regions": {}, "countries": {}}
review = []
for kind, items in (("regions", rows), ("countries", countries)):
    for item in items:
        source = item["Name"]
        name = shown(source)
        if name != source:
            result[kind][item["Code"]] = [source, name]
        plain = convert(source)
        # A character changed although it is not Simplified-only (it is Traditional too): the name needs a look.
        if len(source) != len(plain) or any(a != b and (a not in characters or a in characters[a]) for a, b in zip(source, plain)):
            review.append((item["Code"], source, plain, name))
unused = set(OVERRIDES) - {item["Name"] for item in rows + countries}
if unused:
    sys.exit("overrides for names no longer in the data: " + ", ".join(sorted(unused)))
if "--review" in sys.argv:
    for code, source, plain, name in review:
        print(f"{code}\t{source}\t{plain}\t{name}" + ("\tOVERRIDE" if name != plain else ""))
(regions / "region-display-names.json").write_text(
    json.dumps(result, ensure_ascii=False, separators=(",", ":"), sort_keys=True) + "\n", encoding="utf-8")
print(f"{len(result['regions'])} regions and {len(result['countries'])} countries shown in Traditional script; "
      f"{len(review)} names changed a character that is also Traditional")
