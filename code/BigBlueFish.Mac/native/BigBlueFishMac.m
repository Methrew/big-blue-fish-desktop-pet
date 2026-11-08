// Native bridge for the macOS edition. It keeps every AppKit, ScreenCaptureKit,
// Vision and IOKit call behind a small C ABI that BigBlueFish.Mac P/Invokes:
// pet window behaviour and pointer pass-through, foreground window, idle and
// power state, desktop capture that excludes the pet, and on-device OCR.
#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#import <IOKit/ps/IOPowerSources.h>
#import <IOKit/ps/IOPSKeys.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#import <Vision/Vision.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#define BBF_EXPORT __attribute__((visibility("default")))

typedef struct {
    int32_t pid;        // frontmost application
    uint32_t window;    // its frontmost normal window, 0 when it shows none
    int32_t desktop;    // Finder is frontmost without an open window
    int32_t fullscreen; // that window covers a whole display
} bbf_foreground;

static char *copy_string(NSString *text) { return strdup(text.UTF8String ?: ""); }

static NSWindow *window_from(void *handle) {
    id object = (__bridge id)handle;
    return [object isKindOfClass:NSWindow.class] ? (NSWindow *)object : nil;
}

static NSArray<NSDictionary *> *window_list(CGWindowListOption options, CGWindowID relative) {
    CFArrayRef list = CGWindowListCopyWindowInfo(options, relative);
    return list ? CFBridgingRelease(list) : @[];
}

BBF_EXPORT void bbf_free(void *pointer) { free(pointer); }

BBF_EXPORT double bbf_idle_seconds(void) {
    return CGEventSourceSecondsSinceLastEventType(kCGEventSourceStateCombinedSessionState, kCGAnyInputEventType);
}

BBF_EXPORT int bbf_on_ac(void) {
    CFTypeRef info = IOPSCopyPowerSourcesInfo();
    if (!info) return 1;
    CFStringRef type = IOPSGetProvidingPowerSourceType(info);
    int ac = !(type && CFStringCompare(type, CFSTR(kIOPSBatteryPowerValue), 0) == kCFCompareEqualTo);
    CFRelease(info);
    return ac;
}

// Pet, bubble and input windows follow the user across Spaces, stay put in
// Mission Control, may float over full-screen apps and draw no window shadow.
BBF_EXPORT void bbf_configure_window(void *handle) {
    NSWindow *window = window_from(handle);
    if (!window) return;
    window.hasShadow = NO;
    window.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorStationary
        | NSWindowCollectionBehaviorIgnoresCycle | NSWindowCollectionBehaviorFullScreenAuxiliary;
}

// Pointer position relative to the window's top-left corner, in points.
BBF_EXPORT int bbf_pointer_in_window(void *handle, double *x, double *y, double *width, double *height) {
    NSWindow *window = window_from(handle);
    if (!window) { *x = *y = -1; *width = *height = 0; return 0; }
    NSPoint point = NSEvent.mouseLocation;
    NSRect frame = window.frame;
    *x = point.x - frame.origin.x;
    *y = NSMaxY(frame) - point.y;
    *width = frame.size.width;
    *height = frame.size.height;
    return (int)NSEvent.pressedMouseButtons;
}

// Launching activates the app; the pet then hands the focus back to whatever
// application the user was working in before it started.
static NSRunningApplication *previous_frontmost;
BBF_EXPORT void bbf_remember_frontmost(void) {
    NSRunningApplication *application = NSWorkspace.sharedWorkspace.frontmostApplication;
    if (application.processIdentifier != getpid()) previous_frontmost = application;
}
BBF_EXPORT void bbf_restore_frontmost(void) {
    NSRunningApplication *previous = previous_frontmost;
    previous_frontmost = nil;
    if (!previous || previous.terminated || !NSRunningApplication.currentApplication.active) return;
    [NSApp yieldActivationToApplication:previous];
    [previous activateFromApplication:NSRunningApplication.currentApplication options:0];
}

BBF_EXPORT void bbf_set_ignores_mouse(void *handle, int ignore) {
    NSWindow *window = window_from(handle);
    if (window && window.ignoresMouseEvents != (ignore != 0)) window.ignoresMouseEvents = ignore != 0;
}

