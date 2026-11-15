// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
// What is on the screen for find_on_screen and walk_to: the on-screen windows (CGWindowList), Finder's desktop icons,
// the Dock and the named interface elements of every window that shows (Accessibility). Prints one JSON object.
//   BigBlueFish.MacScreen trusted [--prompt]
//   BigBlueFish.MacScreen find <all|desktop|windows> [--exclude-pid PID] [--budget SECONDS]
// Coordinates are global points: the top-left corner of the main display is (0, 0), as in CGWindowList and AXPosition.
// It never hangs: every Accessibility message times out after half a second, the reading stops at the budget, and a
// watchdog prints an error and exits a few seconds later whatever happens. Without the Accessibility permission the
// windows are still listed and "trusted" is false.
import AppKit
import ApplicationServices
import CoreGraphics
import Foundation

let arguments = Array(CommandLine.arguments.dropFirst())
let outputLock = NSLock()
nonisolated(unsafe) var printed = false

func emit(_ object: [String: Any], code: Int32) -> Never {
    outputLock.lock()
    if !printed {
        printed = true
        let data = (try? JSONSerialization.data(withJSONObject: object, options: [])) ?? Data("{\"error\":\"json\"}".utf8)
        FileHandle.standardOutput.write(data)
        FileHandle.standardOutput.write(Data("\n".utf8))
    }
    exit(code)
}

func option(_ name: String) -> String? {
    guard let at = arguments.firstIndex(of: name), at + 1 < arguments.count else { return nil }
    return arguments[at + 1]
}

func screenRecordingAllowed() -> Bool { CGPreflightScreenCaptureAccess() }

guard let command = arguments.first else { emit(["error": "usage: trusted [--prompt] | find <all|desktop|windows>"], code: 2) }

if command == "trusted" {
    let trusted: Bool
    if arguments.contains("--prompt") {
        let key = kAXTrustedCheckOptionPrompt.takeUnretainedValue() as String
        trusted = AXIsProcessTrustedWithOptions([key: true] as CFDictionary)
    } else {
        trusted = AXIsProcessTrusted()
    }
    emit(["trusted": trusted, "screenRecording": screenRecordingAllowed()], code: 0)
}

guard command == "find", arguments.count >= 2, ["all", "desktop", "windows"].contains(arguments[1]) else {
    emit(["error": "usage: find <all|desktop|windows> [--exclude-pid PID] [--budget SECONDS]"], code: 2)
}
let scope = arguments[1]
let excluded = Int32(option("--exclude-pid") ?? "") ?? -1
let budget = min(20, max(0.5, Double(option("--budget") ?? "") ?? 6))
let started = Date()
let deadline = started.addingTimeInterval(budget)
// Past the budget the reading has stopped on its own; this only catches a call that does not return at all.
DispatchQueue.global().asyncAfter(deadline: .now() + budget + 4) {
    emit(["error": "timeout", "elapsedMs": Int(Date().timeIntervalSince(started) * 1000)], code: 124)
}

// Limits: a window with a huge accessibility tree (a long web page, a table of thousands of rows) is cut off and
// reported as unfinished rather than read to the end.
let visitLimit = 6000, reportLimit = 3000, childLimit = 800, depthLimit = 60, windowLimit = 40

func rect(_ value: [String: Any]) -> CGRect {
    CGRect(dictionaryRepresentation: value as CFDictionary) ?? .null
}
@Sendable func box(_ r: CGRect) -> [String: Any] {
    ["x": (r.origin.x * 10).rounded() / 10, "y": (r.origin.y * 10).rounded() / 10, "width": (r.size.width * 10).rounded() / 10, "height": (r.size.height * 10).rounded() / 10]
}

// Displays
var displayIds = [CGDirectDisplayID](repeating: 0, count: 32)
var displayCount: UInt32 = 0
CGGetActiveDisplayList(32, &displayIds, &displayCount)
let displays = displayIds.prefix(Int(displayCount)).map { CGDisplayBounds($0) }
let mainDisplay = CGDisplayBounds(CGMainDisplayID())
func onDisplay(_ x: Double, _ y: Double) -> Bool { displays.contains { $0.contains(CGPoint(x: x, y: y)) } }

