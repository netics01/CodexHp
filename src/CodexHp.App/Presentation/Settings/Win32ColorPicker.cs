using System.Runtime.InteropServices;
using CodexHp.Core.Settings;

namespace CodexHp.App.Presentation.Settings;

internal interface IColorPicker
{
    ColorValue? PickColor(nint ownerWindow, ColorValue current);
}

internal sealed class Win32ColorPicker : IColorPicker
{
    private const uint InitializeColor = 0x00000001;
    private const uint FullOpen = 0x00000002;
    private const uint AnyColor = 0x00000100;
    private readonly int[] customColors = new int[16];

    public ColorValue? PickColor(nint ownerWindow, ColorValue current)
    {
        var customColorBuffer = Marshal.AllocHGlobal(this.customColors.Length * sizeof(int));
        try
        {
            Marshal.Copy(this.customColors, 0, customColorBuffer, this.customColors.Length);
            var dialog = new ChooseColorData
            {
                StructSize = (uint)Marshal.SizeOf<ChooseColorData>(),
                OwnerWindow = ownerWindow,
                ResultColor = ToColorRef(current),
                CustomColors = customColorBuffer,
                Flags = InitializeColor | FullOpen | AnyColor,
            };
            var accepted = NativeMethods.ChooseColor(ref dialog);
            Marshal.Copy(customColorBuffer, this.customColors, 0, this.customColors.Length);
            return accepted ? FromColorRef(dialog.ResultColor) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(customColorBuffer);
        }
    }

    internal static uint ToColorRef(ColorValue color) =>
        color.Red | ((uint)color.Green << 8) | ((uint)color.Blue << 16);

    internal static ColorValue FromColorRef(uint colorRef) => new(
        Red: (byte)(colorRef & 0xFF),
        Green: (byte)((colorRef >> 8) & 0xFF),
        Blue: (byte)((colorRef >> 16) & 0xFF));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ChooseColorData
    {
        public uint StructSize;
        public nint OwnerWindow;
        public nint Instance;
        public uint ResultColor;
        public nint CustomColors;
        public uint Flags;
        public nint CustomData;
        public nint Hook;
        public nint TemplateName;
    }

    private static class NativeMethods
    {
        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChooseColorW")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ChooseColor(ref ChooseColorData chooseColor);
    }
}
