# Modified by Methrew on 2026-11-15 for the Taiwan Traditional Chinese edition of Bai-Cute/big-blue-fish-desktop-pet 1.1.3, which also takes in acer1204's taiwan-edition fork (Apache-2.0).
"""Validate the bundled Apple Silicon application on a macOS build host."""
import argparse
import hashlib
import json
import os
import pathlib
import plistlib
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument("app", type=pathlib.Path)
parser.add_argument("--edition", choices=["Standard", "Safe"], required=True)
parser.add_argument("--output", type=pathlib.Path, required=True)
args = parser.parse_args()
app = args.app.resolve()
payload = app / "Contents/MacOS"
args.output.mkdir(parents=True, exist_ok=True)
checks = []


def check(condition, label):
    if not condition:
        raise RuntimeError(label)
    checks.append(label)
    print("PASS", label, flush=True)


def run(*command, **kwargs):
    return subprocess.check_output(command, stderr=subprocess.STDOUT, **kwargs)


info = plistlib.loads((app / "Contents/Info.plist").read_bytes())
expected_version = ET.parse(pathlib.Path(__file__).resolve().parents[1] / "Version.props").findtext(".//Version")
check(info["CFBundleShortVersionString"] == expected_version.split("-")[0], "bundle product version")
check(info["CFBundleVersion"] == expected_version, "bundle build version keeps the Taiwan revision")
identity = json.loads(subprocess.check_output([sys.executable, str(pathlib.Path(__file__).resolve().parent / "Get-EditionIdentity.py"), "--json"]))
safe = args.edition == "Safe"
check(info["CFBundleIdentifier"] == identity["MacSafeBundleId" if safe else "MacBundleId"], "edition bundle identifier")
check(info["CFBundleName"] == identity["SafeDisplayName" if safe else "DisplayName"] and app.name == info["CFBundleName"] + ".app", "edition application name")
check(info.get("CFBundleDevelopmentRegion") == "zh_TW", "Taiwan development region")
check(identity["Credits"] in info.get("NSHumanReadableCopyright", ""), "credits to the original author and the Taiwan edition")
for name in ["BigBlueFish.Mac", "libcoreclr.dylib", "libhostfxr.dylib",
             "runtime/llama-server", "runtime/BigBlueFish.RunnerGuard",
             "runtime/BigBlueFish.MacOcr", "runtime/BigBlueFish.MacScreen", "media/gstreamer/lib/libgstreamer-1.0.0.dylib"]:
    check((payload / name).is_file(), "bundled dependency: " + name)
for name in ["BigBlueFish.Mcp", "BigBlueFish.HermesBridge", "BigBlueFish.Control.dll", "BigBlueFish.Agent.dll"]:
    check((payload / name).is_file() == (args.edition == "Standard"), "edition isolation: " + name)
# The 1280x720, 60 fps clips play through VideoToolbox (the applemedia plugin and the libraries it loads); the 640x360
# compatibility clips are the fallback and the preloaded openings.
check(len(list((payload / "assets/fish").glob("*.webm"))) == 106, "106 enhanced clips")
check(len(list((payload / "assets/fish/compat").glob("*.webm"))) == 106, "106 compatibility clips")
for name in ["plugins/libgstapplemedia.dylib", "lib/libgstgl-1.0.0.dylib", "lib/libgstvulkan-1.0.0.dylib", "lib/libgstiosurface-1.0.0.dylib", "lib/libMoltenVK.dylib"]:
    check((payload / "media/gstreamer" / name).is_file(), "hardware video decoding: " + name)
for name in ["MoltenVK-LICENSE.txt", "glslang-LICENSE.txt", "cereal-LICENSE.txt"]:
    check((payload / "licenses/gstreamer" / name).is_file(), "license of MoltenVK and what it contains: " + name)


def hardware_vp9():
    """Whether this Mac decodes VP9 on its media engine, as CompanionMedia asks VideoToolbox."""
    import ctypes
    toolbox = ctypes.CDLL("/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox")
    toolbox.VTRegisterSupplementalVideoDecoderIfAvailable(ctypes.c_uint32(0x76703039))
    toolbox.VTIsHardwareDecodeSupported.restype = ctypes.c_bool
    return bool(toolbox.VTIsHardwareDecodeSupported(ctypes.c_uint32(0x76703039)))
