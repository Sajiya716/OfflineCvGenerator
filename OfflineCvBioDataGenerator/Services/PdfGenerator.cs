using System;
using System.IO;
using System.Text;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.PDF;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OfflineCvBioDataGenerator.Services
{
    /// <summary>The templates offered in the Generate tab (values match the combo box order).</summary>
    public enum TemplateType
    {
        StandardBioData = 0,
        ModernTwoColumn = 1,
        MinimalistBw = 2,
        CorporateGray = 3,
        CreativeColorful = 4
    }

    /// <summary>
    /// Single place where PDFs are produced, so the UI and the tests exercise exactly
    /// the same code path.
    /// </summary>
    public static class PdfGenerator
    {
        /// <summary>
        /// The combo box labels, in the same order as <see cref="TemplateType"/>.
        /// The UI binds to this array so the labels can never drift out of sync with
        /// the enum (a unit test pins the two together).
        /// </summary>
        public static readonly string[] TemplateNames =
        {
            "Template 1: Standard Bio-Data",
            "Template 2: Modern 2-Column CV",
            "Template 3: Minimalist Black & White",
            "Template 4: Corporate Gray",
            "Template 5: Creative Colourful"
        };

        /// <summary>Number of entries in the template combo box.</summary>
        public static int TemplateCount => TemplateNames.Length;

        static PdfGenerator()
        {
            EnsureLicense();
        }

        /// <summary>
        /// Font files (regular, bold, italic, bold&nbsp;italic) that the templates ask for.
        /// QuestPDF ships only Lato and, on this build, does not pick up the installed
        /// Windows fonts even with <c>UseSystemFonts</c> enabled - so we register the
        /// files we need explicitly. Any missing file is skipped, in which case that
        /// family gracefully falls back to the bundled default font.
        /// </summary>
        private static readonly string[] TemplateFontFiles =
        {
            "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf",       // Template 3
            "cambria.ttc", "cambriab.ttf", "cambriai.ttf", "cambriaz.ttf",       // Template 4 headings
            "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf",       // Template 4 body
            "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf",       // Template 5 headings
            "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf"        // Template 5 body
        };

        private static readonly object FontLock = new();
        private static bool _fontsRegistered;

        /// <summary>
        /// Registers the Windows font files the templates use. Idempotent and never
        /// throws: a missing or unreadable font simply falls back to the default font.
        /// </summary>
        public static void RegisterFonts()
        {
            lock (FontLock)
            {
                if (_fontsRegistered) return;
                _fontsRegistered = true;

                try
                {
                    string fontsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    if (string.IsNullOrWhiteSpace(fontsDir))
                        fontsDir = Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(fontsDir))
                        return;

                    string fontsPath = Path.Combine(fontsDir, "Fonts");
                    if (!Directory.Exists(fontsPath))
                        return;

                    foreach (string file in TemplateFontFiles)
                    {
                        try
                        {
                            string fullPath = Path.Combine(fontsPath, file);
                            if (File.Exists(fullPath))
                                FontManager.RegisterFontFromFile(fullPath);
                        }
                        catch
                        {
                            // One unreadable font must not stop the others.
                        }
                    }
                }
                catch
                {
                    // Font registration is a best-effort enhancement.
                }
            }
        }

        /// <summary>QuestPDF requires an explicit license to be set once per process.</summary>
        public static void EnsureLicense()
        {
            QuestPDF.Settings.License = LicenseType.Community;

            try
            {
                QuestPDF.Settings.UseSystemFonts = true;
            }
            catch
            {
                // Older/newer QuestPDF builds may not expose the switch.
            }

            RegisterFonts();
        }

        /// <summary>
        /// Maps a combo box index to a template. Falls back to the first template for
        /// anything out of range so generation can never crash on a bad selection.
        /// </summary>
        public static TemplateType FromIndex(int index)
            => index >= 0 && index < TemplateCount ? (TemplateType)index : TemplateType.StandardBioData;

        /// <summary>Builds the QuestPDF document for the chosen template.</summary>
        public static IDocument CreateDocument(ClientDetail detail, TemplateType template)
        {
            if (detail == null) throw new ArgumentNullException(nameof(detail));
            if (detail.Client == null) throw new ArgumentException("ClientDetail.Client must not be null.", nameof(detail));

            return template switch
            {
                TemplateType.StandardBioData => new Template1_BioData(detail),
                TemplateType.ModernTwoColumn => new Template2_ModernCV(detail),
                TemplateType.MinimalistBw => new MinimalistBWTemplate(detail),
                TemplateType.CorporateGray => new CorporateGrayTemplate(detail),
                TemplateType.CreativeColorful => new CreativeColorfulTemplate(detail),
                _ => throw new ArgumentOutOfRangeException(nameof(template), template, "Unknown template.")
            };
        }

        /// <summary>Generates the PDF into memory - used by the tests and the UI.</summary>
        public static byte[] Generate(ClientDetail detail, TemplateType template)
        {
            using var stream = new MemoryStream();
            CreateDocument(detail, template).GeneratePdf(stream);
            return stream.ToArray();
        }

        /// <summary>
        /// Writes the PDF to <paramref name="folder"/> and returns the full path.
        /// Never returns an existing file name, so a previous PDF is never overwritten.
        /// </summary>
        public static string GenerateToFile(ClientDetail detail, TemplateType template, string folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                throw new ArgumentException("Output folder must not be empty.", nameof(folder));

            Directory.CreateDirectory(folder);

            string fileName = BuildSafeFileName(detail.Client, DateTime.Now);
            string filePath = GetUniquePath(Path.Combine(folder, fileName));

            byte[] bytes = Generate(detail, template);
            File.WriteAllBytes(filePath, bytes);

            return filePath;
        }

        /// <summary>
        /// Builds a file name from the client name. Everything that is illegal in a
        /// Windows file name is replaced, and the name is length limited so very long
        /// names cannot break the path.
        /// </summary>
        public static string BuildSafeFileName(Client? client, DateTime timestamp, string extension = ".pdf")
        {
            string name = (client?.FullName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = "Client";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            // Collapse runs of whitespace so the name stays readable.
            string cleaned = string.Join("_",
                sb.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (cleaned.Length > 60)
                cleaned = cleaned.Substring(0, 60).TrimEnd('_');

            if (cleaned.Length == 0)
                cleaned = "Client";

            string ext = string.IsNullOrEmpty(extension) ? ".pdf" : extension;
            if (!ext.StartsWith(".", StringComparison.Ordinal)) ext = "." + ext;

            return $"CV_{cleaned}_{timestamp:yyyyMMdd_HHmmss}{ext}";
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path)) return path;

            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);

            for (int i = 1; i < 10_000; i++)
            {
                string candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
                if (!File.Exists(candidate)) return candidate;
            }

            return Path.Combine(dir, $"{stem}_{Guid.NewGuid():N}{ext}");
        }
    }
}
