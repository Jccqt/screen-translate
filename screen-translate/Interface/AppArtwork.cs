namespace screen_translate.Interface;

/// <summary>Original, size-specific brand exports, embedded so installs need no loose image files.</summary>
internal sealed class AppArtwork : IDisposable
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private readonly Dictionary<(int Size, bool Dark), Bitmap> _images = [];
    public Icon WindowIcon { get; }

    public AppArtwork()
    {
        using var iconStream = Open("screen-translate.ico");
        using var icon = new Icon(iconStream);
        WindowIcon = (Icon)icon.Clone();
        foreach (int size in Sizes)
        foreach (bool dark in new[] { false, true })
        {
            using var stream = Open($"{size}{(dark ? "-dark" : "")}.png");
            using var source = new Bitmap(stream);
            _images.Add((size, dark), new Bitmap(source));
        }
    }

    public Bitmap ForSurface(int pixels, bool dark) =>
        _images[(Sizes.FirstOrDefault(size => size >= pixels, Sizes[^1]), dark)];

    private static Stream Open(string name) => typeof(AppArtwork).Assembly.GetManifestResourceStream(
        $"screen_translate.Assets.Brand.{name}") ?? throw new InvalidOperationException($"Missing application artwork: {name}");

    public void Dispose()
    {
        WindowIcon.Dispose();
        foreach (var image in _images.Values) image.Dispose();
    }
}