BBF_EXPORT void bbf_foreground_snapshot(bbf_foreground *result) {
    memset(result, 0, sizeof *result);
    @autoreleasepool {
        NSRunningApplication *application = NSWorkspace.sharedWorkspace.frontmostApplication;
        if (!application) { result->desktop = 1; return; }
        pid_t pid = application.processIdentifier;
        result->pid = pid;
        // The list is ordered front to back; layer 0 holds ordinary document windows.
        for (NSDictionary *info in window_list(kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, kCGNullWindowID)) {
            if ([info[(id)kCGWindowOwnerPID] intValue] != pid || [info[(id)kCGWindowLayer] intValue] != 0) continue;
            if ([info[(id)kCGWindowAlpha] doubleValue] <= 0) continue;
            CGRect bounds;
            if (!CGRectMakeWithDictionaryRepresentation((__bridge CFDictionaryRef)info[(id)kCGWindowBounds], &bounds)) continue;
            if (bounds.size.width < 48 || bounds.size.height < 48) continue;
            result->window = [info[(id)kCGWindowNumber] unsignedIntValue];
            CGDirectDisplayID displays[16];
            uint32_t count = 0;
            if (CGGetActiveDisplayList(16, displays, &count) == kCGErrorSuccess)
                for (uint32_t i = 0; i < count; i++)
                    if (CGRectEqualToRect(CGRectIntegral(bounds), CGDisplayBounds(displays[i]))) result->fullscreen = 1;
            break;
        }
        if (!result->window && [application.bundleIdentifier isEqualToString:@"com.apple.finder"]) result->desktop = 1;
    }
}

// Returns 1 while the window is on screen, with its owner's pid.
BBF_EXPORT int bbf_window_owner(uint32_t window, int32_t *pid) {
    *pid = 0;
    if (!window) return 0;
    @autoreleasepool {
        NSDictionary *info = window_list(kCGWindowListOptionIncludingWindow, window).firstObject;
        if (!info) return 0;
        *pid = [info[(id)kCGWindowOwnerPID] intValue];
        return [info[(id)kCGWindowIsOnscreen] boolValue] ? 1 : 0;
    }
}

