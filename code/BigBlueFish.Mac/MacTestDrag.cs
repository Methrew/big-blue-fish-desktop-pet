// Fork-original file of the Taiwan Traditional Chinese edition by Methrew (2026-11-15); not part of upstream Bai-Cute/big-blue-fish-desktop-pet.
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

// --test-mode only (the native-drop command): the part of a file drag from Finder that happens in this app. A real drag
// needs mouse events, and posting those needs the Accessibility permission, so the window's AvnView is handed the
// NSDraggingDestination calls AppKit makes (draggingEntered:, draggingUpdated:, then prepareForDragOperation:,
// performDragOperation: and concludeDragOperation:, or draggingExited:) with an NSDraggingInfo of its own whose pasteboard
// holds file URLs, written as Finder writes them (or text). Which window AppKit finds under the pointer is left out: for the pet,
// UpdatePointerPassThrough (MacWindowBehavior.cs) decides that.
internal static partial class CompanionNative
{
    // What a drag from Finder allows while no key is held: copy, link, generic, move and delete.
    const nuint FinderDragOperations = 1 | 2 | 4 | 16 | 32;
    delegate CGPoint PointMethod(IntPtr self, IntPtr selector);
    delegate IntPtr ObjectMethod(IntPtr self, IntPtr selector);
    delegate nuint OperationsMethod(IntPtr self, IntPtr selector);
    // Static, so the methods the class points at are never collected.
    static readonly PointMethod testDragLocationMethod = (_, _) => testDragLocation;
    static readonly ObjectMethod testDragPasteboardMethod = (_, _) => testDragPasteboard, testDragNothing = (_, _) => IntPtr.Zero;
    static readonly OperationsMethod testDragOperations = (_, _) => FinderDragOperations;
    static IntPtr testDragClass, testDragPasteboard;
    static CGPoint testDragLocation;

    // The operation each call gave AppKit, the drop made in the middle of the window. Each item is a file's path, or
    // text:<words> for text dragged from a page.
    internal static (ulong Entered, ulong Updated, bool Prepared, bool Performed) DropForTest(Avalonia.Controls.TopLevel topLevel, IReadOnlyList<string> items)
    {
        IntPtr window = WindowOf(topLevel.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero), view = DragDestination(window);
        if (view == IntPtr.Zero) throw new InvalidOperationException("the window has no view that takes drops");
        var bounds = SendRect(view, "bounds");
        // draggingLocation is in the window's coordinates.
        testDragLocation = objc_msgSend_convert(view, Selector("convertPoint:toView:"), new CGPoint { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 }, IntPtr.Zero);
        IntPtr pasteboard = Send(Class("NSPasteboard"), "pasteboardWithUniqueName"), info = IntPtr.Zero;
        try
        {
            objc_msgSend(pasteboard, Selector("clearContents"));
            IntPtr urls = Send(Class("NSMutableArray"), "array");
            foreach (var item in items)
            {
                bool text = item.StartsWith("text:", StringComparison.Ordinal);
                string content = text ? item[5..] : item;
                // addObject: nil raises an Objective-C exception, which would end the app rather than fail the command.
                if (content.Length == 0) throw new ArgumentException("an empty item to drag");
                IntPtr value = objc_msgSend_utf8(Class("NSString"), Selector("stringWithUTF8String:"), content);
                IntPtr dragged = text ? value : objc_msgSend_id(Class("NSURL"), Selector("fileURLWithPath:"), value);
                if (dragged == IntPtr.Zero) throw new ArgumentException("not a path or text AppKit can take: " + item);
                objc_msgSend_ptr(urls, Selector("addObject:"), dragged);
            }
            if (!SendBool(pasteboard, "writeObjects:", urls)) throw new InvalidOperationException("the pasteboard took nothing");
            testDragPasteboard = pasteboard;
            info = Send(Send(DraggingInfoClass(), "alloc"), "init");
            ulong entered = objc_msgSend_operation(view, Selector("draggingEntered:"), info), updated = objc_msgSend_operation(view, Selector("draggingUpdated:"), info);
            // AppKit drops only where the last update allowed an operation; elsewhere the drag just leaves.
            if (updated == 0) { objc_msgSend_ptr(view, Selector("draggingExited:"), info); return (entered, updated, false, false); }
            bool prepared = SendBool(view, "prepareForDragOperation:", info), performed = prepared && SendBool(view, "performDragOperation:", info);
            if (performed) objc_msgSend_ptr(view, Selector("concludeDragOperation:"), info);
            return (entered, updated, prepared, performed);
        }
        finally
        {
            testDragPasteboard = IntPtr.Zero;
            if (info != IntPtr.Zero) objc_msgSend(info, Selector("release"));
            objc_msgSend(pasteboard, Selector("releaseGlobally"));
        }
    }

    // The view AppKit hands a drag to: one registered for dragged types, as Avalonia's AvnView is (a subview of the content
    // view). Every NSView answers the NSDraggingDestination calls, and AppKit's own answers need AppKit's NSDraggingInfo.
    static IntPtr DragDestination(IntPtr window)
    {
        IntPtr content = Send(window, "contentView"), subviews = Send(content, "subviews");
        long count = subviews == IntPtr.Zero ? 0 : (long)Send(subviews, "count");
        for (long i = 0; i <= count; i++)
        {
            IntPtr view = i == count ? content : objc_msgSend_index(subviews, Selector("objectAtIndex:"), (nuint)i), types = Send(view, "registeredDraggedTypes");
            if (types != IntPtr.Zero && (long)Send(types, "count") > 0) return view;
        }
        return IntPtr.Zero;
    }

    static IntPtr DraggingInfoClass()
    {
        if (testDragClass != IntPtr.Zero) return testDragClass;
        const string name = "BigBlueFishTestDraggingInfo";
        IntPtr type = objc_allocateClassPair(Class("NSObject"), name, 0);
        if (type == IntPtr.Zero) return testDragClass = Class(name);
        class_addMethod(type, Selector("draggingLocation"), Marshal.GetFunctionPointerForDelegate(testDragLocationMethod), "{CGPoint=dd}16@0:8");
        class_addMethod(type, Selector("draggingPasteboard"), Marshal.GetFunctionPointerForDelegate(testDragPasteboardMethod), "@16@0:8");
        class_addMethod(type, Selector("draggingSourceOperationMask"), Marshal.GetFunctionPointerForDelegate(testDragOperations), "Q16@0:8");
        // No source in this app (Avalonia looks for its own), no image, no window of the drag's own.
        foreach (var nothing in new[] { "draggingSource", "draggedImage", "draggingDestinationWindow" })
            class_addMethod(type, Selector(nothing), Marshal.GetFunctionPointerForDelegate(testDragNothing), "@16@0:8");
        if (objc_getProtocol("NSDraggingInfo") is var protocol && protocol != IntPtr.Zero) class_addProtocol(type, protocol);
        objc_registerClassPair(type);
        return testDragClass = type;
    }

    [DllImport(Objc)] static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, nint extraBytes);
    [DllImport(Objc)] static extern void objc_registerClassPair(IntPtr type);
    [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);
    [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addProtocol(IntPtr type, IntPtr protocol);
    [DllImport(Objc)] static extern IntPtr objc_getProtocol(string name);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_id(IntPtr target, IntPtr selector, IntPtr argument);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr objc_msgSend_utf8(IntPtr target, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string text);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern ulong objc_msgSend_operation(IntPtr target, IntPtr selector, IntPtr info);
    [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern CGPoint objc_msgSend_convert(IntPtr target, IntPtr selector, CGPoint point, IntPtr view);
}