// Windows, front to back
struct Shown {
    let id: Int, pid: Int32, layer: Int, alpha: Double, owner: String, title: String, frame: CGRect
}
let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID) as? [[String: Any]] ?? []
var shown: [Shown] = []
for item in list {
    let pid = (item[kCGWindowOwnerPID as String] as? NSNumber)?.int32Value ?? 0
    if pid == excluded || pid == getpid() { continue }
    let frame = rect(item[kCGWindowBounds as String] as? [String: Any] ?? [:])
    if frame.isNull || frame.width < 1 || frame.height < 1 { continue }
    shown.append(Shown(id: (item[kCGWindowNumber as String] as? NSNumber)?.intValue ?? 0, pid: pid,
                       layer: (item[kCGWindowLayer as String] as? NSNumber)?.intValue ?? 0,
                       alpha: (item[kCGWindowAlpha as String] as? NSNumber)?.doubleValue ?? 1,
                       owner: item[kCGWindowOwnerName as String] as? String ?? "",
                       title: item[kCGWindowName as String] as? String ?? "", frame: frame))
}
// A window above the normal level spanning a whole display (the lock screen shield, a dimming overlay) is not taken
// for one that hides what is under it; nor is a fully transparent one.
func hides(_ w: Shown) -> Bool {
    w.alpha > 0.01 && !(w.layer != 0 && displays.contains { w.frame.contains($0) })
}
let occluders = shown.filter(hides)
func showsAt(_ id: Int, _ x: Double, _ y: Double) -> Bool {
    onDisplay(x, y) && occluders.first { $0.frame.contains(CGPoint(x: x, y: y)) }?.id == id
}
// Five by five points across a window plus a ring just inside its edges: one point showing is enough.
func samples(_ r: CGRect) -> [CGPoint] {
    func across(_ start: Double, _ size: Double) -> [Double] {
        ([min(16, size / 2)] + (0..<5).map { size * (Double($0) + 0.5) / 5 } + [max(size - 16, size / 2)]).map { start + $0 }
    }
    let xs = across(r.minX, r.width), ys = across(r.minY, r.height)
    return ys.flatMap { y in xs.map { CGPoint(x: $0, y: y) } }
}

struct App { let name: String, bundle: String? }
var apps: [Int32: App] = [:]
func app(_ pid: Int32) -> App {
    if let known = apps[pid] { return known }
    let running = NSRunningApplication(processIdentifier: pid)
    let found = App(name: running?.localizedName ?? "", bundle: running?.bundleIdentifier)
    apps[pid] = found
    return found
}

var windowRows: [[String: Any]] = []
for w in shown {
    let owner = app(w.pid)
    var row = box(w.frame)
    row["id"] = w.id; row["pid"] = Int(w.pid); row["layer"] = w.layer; row["alpha"] = w.alpha
    row["process"] = owner.name.isEmpty ? w.owner : owner.name
    row["title"] = w.title
    if let bundle = owner.bundle { row["bundleId"] = bundle }
    windowRows.append(row)
}

let trusted = AXIsProcessTrusted()
var result: [String: Any] = [
    "trusted": trusted, "screenRecording": screenRecordingAllowed(),
    "displays": displays.map { var row = box($0); row["main"] = $0 == mainDisplay; return row },
    "windows": windowRows,
]
if !trusted {
    result["elapsedMs"] = Int(Date().timeIntervalSince(started) * 1000)
    emit(result, code: 0)
}

// Accessibility
AXUIElementSetMessagingTimeout(AXUIElementCreateSystemWide(), 0.5)