# Taiwan edition bubble: jf open huninn and the OpenCC dictionaries are embedded and ship with their licenses
# (Kosugi Maru, the source of jf's Hanzi, under Apache-2.0); the Simplified-only Lemi font is gone.
program = (payload / "BigBlueFish.Mac.dll").read_bytes()
check(b"Assets/jf-openhuninn-2.1.ttf" in program and b"BigBlueFish.Windows.assets.opencc.STPhrases.txt" in program,
      "bubble font and OpenCC dictionaries embedded")
for name in ["licenses/jf-open-huninn-OFL.txt", "licenses/OpenCC-Apache-2.0.txt", "licenses/Apache-2.0.txt"]:
    check((payload / name).is_file(), "bundled license: " + name)
check(b"LemiXiaoNaiPaoTi" not in program and not any(p.name.lower().replace("-", "").startswith("lemixiao") for p in payload.rglob("*")),
      "no Lemi font or license in the bundle")
native = list(payload.rglob("*.dylib")) + [payload / n for n in [
    "BigBlueFish.Mac", "runtime/llama-server", "runtime/BigBlueFish.RunnerGuard", "runtime/BigBlueFish.MacOcr", "runtime/BigBlueFish.MacScreen"]]
if args.edition == "Standard":
    native += [payload / "BigBlueFish.Mcp", payload / "BigBlueFish.HermesBridge"]
names = {p.name for p in payload.rglob("*") if p.is_file()}
dependency_errors = []
for file in native:
    arch = run("lipo", "-archs", str(file), text=True).strip()
    check("arm64" in arch.split(), "arm64: " + str(file.relative_to(payload)))
    own_ids = set(run("otool", "-arch", "arm64", "-D", str(file), text=True).splitlines()[1:]) if file.suffix == ".dylib" else set()
    for line in run("otool", "-arch", "arm64", "-L", str(file), text=True).splitlines()[1:]:
        dependency = line.strip().split(" (", 1)[0]
        if dependency in own_ids or dependency.startswith(("/System/Library/", "/usr/lib/")):
            continue
        if dependency.startswith("@") and pathlib.Path(dependency).name in names:
            check(True, "self-contained native dependency: " + dependency)
        else:
            dependency_errors.append({"file": str(file.relative_to(payload)), "dependency": dependency})
(args.output / "native-dependency-errors.json").write_text(json.dumps(dependency_errors, indent=2))
check(not dependency_errors, "native dependency closure: " + json.dumps(dependency_errors))
run("codesign", "--verify", "--deep", "--strict", str(app))
check(True, "application bundle signature verifies")
with (args.output / "llama-version.log").open("w") as log:
    version_process = subprocess.Popen([str(payload / "runtime/llama-server"), "--version"],
        stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT)
    try:
        version_process.wait(timeout=90)
    except subprocess.TimeoutExpired:
        subprocess.run(["sample", str(version_process.pid), "3", "-file", str(args.output / "llama-stack.txt")], timeout=20)
        version_process.kill()
        version_process.wait()
        raise
    check(version_process.returncode == 0, "llama-server version query exits successfully")
check(True, "bundled llama-server starts")
run(str(payload / "runtime/BigBlueFish.RunnerGuard"), "/usr/bin/true", timeout=10)
check(True, "native runner guard executes a child")

