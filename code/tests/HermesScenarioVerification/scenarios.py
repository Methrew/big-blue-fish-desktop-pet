# Fork-original file of the taiwan-edition fork by acer1204 (2026-10-28); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
# Real-use scenarios for the fish + Hermes, on this machine's running pet, Hermes gateway and model: each one sets
# up files (and a File Explorer or Finder window when the request says "this folder on screen"), asks through the
# bridge's --ask path in a session of its own, then checks the outcome on disk and every tool call Hermes made:
# failures, identical calls repeated, garbled output, and how long it took. Needs the pet with MCP on and the gateway
# up; it changes two reminder settings and puts them back, and deletes any cron job it made.
# Windows, macOS and Linux (Python 3.9+). Build the bridge first (run-hermes-bridge.ps1 or run-hermes-bridge.sh), or
# point BIGBLUEFISH_BRIDGE at the one inside the macOS app. Scenarios needing a pet tool this pet does not list
# (walk_to on macOS and Linux for now) are skipped.
#   python3 tests/HermesScenarioVerification/scenarios.py [scenario ...]
import json, os, shutil, sqlite3, subprocess, sys, tempfile, time, urllib.request, zipfile
from pathlib import Path

sys.stdout.reconfigure(encoding='utf-8')
WINDOWS, MAC = sys.platform == 'win32', sys.platform == 'darwin'
CODE = Path(__file__).resolve().parents[2]
BRIDGE = Path(os.environ.get('BIGBLUEFISH_BRIDGE') or CODE / 'BigBlueFish.HermesBridge' / 'bin' / 'Release' / ('net10.0-windows' if WINDOWS else 'net10.0') / 'BigBlueFish.HermesBridge.dll')
HOME = Path.home()
HERMES = Path(os.environ.get('HERMES_HOME') or (Path(os.environ['LOCALAPPDATA']) / 'hermes' if WINDOWS else HOME / '.hermes'))
STATE_DB = 'file:' + str(HERMES / 'state.db') + '?mode=ro'


def _prefs_path():
    if os.environ.get('BIGBLUEFISH_DATA'):
        return Path(os.environ['BIGBLUEFISH_DATA']) / 'preferences.json'
    if MAC:
        # The Taiwan edition's own data folder (CompanionEdition.MacDataFolder), else the original edition's.
        support = HOME / 'Library' / 'Application Support'
        return next((p for p in (support / 'bigbluefish-tw' / 'preferences.json', support / 'bigbluefish' / 'preferences.json') if p.exists()), support / 'bigbluefish-tw' / 'preferences.json')
    if not WINDOWS:
        return Path(os.environ.get('XDG_DATA_HOME') or HOME / '.local' / 'share') / 'bigbluefish' / 'preferences.json'
    # Since 1.0.5 the pet keeps its settings in the data folder beside the program; older builds kept them next to it.
    build = CODE / 'BigBlueFish.Windows' / 'bin' / 'x64' / 'Release' / 'net10.0-windows10.0.26100.0'
    return next((p for p in (build / 'data' / 'preferences.json', build / 'preferences.json') if p.exists()), build / 'data' / 'preferences.json')


PREFS = _prefs_path()
ROOT = Path(tempfile.gettempdir()) / '大肥魚情境測試'
OUT = ROOT / 'results'
OUT.mkdir(parents=True, exist_ok=True)


# The bridge as a command: the dll through dotnet, or a self-contained one (the macOS app's) as it is.
def bridge(*arguments):
    if BRIDGE.suffix != '.dll':
        return [str(BRIDGE), *arguments]
    dotnet = os.environ.get('DOTNET') or next((str(p) for p in (HOME / '.dotnet' / 'dotnet',) if p.exists()), 'dotnet')
    return [dotnet, str(BRIDGE), *arguments]


