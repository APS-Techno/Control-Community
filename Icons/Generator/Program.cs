using SkiaSharp;
using Svg.Skia;

namespace APS.Icons.Generator;

public static class Program
{
    public static readonly int[] Sizes = { 16, 24, 32, 48 };

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: SvgToPng <svgDir> <pngOutDir>");
            return 1;
        }

        var svgDir = Path.GetFullPath(args[0]);
        var pngDir = Path.GetFullPath(args[1]);

        if (!Directory.Exists(svgDir))
        {
            Console.Error.WriteLine($"SVG directory not found: {svgDir}");
            return 1;
        }

        foreach (var size in Sizes)
        {
            Directory.CreateDirectory(Path.Combine(pngDir, size.ToString()));
        }

        var svgs = Directory.GetFiles(svgDir, "*.svg");
        Console.WriteLine($"Rasterizing {svgs.Length} SVGs into {Sizes.Length} sizes...");

        foreach (var svgPath in svgs)
        {
            var name = Path.GetFileNameWithoutExtension(svgPath);
            using var svg = new SKSvg();
            svg.Load(svgPath);

            if (svg.Picture is null)
            {
                Console.Error.WriteLine($"  SKIP {name}.svg (parse failed)");
                continue;
            }

            foreach (var size in Sizes)
            {
                var outPath = Path.Combine(pngDir, size.ToString(), $"{name}.png");
                RenderToPng(svg, size, outPath);
            }

            Console.WriteLine($"  OK   {name}");
        }

        var previewPath = Path.Combine(Path.GetDirectoryName(pngDir)!, "preview.html");
        PreviewGenerator.WritePreview(svgDir, previewPath);
        Console.WriteLine($"Wrote preview: {previewPath}");

        return 0;
    }

    private static void RenderToPng(SKSvg svg, int size, string outPath)
    {
        using var bitmap = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var pic = svg.Picture!;
        var cull = pic.CullRect;
        var scale = size / Math.Max(cull.Width, cull.Height);
        canvas.Scale(scale, scale);
        canvas.DrawPicture(pic);
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(outPath);
        data.SaveTo(stream);
    }
}
