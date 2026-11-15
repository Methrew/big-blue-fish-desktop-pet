"""Check that shipped strings and user-facing documents are Taiwan Traditional Chinese.

Errors (fail the run): Simplified-only characters, i.e. the keys of OpenCC's STCharacters.txt whose candidates do
not include the key itself (plus 着, which only TWVariants converts), minus the Taiwan forms TWVariants writes.
Warnings (fail only with --strict): Mainland vocabulary written in Traditional script (設置, 默認, 窗口...), characters
TWVariants replaces in Taiwan (爲, 裏, 衆, 啓...), and characters or words that are valid Traditional but practically
always Simplified (后, 几, 之后, 哪里...).

C#, Swift and C files: only string and character literals count (comments do not). Other files: line by line, full-line
comments of scripts skipped. A literal part equal to an animation id or "<id>.webm" from animations.json is always
allowed (they are file names and MCP action names). tools/traditional-allowlist.txt lists the other legitimate
Simplified texts as "path<TAB>text<TAB>reason"; an entry allows the hits inside each occurrence of its text in a
literal of that path ("*" = any path). An --assembly scan reads the #US user-string heap of .NET assemblies, which
records no source files: there an entry applies when its path is "*", matches the assembly file name, or is a source
file the repository's project for that assembly compiles (by the project's AssemblyName and its Compile items).

Scope: what code/BigBlueFish.Mac/BigBlueFish.Mac.csproj compiles (links resolved) and the projects it references; the
.cs, .swift, .c, .xaml, .csproj and .props files of the Linux, Mac, Windows, Agent, Control, Diagnostics, Mcp and
HermesBridge projects, deployment/, installer/ and uninstaller/; the Agent skills; tools/ and code/ scripts; workflows;
README.md, code/README.md, docs/MACOS.md, MCP.md, ACP.md, CHARACTER_CARD.txt, release/README.md, code/THIRD-PARTY.md and
the top of CHANGELOG.md up to its first original (non "-tw") version heading. Never checked: OpenCC and region data,
licenses, docs/specs, docs/AGENT_FUNCTIONAL_SPEC.md, .github/CONTRIBUTING.md, test corpora, .editorconfig,
tools/Import-AnimationRules.py, lock files, bin/ and obj/.

Usage:
  python3 tools/Check-Traditional.py                      whole scope; exit 1 on errors
  python3 tools/Check-Traditional.py PATH...              only these files, or the in-scope files under these folders
  python3 tools/Check-Traditional.py --strict             warnings fail too
  python3 tools/Check-Traditional.py --scope mac          only what the macOS app compiles or ships
  python3 tools/Check-Traditional.py --include-tests      also report code/tests (corpora stay excluded)
  python3 tools/Check-Traditional.py --summary            per-file counts instead of every finding
  python3 tools/Check-Traditional.py --errors-only        print only errors (warnings still counted)
  python3 tools/Check-Traditional.py --json out.json      machine-readable results ("-" = standard output)
  python3 tools/Check-Traditional.py --list-files         print the resolved scope
  python3 tools/Check-Traditional.py --assembly A.dll...  scan built assemblies with the same rules
  python3 tools/Check-Traditional.py --self-test          lexer and assembly reader tests, then a v1.1.3 scan
Python 3.9 standard library only.
"""
import argparse
import fnmatch
import json
import re
import struct
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
OPENCC = "code/BigBlueFish.Windows/assets/opencc"
ANIMATIONS = "code/BigBlueFish.Windows/assets/fish/animations.json"
ALLOWLIST = REPO / "tools/traditional-allowlist.txt"
MAC_PROJECT = "code/BigBlueFish.Mac/BigBlueFish.Mac.csproj"
ORIGINAL_V113 = "75cd217"

# ---------------------------------------------------------------------------------------------------- scope

PRODUCT_DIRS = ["code/BigBlueFish.Linux", "code/BigBlueFish.Mac", "code/BigBlueFish.Windows", "code/BigBlueFish.Agent",
                "code/BigBlueFish.Control", "code/BigBlueFish.Diagnostics", "code/BigBlueFish.Mcp",
                "code/BigBlueFish.HermesBridge", "deployment", "installer", "uninstaller"]
PRODUCT_SUFFIXES = {".cs", ".swift", ".c", ".xaml", ".csproj", ".props"}
SCOPE_FILES = ["README.md", "code/README.md", "docs/MACOS.md", "docs/MCP.md", "docs/ACP.md", "docs/CHARACTER_CARD.txt",
               "release/README.md", "code/THIRD-PARTY.md", "CHANGELOG.md"]
SCOPE_GLOBS = ["code/BigBlueFish.Agent/Skills/*.md", "tools/*.sh", "tools/*.py", "tools/*.ps1", "code/*.ps1", "code/*.sh",
               "code/tools/*.ps1", ".github/workflows/*.yml"]
TEST_SUFFIXES = {".cs", ".swift", ".c", ".py", ".ps1", ".sh"}
# What the macOS package compiles or carries: the Mac project with its links and references, the two companion
# executables build-macos.sh publishes next to it, the native helpers, and the files copied into the bundle.
MAC_EXTRA_DIRS = ["code/BigBlueFish.Mcp", "code/BigBlueFish.HermesBridge"]
MAC_EXTRA_FILES = ["code/BigBlueFish.Linux/runner-guard.c", "tools/build-macos.sh", ".github/workflows/macos-package.yml",
                   "docs/MACOS.md", "code/THIRD-PARTY.md", "README.md"]
EXCLUDED = ["code/BigBlueFish.Windows/assets/opencc/*", "code/BigBlueFish.Windows/assets/regions/*.json",
            "code/licenses/*", "docs/specs/*", "docs/AGENT_FUNCTIONAL_SPEC.md", ".github/CONTRIBUTING.md", "LICENSE",
            "*/LICENSE", "*/corpora/*", "*.editorconfig", "tools/Import-AnimationRules.py", "*.lock.json",
            "tools/Check-Traditional.py", "tools/traditional-allowlist.txt", "*/bin/*", "*/obj/*", ".work/*", ".git/*"]


def excluded(rel):
    return any(fnmatch.fnmatchcase(rel, pattern) for pattern in EXCLUDED)


def project_sources(root, project):
    """The .cs and other files a project compiles: default items of its folder plus every (linked) Compile Include."""
    path = root / project
    if not path.is_file():
        return set(), []
    tree = ET.parse(path)
    folder = path.parent
    found, references = set(), []
    defaults = all((e.text or "").strip().lower() != "false" for e in tree.iter("EnableDefaultCompileItems"))
    if defaults:
        found |= {p for p in folder.rglob("*.cs")}
    for element in tree.iter("Compile"):
        for include in (element.get("Include") or "").split(";"):
            include = include.strip().replace("\\", "/")
            if not include:
                continue
            found |= set(folder.glob(include)) if any(c in include for c in "*?[") else {(folder / include)}
    for element in tree.iter("ProjectReference"):
        include = (element.get("Include") or "").replace("\\", "/")
        if include:
            references.append((folder / include).resolve())
    return {p.resolve() for p in found if p.is_file()}, references


