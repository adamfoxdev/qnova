using Raylib_cs;

/// <summary>Loads a system TTF (no font files ship with the game); falls back to Raylib's built-in bitmap font.</summary>
static class Fonts
{
    static readonly string[] Regular =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/dejavu/DejaVuSansMono.ttf",
        "/usr/share/fonts/TTF/DejaVuSansMono.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationMono-Regular.ttf",
        "/usr/share/fonts/liberation-mono/LiberationMono-Regular.ttf",
        "/usr/share/fonts/truetype/ubuntu/UbuntuMono-R.ttf",
        "/System/Library/Fonts/Menlo.ttc",
        "/System/Library/Fonts/Monaco.ttf",
        "/Library/Fonts/Courier New.ttf",
        @"C:\Windows\Fonts\consola.ttf",
        @"C:\Windows\Fonts\cour.ttf",
    };

    // Heavy faces suit the grim menu look.
    static readonly string[] Bold =
    {
        "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf",
        "/usr/share/fonts/dejavu/DejaVuSansMono-Bold.ttf",
        "/usr/share/fonts/TTF/DejaVuSansMono-Bold.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationMono-Bold.ttf",
        "/usr/share/fonts/liberation-mono/LiberationMono-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
        "/System/Library/Fonts/Supplemental/Impact.ttf",
        "/System/Library/Fonts/Supplemental/Courier New Bold.ttf",
        @"C:\Windows\Fonts\consolab.ttf",
        @"C:\Windows\Fonts\impact.ttf",
        @"C:\Windows\Fonts\courbd.ttf",
    };

    public static Font Load(int size, bool bold, out bool custom)
    {
        foreach (var path in bold ? Bold.Concat(Regular) : Regular)
        {
            if (!File.Exists(path)) continue;
            var f = Raylib.LoadFontEx(path, size, null, 0);   // null codepoints = basic ASCII
            if (f.Texture.Id == 0) continue;
            Raylib.SetTextureFilter(f.Texture, TextureFilter.Bilinear);
            custom = true;
            return f;
        }
        custom = false;
        return Raylib.GetFontDefault();
    }
}
