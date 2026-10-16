using System;
using System.IO;

namespace OfflineCvBioDataGenerator.PDF
{
    /// <summary>
    /// Shared helper for loading the profile photo into the PDF templates.
    /// </summary>
    public static class PdfHelper
    {
        /// <summary>
        /// Resolves the stored photo path (it may be relative to the application folder)
        /// and returns the raw image bytes, or null when the photo is missing or is not a
        /// readable image. Reading bytes instead of handing the path to QuestPDF keeps no
        /// file lock open, and validating here means one corrupt photo cannot break the
        /// whole PDF.
        /// </summary>
        public static byte[]? LoadPhotoBytes(string? photoPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(photoPath))
                    return null;

                string fullPath = Services.PhotoStore.Resolve(photoPath);

                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    return null;

                byte[] bytes = File.ReadAllBytes(fullPath);

                return IsDecodableImage(bytes) ? bytes : null;
            }
            catch
            {
                // A broken/deleted photo must never stop the CV from being produced.
                return null;
            }
        }

        /// <summary>Checks that the bytes really are an image that can be drawn.</summary>
        public static bool IsDecodableImage(byte[]? bytes)
        {
            if (bytes == null || bytes.Length == 0) return false;

            try
            {
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = System.Drawing.Image.FromStream(stream);
                return image.Width > 0 && image.Height > 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