def assembly_projects(root):
    """Assembly name -> repository paths of the sources its project compiles, for every project in the tree."""
    projects = {}
    found = [p for folder in ("code", "deployment", "installer", "uninstaller") for p in (root / folder).rglob("*.csproj")]
    for project in sorted(p for p in found if not excluded(relative(root, p)) and not relative(root, p).startswith("code/tests/")):
        name = next((e.text.strip() for e in ET.parse(project).iter("AssemblyName") if e.text and "$" not in e.text), project.stem)
        compiled, _ = project_sources(root, relative(root, project))
        projects.setdefault(name, set()).update(relative(root, p) for p in compiled)
    return projects


def relative(root, path):
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def scope_files(root, scope="all", include_tests=False):
    root = root.resolve()
    files = set()
    mac, references = project_sources(root, MAC_PROJECT)
    files |= mac
    for project in references:
        compiled, _ = project_sources(root, relative(root, project))
        files |= compiled
    files |= {p.resolve() for p in (root / "code/BigBlueFish.Mac").glob("*.swift")}
    if scope == "mac":
        for folder in MAC_EXTRA_DIRS:
            files |= {p.resolve() for p in (root / folder).rglob("*.cs")}
        files |= {(root / f).resolve() for f in MAC_EXTRA_FILES if (root / f).is_file()}
        files |= {p.resolve() for p in (root / "code/BigBlueFish.Agent/Skills").glob("*.md")}
    else:
        for folder in PRODUCT_DIRS:
            files |= {p.resolve() for p in (root / folder).rglob("*") if p.suffix in PRODUCT_SUFFIXES and p.is_file()}
        files |= {(root / f).resolve() for f in SCOPE_FILES if (root / f).is_file()}
        for pattern in SCOPE_GLOBS:
            files |= {p.resolve() for p in root.glob(pattern) if p.is_file()}
    if include_tests:
        files |= {p.resolve() for p in (root / "code/tests").rglob("*") if p.suffix in TEST_SUFFIXES and p.is_file()}
    return sorted((p for p in files if not excluded(relative(root, p))), key=lambda p: relative(root, p))


# ---------------------------------------------------------------------------------------------------- rules

def load_dictionary(path):
    table = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        if line and not line.startswith("#") and "\t" in line:
            key, values = line.split("\t", 1)
            table[key] = values.split(" ")
    return table


# Mainland wording in Traditional script -> Taiwan wording, then legitimate Taiwan phrases containing it.
VOCABULARY = """
設置 設定
默認 預設
缺省 預設
窗口 視窗
窗體 視窗
屏幕 螢幕
全屏 全螢幕
截屏 螢幕截圖
錄屏 螢幕錄影
讀屏 讀取螢幕
鼠標 滑鼠
光標 游標
用戶 使用者
當前 目前
本地 本機 | 本地時間 本地日期 本地時區
接口 介面
界面 介面
文件夾 資料夾
文件名 檔名
字段 欄位
點擊 點選
拖動 拖曳
拖拽 拖曳
圖標 圖示
調用 呼叫
回調 回呼
運行 執行
內存 記憶體
顯存 顯示記憶體
內置 內建
保存 儲存 | 保存期限
存儲 儲存
數據 資料 | 數據機
日誌 記錄
前臺 前景
前台 前景
後臺 背景
後台 背景
托盤 系統匣（Windows）／選單列（macOS）
軟件 軟體
硬件 硬體
固件 韌體
網絡 網路
互聯網 網際網路
聯網 連網 | 物聯網
在線 線上
信息 資訊／訊息
視頻 影片
音頻 音訊
加載 載入
獲取 取得
菜單 選單
支持 支援 | 支持你 支持妳 支持主人 支持者
創建 建立
搜索 搜尋
斷開 中斷連線
超時 逾時
通過 透過 | 未通過 不通過 沒通過 沒有通過 通過驗證 驗證通過 通過測試 測試通過 通過檢查 檢查通過 審核通過 通過審核
推理 推論
區域性 局部（OpenCC 把「局部多雲」誤轉成「區域性多雲」）
釋出 發布
吐字 說話
程序 程式 | 處理程序 作業程序 標準程序 法律程序
進程 處理程序
線程 執行緒
激活 啟用
兼容 相容
剪貼板 剪貼簿
粘貼 貼上
剪切 剪下
服務器 伺服器
遠程 遠端
卸載 解除安裝
識別 辨識 | 識別碼 識別字 身分識別 識別證
調度 排程
對象 物件 | 適用對象 交往對象 聊天對象 說話對象 傾訴對象
字符串 字串
字符 字元
源代碼 原始碼
源碼 原始碼
代碼 程式碼 | 結束代碼 鄉鎮代碼 錯誤代碼 區縣代碼 縣市代碼 國家代碼 地區代碼 行政區代碼 城市代碼 語言代碼 FIPS代碼 郡代碼 位代碼
項目 專案 | 清單項目 選單項目 登入項目 項目符號 檢查項目 設定項目 個項目 些項目 各項目
連接 連線／連結 | 連接埠
鏈接 連結
發送 傳送
截取 擷取
組件 元件
控件 控制項
查看 檢視
壁紙 桌布（Windows）／背景圖片（macOS）
快捷方式 捷徑
安裝包 安裝程式
概率 機率
顯卡 顯示卡
離電 拔掉電源
筆記本 筆電
交互 互動
退出碼 結束代碼
退出 結束／離開 | 退出映像 退出磁碟 退出群組
隊列 佇列
坐標 座標
枚舉 列舉
顯式 明確
緩存 快取
命令行 命令列
智能 智慧
芯片 晶片
硬盤 硬碟
磁盤 磁碟
打印 列印
響應 回應
異步 非同步
變量 變數
模塊 模組
插件 外掛
質量 品質
實時 即時
帶寬 頻寬
端口 連接埠
刷新 重新整理
調試 偵錯
分辨率 解析度
二維碼 QR 碼
短信 簡訊
郵箱 信箱
攝像頭 攝影機
觸摸 觸控
賬 帳
登錄 登入 | 登錄檔 登錄機碼 登錄編輯
彈窗 彈出視窗
標籤頁 分頁
主頁 首頁
國內來源 中國大陸鏡像（ModelScope）
文本 文字
"""
# Taiwan phrases that are fine whatever term they contain.
GLOBAL_EXCEPTIONS = ["結束代碼", "鄉鎮代碼", "清單項目", "選單項目", "登入項目", "驗證未通過", "本地時間", "用戶端", "連接埠",
                     "識別碼", "識別字", "身分識別", "登錄檔", "數據機", "處理程序", "終端機"]