# The tools the running pet lists, asked through BigBlueFish.Mcp the way the bridge asks (None when it cannot be reached).
def _find_mcp():
    if os.environ.get('BIGBLUEFISH_MCP'):
        return os.environ['BIGBLUEFISH_MCP']
    name = 'BigBlueFish.Mcp.exe' if WINDOWS else 'BigBlueFish.Mcp'
    places = [BRIDGE.parent / name]
    if MAC:
        places += [Path(folder) / (app + '.app') / 'Contents' / 'MacOS' / name for folder in (CODE.parent / 'release', '/Applications', HOME / 'Applications') for app in ('藍色大肥魚', '蓝色大肥鱼')]
    elif WINDOWS:
        places += [CODE / 'BigBlueFish.Windows' / 'bin' / 'x64' / 'Release' / 'net10.0-windows10.0.26100.0' / name]
    else:
        places += [Path('/opt/bigbluefish') / name]
    return next((str(p) for p in places if Path(p).exists()), None)


_TOOLS = []
def pet_tools():
    if _TOOLS:
        return _TOOLS[0]
    found = None
    mcp = _find_mcp()
    if mcp:
        lines = '\n'.join(json.dumps(m) for m in (
            {'jsonrpc': '2.0', 'id': 1, 'method': 'initialize', 'params': {'protocolVersion': '2025-11-25', 'capabilities': {}, 'clientInfo': {'name': 'scenarios', 'version': '1'}}},
            {'jsonrpc': '2.0', 'method': 'notifications/initialized'},
            {'jsonrpc': '2.0', 'id': 2, 'method': 'tools/list', 'params': {}})) + '\n'
        try:
            process = subprocess.Popen([mcp], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True, encoding='utf-8')
            process.stdin.write(lines); process.stdin.flush()
            deadline = time.time() + 15
            while found is None and time.time() < deadline:
                line = process.stdout.readline()
                if not line:
                    break
                reply = json.loads(line)
                if reply.get('id') == 2:
                    found = {tool['name'] for tool in reply.get('result', {}).get('tools', [])}
            process.kill()
        except (OSError, ValueError):
            found = None
    _TOOLS.append(found)
    return found


def has_tool(name):
    tools = pet_tools()
    return tools is None or name in tools


# Hermes' job list over its API; the key is read from Hermes' .env and never printed.
def _api(method, path):
    key = next((line.partition('=')[2].strip().strip('"\'') for line in open(HERMES / '.env', encoding='utf-8')
                if line.partition('=')[0].strip() == 'API_SERVER_KEY'), '')
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    request = urllib.request.Request('http://127.0.0.1:8642' + path, method=method, headers={'Authorization': 'Bearer ' + key})
    with opener.open(request, timeout=20) as response:
        return json.loads(response.read() or b'{}')


def cron_jobs():
    value = _api('GET', '/api/jobs')
    return value.get('jobs', []) if isinstance(value, dict) else value


def delete_job(job_id):
    return _api('DELETE', '/api/jobs/' + job_id)


def pwsh(script):
    return subprocess.run(['pwsh', '-NoProfile', '-Command', '[Console]::OutputEncoding = [Text.Encoding]::UTF8; ' + script], capture_output=True, text=True, encoding='utf-8', errors='replace')


def osascript(script):
    return subprocess.run(['osascript', '-e', script], capture_output=True, text=True, encoding='utf-8', errors='replace')


def _applescript_text(text):
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"') + '"'


# The folder window the request calls "this folder on screen": File Explorer on Windows, Finder on macOS (asking Finder needs
# the Automation permission for the terminal that runs this), the desktop's file manager on Linux.
def open_explorer(folder):
    if WINDOWS:
        subprocess.Popen(['explorer.exe', f'/n,{folder}'])
    else:
        subprocess.Popen(['open' if MAC else 'xdg-open', str(folder)])
    if not WINDOWS and not MAC:
        time.sleep(3)
        return
    for _ in range(30):
        time.sleep(0.5)
        names = pwsh("(New-Object -ComObject Shell.Application).Windows() | ForEach-Object { $_.LocationName }").stdout if WINDOWS \
            else osascript('tell application "Finder" to get name of every Finder window').stdout
        if folder.name in names:
            time.sleep(1.5)
            return
    raise RuntimeError('The folder window did not open: ' + str(folder))