with tempfile.TemporaryDirectory(prefix="bbf-macos-verify-") as temporary:
    data = pathlib.Path(temporary)
    # The Safe edition shares the data folder with the Standard edition: it must leave the Standard Agent switches alone.
    standard_agent = {"AgentEnabled": True, "PuppetMode": True, "AgentDailySpeech": True, "McpEnabled": True, "McpChatEnabled": True} if args.edition == "Safe" else {}
    (data / "preferences.json").write_text(json.dumps({"ModelEnabled": False, "PublicInfo": False,
        "CheckUpdatesOnStartup": False, "HideOnFullscreen": False, "Left": 300, "Top": 250, **standard_agent}))
    (data / "model-selection.json").write_text('{"Source":"none"}')
    env = {**os.environ, "BIGBLUEFISH_DATA": str(data), "DOTNET_ROOT": "/nonexistent",
           "DOTNET_MULTILEVEL_LOOKUP": "0", "DYLD_LIBRARY_PATH": "", "DYLD_FALLBACK_LIBRARY_PATH": ""}
    with (args.output / "app.log").open("w") as log:
        process = subprocess.Popen([str(payload / "BigBlueFish.Mac"), "--test-mode"], env=env,
                                   stdout=log, stderr=subprocess.STDOUT)
        try:
            deadline = time.monotonic() + 60
            status = None
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError("application exited: " + (args.output / "app.log").read_text())
                try:
                    status = json.loads((data / "test-status.json").read_text())
                except (FileNotFoundError, json.JSONDecodeError):
                    time.sleep(.25)
                    continue
                if status["phase"] != "Startup" and status["frames"] > 3:
                    break
                time.sleep(.25)
            (args.output / "status.json").write_text(json.dumps(status, indent=2))
            check(status is not None and status["phase"] != "Startup" and status["frames"] > 3,
                  "real application completes startup clip and presents subsequent decoded frames")
            check(status["McpAvailable"] == (args.edition == "Standard"), "running edition matches package")
            expected = "GPU VP9 + alpha" if hardware_vp9() else "CPU VP9 + alpha (24 fps)"
            check(status.get("animationBackend") == expected, "animation plays " + expected + " (VideoToolbox VP9: " + str(hardware_vp9()) + ")")
            (data / "test-command.txt").write_text("render")
            deadline = time.monotonic() + 10
            while not (data / "pet.png").exists() and time.monotonic() < deadline:
                time.sleep(.25)
            check((data / "pet.png").stat().st_size > 1000, "real pet window renders")
            (args.output / "pet.png").write_bytes((data / "pet.png").read_bytes())
            ocr = subprocess.run([str(payload / "runtime/BigBlueFish.MacOcr")], input=(data / "pet.png").read_bytes(),
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=True, timeout=30)
            check(ocr.stdout.startswith(b"level\tpage_num\t"), "bundled Apple Vision OCR processes a real PNG")
            (data / "test-command.txt").write_text("bubble 主人，我们一起干杯吧！", encoding="utf-8")
            deadline = time.monotonic() + 10
            while time.monotonic() < deadline:
                try:
                    status = json.loads((data / "test-status.json").read_text(encoding="utf-8"))
                    if status["text"] == "主人，我們一起乾杯吧！":
                        break
                except (FileNotFoundError, json.JSONDecodeError):
                    pass
                time.sleep(.25)
            check(status["text"] == "主人，我們一起乾杯吧！" and status["bubbleVisible"], "bubble shows Simplified text in Taiwan Traditional")

            def command(text, timeout=10):
                for name in ("test-done.txt", "test-error.txt"):
                    (data / name).unlink(missing_ok=True)
                (data / "test-command.txt").write_text(text, encoding="utf-8")
                deadline = time.monotonic() + timeout
                while time.monotonic() < deadline:
                    try:
                        if (data / "test-done.txt").read_text(encoding="utf-8") == text:
                            return not (data / "test-error.txt").exists()
                    except FileNotFoundError:
                        pass
                    time.sleep(.1)
                return False

            def surface(name):
                ok = command("surface " + name)
                return json.loads((data / name).read_text(encoding="utf-8")) if ok else {}

            # A bubble whose Metal surface kept an earlier size (frames drawn off the main thread after a resize) shows its
            # text stretched with the last lines cut off, and Avalonia leaves it so while the bubble does not change; the
            # app finds it once the bubble's size has held and has it drawn again (KeepSurfacePlatform in MacPetWindow.cs).
            time.sleep(.5)
            settled = surface("surface-settled.json")
            command("surface-check off")
            command("surface-shrink")
            shrunk = surface("surface-shrunk.json")
            time.sleep(1)
            left = surface("surface-left.json")
            command("surface-check on")
            time.sleep(.5)
            repaired = surface("surface-repaired.json")
            stale = lambda s: s.get("found") and s["drawableHeight"] < s["height"] - 1
            check(settled.get("found") and settled["drawableWidth"] == settled["width"] and settled["drawableHeight"] == settled["height"],
                  "the bubble's Metal surface matches its window")
            check(stale(shrunk) and stale(left), "a stale bubble surface stays stale without the repair (the failure it guards against)")
            check(repaired.get("found") and repaired["drawableHeight"] == repaired["height"] and repaired["drawableWidth"] == repaired["width"],
                  "the app draws a stale bubble surface again at the window's size")
            # The screen reader answers in JSON, Accessibility granted or not, and never prompts without --prompt.
            screen = json.loads(subprocess.run([str(payload / "runtime/BigBlueFish.MacScreen"), "find", "windows"], stdout=subprocess.PIPE, check=True, timeout=30).stdout)
            check(isinstance(screen.get("trusted"), bool) and len(screen.get("displays", [])) > 0 and isinstance(screen.get("windows"), list),
                  "bundled screen reader lists the displays and windows")

            # Files dragged from Finder and dropped on the fish: the app's window gets the calls AppKit makes (native-drop,
            # MacTestDrag.cs). With the Agent off the Standard fish takes them and says where to switch it on; the Safe fish has
            # no input box and takes none; dragged text is turned away.
            def native_drop(*items):
                ok = command("native-drop pet " + "|".join(items))
                return json.loads((data / "native-drop.json").read_text(encoding="utf-8")) if ok else {}
            dropped_file = data / "拖放 測試.txt"
            dropped_file.write_text("drop", encoding="utf-8")
            dropped = native_drop(str(dropped_file))
            if args.edition == "Standard":
                deadline = time.monotonic() + 10
                while time.monotonic() < deadline:
                    try:
                        status = json.loads((data / "test-status.json").read_text(encoding="utf-8"))
                        if "連線 Agent" in status["text"]:
                            break
                    except (FileNotFoundError, json.JSONDecodeError):
                        pass
                    time.sleep(.25)
                check(dropped.get("updated") == 1 and dropped.get("performed") is True, "the fish takes files dropped on her")
                check(status["bubbleVisible"] and "連線 Agent" in status["text"], "a drop without the Agent says where to switch it on")
            else:
                check(dropped.get("updated") == 0 and dropped.get("performed") is False, "the Safe edition's fish takes no dropped files")
            text_drop = native_drop("text:網頁上的文字")
            check(text_drop.get("updated") == 0 and text_drop.get("performed") is False, "the fish turns away dragged text")
            (data / "test-command.txt").write_text("exit")
            process.wait(timeout=15)
            check(process.returncode == 0, "application exits normally")
            if standard_agent:
                saved = json.loads((data / "preferences.json").read_text(encoding="utf-8-sig"))
                check(all(saved.get(name) is True for name in standard_agent), "Safe edition keeps the Standard edition's Agent switches")
        finally:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()
# Taiwan edition: no string a bundled assembly carries may contain a Simplified-only character (animation ids and the
# allowlisted markers aside); Mainland wording is reported in traditional-strings.json but does not fail the package.
assemblies = sorted(str(p) for p in payload.glob("BigBlueFish.*.dll"))
traditional = subprocess.run([sys.executable, str(pathlib.Path(__file__).resolve().parent / "Check-Traditional.py"), "--errors-only",
                              "--json", str(args.output / "traditional-strings.json"), "--assembly", *assemblies],
                             stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
print(traditional.stdout, end="", flush=True)
check(str(payload / "BigBlueFish.Mac.dll") in assemblies and traditional.returncode == 0,
      "assembly strings are Taiwan Traditional Chinese: " + ", ".join(pathlib.Path(a).name for a in assemblies))
(args.output / "checks.json").write_text(json.dumps(checks, indent=2))
