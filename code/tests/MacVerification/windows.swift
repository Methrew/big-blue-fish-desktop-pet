import CoreGraphics
import Foundation

// Prints the on-screen windows of one process as JSON, in CoreGraphics points.
let pid = Int32(CommandLine.arguments[1]) ?? 0
let list = CGWindowListCopyWindowInfo([.optionOnScreenOnly], kCGNullWindowID) as? [[String: Any]] ?? []
let windows: [[String: Any]] = list.compactMap { info in
    guard (info[kCGWindowOwnerPID as String] as? Int32) == pid, let bounds = info[kCGWindowBounds as String] as? [String: Double] else { return nil }
    return ["id": info[kCGWindowNumber as String] ?? 0, "layer": info[kCGWindowLayer as String] ?? 0, "x": bounds["X"] ?? 0, "y": bounds["Y"] ?? 0,
            "width": bounds["Width"] ?? 0, "height": bounds["Height"] ?? 0, "title": info[kCGWindowName as String] ?? ""]
}
print(String(data: try JSONSerialization.data(withJSONObject: windows), encoding: .utf8)!)
