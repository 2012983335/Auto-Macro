using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BAAxis;

internal static class Native
{
    public static readonly string[] Buttons = ["Left", "Right", "Middle", "X1", "X2"];
    public const nuint Marker = 0x42414158;
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] public struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput
    { public int X, Y; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardInput
    { public ushort Vk, Scan; public uint Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct KeyboardHook
    { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseHook
    { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    public delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandle(string? name);
    [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint milliseconds);
    [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint milliseconds);

    public static void Send(AxisEvent e)
    {
        var input = new Input();
        if (e.Action is ActionKind.KeyDown or ActionKind.KeyUp)
        {
            input.Type = 1;
            input.Data.Keyboard = new() { Scan = ushort.Parse(e.Code), Flags = 8u | (e.Extended ? 1u : 0u) | (e.Action == ActionKind.KeyUp ? 2u : 0u), Extra = Marker };
        }
        else
        {
            uint flags = 0x8000 | 0x4000 | 1;
            uint data = 0;
            if (e.Action is ActionKind.MouseDown or ActionKind.MouseUp)
            {
                flags |= e.Code switch { "Left" => 2u, "Right" => 8u, "Middle" => 32u, _ => 128u };
                if (e.Action == ActionKind.MouseUp) { flags &= ~((uint)2 | 8 | 32 | 128); flags |= e.Code switch { "Left" => 4u, "Right" => 16u, "Middle" => 64u, _ => 256u }; }
                data = e.Code == "X1" ? 1u : e.Code == "X2" ? 2u : 0u;
            }
            if (e.Action == ActionKind.Wheel) { flags |= 0x800; data = unchecked((uint)e.Delta); }
            var desktop = SystemInformation.VirtualScreen;
            input.Data.Mouse = new() { X = (int)Math.Round((e.X - desktop.Left) * 65535.0 / (desktop.Width - 1)), Y = (int)Math.Round((e.Y - desktop.Top) * 65535.0 / (desktop.Height - 1)), Flags = flags, Data = data, Extra = Marker };
        }
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "输入发送失败，请检查游戏与软件权限等级。");
    }
}

internal sealed class Recorder : IDisposable
{
    private readonly Native.HookProc keyboard, mouse;
    private readonly nint keyboardHandle, mouseHandle;
    public Action<AxisEvent>? Captured;
    public Action<int>? ControlKey;
    private readonly HashSet<uint> controlKeysHeld = [];
    public Recorder()
    {
        keyboard = OnKeyboard; mouse = OnMouse;
        keyboardHandle = Native.SetWindowsHookEx(13, keyboard, Native.GetModuleHandle(null), 0);
        mouseHandle = Native.SetWindowsHookEx(14, mouse, Native.GetModuleHandle(null), 0);
        if (keyboardHandle == 0 || mouseHandle == 0) { Dispose(); throw new Win32Exception("无法注册输入录制钩子。"); }
    }
    private nint OnKeyboard(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<Native.KeyboardHook>(data);
            if (ControlKey != null && k.Extra != Native.Marker && k.Vk is >= 0x75 and <= 0x78)
            {
                if ((k.Flags & 128) != 0) controlKeysHeld.Remove(k.Vk);
                else if (controlKeysHeld.Add(k.Vk)) ControlKey((int)k.Vk-0x74);
                return 1;
            }
            if (k.Extra != Native.Marker && k.Vk is not (>= 0x75 and <= 0x78))
                Captured?.Invoke(new() { Action = (k.Flags & 128) != 0 ? ActionKind.KeyUp : ActionKind.KeyDown, Code = k.Scan.ToString(), Extended = (k.Flags & 1) != 0 });
        }
        return Native.CallNextHookEx(0, code, message, data);
    }
    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0)
        {
            var m = Marshal.PtrToStructure<Native.MouseHook>(data);
            if (m.Extra != Native.Marker)
            {
                var action = (int)message switch { 0x200 => ActionKind.Move, 0x201 or 0x204 or 0x207 or 0x20B => ActionKind.MouseDown, 0x202 or 0x205 or 0x208 or 0x20C => ActionKind.MouseUp, 0x20A => ActionKind.Wheel, _ => (ActionKind?)null };
                if (action.HasValue) Captured?.Invoke(new() { Action = action.Value, X = m.Point.X, Y = m.Point.Y, Delta = (short)(m.Data >> 16), Code = (int)message switch { 0x201 or 0x202 => "Left", 0x204 or 0x205 => "Right", 0x207 or 0x208 => "Middle", 0x20B or 0x20C => (m.Data >> 16) == 1 ? "X1" : "X2", _ => "" } });
            }
        }
        return Native.CallNextHookEx(0, code, message, data);
    }
    public void Dispose() { if (keyboardHandle != 0) Native.UnhookWindowsHookEx(keyboardHandle); if (mouseHandle != 0) Native.UnhookWindowsHookEx(mouseHandle); }
}
