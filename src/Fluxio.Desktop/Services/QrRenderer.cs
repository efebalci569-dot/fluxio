using System.Windows;
using System.Windows.Media;
using QRCoder;

namespace Fluxio.Desktop.Services;

/// <summary>
/// Builds a crisp vector QR code (WPF geometry) with QRCoder. The code contains only the
/// pairing URL: LAN address, port, random session id and one-time secret.
/// </summary>
public static class QrRenderer
{
    public static Geometry Render(string text, out int modules)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var matrix = data.ModuleMatrix;
        modules = matrix.Count;

        var geometry = new StreamGeometry { FillRule = FillRule.Nonzero };
        using (var context = geometry.Open())
        {
            for (var y = 0; y < matrix.Count; y++)
            {
                var row = matrix[y];
                var x = 0;
                while (x < row.Length)
                {
                    if (!row[x])
                    {
                        x++;
                        continue;
                    }

                    // Merge horizontal runs of dark modules into one rectangle (fewer seams).
                    var start = x;
                    while (x < row.Length && row[x])
                    {
                        x++;
                    }

                    context.BeginFigure(new Point(start, y), isFilled: true, isClosed: true);
                    context.PolyLineTo([new Point(x, y), new Point(x, y + 1), new Point(start, y + 1)], isStroked: false, isSmoothJoin: false);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }
}
