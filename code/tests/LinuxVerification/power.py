"""Real window check of the fixed offline notice, using an actual GGUF without starting inference."""
import json,os,pathlib,subprocess,sys,time
payload=pathlib.Path(sys.argv[1]).resolve();data=pathlib.Path(sys.argv[2]).resolve();model=pathlib.Path(sys.argv[3]).resolve()
assert model.exists() and model.stat().st_size>1_000_000
(data/'models').mkdir(parents=True,exist_ok=True)
link=data/'models/Qwen3.5-4B-heretic-Q4_K_M.gguf'
if not link.exists():link.symlink_to(model)
(data/'preferences.json').write_text(json.dumps(dict(ModelEnabled=True,StopModelOnBattery=True,PublicInfo=False,CheckUpdatesOnStartup=False)))
log=(data/'app.log').open('w')
app=subprocess.Popen([str(payload/'BigBlueFish.Linux'),'--test-mode','--test-battery'],env=dict(os.environ,BIGBLUEFISH_DATA=str(data),XDG_SESSION_TYPE='x11'),stdout=log,stderr=log)
def state():return json.loads((data/'test-status.json').read_text())
try:
 time.sleep(3);first=state()
 # The bubble shows every line through CompanionTraditional, so the expectations are its Taiwan Traditional output.
 assert first['bubbleVisible'] and '請安' in first['text'] and first['modelPid'] is None,first
 print('PASS: battery startup welcome remains visible without model preload',flush=True)
 time.sleep(5)
 (data/'test-command.txt').write_text('speech-test');time.sleep(2)
 notice=state()
 assert notice['bubbleVisible'] and notice['text']=='主人，目前設定為筆電使用電池時暫停本機模型，可以到設定裡調整喔。' and notice['modelPid'] is None,notice
 print('PASS: real speech-test shows persistent battery notice and starts no inference',flush=True)
finally:
 (data/'test-command.txt').write_text('exit')
 try:app.wait(timeout=5)
 except subprocess.TimeoutExpired:app.kill();app.wait()
 log.close()
