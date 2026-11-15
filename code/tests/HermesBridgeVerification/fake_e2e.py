#!/usr/bin/env python3
# Fork-original file of the Taiwan macOS edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
# The Hermes bridge end to end on macOS or Linux without the real pet or Hermes: a fake pet (an MCP server on a Unix
# socket, reached through the real BigBlueFish.Mcp --pipe, listing only the tools a macOS pet has) and a fake Hermes API
# server that streams one tool progress event and an answer. Checks that the answer reaches say once, that progress lines
# go with transient=true, that the prompt is the macOS/Linux one naming only listed tools, and that SIGTERM ends the bridge.
# Python 3.9+. Build both first (run-hermes-bridge.sh --build, and dotnet publish BigBlueFish.Mcp -r osx-arm64), then:
#   python3 tests/HermesBridgeVerification/fake_e2e.py [BigBlueFish.HermesBridge.dll] <BigBlueFish.Mcp>
import http.server, json, os, shutil, signal, socket, socketserver, subprocess, sys, tempfile, threading, time, uuid
from pathlib import Path

if len(sys.argv) not in (2, 3):
    sys.exit("用法：fake_e2e.py [BigBlueFish.HermesBridge.dll] <BigBlueFish.Mcp>")
bridge_dll = sys.argv[1] if len(sys.argv) == 3 else str(Path(__file__).resolve().parents[2] / "BigBlueFish.HermesBridge" / "bin" / "Release" / "net10.0" / "BigBlueFish.HermesBridge.dll")
mcp = sys.argv[-1]
dotnet = os.environ.get("DOTNET") or (os.path.expanduser("~/.dotnet/dotnet") if os.path.exists(os.path.expanduser("~/.dotnet/dotnet")) else shutil.which("dotnet") or "dotnet")
work = tempfile.mkdtemp(prefix="bbf-e2e-")
sock_path = "/tmp/bbf-e2e-" + uuid.uuid4().hex[:10]
says, prompts, failures = [], [], []
done = threading.Event()

class Hermes(http.server.BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    def log_message(self, *a): pass
    def _json(self, code, body):
        data = json.dumps(body).encode()
        self.send_response(code); self.send_header("Content-Type", "application/json"); self.send_header("Content-Length", str(len(data))); self.end_headers(); self.wfile.write(data)
    def do_GET(self):
        if self.path.endswith("/health"): return self._json(200, {"status": "ok"})
        if self.path.endswith("/v1/toolsets"): return self._json(200, {"data": [{"name": "terminal", "enabled": True}]})
        return self._json(404, {})
    def do_POST(self):
        body = json.loads(self.rfile.read(int(self.headers["Content-Length"])))
        if not self.path.endswith("/v1/chat/completions"): return self._json(404, {})
        prompts.append(body["messages"][0]["content"])
        self.send_response(200); self.send_header("Content-Type", "text/event-stream"); self.send_header("Connection", "close"); self.end_headers()
        def send(text): self.wfile.write(text.encode()); self.wfile.flush()
        send("event: hermes.tool.progress\ndata: " + json.dumps({"tool": "web_search", "status": "running", "label": "台北天氣"}) + "\n\n")
        time.sleep(7)
        send("data: " + json.dumps({"id": "chatcmpl-e2e", "choices": [{"delta": {"content": "今天台北晴天，記得多喝水。"}}]}) + "\n\n")
        send("data: [DONE]\n\n")
        self.close_connection = True

class Server(socketserver.ThreadingMixIn, http.server.HTTPServer): daemon_threads = True
hermes = Server(("127.0.0.1", 0), Hermes)
threading.Thread(target=hermes.serve_forever, daemon=True).start()

SETTINGS = {"BubbleDurationIndex": 3, "DailyBriefing": False, "WelcomeBackMinutes": 0, "EventReminderMinutes": 0, "PublicInfo": False,
            "MealReminder": False, "BreakfastHour": 8, "LunchHour": 12, "DinnerHour": 18}
TOOLS = ["get_settings", "update_settings", "list_actions", "capture_foreground", "capture_screen", "play_action", "interrupt_action", "say", "say_next", "request_local_speech"]

def pet():
    server = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM); server.bind(sock_path); server.listen(4)
    connection, _ = server.accept(); stream = connection.makefile("rwb")
    def send(value): stream.write((json.dumps(value, ensure_ascii=False) + "\n").encode()); stream.flush()
    sent_message = False
    for raw in stream:
        request = json.loads(raw)
        method, rid = request.get("method"), request.get("id")
        if method == "initialize": send({"jsonrpc": "2.0", "id": rid, "result": {"protocolVersion": "2025-11-25", "capabilities": {"tools": {}, "logging": {}}, "serverInfo": {"name": "fake", "version": "1"}}})
        elif method == "logging/setLevel": send({"jsonrpc": "2.0", "id": rid, "result": {}})
        elif method == "tools/list":
            send({"jsonrpc": "2.0", "id": rid, "result": {"tools": [{"name": n, "inputSchema": {"type": "object"}} for n in TOOLS]}})
            if not sent_message:
                sent_message = True
                send({"jsonrpc": "2.0", "method": "notifications/message", "params": {"level": "info", "logger": "bigbluefish.interaction",
                      "data": {"sequence": 1, "kind": "user_message", "detail": {"text": "今天天氣如何？"}}}})
        elif method == "tools/call":
            name, args = request["params"]["name"], request["params"].get("arguments", {})
            if name == "say":
                says.append(args)
                if not args.get("transient") and "晴天" in args.get("text", ""): done.set()
            if name == "get_settings":
                text = json.dumps({"settings": SETTINGS, "state": {"visible": True, "action": "待机呼吸休闲", "animationPhase": "Idle", "queuedActions": [], "inputOpen": False,
                                   "bubbleVisible": False, "externalBubble": False, "speechPending": False, "idleSeconds": 1, "fullscreen": False, "platform": "macos"}}, ensure_ascii=False)
            else: text = json.dumps({"ok": True})
            send({"jsonrpc": "2.0", "id": rid, "result": {"content": [{"type": "text", "text": text}]}})
        elif rid is not None: send({"jsonrpc": "2.0", "id": rid, "result": {}})