def close_explorer(folder):
    if WINDOWS:
        pwsh(f"(New-Object -ComObject Shell.Application).Windows() | Where-Object {{ $_.LocationName -eq '{folder.name}' }} | ForEach-Object {{ $_.Quit() }}")
    elif MAC:
        osascript('tell application "Finder" to close (every Finder window whose name is ' + _applescript_text(folder.name) + ')')


def ask(name, text, timeout=480):
    state = OUT / f'state-{name}.json'
    if state.exists():
        state.unlink()
    started = time.time()
    run = subprocess.run(bridge('--ask', text, '--state', str(state)), capture_output=True, text=True, encoding='utf-8', timeout=timeout)
    session = json.loads(state.read_text(encoding='utf-8'))['session']
    return run.stdout.strip(), session, time.time() - started


def trace(session):
    con = sqlite3.connect(STATE_DB, uri=True)
    rows = con.execute('select role, content, tool_calls, tool_name, timestamp from messages where session_id=? order by id', (session,)).fetchall()
    calls, results = [], []
    for role, content, tool_calls, tool_name, stamp in rows:
        if role == 'assistant' and tool_calls:
            for call in json.loads(tool_calls):
                function = call.get('function', {})
                calls.append((function.get('name'), function.get('arguments') or ''))
        elif role == 'tool':
            results.append((tool_name, content or ''))
    return rows, calls, results


def failed(content):
    text = content.strip()
    try:
        value = json.loads(text)
        if isinstance(value, dict):
            if value.get('exit_code') not in (None, 0) or value.get('error') or value.get('isError') or value.get('success') is False:
                return True
            inner = value.get('result')
            if isinstance(inner, str) and inner.lstrip().startswith(('Error', '錯誤')):
                return True
    except (json.JSONDecodeError, TypeError):
        pass
    return text.startswith(('Error', 'error:', '錯誤')) or '"isError": true' in text


def report(name, reply, session, seconds, checks, extra_rules=None):
    rows, calls, results = trace(session)
    failures = [(n, c[:200]) for n, c in results if failed(c)]
    # A repeat the tool asked for (find_on_screen had windows left unread) is not going round in circles.
    seen, asked = {}, set()
    for i, call in enumerate(calls):
        if call in asked:
            asked.discard(call)
        else:
            seen[call] = seen.get(call, 0) + 1
        if i < len(results) and '"unfinished"' in results[i][1].replace('\\"', '"'):
            asked.add(call)
    repeated = [(n, a[:160], k) for (n, a), k in seen.items() if k > 1]
    inner = []
    for n, a in calls:
        if n == 'tool_call':
            try:
                arguments = json.loads(a)
                inner.append(arguments.get('name') or arguments.get('tool') or a[:80])
            except json.JSONDecodeError:
                inner.append(a[:80])
    lines = [f'## {name}', f'- 回覆：{reply}', f'- 花了 {seconds:.0f} 秒，工具呼叫 {len(calls)} 次，失敗 {len(failures)} 次，完全相同的呼叫重複 {len(repeated)} 組']
    for i, (n, a) in enumerate(calls, 1):
        lines.append(f'  {i}. {n} {a[:220]}')
    for n, c in failures:
        lines.append(f'  ✗ {n}: {c}')
    for n, a, k in repeated:
        lines.append(f'  ↻ {k} 次 {n} {a}')
    ok = True
    for label, passed in checks:
        lines.append(('  PASS ' if passed else '  FAIL ') + label)
        ok &= bool(passed)
    garbled = [n for n, c in results if '�' in c]
    rules = [('沒有完全相同的呼叫重複兩次以上', not repeated), ('工具失敗不超過 1 次', len(failures) <= 1), (f'工具輸出沒有亂碼（{len(garbled)} 次有）', not garbled)] + (extra_rules(calls, inner) if extra_rules else [])
    for label, passed in rules:
        lines.append(('  PASS ' if passed else '  FAIL ') + label)
        ok &= bool(passed)
    text = '\n'.join(lines)
    print(text, flush=True)
    (OUT / f'{name}.md').write_text(text, encoding='utf-8')
    return ok


def names(calls, inner):
    return [n for n, _ in calls] + inner


