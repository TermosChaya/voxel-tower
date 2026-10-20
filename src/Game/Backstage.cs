using System;
using System.Runtime.InteropServices;
using Godot;

namespace VoxelTower.Game;

/// <summary>
/// A run that only checks the game or takes its pictures (the self-test, a screenshot, the trailer) plays out of sight:
/// on macOS the app hides itself as soon as it can and keeps no place in the Dock, so its window does not come up over
/// whatever the player is doing. Godot draws only a window the window server says can be seen, and once it has noticed
/// the window hidden it would draw nothing more, the pictures going stale or black; so a hidden run draws every frame
/// itself, last of all, when everything has moved (the pictures are read from what it draws, not from the screen).
/// </summary>
public static partial class Backstage
{
    [DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.dylib")] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")] static extern void Send(IntPtr receiver, IntPtr selector, long arg);

    /// <summary>Takes the app off the screen and out of the Dock (an accessory app, hidden), and draws its frames for it; elsewhere does nothing.</summary>
    public static void Withdraw(Node host)
    {
        if (OS.GetName() != "macOS") return;
        try
        {
            var app = Send(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
            // NSApplicationActivationPolicyAccessory: no Dock icon, no menu bar of its own
            Send(app, sel_registerName("setActivationPolicy:"), 1L);
            Send(app, sel_registerName("hide:"), IntPtr.Zero);
            RenderingServer.RenderLoopEnabled = false;
            host.AddChild(new Projector());
        }
        catch (Exception e) { GD.PrintErr($"[voxel-tower] could not hide the window: {e.Message}"); }
    }

    /// <summary>Draws the frame once every node has had its turn (the render loop being handed over to it).</summary>
    partial class Projector : Node
    {
        public override void _Ready()
        {
            ProcessPriority = int.MaxValue;
            ProcessMode = ProcessModeEnum.Always;
        }

        public override void _Process(double delta) => RenderingServer.ForceDraw(true, delta);
    }
}
