import AppKit
import CoreGraphics
import Foundation

let frontmostPid = NSWorkspace.shared.frontmostApplication?.processIdentifier ?? 0
let entries = CGWindowListCopyWindowInfo([.optionOnScreenOnly, .excludeDesktopElements], kCGNullWindowID)
    as? [[String: Any]] ?? []

var windows: [[String: Any]] = []
for entry in entries {
    guard let layer = entry[kCGWindowLayer as String] as? Int, layer == 0,
          let id = entry[kCGWindowNumber as String] as? Int,
          let pid = entry[kCGWindowOwnerPID as String] as? Int,
          pid != ProcessInfo.processInfo.processIdentifier,
          let bounds = entry[kCGWindowBounds as String] as? [String: Any],
          let x = bounds["X"] as? Double,
          let y = bounds["Y"] as? Double,
          let width = bounds["Width"] as? Double,
          let height = bounds["Height"] as? Double,
          width >= 100, height >= 100 else { continue }

    windows.append([
        "id": id,
        "pid": pid,
        "title": entry[kCGWindowName as String] as? String ?? "",
        "owner": entry[kCGWindowOwnerName as String] as? String ?? "",
        "x": Int(x.rounded()),
        "y": Int(y.rounded()),
        "width": Int(width.rounded()),
        "height": Int(height.rounded())
    ])
}

let result: [String: Any] = ["frontmostPid": Int(frontmostPid), "windows": windows]
let data = try JSONSerialization.data(withJSONObject: result)
FileHandle.standardOutput.write(data)