@Sendable func axValue(_ value: AnyObject?) -> AnyObject? {
    guard let value else { return nil }
    if CFGetTypeID(value) == AXValueGetTypeID(), AXValueGetType(value as! AXValue) == .axError { return nil }
    return value
}
func read(_ element: AXUIElement, _ names: [String]) -> ([AnyObject?], AXError) {
    var values: CFArray?
    let error = AXUIElementCopyMultipleAttributeValues(element, names as CFArray, AXCopyMultipleAttributeOptions(rawValue: 0), &values)
    guard error == .success, let array = values as [AnyObject]?, array.count == names.count else { return (names.map { _ in nil }, error) }
    return (array.map { axValue($0) }, .success)
}
func text(_ value: AnyObject?) -> String {
    guard let string = value as? String else { return "" }
    return string.split(whereSeparator: { $0.isWhitespace || $0.isNewline }).joined(separator: " ")
}
func frame(_ position: AnyObject?, _ size: AnyObject?) -> CGRect? {
    guard let position, let size, CFGetTypeID(position) == AXValueGetTypeID(), CFGetTypeID(size) == AXValueGetTypeID() else { return nil }
    var point = CGPoint.zero, extent = CGSize.zero
    guard AXValueGetValue(position as! AXValue, .cgPoint, &point), AXValueGetValue(size as! AXValue, .cgSize, &extent) else { return nil }
    return CGRect(origin: point, size: extent)
}
func elements(_ value: AnyObject?) -> [AXUIElement] {
    guard let array = value as? [AnyObject] else { return [] }
    return array.compactMap { CFGetTypeID($0) == AXUIElementGetTypeID() ? ($0 as! AXUIElement) : nil }
}
@Sendable func short(_ value: String, _ limit: Int) -> String { value.count <= limit ? value : String(value.prefix(limit)) + "…" }

let fields = [kAXRoleAttribute, kAXSubroleAttribute, kAXRoleDescriptionAttribute, kAXTitleAttribute, kAXDescriptionAttribute,
              kAXValueAttribute, kAXPositionAttribute, kAXSizeAttribute, kAXChildrenAttribute] as [String]
// Text that names an element: its title, else its description; the value only of static text and of a text field
// that cannot be edited (a file name in a Finder list), never what the user is typing.
let valueRoles: Set<String> = ["AXStaticText", "AXHeading", "AXCell"]
let visibleAttribute = ["AXTable": "AXVisibleRows", "AXOutline": "AXVisibleRows", "AXList": "AXVisibleChildren", "AXGrid": "AXVisibleChildren", "AXBrowser": "AXVisibleColumns"]

// The named elements of one window that are inside it and not scrolled out of view. finished is false when the
// budget, the size limit or an application that stopped answering cut the reading short.
func scan(_ root: AXUIElement, _ window: CGRect) -> (rows: [[String: Any]], finished: Bool) {
    var rows: [[String: Any]] = []
    var stack: [(AXUIElement, CGRect, Int)] = [(root, window, 0)]
    var visited = 0, failures = 0
    while let (element, clip, depth) = stack.popLast() {
        if Date() >= deadline || visited >= visitLimit || rows.count >= reportLimit { return (rows, false) }
        visited += 1
        let (values, error) = read(element, fields)
        if error == .cannotComplete { failures += 1; if failures >= 3 || depth == 0 { return (rows, false) }; continue }
        let role = text(values[0])
        let bounds = frame(values[6], values[7])
        // A subtree wholly outside the visible part (rows below a scrolled list, a page below the fold) is skipped.
        if depth > 0, let bounds, bounds.width > 0, bounds.height > 0, !bounds.intersects(clip) { continue }
        var inner = clip
        if role == "AXScrollArea", let bounds, bounds.width > 0, bounds.height > 0 { inner = clip.intersection(bounds) }
        if depth > 0, let bounds, bounds.width >= 1, bounds.height >= 1, clip.contains(CGPoint(x: bounds.midX, y: bounds.midY)) {
            var name = text(values[3])
            if name.isEmpty { name = text(values[4]) }
            if name.isEmpty, valueRoles.contains(role) { name = text(values[5]) }
            if name.isEmpty, role == "AXTextField" {
                var settable: DarwinBoolean = false
                if AXUIElementIsAttributeSettable(element, kAXValueAttribute as CFString, &settable) == .success, !settable.boolValue { name = text(values[5]) }
            }
            if !name.isEmpty, role != "AXWindow" {
                var row = box(bounds)
                row["role"] = role
                row["name"] = short(name, 200)
                let type = text(values[2]); if !type.isEmpty { row["type"] = type }
                let subrole = text(values[1]); if !subrole.isEmpty { row["subrole"] = subrole }
                rows.append(row)
            }
        }
        if depth < depthLimit {
            var children = elements(values[8])
            // A long table or list hands over the rows in view, so a list scrolled far down is not cut off at the top.
            if children.count > 50, let visible = visibleAttribute[role] {
                let rows = elements(read(element, [visible]).0[0])
                if !rows.isEmpty { children = rows }
            }
            for child in children.prefix(childLimit).reversed() { stack.append((child, inner, depth + 1)) }
        }
    }
    return (rows, true)
}

