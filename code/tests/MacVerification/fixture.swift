import AppKit

// A separate foreground application with known text, so capture and OCR read a
// real window of another process instead of anything drawn by the pet itself.
let application = NSApplication.shared
application.setActivationPolicy(.regular)
let window = NSWindow(contentRect: NSRect(x: 160, y: 260, width: 900, height: 420), styleMask: [.titled, .closable], backing: .buffered, defer: false)
window.title = "BigBlueFish Development"
let label = NSTextField(wrappingLabelWithString: "Developing BigBlueFish Project\n正在开发蓝色大肥鱼 macOS 版本")
label.font = .systemFont(ofSize: 40)
label.frame = NSRect(x: 40, y: 120, width: 820, height: 200)
window.contentView?.addSubview(label)
window.makeKeyAndOrderFront(nil)
application.activate()
application.run()