# Valid Traditional characters that are practically only Simplified (CompanionTraditional.PracticallySimplified),
# and Simplified words made of characters both scripts share, so the character check cannot see them.
AMBIGUOUS_CHARACTERS = "亘挂极蚝蝎万厂据虫种胜柜叶适党价蜡愿广帘确几云丰后腊淀苹"
AMBIGUOUS_EXCEPTIONS = ["茶几", "拮据", "胜肽", "皇后", "王后", "太后", "后羿", "后土", "人云亦云"]
AMBIGUOUS_WORDS = """
之后 之後
以后 以後
然后 然後
最后 最後
后面 後面
前后 前後
背后 背後
后台 後台
哪里 哪裡
那里 那裡
這里 這裡
里面 裡面
心里 心裡
家里 家裡
夜里 夜裡
手里 手裡
屋里 屋裡
干杯 乾杯
干嘛 幹嘛
干活 幹活
干啥 幹啥
面包 麵包
吃面 吃麵
放松 放鬆
打斗 打鬥
日志 日誌（台灣用「記錄」）
其余 其餘
云端 雲端
叶子 葉子
制作 製作
谷歌 Google
"""
# Characters TWVariants does not list but Taiwan never writes (Hong Kong or old forms).
EXTRA_VARIANTS = {"綫": "線", "衞": "衛", "敎": "教", "眞": "真"}
# Taiwan standard forms that STCharacters still maps to an older form (床->牀, 群->羣); 么 stays an error.
TAIWAN_FORMS_KEPT = set("峰床灶痴秘粽群")


class Hit:
    __slots__ = ("start", "end", "level", "kind", "text", "suggestion")

    def __init__(self, start, end, level, kind, text, suggestion):
        self.start, self.end, self.level, self.kind, self.text, self.suggestion = start, end, level, kind, text, suggestion


def parse_terms(table):
    terms = {}
    for line in table.strip().splitlines():
        head, _, tail = line.partition("|")
        term, _, suggestion = head.strip().partition(" ")
        terms[term] = (suggestion.strip(), tail.split())
    return terms


class Rules:
    def __init__(self, dictionaries, animations):
        st = load_dictionary(dictionaries / "STCharacters.txt")
        tw = load_dictionary(dictionaries / "TWVariants.txt")
        variants = {key: values[0] for key, values in tw.items() if key not in values}
        self.simplified = {key: values[0] for key, values in st.items() if key not in values and key not in TAIWAN_FORMS_KEPT}
        self.simplified["着"] = "著"
        for key, value in self.simplified.items():
            self.simplified[key] = variants.get(value, value)
        self.variants = {key: value for key, value in variants.items() if key not in self.simplified and key != "幺"}
        self.variants.update(EXTRA_VARIANTS)
        terms = {term: ("vocabulary", suggestion, exceptions) for term, (suggestion, exceptions) in parse_terms(VOCABULARY).items()}
        for term, (suggestion, _) in parse_terms(AMBIGUOUS_WORDS).items():
            terms[term] = ("ambiguous", suggestion, [])
        for character in AMBIGUOUS_CHARACTERS:
            terms[character] = ("ambiguous", st.get(character, ["?"])[0] + "？", AMBIGUOUS_EXCEPTIONS)
        self.terms = {}
        for term, (kind, suggestion, exceptions) in terms.items():
            contexts = []
            for phrase in exceptions + GLOBAL_EXCEPTIONS:
                offset = phrase.find(term)
                while offset >= 0:
                    contexts.append((phrase, offset))
                    offset = phrase.find(term, offset + 1)
            self.terms[term] = (kind, suggestion, contexts)
        self.term_pattern = re.compile("(?=(" + "|".join(map(re.escape, sorted(self.terms, key=len, reverse=True))) + "))")
        self.ids = []
        if animations and animations.is_file():
            for item in json.loads(animations.read_text(encoding="utf-8-sig")):
                self.ids += [item["Id"], item["Id"] + ".webm", item.get("File") or item["Id"] + ".webm"]
        self.ids = sorted(set(self.ids), key=len, reverse=True)

    def hits(self, text, mask):
        found = []
        for index, character in enumerate(text):
            if not mask[index]:
                continue
            if character in self.simplified:
                found.append(Hit(index, index + 1, "error", "simplified", character, self.simplified[character]))
            elif character in self.variants:
                found.append(Hit(index, index + 1, "warning", "variant", character, self.variants[character]))
        for match in self.term_pattern.finditer(text):
            term, start = match.group(1), match.start()
            end = start + len(term)
            if not all(mask[start:end]):
                continue
            kind, suggestion, contexts = self.terms[term]
            if any(text[start - offset:start - offset + len(phrase)] == phrase for phrase, offset in contexts if start >= offset):
                continue
            found.append(Hit(start, end, "warning", kind, term, suggestion))
        # A term inside a longer one at another position (代碼 in 源代碼, 后 in 之后) is reported once, as the longer one.
        return [h for h in found if not any(o is not h and o.start <= h.start and h.end <= o.end and o.end - o.start > h.end - h.start
                                            for o in found)]


CJK = re.compile("[\u2e80-\u9fff\uf900-\ufaff\U00020000-\U0003ffff]")


class Allowlist:
    def __init__(self, path):
        self.entries = []
        self.path = path
        if path is None:
            return
        for number, line in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            fields = line.split("\t")
            if len(fields) != 3 or not all(f.strip() for f in fields):
                raise SystemExit("%s:%d: expected path<TAB>text<TAB>reason" % (path, number))
            needle = fields[1].replace("\\n", "\n").replace("\\t", "\t")
            self.entries.append({"line": number, "path": fields[0].strip(), "text": needle, "reason": fields[2].strip(), "used": 0})

    def applicable(self, rel, sources=()):
        """Entries for a source file, or (rel = an assembly file name) for an assembly compiled from these sources."""
        return [e for e in self.entries if e["path"] == "*" or fnmatch.fnmatchcase(rel, e["path"])
                or any(fnmatch.fnmatchcase(source, e["path"]) for source in sources)]


# ---------------------------------------------------------------------------------------------------- lexers

class Literal:
    __slots__ = ("start", "text", "mask", "positions", "kind")

    def __init__(self, start, kind):
        self.start, self.kind = start, kind
        self.text, self.mask, self.positions = [], [], []

    def add(self, characters, position, checked=True):
        for character in characters:
            self.text.append(character)
            self.mask.append(checked)
            self.positions.append(position)

    def done(self):
        self.text = "".join(self.text)
        return self


SIMPLE_ESCAPES = {"'": "'", '"': '"', "\\": "\\", "0": "\0", "a": "\a", "b": "\b", "f": "\f", "n": "\n", "r": "\r",
                  "t": "\t", "v": "\v", "e": "\x1b"}