// The private call every window manager uses to tell which CGWindowList window an AX window is; without it the
// window is recognised by its owner and frame.
typealias WindowIdFunction = @convention(c) (AXUIElement, UnsafeMutablePointer<CGWindowID>) -> AXError
let windowIdFunction: WindowIdFunction? = dlsym(UnsafeMutableRawPointer(bitPattern: -2), "_AXUIElementGetWindow").map { unsafeBitCast($0, to: WindowIdFunction.self) }
func windowId(_ element: AXUIElement, pid: Int32, frame: CGRect?) -> Int? {
    if let windowIdFunction {
        var id: CGWindowID = 0
        if windowIdFunction(element, &id) == .success, id != 0 { return Int(id) }
    }
    guard let frame else { return nil }
    return shown.first { $0.pid == pid && abs($0.frame.minX - frame.minX) < 2 && abs($0.frame.minY - frame.minY) < 2
        && abs($0.frame.width - frame.width) < 2 && abs($0.frame.height - frame.height) < 2 }?.id
}

let finderPid = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.finder").first?.processIdentifier
let dockPid = NSRunningApplication.runningApplications(withBundleIdentifier: "com.apple.dock").first?.processIdentifier

// Finder's desktop: AXApplication › AXScrollArea (one per display) › AXGroup › AXImage per icon, each with its title
// and, where Finder gives it, the file's URL (no need to read ~/Desktop, which macOS guards separately).
if scope != "windows" {
    if let finderPid {
        let finder = AXUIElementCreateApplication(finderPid)
        var icons: [[String: Any]] = []
        var finished = true
        var stack: [(AXUIElement, Int)] = elements(read(finder, [kAXChildrenAttribute]).0[0])
            .filter { text(read($0, [kAXRoleAttribute]).0[0]) == "AXScrollArea" }.map { ($0, 0) }
        while let (element, depth) = stack.popLast() {
            if Date() >= deadline || icons.count >= 2000 { finished = false; break }
            let (values, error) = read(element, [kAXRoleAttribute, kAXTitleAttribute, kAXPositionAttribute, kAXSizeAttribute, kAXChildrenAttribute, kAXURLAttribute])
            if error == .cannotComplete { finished = false; break }
            let role = text(values[0])
            if role == "AXScrollArea" || role == "AXGroup" || role == "AXList" {
                if depth < 4 { for child in elements(values[4]).prefix(4000).reversed() { stack.append((child, depth + 1)) } }
                continue
            }
            let name = text(values[1])
            guard !name.isEmpty, let bounds = frame(values[2], values[3]), bounds.width >= 1, bounds.height >= 1 else { continue }
            var row = box(bounds)
            row["name"] = name
            row["role"] = role
            if let link = values[5], CFGetTypeID(link) == CFURLGetTypeID() {
                let url = (link as! CFURL) as URL
                if url.isFileURL { row["path"] = url.path; row["folder"] = url.hasDirectoryPath }
            } else if let link = values[5] as? String, let url = URL(string: link), url.isFileURL {
                row["path"] = url.path; row["folder"] = url.hasDirectoryPath
            }
            icons.append(row)
        }
        result["desktop"] = ["icons": icons, "finished": finished]
    } else {
        result["desktop"] = ["icons": [], "finished": true, "finderMissing": true]
    }
}

