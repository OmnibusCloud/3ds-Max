using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace OutWit.Render.ThreeDsMax.Plugin.Export.Tests.Services;

internal static class MaxBatchRenderAssertions
{
    #region Functions

    public static void AssertImageIsReadableAndNotSolidBlack(string imagePath, string context)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            throw new InvalidOperationException("Image path is required.");

        using var image = Image.Load<Rgba32>(imagePath);

        long nonBlackPixels = 0;
        long meaningfullyLitPixels = 0;
        long totalPixels = (long)image.Width * image.Height;

        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var pixel = image[x, y];
                if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0)
                    nonBlackPixels++;

                if (pixel.R >= 8 || pixel.G >= 8 || pixel.B >= 8)
                    meaningfullyLitPixels++;
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(image.Width, Is.GreaterThan(0), $"{context}: image width must be greater than zero.");
            Assert.That(image.Height, Is.GreaterThan(0), $"{context}: image height must be greater than zero.");
            Assert.That(totalPixels, Is.GreaterThan(0), $"{context}: image contains no pixels.");
            Assert.That(nonBlackPixels, Is.GreaterThan(0), $"{context}: rendered image is completely black.");
            Assert.That(meaningfullyLitPixels, Is.GreaterThan(0), $"{context}: rendered image contains only near-black pixels and is not visually meaningful.");
        });
    }

    #endregion
}