def decode_escape(src, i, language):
    """src[i] is the backslash. Returns (decoded text, index after the escape)."""
    n = len(src)
    if i + 1 >= n:
        return "\\", i + 1
    c = src[i + 1]
    if language == "swift" and c == "u" and i + 2 < n and src[i + 2] == "{":
        end = src.find("}", i + 3)
        if end > 0 and re.fullmatch("[0-9a-fA-F]{1,8}", src[i + 3:end]):
            return chr(int(src[i + 3:end], 16)), end + 1
    if c in "uU" and language != "swift":
        width = 4 if c == "u" else 8
        digits = src[i + 2:i + 2 + width]
        if re.fullmatch("[0-9a-fA-F]{%d}" % width, digits):
            value = int(digits, 16)
            return (chr(value) if value <= 0x10FFFF else "?"), i + 2 + width
    if c == "x" and language != "swift":
        match = re.compile("[0-9a-fA-F]{1,%d}" % (4 if language == "cs" else 2)).match(src, i + 2)
        if match:
            return chr(int(match.group(), 16)), match.end()
    return SIMPLE_ESCAPES.get(c, c), i + 2


class Lexer:
    """String and character literals of C# (regular, verbatim, interpolated with nested holes, raw), Swift and C."""

    def __init__(self, src, language):
        self.src, self.language, self.literals = src, language, []

    def run(self):
        src, n, i, line_start = self.src, len(self.src), 0, True
        while i < n:
            c = src[i]
            if c == "\n":
                line_start = True
                i += 1
                continue
            if c in " \t\r\f\v\ufeff":
                i += 1
                continue
            if c == "#" and line_start and self.language in ("cs", "c"):
                directive = re.compile(r"#\s*(\w*)").match(src, i).group(1)
                if self.language == "cs" or directive != "define":
                    i = self.line_end(i)
                    continue
            line_start = False
            if src.startswith("//", i):
                i = self.line_end(i)
            elif src.startswith("/*", i):
                i = self.block_comment(i)
            else:
                i = self.token(i)
        return self.literals

    def line_end(self, i):
        end = self.src.find("\n", i)
        return len(self.src) if end < 0 else end

    def block_comment(self, i):
        src, n, depth, i = self.src, len(self.src), 1, i + 2
        while i < n and depth:
            if src.startswith("*/", i):
                depth -= 1
                i += 2
            elif self.language == "swift" and src.startswith("/*", i):
                depth += 1
                i += 2
            else:
                i += 1
        return i

    def token(self, i):
        """Lexes the literal starting at i, or skips one character; returns the next index."""
        src, c = self.src, self.src[i]
        if self.language == "swift":
            if c == '"':
                return self.swift_string(i, 0)
            if c == "#":
                j = i
                while j < len(src) and src[j] == "#":
                    j += 1
                if j < len(src) and src[j] == '"':
                    return self.swift_string(j, j - i)
            return i + 1
        if c == "'":
            return self.char_literal(i)
        if c == '"' and self.language == "c":
            literal = Literal(i, "string")
            end = self.regular(i, literal, 0)
            self.literals.append(literal.done())
            return end
        if self.language == "cs" and c in '"@$':
            end = self.cs_string(i)
            if end is not None:
                return end
        if c.isalnum() or c == "_":
            while i < len(src) and (src[i].isalnum() or src[i] == "_"):
                i += 1
            return i
        return i + 1

    def char_literal(self, i):
        src, n = self.src, len(self.src)
        literal = Literal(i, "char")
        j = i + 1
        if j < n and src[j] == "\\":
            text, j = decode_escape(src, j, self.language)
        elif j < n and src[j] not in "'\n":
            text, j = src[j], j + 1
        else:
            return i + 1
        if j < n and src[j] == "'":
            literal.add(text, i + 1)
            self.literals.append(literal.done())
            return j + 1
        return i + 1

    def cs_string(self, i):
        src, n = self.src, len(self.src)
        j, dollars, verbatim = i, 0, False
        if j < n and src[j] == "@":
            verbatim, j = True, j + 1
            while j < n and src[j] == "$":
                dollars, j = dollars + 1, j + 1
        else:
            while j < n and src[j] == "$":
                dollars, j = dollars + 1, j + 1
            if j < n and src[j] == "@":
                verbatim, j = True, j + 1
        if j >= n or src[j] != '"':
            return None
        quotes = 0
        while j + quotes < n and src[j + quotes] == '"':
            quotes += 1
        literal = Literal(i, "string")
        if verbatim:
            end = self.verbatim(j + 1, literal, dollars)
        elif quotes >= 3:
            end = self.raw(j + quotes, literal, dollars, quotes)
        elif quotes == 2:
            end = j + 2
        else:
            end = self.regular(j, literal, dollars)
        self.literals.append(literal.done())
        return end + 2 if src.startswith("u8", end) else end

    def regular(self, j, literal, dollars):
        """Regular (or $-interpolated) string; src[j] is the opening quote."""
        src, n, k = self.src, len(self.src), j + 1
        while k < n:
            c = src[k]
            if c == '"':
                return k + 1
            if c == "\n":
                return k
            if c == "\\":
                text, after = decode_escape(src, k, self.language)
                literal.add(text, k)
                k = after
            elif dollars and c == "{" and src.startswith("{{", k):
                literal.add("{", k)
                k += 2
            elif dollars and c == "}" and src.startswith("}}", k):
                literal.add("}", k)
                k += 2
            elif dollars and c == "{":
                k = self.hole(k, literal, 1)
            else:
                literal.add(c, k)
                k += 1
        return k

    def verbatim(self, k, literal, dollars):
        src, n = self.src, len(self.src)
        while k < n:
            c = src[k]
            if c == '"':
                if src.startswith('""', k):
                    literal.add('"', k)
                    k += 2
                    continue
                return k + 1
            if dollars and (src.startswith("{{", k) or src.startswith("}}", k)):
                literal.add(c, k)
                k += 2
            elif dollars and c == "{":
                k = self.hole(k, literal, 1)
            else:
                literal.add(c, k)
                k += 1
        return k

    def raw(self, k, literal, dollars, quotes):
        src, n, close = self.src, len(self.src), '"' * quotes
        while k < n:
            if src.startswith(close, k):
                return k + quotes
            c = src[k]
            if dollars and c == "{":
                run = 0
                while k + run < n and src[k + run] == "{":
                    run += 1
                if run < dollars:
                    literal.add("{" * run, k)
                    k += run
                    continue
                literal.add("{" * (run - dollars), k)
                k = self.hole(k + run - dollars, literal, dollars)
            else:
                literal.add(c, k)
                k += 1
        return k

    def hole(self, k, literal, braces):
        """Interpolation hole starting at src[k] ({ or a run of braces); nested literals become literals of their own."""
        src, n = self.src, len(self.src)
        start, k, depth = k, k + braces, 0
        while k < n:
            c = src[k]
            if c in "([{":
                depth += 1
                k += 1
            elif c in ")]":
                depth -= 1
                k += 1
            elif c == "}":
                if depth == 0:
                    break
                depth -= 1
                k += 1
            elif c == ":" and depth == 0 and not src.startswith("::", k) and src[k - 1] != ":":
                literal.add(src[start:k], start, False)
                start = k
                while k < n and src[k] != "}" and src[k] != "\n":
                    k += 1
                literal.add(src[start:k], start)
                start = k
                break
            elif c == "'" or c == '"' or (c in "@$" and re.compile(r'[@$]+"').match(src, k)):
                k = self.token(k)
            elif src.startswith("//", k):
                k = self.line_end(k)
            elif src.startswith("/*", k):
                k = self.block_comment(k)
            else:
                k += 1
        literal.add(src[start:k] + "}" * braces, start, False)
        return min(n, k + braces)

    def swift_string(self, j, hashes):
        """Swift "...", \"\"\"...\"\"\" and #"..."# strings with \\( ) interpolation; src[j] is the first quote."""
        src, n = self.src, len(self.src)
        literal = Literal(j - hashes, "string")
        multi = src.startswith('"""', j)
        k = j + (3 if multi else 1)
        close = ('"""' if multi else '"') + "#" * hashes
        escape = "\\" + "#" * hashes
        while k < n:
            if src.startswith(close, k):
                k += len(close)
                break
            c = src[k]
            if c == "\n" and not multi:
                break
            if src.startswith(escape + "(", k):
                start, k, depth = k, k + len(escape) + 1, 1
                while k < n and depth:
                    if src[k] == '"':
                        k = self.token(k)
                        continue
                    depth += {"(": 1, ")": -1}.get(src[k], 0)
                    k += 1
                literal.add(src[start:k], start, False)
            elif src.startswith(escape, k):
                # In a #"..."# string only \# starts an escape; decode what follows it as after a plain backslash.
                text, used = decode_escape("\\" + src[k + len(escape):k + len(escape) + 12], 0, "swift")
                literal.add(text, k)
                k += len(escape) + used - 1
            else:
                literal.add(c, k)
                k += 1
        self.literals.append(literal.done())
        return k


