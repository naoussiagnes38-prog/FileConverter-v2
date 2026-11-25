using FileConverter.Core.Interfaces;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace FileConverter.Core.Services
{
    public sealed class ConversionService : IFileConverter
    {
        public void ConvertImage(string input, string output, string format)
        {
            ImageFormat encoding;
            switch (format)
            {
                case "PNG": encoding = ImageFormat.Png; break;
                case "JPG": encoding = ImageFormat.Jpeg; break;
                case "BMP": encoding = ImageFormat.Bmp; break;
                default: throw new ArgumentException("Unsupported image output format.");
            }
            using (var image = Image.FromFile(input))
            {
                if (image.GetFrameCount(new FrameDimension(image.FrameDimensionsList[0])) > 1)
                    throw new InvalidDataException("Multi-frame images are not supported.");
                ApplyExifOrientation(image);
                image.Save(output, encoding);
            }
        }

        /// <summary>
        /// Applies the camera orientation tag to the pixels before exporting.
        /// This prevents viewers that do not understand EXIF metadata from
        /// displaying converted phone photos rotated or mirrored.
        /// </summary>
        public static void ApplyExifOrientation(Image image)
        {
            const int orientationId = 0x0112;
            if (image == null) throw new ArgumentNullException(nameof(image));
            PropertyItem property;
            try
            {
                property = image.GetPropertyItem(orientationId);
            }
            catch (ArgumentException)
            {
                // Most non-camera images have no EXIF orientation property.
                return;
            }
            if (property == null || property.Value == null || property.Value.Length < 2) return;
            ushort orientation = BitConverter.ToUInt16(property.Value, 0);
            RotateFlipType transform;
            switch (orientation)
            {
                case 2: transform = RotateFlipType.RotateNoneFlipX; break;
                case 3: transform = RotateFlipType.Rotate180FlipNone; break;
                case 4: transform = RotateFlipType.Rotate180FlipX; break;
                case 5: transform = RotateFlipType.Rotate90FlipX; break;
                case 6: transform = RotateFlipType.Rotate90FlipNone; break;
                case 7: transform = RotateFlipType.Rotate270FlipX; break;
                case 8: transform = RotateFlipType.Rotate270FlipNone; break;
                default: return;
            }
            image.RotateFlip(transform);
            try { image.RemovePropertyItem(orientationId); }
            catch (ArgumentException) { }
        }
    }
}