def scenario_rename():
    folder = ROOT / '情境-改名'
    shutil.rmtree(folder, ignore_errors=True); folder.mkdir(parents=True)
    for i in (1, 2, 3):
        (folder / f'第{i}集.zip').write_bytes(f'episode {i}'.encode())
    (folder / '說明.txt').write_text('不要動我', encoding='utf-8')
    open_explorer(folder)
    try:
        reply, session, seconds = ask('rename', '把畫面上這個資料夾裡的 zip 檔改名成 VOL01.zip、VOL02.zip、VOL03.zip，照集數順序，其他檔案不要動。')
    finally:
        close_explorer(folder)
    files = sorted(p.name for p in folder.iterdir())
    content = {p.name: p.read_bytes() for p in folder.iterdir() if p.suffix == '.zip'}
    return report('rename', reply, session, seconds, [
        (f'資料夾裡剛好是 VOL01–03.zip 和 說明.txt：{files}', files == ['VOL01.zip', 'VOL02.zip', 'VOL03.zip', '說明.txt']),
        ('第1集→VOL01、第2集→VOL02、第3集→VOL03', all(content.get(f'VOL0{i}.zip') == f'episode {i}'.encode() for i in (1, 2, 3))),
        ('說明.txt 沒被動到', (folder / '說明.txt').read_text(encoding='utf-8') == '不要動我'),
    ], lambda calls, inner: [('用 get_context、find_on_screen 或問 Finder 拿到資料夾', any(t in a for _, a in calls for t in ('get_context', 'find_on_screen', 'Finder')))])


def scenario_zip():
    folder = ROOT / '情境-壓縮'
    shutil.rmtree(folder, ignore_errors=True); folder.mkdir(parents=True)
    (folder / '會議紀錄一.txt').write_text('第一次會議', encoding='utf-8')
    (folder / '會議紀錄二.txt').write_text('第二次會議', encoding='utf-8')
    (folder / '照片.png').write_bytes(b'not really a png')
    open_explorer(folder)
    try:
        reply, session, seconds = ask('zip', '把畫面上這個資料夾裡的兩個 txt 檔壓縮成「會議紀錄.zip」，放在同一個資料夾裡。')
    finally:
        close_explorer(folder)
    archive = folder / '會議紀錄.zip'
    entries = []
    if archive.exists():
        with zipfile.ZipFile(archive) as z:
            entries = sorted(Path(n).name for n in z.namelist())
    return report('zip', reply, session, seconds, [
        ('會議紀錄.zip 存在', archive.exists()),
        (f'壓縮檔裡正好是兩個 txt，中文檔名正確：{entries}', entries == ['會議紀錄一.txt', '會議紀錄二.txt']),
        ('原本的檔案都還在', all((folder / n).exists() for n in ('會議紀錄一.txt', '會議紀錄二.txt', '照片.png'))),
    ])


def scenario_context():
    if not has_tool('get_context'):
        print('## context\n  SKIP 這個魚魚還沒有 get_context', flush=True)
        return True
    reply, session, seconds = ask('context', '現在幾點？我已經連續用電腦多久了？等一下出門要帶傘嗎？')
    def rules(calls, inner):
        everything = ' '.join(n + ' ' + a for n, a in calls)
        return [('用了 get_context', 'get_context' in everything),
                ('沒有截圖', not any(word in everything for word in ('capture_screen', 'capture_foreground', 'screenshot'))),
                ('沒有上網查', not any(word in everything for word in ('web_search', 'web_extract', 'browser')))]
    return report('context', reply, session, seconds, [('回覆提到時間', any(ch.isdigit() for ch in reply))], rules)


def prefs():
    return json.loads(PREFS.read_text(encoding='utf-8-sig'))