// JSON {"app","bundle","title"}; titles need the screen recording permission.
BBF_EXPORT char *bbf_window_identity(uint32_t window) {
    @autoreleasepool {
        NSDictionary *info = window ? window_list(kCGWindowListOptionIncludingWindow, window).firstObject : nil;
        pid_t pid = info ? [info[(id)kCGWindowOwnerPID] intValue] : NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier;
        NSRunningApplication *application = [NSRunningApplication runningApplicationWithProcessIdentifier:pid];
        NSDictionary *identity = @{
            @"app": application.localizedName ?: info[(id)kCGWindowOwnerName] ?: @"",
            @"bundle": application.bundleIdentifier ?: @"",
            @"title": info[(id)kCGWindowName] ?: @"",
        };
        NSData *json = [NSJSONSerialization dataWithJSONObject:identity options:0 error:nil];
        return copy_string([[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
    }
}

BBF_EXPORT int bbf_screen_capture_access(int request) {
    if (CGPreflightScreenCaptureAccess()) return 1;
    return request && CGRequestScreenCaptureAccess() ? 1 : 0;
}

static NSString *permission_hint(NSError *error) {
    if (!CGPreflightScreenCaptureAccess())
        return @"读屏需要屏幕录制权限：请在“系统设置 › 隐私与安全性 › 录屏与系统录音”中允许蓝色大肥鱼，然后重新启动桌宠。";
    return error.localizedDescription ?: @"无法读取屏幕内容。";
}

static SCShareableContent *shareable_content(NSString **message) {
    __block SCShareableContent *result = nil;
    __block NSError *failure = nil;
    dispatch_semaphore_t done = dispatch_semaphore_create(0);
    [SCShareableContent getShareableContentExcludingDesktopWindows:NO onScreenWindowsOnly:YES
        completionHandler:^(SCShareableContent *content, NSError *error) { result = content; failure = error; dispatch_semaphore_signal(done); }];
    if (dispatch_semaphore_wait(done, dispatch_time(DISPATCH_TIME_NOW, 15 * NSEC_PER_SEC)) != 0) { *message = @"读取屏幕内容超时。"; return nil; }
    if (!result) *message = permission_hint(failure);
    return result;
}

static CGImageRef capture_image(SCContentFilter *filter, SCStreamConfiguration *configuration, NSString **message) {
    __block CGImageRef image = NULL;
    __block NSError *failure = nil;
    dispatch_semaphore_t done = dispatch_semaphore_create(0);
    [SCScreenshotManager captureImageWithFilter:filter configuration:configuration completionHandler:^(CGImageRef captured, NSError *error) {
        if (captured) image = CGImageRetain(captured);
        failure = error;
        dispatch_semaphore_signal(done);
    }];
    if (dispatch_semaphore_wait(done, dispatch_time(DISPATCH_TIME_NOW, 15 * NSEC_PER_SEC)) != 0) { *message = @"截图超时。"; return NULL; }
    if (!image) *message = permission_hint(failure);
    return image;
}

static SCStreamConfiguration *configuration_for(CGSize points, double scale) {
    SCStreamConfiguration *configuration = [SCStreamConfiguration new];
    configuration.width = (size_t)MAX(1, lround(points.width * scale));
    configuration.height = (size_t)MAX(1, lround(points.height * scale));
    configuration.showsCursor = NO;
    configuration.pixelFormat = kCVPixelFormatType_32BGRA;
    configuration.colorSpaceName = kCGColorSpaceSRGB;
    return configuration;
}

// Scale so the longer edge stays within the caller's limit, never above native pixels.
static double fit_scale(CGSize points, double native, int maximum) {
    double longest = MAX(points.width, points.height);
    return longest <= 0 ? native : MIN(native, maximum / longest);
}

// Captures one window (desktop independent, so the pet never covers it) or the
// whole desktop of all displays with this process's own windows excluded.
// Pixels are BGRA, premultiplied, top row first; free them with bbf_free.
BBF_EXPORT int bbf_capture(uint32_t window, int whole, int maximum, uint8_t **pixels, int *width, int *height, int *stride, char **error) {
    *pixels = NULL; *width = *height = *stride = 0; *error = NULL;
    @autoreleasepool {
        NSString *message = nil;
        SCShareableContent *content = shareable_content(&message);
        if (!content) { *error = copy_string(message); return 0; }
        CGColorSpaceRef space = CGColorSpaceCreateWithName(kCGColorSpaceSRGB);
        CGContextRef context = NULL;
        if (!whole && window) {
            SCWindow *target = nil;
            for (SCWindow *candidate in content.windows) if (candidate.windowID == window) { target = candidate; break; }
            if (!target) { CGColorSpaceRelease(space); *error = copy_string(@"前台窗口不可见。"); return 0; }
            SCContentFilter *filter = [[SCContentFilter alloc] initWithDesktopIndependentWindow:target];
            CGSize size = filter.contentRect.size;
            SCStreamConfiguration *configuration = configuration_for(size, fit_scale(size, filter.pointPixelScale, maximum));
            configuration.ignoreShadowsSingleWindow = YES;
            CGImageRef image = capture_image(filter, configuration, &message);
            if (!image) { CGColorSpaceRelease(space); *error = copy_string(message); return 0; }
            size_t w = CGImageGetWidth(image), h = CGImageGetHeight(image);
            context = CGBitmapContextCreate(NULL, w, h, 8, 0, space, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
            if (context) CGContextDrawImage(context, CGRectMake(0, 0, w, h), image);
            CGImageRelease(image);
        } else {
            SCRunningApplication *own = nil;
            for (SCRunningApplication *application in content.applications) if (application.processID == getpid()) { own = application; break; }
            CGRect desktop = CGRectNull;
            double native = 1;
            for (SCDisplay *display in content.displays) {
                desktop = CGRectUnion(desktop, display.frame);
                SCContentFilter *probe = [[SCContentFilter alloc] initWithDisplay:display excludingWindows:@[]];
                native = MAX(native, probe.pointPixelScale);
            }
            if (CGRectIsNull(desktop)) { CGColorSpaceRelease(space); *error = copy_string(@"没有可用的显示器。"); return 0; }
            double scale = fit_scale(desktop.size, native, maximum);
            size_t w = (size_t)MAX(1, lround(desktop.size.width * scale)), h = (size_t)MAX(1, lround(desktop.size.height * scale));
            context = CGBitmapContextCreate(NULL, w, h, 8, 0, space, kCGImageAlphaPremultipliedFirst | kCGBitmapByteOrder32Little);
            if (context) {
                CGContextSetRGBFillColor(context, 0, 0, 0, 1);
                CGContextFillRect(context, CGRectMake(0, 0, w, h));
                for (SCDisplay *display in content.displays) {
                    SCContentFilter *filter = [[SCContentFilter alloc] initWithDisplay:display
                        excludingApplications:own ? @[own] : @[] exceptingWindows:@[]];
                    CGImageRef image = capture_image(filter, configuration_for(display.frame.size, scale), &message);
                    if (!image) { CGContextRelease(context); CGColorSpaceRelease(space); *error = copy_string(message); return 0; }
                    // Display frames use a top-left origin; bitmap contexts draw from the bottom left.
                    CGRect target = CGRectMake((display.frame.origin.x - desktop.origin.x) * scale,
                        (CGRectGetMaxY(desktop) - CGRectGetMaxY(display.frame)) * scale,
                        display.frame.size.width * scale, display.frame.size.height * scale);
                    CGContextDrawImage(context, target, image);
                    CGImageRelease(image);
                }
            }
        }
        CGColorSpaceRelease(space);
        if (!context) { *error = copy_string(@"截图尺寸无效。"); return 0; }
        *width = (int)CGBitmapContextGetWidth(context);
        *height = (int)CGBitmapContextGetHeight(context);
        *stride = (int)CGBitmapContextGetBytesPerRow(context);
        size_t bytes = (size_t)*stride * (size_t)*height;
        *pixels = malloc(bytes);
        if (*pixels) memcpy(*pixels, CGBitmapContextGetData(context), bytes);
        CGContextRelease(context);
        if (!*pixels) { *error = copy_string(@"内存不足。"); return 0; }
        return 1;
    }
}

static NSArray<VNRecognizedTextObservation *> *recognize(NSData *image, NSArray<NSString *> *languages, NSError **failure) {
    VNRecognizeTextRequest *request = [VNRecognizeTextRequest new];
    request.recognitionLevel = VNRequestTextRecognitionLevelAccurate;
    request.usesLanguageCorrection = YES;
    request.recognitionLanguages = languages;
    VNImageRequestHandler *handler = [[VNImageRequestHandler alloc] initWithData:image options:@{}];
    return [handler performRequests:@[request] error:failure] ? request.results ?: @[] : nil;
}

static NSString *best_text(VNRecognizedTextObservation *line) {
    return [[line topCandidates:1].firstObject.string stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet] ?: @"";
}

static BOOL has_cjk(NSString *text) {
    for (NSUInteger i = 0; i < text.length; i++) {
        unichar c = [text characterAtIndex:i];
        if ((c >= 0x2E80 && c <= 0x9FFF) || (c >= 0xAC00 && c <= 0xD7AF) || (c >= 0xF900 && c <= 0xFAFF)) return YES;
    }
    return NO;
}

static double overlap(CGRect a, CGRect b) {
    CGRect shared = CGRectIntersection(a, b);
    if (CGRectIsNull(shared)) return 0;
    double area = shared.size.width * shared.size.height, smaller = MIN(a.size.width * a.size.height, b.size.width * b.size.height);
    return smaller > 0 ? area / smaller : 0;
}

// Recognizes text in an encoded image (PNG/JPEG) with the on-device Vision
// framework. Chinese has to lead the language list, but that model misreads
// purely Latin lines ("Build" as "Buiia"), so lines without CJK characters take
// the matching line of a second, English-only pass. Returns a JSON array of
// {Text, X, Y, Width, Height} line boxes in image pixels with a top-left origin,
// or NULL with *error set.
BBF_EXPORT char *bbf_recognize_text(const uint8_t *data, int length, char **error) {
    *error = NULL;
    @autoreleasepool {
        NSData *image = [NSData dataWithBytes:data length:(NSUInteger)length];
        CGImageSourceRef source = CGImageSourceCreateWithData((__bridge CFDataRef)image, NULL);
        NSDictionary *properties = source ? CFBridgingRelease(CGImageSourceCopyPropertiesAtIndex(source, 0, NULL)) : nil;
        if (source) CFRelease(source);
        double width = [properties[(id)kCGImagePropertyPixelWidth] doubleValue], height = [properties[(id)kCGImagePropertyPixelHeight] doubleValue];
        if (width <= 0 || height <= 0) { *error = copy_string(@"OCR 输入不是有效图像。"); return NULL; }
        NSError *failure = nil;
        NSArray<VNRecognizedTextObservation *> *lines = recognize(image, @[@"zh-Hans", @"zh-Hant", @"en-US"], &failure);
        if (!lines) lines = recognize(image, @[@"zh-Hans", @"en-US"], &failure);
        if (!lines) { *error = copy_string([@"本地 OCR 未完成：" stringByAppendingString:failure.localizedDescription ?: @""]); return NULL; }
        NSArray<VNRecognizedTextObservation *> *latin = nil;
        for (VNRecognizedTextObservation *line in lines)
            if (!has_cjk(best_text(line))) { latin = recognize(image, @[@"en-US"], nil) ?: @[]; break; }
        NSMutableArray *words = [NSMutableArray array];
        for (VNRecognizedTextObservation *line in lines) {
            NSString *text = best_text(line);
            if (text.length == 0) continue;
            if (!has_cjk(text)) {
                VNRecognizedTextObservation *match = nil;
                double score = .5;
                for (VNRecognizedTextObservation *candidate in latin) {
                    double value = overlap(line.boundingBox, candidate.boundingBox);
                    if (value > score) { score = value; match = candidate; }
                }
                if (match && best_text(match).length > 0 && !has_cjk(best_text(match))) text = best_text(match);
            }
            CGRect box = line.boundingBox; // normalized, bottom-left origin
            [words addObject:@{ @"Text": text, @"X": @(box.origin.x * width), @"Y": @((1 - CGRectGetMaxY(box)) * height),
                @"Width": @(box.size.width * width), @"Height": @(box.size.height * height) }];
        }
        NSData *json = [NSJSONSerialization dataWithJSONObject:words options:0 error:nil];
        return copy_string([[NSString alloc] initWithData:json encoding:NSUTF8StringEncoding]);
    }
}
