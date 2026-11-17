#import <AppKit/AppKit.h>
#import <CoreGraphics/CoreGraphics.h>

static NSWindow *ASPAWindow(void *handle) {
    if (!handle) return nil;
    id object = (__bridge id)handle;
    if ([object isKindOfClass:[NSWindow class]]) return object;
    if ([object respondsToSelector:@selector(window)]) return [object window];
    return nil;
}

void ASPASetClickThrough(void *handle) {
    NSWindow *window = ASPAWindow(handle);
    if (window) window.ignoresMouseEvents = YES;
}

static NSPoint ASPAQuartzTopLeft(CGFloat x, CGFloat y) {
    CGRect main = CGDisplayBounds(CGMainDisplayID());
    return NSMakePoint(x, CGRectGetHeight(main) - y);
}

void ASPASetQuartzTopLeft(void *handle, double x, double y) {
    NSWindow *window = ASPAWindow(handle);
    if (window) [window setFrameTopLeftPoint:ASPAQuartzTopLeft(x, y)];
}

void ASPAPositionBeside(void *handle, double left, double top, double right) {
    NSWindow *window = ASPAWindow(handle);
    if (!window) return;
    NSPoint topLeft = ASPAQuartzTopLeft(left, top);
    NSPoint gameCenter = ASPAQuartzTopLeft((left + right) / 2.0, top + 100);
    NSScreen *target = nil;
    for (NSScreen *screen in [NSScreen screens]) {
        if (NSPointInRect(gameCenter, screen.frame)) { target = screen; break; }
    }
    if (!target) target = [NSScreen mainScreen];
    NSRect visible = target ? target.visibleFrame : NSZeroRect;
    CGFloat width = window.frame.size.width;
    CGFloat height = window.frame.size.height;
    CGFloat x = right + 10;
    if (target && x + width > NSMaxX(visible)) {
        CGFloat leftSide = left - width - 10;
        if (leftSide >= NSMinX(visible)) x = leftSide;
        else x = right - width - 12; // Keep the panel attached to the game's right edge when neither side fits.
    }
    if (target) x = MIN(MAX(x, NSMinX(visible)), MAX(NSMinX(visible), NSMaxX(visible) - width));
    CGFloat y = topLeft.y;
    if (target) y = MIN(MAX(y, NSMinY(visible) + height), NSMaxY(visible));
    [window setFrameTopLeftPoint:NSMakePoint(x, y)];
}