LANGUAGES = {".cs": "cs", ".swift": "swift", ".c": "c", ".h": "c"}
HASH_COMMENTS = {".py", ".sh", ".ps1", ".psm1", ".yml", ".yaml"}
XML_SUFFIXES = {".csproj", ".props", ".targets", ".xaml", ".xml", ".manifest"}


def line_literals(rel, src, suffix):
    """Every line is one literal; full-line script comments, PowerShell <# #> blocks and XML comments are skipped."""
    literals, offset, in_block = [], 0, False
    lines = src.split("\n")
    if rel == "CHANGELOG.md":
        # Only the top section (the Taiwan edition's own entries): history from the first original version heading on stays.
        for index, line in enumerate(lines):
            match = re.match(r"##\s+(\S+)", line)
            if match and "-tw" not in match.group(1):
                lines = lines[:index]
                break
    for line in lines:
        start = offset
        offset += len(line) + 1
        text, mask = line, [True] * len(line)
        stripped = line.strip()
        if suffix == ".ps1" and (in_block or stripped.startswith("<#")):
            in_block = "#>" not in stripped
            continue
        if suffix in HASH_COMMENTS and stripped.startswith("#"):
            continue
        if suffix in XML_SUFFIXES:
            mask = list(mask)
            k = 0
            while k < len(line):
                if in_block:
                    end = line.find("-->", k)
                    stop = len(line) if end < 0 else end + 3
                    for m in range(k, stop):
                        mask[m] = False
                    in_block, k = end < 0, stop
                else:
                    begin = line.find("<!--", k)
                    if begin < 0:
                        break
                    in_block, k = True, begin
        if not CJK.search(text):
            continue
        literal = Literal(start, "line")
        literal.text, literal.mask, literal.positions = text, mask, list(range(start, start + len(text)))
        literals.append(literal)
    return literals


def file_literals(rel, src, suffix):
    if suffix in LANGUAGES:
        return [lit for lit in Lexer(src, LANGUAGES[suffix]).run() if CJK.search(lit.text)]
    return line_literals(rel, src, suffix)


# ---------------------------------------------------------------------------------------------------- scanning

class Scan:
    def __init__(self, rules, allowlist):
        self.rules, self.allowlist = rules, allowlist
        self.findings, self.files = [], {}
        self.suppressed = {"animation-id": 0, "allowlist": 0}

    def literal(self, rel, literal, location, sources=()):
        hits = self.rules.hits(literal.text, literal.mask)
        if not hits:
            return
        text = literal.text
        spans = []
        for name in self.rules.ids:
            k = text.find(name)
            while k >= 0:
                spans.append((k, k + len(name), None))
                k = text.find(name, k + 1)
        for entry in self.allowlist.applicable(rel, sources):
            k = text.find(entry["text"])
            while k >= 0:
                spans.append((k, k + len(entry["text"]), entry))
                k = text.find(entry["text"], k + 1)
        kept = []
        for hit in hits:
            cover = next((s for s in spans if s[0] <= hit.start and hit.end <= s[1]), None)
            if cover is None:
                kept.append(hit)
            elif cover[2] is None:
                self.suppressed["animation-id"] += 1
            else:
                cover[2]["used"] += 1
                self.suppressed["allowlist"] += 1
        counts = self.files.setdefault(rel, {"errors": 0, "warnings": 0})
        for level in ("error", "warning"):
            chosen = [h for h in kept if h.level == level]
            if not chosen:
                continue
            counts[level + "s"] += 1
            line, column = location(literal.positions[chosen[0].start] if literal.positions else literal.start)
            self.findings.append({"path": rel, "line": line, "column": column, "level": level, "literal": literal.kind,
                                  "hits": [{"kind": h.kind, "text": h.text, "suggestion": h.suggestion, "offset": h.start} for h in chosen],
                                  "text": text if len(text) <= 600 else text[:600] + "…"})

    def source(self, root, path):
        rel = relative(root, path)
        try:
            src = path.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            src = path.read_bytes().decode("utf-8", errors="replace")
        self.files.setdefault(rel, {"errors": 0, "warnings": 0})
        starts = [0] + [m.end() for m in re.finditer("\n", src)]

        def location(offset):
            low, high = 0, len(starts) - 1
            while low < high:
                mid = (low + high + 1) // 2
                if starts[mid] <= offset:
                    low = mid
                else:
                    high = mid - 1
            return low + 1, offset - starts[low] + 1

        for literal in file_literals(rel, src, path.suffix.lower()):
            self.literal(rel, literal, location)

    def assembly(self, path, projects):
        rel = path.name
        self.files.setdefault(rel, {"errors": 0, "warnings": 0})
        sources = projects.get(path.stem)
        if sources is None:
            print("note: no project in this repository builds %s; only allowlist entries for * or %s apply" % (rel, rel), file=sys.stderr)
        for offset, text in user_strings(path.read_bytes()):
            if CJK.search(text):
                literal = Literal(offset, "#US")
                literal.text, literal.mask, literal.positions = text, [True] * len(text), []
                self.literal(rel, literal, lambda _o, at=offset: (0, at), sources or ())

    def totals(self):
        errors = sum(1 for f in self.findings if f["level"] == "error")
        return errors, len(self.findings) - errors


