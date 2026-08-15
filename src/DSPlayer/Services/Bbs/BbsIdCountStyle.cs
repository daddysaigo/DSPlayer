using System.Windows.Media;

namespace DSPlayer.Services.Bbs;

/// <summary>2ch-browser style ID appearance count colors.</summary>
public static class BbsIdCountStyle
{
    public static (System.Windows.Media.Color Color, bool Bold) ForCount(int count)
    {
        if (count <= 1)
            return (System.Windows.Media.Color.FromRgb(0x00, 0x55, 0xCC), false);
        if (count == 2)
            return (System.Windows.Media.Color.FromRgb(0x00, 0x80, 0x80), false);
        if (count == 3)
            return (System.Windows.Media.Color.FromRgb(0x22, 0x8B, 0x22), false);
        if (count == 4)
            return (System.Windows.Media.Color.FromRgb(0xC0, 0x60, 0x00), false);
        if (count < 10)
            return (System.Windows.Media.Color.FromRgb(0xD0, 0x20, 0x20), false);
        return (System.Windows.Media.Color.FromRgb(0xA0, 0x00, 0x00), false);
    }
}
