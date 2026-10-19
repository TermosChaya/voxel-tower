using System;
using System.Runtime.InteropServices;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// A run that only checks the game or takes its pictures (the self-test, a screenshot) plays out of sight: on macOS the
/// app hides itself as soon as it can and keeps no place in the Dock, so its window does not come up over whatever the
/// player is doing. It still draws every frame (the pictures are read from what it draws, not from the screen).
/// </summary>
public static class Backstage
{
    [DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern void Send(IntPtr receiver, IntPtr selector, long arg);

    /// <summary>Takes the app off the screen and out of the Dock (an accessory app, hidden); elsewhere does nothing.</summary>
    public static void Withdraw()
    {
        if (OS.GetName() != "macOS") return;
        try
        {
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            // NSApplicationActivationPolicyAccessory: no Dock icon, no menu bar of its own
            Send(app, sel_registerName("setActivationPolicy:"), 1L);
            Send(app, sel_registerName("hide:"), IntPtr.Zero);
        }
        catch (Exception e) { GD.PrintErr($"[voxel-tower] could not hide the window: {e.Message}"); }
    }
}