# ---------------------------------------------------------------------------------------------------- assemblies

def compressed_length(data, i):
    b = data[i]
    if b & 0x80 == 0:
        return b, i + 1
    if b & 0xC0 == 0x80:
        return ((b & 0x3F) << 8) | data[i + 1], i + 2
    if b & 0xE0 == 0xC0:
        return ((b & 0x1F) << 24) | (data[i + 1] << 16) | (data[i + 2] << 8) | data[i + 3], i + 4
    raise ValueError("bad compressed length at %d" % i)


def user_strings(image):
    """(heap offset, text) for every entry of the #US heap of a PE/CLI image (ECMA-335 II.24)."""
    if image[:2] != b"MZ":
        raise ValueError("not a PE image")
    pe = struct.unpack_from("<I", image, 0x3C)[0]
    if image[pe:pe + 4] != b"PE\0\0":
        raise ValueError("no PE signature")
    sections, optional_size = struct.unpack_from("<H12xH", image, pe + 6)
    optional = pe + 24
    magic = struct.unpack_from("<H", image, optional)[0]
    directories = optional + (96 if magic == 0x10B else 112)
    count = struct.unpack_from("<I", image, directories - 4)[0]
    if count <= 14:
        raise ValueError("no CLI header directory")
    cli_rva = struct.unpack_from("<I", image, directories + 14 * 8)[0]
    if not cli_rva:
        raise ValueError("not a .NET assembly")
    table = optional + optional_size
    spans = [struct.unpack_from("<8xIIII", image, table + 40 * s) for s in range(sections)]

    def offset(rva):
        for size, address, raw_size, raw in spans:
            if address <= rva < address + max(size, raw_size):
                return raw + rva - address
        raise ValueError("RVA %#x outside every section" % rva)

    metadata = offset(struct.unpack_from("<I", image, offset(cli_rva) + 8)[0])
    if image[metadata:metadata + 4] != b"BSJB":
        raise ValueError("no metadata root")
    version_length = struct.unpack_from("<I", image, metadata + 12)[0]
    k = metadata + 16 + version_length
    streams = struct.unpack_from("<H", image, k + 2)[0]
    k += 4
    heap = None
    for _ in range(streams):
        stream_offset, size = struct.unpack_from("<II", image, k)
        name_end = image.index(b"\0", k + 8)
        name = image[k + 8:name_end].decode("ascii")
        k = k + 8 + ((name_end - (k + 8)) // 4 + 1) * 4
        if name == "#US":
            heap = image[metadata + stream_offset:metadata + stream_offset + size]
    found = []
    if heap is None:
        return found
    i = 1
    while i < len(heap):
        start = i
        length, i = compressed_length(heap, i)
        if length == 0:
            continue
        data = heap[i:i + length]
        i += length
        found.append((start, data[:length - 1 if length % 2 else length].decode("utf-16-le", errors="replace")))
    return found


# ---------------------------------------------------------------------------------------------------- reporting

def excerpt(text, offset, width=70):
    begin = max(0, offset - width // 3)
    part = text[begin:begin + width].replace("\n", "\\n").replace("\t", " ")
    return ("…" if begin else "") + part + ("…" if begin + width < len(text) else "")


def describe(hit):
    return hit["text"] + "→" + hit["suggestion"] if hit["suggestion"] else hit["text"]


def report(scan, args, unused):
    errors, warnings = scan.totals()
    if args.json != "-":
        if not args.summary:
            for f in scan.findings:
                if args.errors_only and f["level"] != "error":
                    continue
                kinds = {}
                for hit in f["hits"]:
                    kinds.setdefault(hit["kind"], []).append(describe(hit))
                where = "%s:%d:%d" % (f["path"], f["line"], f["column"]) if f["line"] else "%s:#US+%d" % (f["path"], f["column"])
                detail = "; ".join("%s %s" % (k, " ".join(dict.fromkeys(v))) for k, v in kinds.items())
                print("%s: %s: %s | %s" % (where, f["level"], detail, excerpt(f["text"], f["hits"][0]["offset"])))
        listed = sorted((rel, c) for rel, c in scan.files.items() if c["errors"] or c["warnings"])
        if listed:
            print("\nerrors warnings  file")
            for rel, counts in listed:
                print("%6d %8d  %s" % (counts["errors"], counts["warnings"], rel))
        for entry in unused:
            print("note: unused allowlist entry, line %d: %s\t%s" % (entry["line"], entry["path"], entry["text"]))
        print("\n%d files, %d errors, %d warnings (one per literal or line and level); suppressed hits: %d animation ids, %d allowlist"
              % (len(scan.files), errors, warnings, scan.suppressed["animation-id"], scan.suppressed["allowlist"]))
    if args.json:
        result = {"errors": errors, "warnings": warnings, "suppressed": scan.suppressed,
                  "files": [{"path": rel, **counts} for rel, counts in sorted(scan.files.items())],
                  "findings": scan.findings,
                  "unusedAllowlist": [{k: e[k] for k in ("line", "path", "text", "reason")} for e in unused]}
        text = json.dumps(result, ensure_ascii=False, indent=1)
        if args.json == "-":
            print(text)
        else:
            Path(args.json).write_text(text + "\n", encoding="utf-8")
    failed = errors or (args.strict and warnings)
    return 1 if failed else 0


def resolve_paths(root, args):
    in_scope = scope_files(root, args.scope, args.include_tests)
    if not args.paths:
        return in_scope
    chosen = set()
    for name in args.paths:
        path = Path(name)
        path = (path if path.is_absolute() else Path.cwd() / path).resolve()
        if path.is_dir():
            chosen |= {p for p in in_scope if path == p or path in p.parents}
            if not args.include_tests and (root / "code/tests").resolve() in [path, *path.parents]:
                chosen |= {p for p in scope_files(root, args.scope, True) if path in p.parents}
        elif path.is_file():
            if not excluded(relative(root, path)):
                chosen.add(path)
        else:
            raise SystemExit("no such file or folder: " + name)
    return sorted(chosen, key=lambda p: relative(root, p))


def main(argv=None):
    parser = argparse.ArgumentParser(description="Report Simplified Chinese and Mainland wording in shipped strings and user-facing docs.")
    parser.add_argument("paths", nargs="*", help="files, or folders whose in-scope files are checked; default: the whole scope")
    parser.add_argument("--strict", action="store_true", help="warnings fail the run too")
    parser.add_argument("--json", metavar="FILE", help="write machine-readable results ('-' for standard output only)")
    parser.add_argument("--summary", action="store_true", help="print per-file counts, not every finding")
    parser.add_argument("--errors-only", action="store_true", help="print only the error findings (warnings are still counted)")
    parser.add_argument("--scope", choices=["all", "mac"], default="all", help="mac: only what the macOS app compiles or ships")
    parser.add_argument("--include-tests", action="store_true", help="also check code/tests (corpora stay excluded)")
    parser.add_argument("--assembly", nargs="+", metavar="DLL", help="scan the #US user strings of .NET assemblies instead of sources")
    parser.add_argument("--root", type=Path, default=REPO, help="tree to scan (default: this repository)")
    parser.add_argument("--allowlist", type=Path, default=ALLOWLIST, help="allowlist file (default: tools/traditional-allowlist.txt)")
    parser.add_argument("--no-allowlist", action="store_true", help="ignore the allowlist")
    parser.add_argument("--list-files", action="store_true", help="print the files in scope and exit")
    parser.add_argument("--self-test", action="store_true", help="run the built-in tests and a scan of the original v1.1.3 sources")
    parser.add_argument("--self-test-dir", type=Path, help="where --self-test exports v1.1.3 (default: a temporary folder)")
    args = parser.parse_args(argv)
    if args.self_test:
        return self_test(args)
    root = args.root.resolve()
    rules = Rules(REPO / OPENCC, root / ANIMATIONS if (root / ANIMATIONS).is_file() else REPO / ANIMATIONS)
    allowlist = Allowlist(None if args.no_allowlist else args.allowlist)
    scan = Scan(rules, allowlist)
    if args.assembly:
        projects = assembly_projects(REPO)
        for name in args.assembly:
            scan.assembly(Path(name), projects)
        return report(scan, args, [])
    files = resolve_paths(root, args)
    if args.list_files:
        print("\n".join(relative(root, p) for p in files))
        return 0
    for path in files:
        scan.source(root, path)
    whole = not args.paths and args.scope == "all" and root == REPO.resolve()
    unused = [e for e in allowlist.entries if not e["used"]] if whole else []
    return report(scan, args, unused)


# ---------------------------------------------------------------------------------------------------- self-test

def fake_assembly(strings):
    """A minimal PE32+ image with a CLI header, a metadata root and a #US heap holding these strings."""
    heap = bytearray(b"\0")
    for text in strings:
        data = text.encode("utf-16-le") + bytes([1 if any(ord(c) > 0x7E for c in text) else 0])
        n = len(data)
        heap += bytes([n]) if n < 0x80 else struct.pack(">H", 0x8000 | n) if n < 0x4000 else struct.pack(">I", 0xC0000000 | n)
        heap += data
    while len(heap) % 4:
        heap += b"\0"
    version = b"v4.0.30319\0\0"
    root_header = b"BSJB" + struct.pack("<HHII", 1, 1, 0, len(version)) + version + struct.pack("<HH", 0, 1)
    stream_header_size = 8 + 4
    metadata = root_header + struct.pack("<II", len(root_header) + stream_header_size, len(heap)) + b"#US\0" + bytes(heap)
    rva, raw = 0x2000, 0x200
    cli = struct.pack("<IHHII", 72, 2, 5, rva + 72, len(metadata)).ljust(72, b"\0")
    section = cli + metadata
    image = bytearray(raw + len(section))
    image[0:2] = b"MZ"
    struct.pack_into("<I", image, 0x3C, 0x40)
    image[0x40:0x44] = b"PE\0\0"
    struct.pack_into("<HHIIIHH", image, 0x44, 0x8664, 1, 0, 0, 0, 240, 0x22)
    optional = 0x58
    struct.pack_into("<H", image, optional, 0x20B)
    struct.pack_into("<I", image, optional + 108, 16)
    struct.pack_into("<II", image, optional + 112 + 14 * 8, rva, 72)
    struct.pack_into("<8sIIII", image, optional + 240, b".text", len(section), rva, len(section), raw)
    image[raw:] = section
    return bytes(image)


def export_tree(revision, target):
    """Writes the text files of a revision that the scope can contain (no media, fonts or binaries) under target."""
    names = subprocess.check_output(["git", "-C", str(REPO), "ls-tree", "-r", "-z", "--name-only", revision]).decode("utf-8").split("\0")
    wanted = {".cs", ".swift", ".c", ".md", ".txt", ".sh", ".py", ".ps1", ".yml", ".csproj", ".props", ".xaml", ".json"}
    names = [n for n in names if n and Path(n).suffix in wanted and (not n.endswith(".json") or n == ANIMATIONS)]
    process = subprocess.run(["git", "-C", str(REPO), "cat-file", "--batch"], input="".join("%s:%s\n" % (revision, n) for n in names).encode("utf-8"),
                             stdout=subprocess.PIPE, check=True)
    data, k = process.stdout, 0
    for name in names:
        header_end = data.index(b"\n", k)
        size = int(data[k:header_end].split()[2])
        body = data[header_end + 1:header_end + 1 + size]
        k = header_end + 1 + size + 1
        path = target / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(body)
    return len(names)


def self_test(args):
    rules = Rules(REPO / OPENCC, REPO / ANIMATIONS)
    failures = []

    def expect(condition, label):
        print(("PASS " if condition else "FAIL ") + label)
        if not condition:
            failures.append(label)

    def levels(src, suffix=".cs", allow=None, rel="code/x.cs"):
        allowlist = Allowlist(None)
        allowlist.entries = [{"line": 1, "path": "*", "text": t, "reason": "test", "used": 0} for t in (allow or [])]
        scan = Scan(rules, allowlist)
        for literal in file_literals(rel, src, suffix):
            scan.literal(rel, literal, lambda o: (1, o + 1))
        return [(f["level"], "".join(h["text"] for h in f["hits"])) for f in scan.findings]

    expect(levels('// 设备\n/* 设备 */\n/// 设备\n#region 设备\nvar a = 1;') == [], "comments and directives are skipped")
    expect(levels('var s = "设备";') == [("error", "设备")], "regular string")
    expect(levels('var s = "\\u8bbe\\u5907";') == [("error", "设备")], "unicode escapes are decoded")
    expect(levels('var s = @"a""设""";') == [("error", "设")], "verbatim string with doubled quotes")
    expect(levels('var s = $"x{Foo("设备")}y{{设}}";') == [("error", "设备"), ("error", "设")], "interpolated string with a nested literal and escaped braces")
    expect(levels('var s = $"{a:yyyy年M月设}";') == [("error", "设")], "format specifier text counts")
    expect(levels('var s = $"{(ok ? "开" : "关")} {global::X.Y}";') == [("error", "开"), ("error", "关")], "ternary and global:: inside a hole")
    expect(levels('var s = """\n  设备 "quoted"\n  """;') == [("error", "设备")], "raw string")
    expect(levels('var s = $$"""{{a}} {b} 设""";') == [("error", "设")], "interpolated raw string with two dollars")
    expect(levels("var c = '着'; var q = '\"'; var t = \"设\";") == [("error", "着"), ("error", "设")], "char literals do not open strings")
    expect(levels('var s = "待机呼吸休闲"; var f = "被鼠标拖拽悬空反馈.webm";') == [], "animation ids and clip file names are allowed")
    expect(levels('var s = "播放待机呼吸休闲后";') == [("warning", "后")], "an id inside a sentence covers only itself")
    expect(levels('var s = "【鱼鱼说话要求】设备";', allow=["【鱼鱼说话要求】"]) == [("error", "设备")], "an allowlist text covers only its own occurrence")
    expect(levels('var s = "請在設置裡打開默認窗口";') == [("warning", "設置默認窗口")], "Mainland vocabulary in Traditional script")
    expect(levels('var s = "驗證未通過，結束代碼 1，鄉鎮代碼 TW-061-01，本地時間 8 點，清單項目";') == [], "Taiwan phrases containing a term")
    expect(levels('var s = "透過「通過」傳送";') == [("warning", "通過")], "the same term outside an exception still warns")
    expect(levels('var s = "爲什麼在這里，之后再說";') == [("warning", "爲這里之后")], "variants and shared-character Simplified words")
    expect(levels('var s = "群組的床";') == [], "Taiwan forms that STCharacters maps to old forms are not errors")
    expect(levels('let s = "\\(foo("设备")) 设"\n// 设\nlet t = #"设\\#(x)"#\nlet m = """\n设\n"""', ".swift", rel="x.swift")
           == [("error", "设备"), ("error", "设"), ("error", "设"), ("error", "设")], "Swift strings, interpolation, raw and multi-line strings")
    expect(levels('#include "x.h"\nfputs("无法连接\\n", stderr); // 设\nchar c = \'设\';', ".c", rel="x.c") == [("error", "无连"), ("error", "设")], "C strings and chars")
    expect(levels("# 设备\nWrite-Host '设备'\n<#\n设备\n#>\n", ".ps1", rel="x.ps1") == [("error", "设备")], "script comments are skipped")
    expect(levels("<!-- 设备 -->\n<Product>设备</Product>\n", ".csproj", rel="x.csproj") == [("error", "设备")], "XML comments are skipped")
    expect(levels("# 更新記錄\n\n## 1.1.3-tw.1\n设备\n## 1.1.3 · 2026-11-13\n设备\n", ".md", rel="CHANGELOG.md") == [("error", "设备")], "only the top CHANGELOG section")
    long_text = "设置" * 40
    image = fake_assembly(["hello", "设置", long_text, "待机呼吸休闲", "【鱼鱼说话要求】"])
    expect([t for _, t in user_strings(image)] == ["hello", "设置", long_text, "待机呼吸休闲", "【鱼鱼说话要求】"], "#US heap of a PE/CLI image")
    with tempfile.TemporaryDirectory() as folder:
        dll = Path(folder) / "BigBlueFish.Fake.dll"
        dll.write_bytes(image)
        allowlist = Allowlist(None)
        allowlist.entries = [{"line": 1, "path": "code/Fake/Marker.cs", "text": "【鱼鱼说话要求】", "reason": "test", "used": 0},
                             {"line": 2, "path": "code/Other/Settings.cs", "text": "设置", "reason": "test", "used": 0}]
        scan = Scan(rules, allowlist)
        scan.assembly(dll, {"BigBlueFish.Fake": {"code/Fake/Marker.cs"}})
        expect(scan.totals() == (2, 0) and scan.suppressed["animation-id"] > 0 and allowlist.entries[0]["used"] == 4 and not allowlist.entries[1]["used"],
               "assembly scan: ids allowed, only entries for sources of that assembly apply")
    projects = assembly_projects(REPO)
    expect("code/BigBlueFish.Windows/Companion/CompanionBrain.cs" in projects.get("BigBlueFish.Mac", ())
           and "code/BigBlueFish.Agent/AcpConnection.cs" in projects.get("BigBlueFish.Agent", ())
           and "code/BigBlueFish.Agent/AcpConnection.cs" not in projects.get("BigBlueFish.Mac", ()), "assemblies map to the sources their projects compile")

    target = args.self_test_dir
    temporary = None
    if target is None:
        temporary = tempfile.TemporaryDirectory(prefix="v113-src-")
        target = Path(temporary.name)
    target = target.resolve()
    try:
        if not (target / MAC_PROJECT).is_file():
            print("exporting %s to %s: %d files" % (ORIGINAL_V113, target, export_tree(ORIGINAL_V113, target)))
        old_rules = Rules(REPO / OPENCC, target / ANIMATIONS if (target / ANIMATIONS).is_file() else REPO / ANIMATIONS)
        results = {}
        for scope in ("mac", "all"):
            scan = Scan(old_rules, Allowlist(None))
            for path in scope_files(target, scope):
                scan.source(target, path)
            results[scope] = scan
        mac = results["mac"]
        cs = sum(1 for f in mac.findings if f["level"] == "error" and f["path"].endswith(".cs"))
        print("v1.1.3 (%s), no allowlist: mac scope %d errors (%d in C# literals, %d in other files), %d warnings; whole scope %d errors, %d warnings"
              % (ORIGINAL_V113, mac.totals()[0], cs, mac.totals()[0] - cs, mac.totals()[1], *results["all"].totals()))
        for rel, counts in sorted(mac.files.items(), key=lambda x: -x[1]["errors"])[:12]:
            print("  %4d  %s" % (counts["errors"], rel))
        for f in [f for f in mac.findings if f["level"] == "error"][::97][:8]:
            print("  sample %s:%d %s" % (f["path"], f["line"], excerpt(f["text"], f["hits"][0]["offset"], 50)))
        by_path = {rel: c["errors"] for rel, c in mac.files.items()}
        expect(400 <= cs <= 700, "v1.1.3 Mac C# literal errors near the audit's ~530 (%d)" % cs)
        expect(by_path.get("code/BigBlueFish.Linux/LinuxSettings.cs", 0) >= 30 and by_path.get("code/BigBlueFish.Mac/MacCapture.cs", 0) >= 5,
               "v1.1.3 Linux settings and Mac capture strings are reported")
        expect(mac.suppressed["animation-id"] > 0 and not any(f["text"] in old_rules.ids for f in mac.findings),
               "v1.1.3 animation ids are never reported")
    finally:
        if temporary is not None:
            temporary.cleanup()
    print("FAIL %d self-test checks" % len(failures) if failures else "PASS Check-Traditional self-test")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