threading.Thread(target=pet, daemon=True).start()
time.sleep(0.3)
wrapper = os.path.join(work, "pet-mcp.sh")
with open(wrapper, "w") as f: f.write("#!/bin/sh\nexec '%s' --pipe '%s'\n" % (mcp, sock_path))
os.chmod(wrapper, 0o755)
env = dict(os.environ, BIGBLUEFISH_HERMES_KEY="e2e-key-0123456789", HERMES_API_URL="http://127.0.0.1:%d" % hermes.server_address[1], HERMES_HOME=os.path.join(work, "hermes"))
bridge = subprocess.Popen([dotnet, bridge_dll, "--mcp", wrapper, "--state", os.path.join(work, "state.json")], env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
ok = done.wait(60)
bridge.send_signal(signal.SIGTERM)
try: out, _ = bridge.communicate(timeout=20)
except subprocess.TimeoutExpired: bridge.kill(); out, _ = bridge.communicate(); failures.append("bridge did not stop on SIGTERM")
print(out)
def check(condition, label):
    print(("PASS " if condition else "FAIL ") + label)
    if not condition: failures.append(label)
check(ok, "the final answer reached the pet's say")
progress = [s for s in says if "晴天" not in s.get("text", "")]
check(len(progress) > 0 and all(s.get("transient") is True for s in progress), "progress bubbles are sent with transient=true (%d)" % len(progress))
final = [s for s in says if "晴天" in s.get("text", "")]
check(len(final) == 1 and "transient" not in final[0], "the answer is sent once, without transient")
check(len(prompts) == 1 and "macOS" in prompts[0] and "Finder" in prompts[0] and "pwsh（PowerShell 7）" not in prompts[0], "the prompt describes macOS, not Windows")
check(len(prompts) == 1 and "mcp__big_blue_fish__walk_to" not in prompts[0] and "find_on_screen" not in prompts[0] and "get_context" not in prompts[0], "the prompt names no tool the pet did not list")
check(bridge.returncode == 0 and "橋接程式結束" in out, "SIGTERM ends the bridge cleanly (exit %s)" % bridge.returncode)
hermes.shutdown()
try: os.unlink(sock_path)
except OSError: pass
shutil.rmtree(work, ignore_errors=True)
sys.exit(1 if failures else 0)