def scenario_settings():
    before = prefs()
    reply, session, seconds = ask('settings', '久坐提醒改成每 90 分鐘，深夜提醒先關掉。')
    after = prefs()
    def rules(calls, inner):
        everything = ' '.join(n + ' ' + a for n, a in calls)
        return [('沒有另外建排程', 'cronjob' not in everything), ('用了 update_settings', 'update_settings' in everything)]
    return report('settings', reply, session, seconds, [
        (f'SitReminderMinutes {before.get("SitReminderMinutes")} → {after.get("SitReminderMinutes")}', after.get('SitReminderMinutes') == 90),
        (f'LateReminder {before.get("LateReminder")} → {after.get("LateReminder")}', after.get('LateReminder') is False),
        ('其他提醒沒被動到（用餐、低電量、下雨都還開著）', all(after.get(k) is True for k in ('MealReminder', 'BatteryReminder', 'RainReminder'))),
    ], rules)


def scenario_restore():
    reply, session, seconds = ask('restore', '久坐提醒改回 60 分鐘，深夜提醒改回從 00:00 開始。')
    after = prefs()
    return report('restore', reply, session, seconds, [
        ('SitReminderMinutes 回到 60', after.get('SitReminderMinutes') == 60),
        ('深夜提醒打開、從 00:00 開始', after.get('LateReminder') is True and after.get('LateReminderHour') == 0)])


def scenario_missing():
    folder = ROOT / '情境-找不到'
    shutil.rmtree(folder, ignore_errors=True); folder.mkdir(parents=True)
    (folder / '期中報告.txt').write_text('內容', encoding='utf-8')
    open_explorer(folder)
    try:
        reply, session, seconds = ask('missing', '把畫面上這個資料夾裡的 期末報告.docx 改名成 期末報告-最終版.docx。')
    finally:
        close_explorer(folder)
    files = sorted(p.name for p in folder.iterdir())
    return report('missing', reply, session, seconds, [
        (f'沒有亂改或亂建檔案：{files}', files == ['期中報告.txt']),
        ('回覆說找不到', any(word in reply for word in ('找不到', '沒有', '不存在'))),
    ], lambda calls, inner: [('沒有繞圈子（工具呼叫 8 次以內）', len(calls) <= 8)])


def scenario_lunch():
    # Without the fish's own reminders (they came with get_context) a cron job is the right answer.
    if not has_tool('get_context'):
        print('## lunch\n  SKIP 這個魚魚不會自己提醒吃飯', flush=True)
        return True
    known = {job['id'] for job in cron_jobs()}
    reply, session, seconds = ask('lunch', '每天中午 12 點提醒我吃午餐。')
    made = [job for job in cron_jobs() if job['id'] not in known]
    for job in made:
        delete_job(job['id'])
    before, after = len(known), len(known) + len(made)
    def rules(calls, inner):
        everything = ' '.join(n + ' ' + a for n, a in calls)
        return [('沒有另外建排程', 'cronjob' not in everything or after == before)]
    return report('lunch', reply, session, seconds, [
        (f'排程數量沒變（{before} → {after}）', after == before),
        ('用餐提醒開著、午餐時間是 12 點', prefs().get('MealReminder') is True and prefs().get('LunchHour') == 12),
        ('回覆說魚魚自己會提醒，沒說排好了排程', any(w in reply for w in ('本來', '自己', '已經會', '會提醒')) and not any(w in reply for w in ('排好', '建好', '幫你排', '已建立'))),
    ], rules)


def brief(kind, timeout=180):
    # The bridge's --brief path: one daily briefing or welcome back, written by Hermes in a hidden session of its
    # own and said on the real pet. Which session it used is found as the newest pet-brief- one.
    started = time.time()
    run = subprocess.run(bridge('--brief', kind), capture_output=True, text=True, encoding='utf-8', timeout=timeout)
    con = sqlite3.connect(STATE_DB, uri=True)
    row = con.execute("select id, hidden from sessions where id like 'pet-brief-%' and started_at >= ? order by started_at desc limit 1", (started - 2,)).fetchone()
    lines = [line for line in run.stdout.strip().splitlines() if not line.startswith('[')]
    return (lines[-1] if lines else ''), row, time.time() - started, run.stdout


