#!/usr/bin/env python3
"""Run against an actual published macOS app: a payload folder or BigBlueFish.app/Contents/MacOS.

Usage: verify.py <payload> <data-directory> [--safe]

Pointer and keyboard gestures need the Accessibility permission on macOS, so they
are left to manual checks; everything else drives the real app through its test
commands and the real stdio MCP bridge. HOME points at a temporary profile, so the
login item test never touches the user's own LaunchAgents."""
import base64, json, os, pathlib, plistlib, queue, signal, subprocess, sys, tempfile, threading, time, xml.etree.ElementTree as ET

here = pathlib.Path(__file__).resolve().parent
payload = pathlib.Path(sys.argv[1]).resolve()
data = pathlib.Path(sys.argv[2]).resolve()
data.mkdir(parents=True, exist_ok=True)
home = pathlib.Path(tempfile.mkdtemp(prefix='bigbluefish-home-'))
env = dict(os.environ, BIGBLUEFISH_DATA=str(data), HOME=str(home))
count = 0
def check(ok, label):
    global count
    assert ok, label
    count += 1
    print(f"PASS {count}: {label}", flush=True)
def wait(condition, seconds=12):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        try:
            result = condition()
            if result: return result
        except (FileNotFoundError, json.JSONDecodeError): pass
        time.sleep(.08)
    raise TimeoutError(str(condition))
def state(): return json.loads((data / 'test-status.json').read_text())
def command(value):
    temporary = data / 'test-command.txt.tmp'; temporary.write_text(value); temporary.replace(data / 'test-command.txt')
def helper(name):
    target = data / name
    subprocess.run(['xcrun', 'swiftc', '-O', '-o', str(target), str(here / (name + '.swift'))], check=True)
    return target
def fixture_app():
    # macOS only brings an application to the front when LaunchServices opens it.
    bundle = data / 'BigBlueFish Fixture.app'; executable = bundle / 'Contents/MacOS/fixture'
    executable.parent.mkdir(parents=True, exist_ok=True)
    helper('fixture').replace(executable)
    (bundle / 'Contents/Info.plist').write_bytes(plistlib.dumps(dict(CFBundleIdentifier='io.github.bigbluefish.verification-fixture', CFBundleName='fixture', CFBundleExecutable='fixture', CFBundlePackageType='APPL')))
    subprocess.run(['open', '-n', str(bundle)], check=True)
    return int(wait(lambda: subprocess.run(['pgrep', '-f', str(executable)], capture_output=True, text=True).stdout.split()[0], 10))
def windows(pid): return json.loads(subprocess.check_output([str(window_list), str(pid)], text=True))
def children(name): return subprocess.run(['pgrep', '-f', str(payload / 'runtime' / name)], capture_output=True, text=True).stdout.split()

for name in ['test-status.json', 'test-error.txt', 'test-command.txt', 'ocr-status.json', 'pet.png', 'bubble.png', 'settings.png']:
    (data / name).unlink(missing_ok=True)
