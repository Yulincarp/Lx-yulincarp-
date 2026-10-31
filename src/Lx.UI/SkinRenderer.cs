using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lx.UI;

// 把 64×64 皮肤图裁成 8×8 头像（脸 + 帽子层叠加），交给 Image 用最近邻放大。
internal static class SkinRenderer
{
    public static BitmapSource? Head(string skinPath)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.UriSource = new Uri(skinPath);
            image.EndInit();

            if (image.PixelWidth < 16 || image.PixelHeight < 16)
            {
                return null;
            }

            var face = new CroppedBitmap(image, new Int32Rect(8, 8, 8, 8));
            var pixels = new byte[8 * 8 * 4];
            face.CopyPixels(pixels, 8 * 4, 0);

            // 64×64 皮肤有独立的帽子层；1.8 之前的 64×32 没有
            if (image.PixelWidth >= 48 && image.PixelHeight >= 48)
            {
                var hat = new CroppedBitmap(image, new Int32Rect(40, 8, 8, 8));
                var overlay = new byte[8 * 8 * 4];
                hat.CopyPixels(overlay, 8 * 4, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    double alpha = overlay[i + 3] / 255.0;
                    if (alpha <= 0)
                    {
                        continue;
                    }

                    for (int channel = 0; channel < 3; channel++)
                    {
                        pixels[i + channel] = (byte)Math.Clamp(overlay[i + channel] * alpha + pixels[i + channel] * (1 - alpha), 0, 255);
                    }

                    pixels[i + 3] = 255;
                }
            }

            var bitmap = new WriteableBitmap(8, 8, 96, 96, PixelFormats.Bgra32, null);
            bitmap.WritePixels(new Int32Rect(0, 0, 8, 8), pixels, 8 * 4, 0);
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or UriFormatException)
        {
            return null;
        }
    }
}
