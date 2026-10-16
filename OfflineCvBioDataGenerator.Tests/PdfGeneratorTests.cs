using System;
using System.IO;
using System.Linq;
using System.Text;
using OfflineCvBioDataGenerator.Models;
using OfflineCvBioDataGenerator.Services;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class PdfGeneratorTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Theory]
        [InlineData(TemplateType.StandardBioData)]
        [InlineData(TemplateType.ModernTwoColumn)]
        [InlineData(TemplateType.MinimalistBw)]
        [InlineData(TemplateType.CorporateGray)]
        [InlineData(TemplateType.CreativeColorful)]
        public void Generate_ProducesAValidPdfFile(TemplateType template)
        {
            byte[] pdf = PdfGenerator.Generate(TestData.NewDetail(), template);

            Assert.NotNull(pdf);
            Assert.True(pdf.Length > 0, "The generated PDF was empty.");
            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Theory]
        [InlineData(TemplateType.StandardBioData)]
        [InlineData(TemplateType.ModernTwoColumn)]
        [InlineData(TemplateType.MinimalistBw)]
        [InlineData(TemplateType.CorporateGray)]
        [InlineData(TemplateType.CreativeColorful)]
        public void Generate_WithNoEducationOrExperience_StillSucceeds(TemplateType template)
        {
            var detail = new ClientDetail
            {
                Client = TestData.NewClient(),
                EducationList = new(),
                ExperienceList = new()
            };

            byte[] pdf = PdfGenerator.Generate(detail, template);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Theory]
        [InlineData(TemplateType.StandardBioData)]
        [InlineData(TemplateType.ModernTwoColumn)]
        [InlineData(TemplateType.MinimalistBw)]
        [InlineData(TemplateType.CorporateGray)]
        [InlineData(TemplateType.CreativeColorful)]
        public void Generate_WithProfilePhoto_StillSucceeds(TemplateType template)
        {
            var client = TestData.NewClient();
            client.PhotoPath = PhotoStore.Import(TempDatabase.WritePngInNewFolder("cv.png"));

            var detail = TestData.NewDetail(client);

            byte[] pdf = PdfGenerator.Generate(detail, template);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Fact]
        public void Generate_WithMissingPhotoFile_DoesNotThrow()
        {
            var client = TestData.NewClient();
            client.PhotoPath = Path.Combine(_db.RootPath, "deleted-photo.png");

            byte[] pdf = PdfGenerator.Generate(TestData.NewDetail(client), TemplateType.StandardBioData);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Fact]
        public void Generate_WithCorruptPhotoFile_DoesNotThrow()
        {
            var client = TestData.NewClient();
            string bogus = Path.Combine(_db.RootPath, "corrupt.png");
            File.WriteAllBytes(bogus, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
            client.PhotoPath = bogus;

            byte[] pdf = PdfGenerator.Generate(TestData.NewDetail(client), TemplateType.StandardBioData);

            Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            // The unreadable photo must be skipped, not embedded.
            Assert.Null(PDF.PdfHelper.LoadPhotoBytes(bogus));
        }

        [Fact]
        public void Generate_UnknownTemplate_Throws()
        {
            var detail = TestData.NewDetail();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                PdfGenerator.Generate(detail, (TemplateType)99));
        }

        [Fact]
        public void GenerateToFile_WritesThePdfAndReturnsItsPath()
        {
            var detail = TestData.NewDetail();

            string path = PdfGenerator.GenerateToFile(detail, TemplateType.StandardBioData, _db.RootPath);

            Assert.True(File.Exists(path));
            Assert.Equal(".pdf", Path.GetExtension(path));
            Assert.Equal("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(path), 0, 4));
        }

        [Fact]
        public void GenerateToFile_NeverOverwritesAnExistingFile()
        {
            var detail = TestData.NewDetail();

            string first = PdfGenerator.GenerateToFile(detail, TemplateType.StandardBioData, _db.RootPath);
            string firstContent = File.ReadAllText(first, Encoding.Latin1);

            // Generating again within the same second must not clobber the first file.
            string second = PdfGenerator.GenerateToFile(detail, TemplateType.StandardBioData, _db.RootPath);

            Assert.NotEqual(first, second);
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));

            // Both files may be identical, but the first must not have been replaced.
            Assert.Equal(firstContent, File.ReadAllText(first, Encoding.Latin1));
        }

        [Fact]
        public void GenerateToFile_CreatesTheFolderWhenMissing()
        {
            string folder = Path.Combine(_db.RootPath, "nested", "output");

            string path = PdfGenerator.GenerateToFile(TestData.NewDetail(), TemplateType.ModernTwoColumn, folder);

            Assert.True(File.Exists(path));
        }

        [Fact]
        public void GenerateToFile_EmptyFolder_Throws()
        {
            Assert.Throws<ArgumentException>(() =>
                PdfGenerator.GenerateToFile(TestData.NewDetail(), TemplateType.StandardBioData, "  "));
        }

        /// <summary>
        /// The two-column templates use a full-height sidebar (ExtendVertical) and long
        /// text values. Make sure pagination and line-wrapping cannot throw or produce an
        /// empty document when there is far more content than fits on one page.
        /// </summary>
        [Fact]
        public void Generate_WithVeryLongContent_DoesNotThrow()
        {
            var detail = new ClientDetail
            {
                Client = TestData.NewClient(
                    fullName: "Chamidu Bandaranayake Wickramasinghe Perera Silva",
                    address: string.Join(" ", Enumerable.Repeat("No. 45, Very Long Street Name, Colombo 03, Sri Lanka.", 12)),
                    email: new string('a', 120) + "@example.com"),
                EducationList = TestData.NewEducation(14),
                ExperienceList = TestData.NewExperience(16)
            };

            foreach (TemplateType template in Enum.GetValues<TemplateType>())
            {
                byte[] pdf = PdfGenerator.Generate(detail, template);

                Assert.True(pdf.Length > 0, $"{template} produced an empty PDF with long content.");
                Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            }
        }

        // ------------------------------------------------------------------ template registry

        /// <summary>
        /// Iterates the enum, so a newly added template is exercised automatically
        /// without anyone remembering to add another InlineData attribute.
        /// </summary>
        [Fact]
        public void Generate_EveryTemplateType_ProducesAPdf()
        {
            TemplateType[] all = Enum.GetValues<TemplateType>();

            Assert.NotEmpty(all);

            foreach (TemplateType template in all)
            {
                byte[] pdf = PdfGenerator.Generate(TestData.NewDetail(), template);

                Assert.True(pdf.Length > 0, $"{template} produced an empty PDF.");
                Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
            }
        }

        /// <summary>
        /// The combo box is filled from TemplateNames, so the labels must line up exactly
        /// with the enum values: one label per value, in the same order.
        /// </summary>
        [Fact]
        public void TemplateNames_MatchTheTemplateTypeEnum()
        {
            TemplateType[] all = Enum.GetValues<TemplateType>();

            Assert.Equal(all.Length, PdfGenerator.TemplateCount);
            Assert.Equal(all.Length, PdfGenerator.TemplateNames.Length);
            Assert.Equal(PdfGenerator.TemplateNames.Length, PdfGenerator.TemplateNames.Distinct().Count());
            Assert.All(PdfGenerator.TemplateNames, name => Assert.False(string.IsNullOrWhiteSpace(name)));

            // "Template 3: ..." must be the label at index 2.
            for (int i = 0; i < PdfGenerator.TemplateNames.Length; i++)
            {
                Assert.StartsWith($"Template {i + 1}:", PdfGenerator.TemplateNames[i], StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData(0, TemplateType.StandardBioData)]
        [InlineData(1, TemplateType.ModernTwoColumn)]
        [InlineData(2, TemplateType.MinimalistBw)]
        [InlineData(3, TemplateType.CorporateGray)]
        [InlineData(4, TemplateType.CreativeColorful)]
        public void FromIndex_MapsTheComboBoxSelection(int index, TemplateType expected)
        {
            Assert.Equal(expected, PdfGenerator.FromIndex(index));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        [InlineData(99)]
        public void FromIndex_OutOfRange_FallsBackToTheFirstTemplate(int index)
        {
            Assert.Equal(TemplateType.StandardBioData, PdfGenerator.FromIndex(index));
        }

        /// <summary>
        /// The templates ask for Georgia / Calibri / Cambria / Segoe UI / Verdana. QuestPDF
        /// only ships Lato and does not auto-discover the installed Windows fonts, so
        /// PdfGenerator registers them explicitly. Without this the PDFs would silently
        /// come out in Lato. Fails loudly if that regresses.
        /// </summary>
        [Fact]
        public void RegisterFonts_MakesTheTemplateFontsAvailable()
        {
            PdfGenerator.EnsureLicense();

            var families = QuestPDF.Drawing.FontManager.GetRegisteredFonts()
                .Select(font => font.FamilyName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            Assert.Contains("Lato", families); // the bundled fallback

            foreach (string family in new[] { "Georgia", "Calibri", "Cambria", "Segoe UI", "Verdana" })
            {
                Assert.True(families.Contains(family), $"Font family '{family}' was not registered.");
            }
        }

        // ------------------------------------------------------------------ file names

        [Fact]
        public void BuildSafeFileName_UsesTheClientNameAndATimestamp()
        {
            var client = TestData.NewClient("Jane Doe");

            string name = PdfGenerator.BuildSafeFileName(client, new DateTime(2024, 3, 7, 14, 5, 6));

            Assert.Equal("CV_Jane_Doe_20240307_140506.pdf", name);
        }

        [Fact]
        public void BuildSafeFileName_RemovesCharactersWindowsRejects()
        {
            var client = TestData.NewClient("Ann/O'Brien: \"Test\"? *Star* <A>|B\\C");

            string name = PdfGenerator.BuildSafeFileName(client, DateTime.Now);

            Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => name.Contains(c));
            Assert.StartsWith("CV_", name);
            Assert.EndsWith(".pdf", name);
        }

        [Fact]
        public void BuildSafeFileName_EmptyName_FallsBackToClient()
        {
            var client = TestData.NewClient("   ");

            string name = PdfGenerator.BuildSafeFileName(client, DateTime.Now);

            Assert.StartsWith("CV_Client_", name);
        }

        [Fact]
        public void BuildSafeFileName_NullClient_FallsBackToClient()
        {
            Assert.StartsWith("CV_Client_", PdfGenerator.BuildSafeFileName(null, DateTime.Now));
        }

        [Fact]
        public void BuildSafeFileName_VeryLongName_IsTrimmed()
        {
            var client = TestData.NewClient(new string('A', 300));

            string name = PdfGenerator.BuildSafeFileName(client, DateTime.Now);

            Assert.True(name.Length < 120, $"File name was too long: {name.Length}");
        }

        [Fact]
        public void BuildSafeFileName_RespectsACustomExtension()
        {
            var client = TestData.NewClient("Jane");

            string name = PdfGenerator.BuildSafeFileName(client, DateTime.Now, "pdf");

            Assert.EndsWith(".pdf", name);
        }
    }
}