(data / 'preferences.json').write_text(json.dumps(dict(ModelEnabled=False, PublicInfo=False, CheckUpdatesOnStartup=False, McpEnabled=True, PuppetMode=True)))
window_list = helper('windows')
log = (data / 'app.log').open('w')
app = subprocess.Popen([str(payload / 'BigBlueFish.Mac'), '--test-mode'], env=env, stdout=log, stderr=log)
bridge = fixture = None
try:
    wait(lambda: state().get('frames', 0) > 10)
    check(app.poll() is None, 'actual self-contained macOS window running')
    pet = wait(lambda: next((w for w in windows(app.pid) if w['title'] == '蓝色大肥鱼'), None), 5)
    check(abs(pet['width'] - state()['width']) < 2 and abs(pet['height'] - state()['height']) < 2 and pet['width'] > 300, 'native pet window has its real size on screen')
    check(abs(pet['x'] - state()['X']) < 2 and abs(pet['y'] - state()['Y']) < 2, 'native pet window sits where the app placed it')
    if '--safe' in sys.argv:
        check(not state()['McpAvailable'] and not state()['Puppet'], 'safe ignores enabled MCP and puppet preferences')
        check(not (payload / 'BigBlueFish.Mcp').exists() and not (payload / 'BigBlueFish.Control.dll').exists(), 'safe package contains no MCP bridge or Control assembly')
        check('BigBlueFish.Control' not in (payload / 'BigBlueFish.Mac.deps.json').read_text(), 'safe executable has no MCP dependency')
        command('settings'); wait(lambda: state()['settingsOpen'])
        command('render-settings'); wait(lambda: (data / 'settings.png').exists())
        command('close-settings'); wait(lambda: not state()['settingsOpen'])
        check(state()['phase'] != 'PuppetIdle', 'safe continues ordinary animation selection')
        check(not (data / 'test-error.txt').exists(), 'no safe application errors')
        print(f'macOS Safe actual integration: {count} checks passed.', flush=True)
        sys.exit(0)
    check(state()['Puppet'] and state()['currentAction'] == '待机呼吸休闲', 'startup puppet uses fixed breathing idle')
    bridge = subprocess.Popen([str(payload / 'BigBlueFish.Mcp')], env=env, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, bufsize=1)
    lines = queue.Queue(); events = []; request_id = 0
    def receive():
        for line in bridge.stdout: lines.put(json.loads(line))
    threading.Thread(target=receive, daemon=True).start()
    def request(method, params={}):
        global request_id
        request_id += 1; number = request_id
        bridge.stdin.write(json.dumps(dict(jsonrpc='2.0', id=number, method=method, params=params), ensure_ascii=False) + '\n'); bridge.stdin.flush()
        while True:
            value = lines.get(timeout=30)
            if value.get('id') == number:
                assert 'result' in value, value
                return value['result']
            events.append(value)
    def tool(name, **arguments):
        result = request('tools/call', dict(name=name, arguments=arguments)); assert not result.get('isError'), result; return result
    def content(name, **arguments): return json.loads(tool(name, **arguments)['content'][0]['text'])
    init = request('initialize', dict(protocolVersion='2025-11-25', capabilities={}, clientInfo=dict(name='macOS verification', version='1')))
    bridge.stdin.write('{"jsonrpc":"2.0","method":"notifications/initialized"}\n'); bridge.stdin.flush()
    version = ET.parse(here.parents[2] / 'Version.props').findtext('.//Version')
    check(init['serverInfo']['version'] == version, 'real stdio bridge negotiates current version')
    check(len(request('tools/list')['tools']) == 9, 'all MCP tools available')
    check(len(content('list_actions')) == 106, 'same 106 production actions')
    text = '主人，macOS 端收到的原文呀~ ABC 123'
    content('say', text=text); wait(lambda: state()['text'] == text)
    check(state()['modelPid'] is None and state()['bubbleVisible'], 'literal speech works without local model')
    bubble = wait(lambda: next((w for w in windows(app.pid) if w['title'] == '蓝色大肥鱼 · 对话'), None), 3)
    check(bubble['width'] > 200 and bubble['height'] > 50, 'bubble window is sized to its text')
    command('render-bubble'); wait(lambda: (data / 'bubble.png').exists())
    content('interrupt_action', action='点击回应-元气挥手')
    content('play_action', action='吃白饭')
    wait(lambda: state()['currentAction'] == '点击回应-元气挥手', 3)
    wait(lambda: state()['currentAction'] == '吃白饭', 14)
    check(True, 'queued animation begins after actual decoder completion')
    wait(lambda: state()['phase'] == 'PuppetIdle', 14)
    check(True, 'external action returns to fixed idle')
    content('say_next', text='下次自动节奏')
    command('speech-test'); time.sleep(2)
    check(state()['text'] == text and state()['modelPid'] is None, 'puppet rejects speech test and does not consume scheduled speech')
    # Foreground fixture is a separate AppKit application, not a title injected into the fish.
    fixture = fixture_app()
    wait(lambda: any(w['title'] == 'BigBlueFish Development' for w in windows(fixture)), 10); time.sleep(1)
    for name in ['capture_foreground', 'capture_screen']:
        result = tool(name, max_dimension=1280); image = base64.b64decode(result['content'][1]['data']); metadata = json.loads(result['content'][0]['text'])
        check(result['content'][1]['mimeType'] == 'image/png' and image[:8] == b'\x89PNG\r\n\x1a\n', name + ' actual PNG')
        (data / (name + '.png')).write_bytes(image)
        if name == 'capture_foreground': check('BigBlueFish Development' in metadata['identity'], 'foreground selected from real external AppKit window')
    command('ocr'); wait(lambda: (data / 'ocr-status.json').exists(), 60)
    ocr = json.loads((data / 'ocr-status.json').read_text())
    check('BigBlueFish' in ocr['Context'] and '蓝色大肥鱼' in ocr['Context'] and ocr['WordCount'] >= 2, 'actual Vision OCR reads Chinese and English foreground pixels')
    command('render'); wait(lambda: (data / 'pet.png').exists())
    command('settings'); wait(lambda: state()['settingsOpen'])
    command('render-settings'); wait(lambda: (data / 'settings.png').exists())
    content('update_settings', settings={'BubbleScale': 1.35, 'AnimationIntervalSeconds': 65})
    time.sleep(.4)
    check(state()['settingsOpen'], 'MCP refresh keeps the macOS settings window open')
    command('close-settings'); wait(lambda: not state()['settingsOpen'])
    content('update_settings', settings={'PuppetMode': False, 'BubbleScale': 1.2})
    wait(lambda: not state()['Puppet'])
    check(json.loads((data / 'preferences.json').read_text())['BubbleScale'] == 1.2, 'settings saved and puppet disabled explicitly')
    agent = home / 'Library/LaunchAgents/io.github.bigbluefish.pet.plist'
    content('update_settings', settings={'StartupEnabled': True})
    item = plistlib.loads(agent.read_bytes())
    check(item['RunAtLoad'] and pathlib.Path(item['ProgramArguments'][0]).resolve() == (payload / 'BigBlueFish.Mac').resolve(), 'login item LaunchAgent starts the installed executable')
    check(content('get_settings')['settings']['StartupEnabled'], 'login item reported as enabled')
    content('update_settings', settings={'StartupEnabled': False})
    check(not agent.exists() and not content('get_settings')['settings']['StartupEnabled'], 'login item removed when disabled')
    content('update_settings', settings={'Left': 420, 'Top': 220})
    wait(lambda: abs(state()['X'] - 420) < 2 and abs(state()['Y'] - 220) < 2)
    moved = wait(lambda: next((w for w in windows(app.pid) if w['title'] == '蓝色大肥鱼' and abs(w['x'] - 420) < 2 and abs(w['y'] - 220) < 2), None), 3)
    check(moved['width'] > 300, 'MCP coordinate settings move the actual macOS window')
    check(children('ffmpeg'), 'animation decoder runs under the runner guard')
    check(not (data / 'test-error.txt').exists(), 'no application diagnostic errors')
    command('close')
    app.wait(timeout=15)
    check(app.returncode == 0, 'closing plays the farewell and exits cleanly')
    time.sleep(.5)
    check(not children('ffmpeg'), 'no decoder outlives a clean exit')
    # A crash must not leave helpers behind either: the guard watches its parent.
    (data / 'test-status.json').unlink(missing_ok=True)
    app = subprocess.Popen([str(payload / 'BigBlueFish.Mac'), '--test-mode'], env=env, stdout=log, stderr=log)
    wait(lambda: state().get('frames', 0) > 5); wait(lambda: children('ffmpeg'), 5)
    app.send_signal(signal.SIGKILL); app.wait(timeout=5)
    wait(lambda: not children('ffmpeg'), 5)
    check(True, 'runner guard stops the decoder when the app is killed')
    print(f'macOS actual integration: {count} checks passed.', flush=True)
finally:
    if bridge: bridge.terminate(); bridge.wait(timeout=5)
    if fixture: os.kill(fixture, signal.SIGTERM)
    if app.poll() is None:
        command('exit')
        try: app.wait(timeout=5)
        except subprocess.TimeoutExpired: app.kill(); app.wait()