if scope != "desktop" {
    // The windows to read: normal and floating windows of other programs that show at least in part.
    let candidates = shown.filter { w in w.layer >= 0 && w.layer < 20 && hides(w) && samples(w.frame).contains { showsAt(w.id, $0.x, $0.y) } }
    let visibleIds = Set(candidates.prefix(windowLimit).map { $0.id })
    let pids = Array(Set(candidates.prefix(windowLimit).map { $0.pid }))
    let resultsLock = NSLock()
    var scanned: [[String: Any]] = []
    var skipped: [Int] = candidates.dropFirst(windowLimit).map { $0.id }
    // One application at a time per thread; slow applications do not hold up the others.
    DispatchQueue.concurrentPerform(iterations: pids.count) { index in
        let pid = pids[index]
        let application = AXUIElementCreateApplication(pid)
        let (values, error) = read(application, [kAXWindowsAttribute])
        if error != .success {
            resultsLock.lock(); skipped += shown.filter { $0.pid == pid && visibleIds.contains($0.id) }.map { $0.id }; resultsLock.unlock()
            return
        }
        var found = Set<Int>()
        for window in elements(values[0]) {
            if Date() >= deadline { break }
            let (head, _) = read(window, [kAXPositionAttribute, kAXSizeAttribute, kAXTitleAttribute, kAXDocumentAttribute])
            let bounds = frame(head[0], head[1])
            guard let id = windowId(window, pid: pid, frame: bounds), visibleIds.contains(id), !found.contains(id),
                  let cg = shown.first(where: { $0.id == id }) else { continue }
            found.insert(id)
            let (rows, finished) = scan(window, cg.frame)
            var entry: [String: Any] = ["window": id, "finished": finished, "elements": rows]
            let title = text(head[2]); if !title.isEmpty { entry["title"] = title }
            if let document = head[3] as? String, !document.isEmpty { entry["document"] = document }
            resultsLock.lock(); scanned.append(entry); resultsLock.unlock()
        }
        let missed = shown.filter { $0.pid == pid && visibleIds.contains($0.id) && !found.contains($0.id) }.map { $0.id }
        if !missed.isEmpty, Date() >= deadline { resultsLock.lock(); skipped += missed; resultsLock.unlock() }
    }
    // The Dock's items, while the Dock shows.
    if let dockPid, Date() < deadline {
        let dockWindows = occluders.filter { $0.pid == dockPid }
        var items: [[String: Any]] = []
        var stack: [(AXUIElement, Int)] = elements(read(AXUIElementCreateApplication(dockPid), [kAXChildrenAttribute]).0[0]).map { ($0, 0) }
        while let (element, depth) = stack.popLast(), Date() < deadline, items.count < 200 {
            let (values, _) = read(element, [kAXRoleAttribute, kAXTitleAttribute, kAXPositionAttribute, kAXSizeAttribute, kAXChildrenAttribute, kAXRoleDescriptionAttribute])
            let role = text(values[0])
            if role == "AXList" || role == "AXGroup" { if depth < 3 { for child in elements(values[4]).reversed() { stack.append((child, depth + 1)) } }; continue }
            guard role == "AXDockItem", let bounds = frame(values[2], values[3]), bounds.width >= 1, bounds.height >= 1 else { continue }
            let name = text(values[1])
            guard !name.isEmpty, let host = dockWindows.first(where: { $0.frame.contains(CGPoint(x: bounds.midX, y: bounds.midY)) }) else { continue }
            var row = box(bounds); row["role"] = role; row["name"] = name; row["dockWindow"] = host.id
            let type = text(values[5]); if !type.isEmpty { row["type"] = type }
            items.append(row)
        }
        if !items.isEmpty {
            for (id, group) in Dictionary(grouping: items, by: { $0["dockWindow"] as! Int }) {
                scanned.append(["window": id, "finished": true, "label": "Dock", "elements": group.map { var row = $0; row["dockWindow"] = nil; return row }])
            }
        }
    }
    result["scanned"] = scanned
    result["unfinishedWindows"] = Array(Set(skipped))
}
result["elapsedMs"] = Int(Date().timeIntervalSince(started) * 1000)
emit(result, code: 0)