def scenario_briefing(kind, name, expect):
    text, row, seconds, log = brief(kind)
    session, hidden = row if row else (None, None)
    calls = 0
    if session:
        con = sqlite3.connect(STATE_DB, uri=True)
        calls = con.execute('select count(*) from messages where session_id = ? and tool_calls is not null', (session,)).fetchone()[0]
    lines = [f'## {name}', f'- 說的話：{text}', f'- 花了 {seconds:.0f} 秒，Hermes 工具呼叫 {calls} 次']
    checks = [
        ('Hermes 寫了內容（沒有改用備用文字）', '改用魚魚自己整理的內容' not in log),
        ('沒有呼叫任何工具', calls == 0),
        ('簡報用的對話已隱藏', hidden == 1),
        (f'長度合理（{len(text)} 字）', 20 <= len(text) <= 220),
        ('沒有 Markdown 或列點', not any(mark in text for mark in ('**', '##', '\n-', '\n*', '1. '))),
    ] + [(label, test(text)) for label, test in expect]
    ok = True
    for label, passed in checks:
        lines.append(('  PASS ' if passed else '  FAIL ') + label)
        ok &= bool(passed)
    out = '\n'.join(lines)
    print(out, flush=True)
    (OUT / f'{name}.md').write_text(out, encoding='utf-8')
    return ok


def scenario_daily():
    return scenario_briefing('daily', 'daily', [('是今天的簡報（提到天氣）', lambda t: any(w in t for w in ('度', '雨', '天氣')))])


def scenario_welcome():
    return scenario_briefing('welcome:150', 'welcome', [
        ('離開多久照原樣說，沒有自己換算錯', lambda t: '2 小時 30 分鐘' in t or '兩個半小時' in t or '2.5' in t or '兩小時半' in t),
        ('沒有說成三個多小時', lambda t: '三個' not in t and '3 小時' not in t)])


def scenario_appointment():
    before = {job['id'] for job in cron_jobs()}
    reply, session, seconds = ask('appointment', '明天中午 12 點半提醒我跟客戶吃飯。')
    created = [job for job in cron_jobs() if job['id'] not in before]
    for job in created:
        delete_job(job['id'])
    return report('appointment', reply, session, seconds, [
        (f'某一天的約會照樣建了一個排程：{[job.get("name") for job in created]}', len(created) == 1),
        ('測試建的排程已經刪掉', not [job for job in cron_jobs() if job['id'] not in before]),
    ])


def scenario_walk():
    if not (has_tool('walk_to') and has_tool('find_on_screen')):
        print('## walk\n  SKIP 這個魚魚還不能 walk_to', flush=True)
        return True
    folder = ROOT / '情境-走過去'
    shutil.rmtree(folder, ignore_errors=True); folder.mkdir(parents=True)
    open_explorer(folder)
    try:
        reply, session, seconds = ask('walk', '魚魚，跑到畫面上那個「情境-走過去」資料夾視窗旁邊。')
    finally:
        close_explorer(folder)
    def rules(calls, inner):
        everything = ' '.join(n + ' ' + a for n, a in calls)
        return [('用 find_on_screen 找視窗', 'find_on_screen' in everything), ('用 walk_to 跑過去', 'walk_to' in everything),
                ('沒有截圖', 'capture_screen' not in everything and 'screenshot' not in everything)]
    return report('walk', reply, session, seconds, [('回覆說跑到了', any(word in reply for word in ('到了', '跑到', '旁邊')))], rules)


SCENARIOS = {'daily': scenario_daily, 'welcome': scenario_welcome, 'appointment': scenario_appointment, 'lunch': scenario_lunch, 'walk': scenario_walk, 'rename': scenario_rename, 'zip': scenario_zip, 'context': scenario_context, 'settings': scenario_settings,
             'restore': scenario_restore, 'missing': scenario_missing}

if __name__ == '__main__':
    chosen = sys.argv[1:] or list(SCENARIOS)
    results = {}
    for name in chosen:
        try:
            results[name] = SCENARIOS[name]()
        except Exception as error:  # a scenario that could not even run is a failure too
            print(f'## {name}\n  FAIL 無法執行：{error!r}', flush=True)
            results[name] = False
    print('\n總結：' + '，'.join(f'{n} {"通過" if ok else "沒過"}' for n, ok in results.items()))
