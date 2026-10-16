using System;
using System.IO;
using System.Linq;
using OfflineCvBioDataGenerator.Data;
using OfflineCvBioDataGenerator.PDF;
using OfflineCvBioDataGenerator.Services;
using OfflineCvBioDataGenerator.Tests.Infrastructure;
using Xunit;

namespace OfflineCvBioDataGenerator.Tests
{
    public class PhotoStoreTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Import_CopiesTheFileIntoTheProfileFolder()
        {
            string source = TempDatabase.WritePngInNewFolder("me.png");

            string stored = PhotoStore.Import(source);
            string resolved = PhotoStore.Resolve(stored);

            Assert.True(File.Exists(resolved));
            Assert.NotEqual(Path.GetFullPath(source), Path.GetFullPath(resolved));
            Assert.StartsWith(Path.GetFullPath(_db.PhotoFolder), Path.GetFullPath(resolved));
            Assert.Equal(TestData.OnePixelPng(), File.ReadAllBytes(resolved));
        }

        [Fact]
        public void Import_TwiceForTheSameFile_DoesNotCreateDuplicates()
        {
            string source = TempDatabase.WritePngInNewFolder("me.png");

            string first = PhotoStore.Import(source);
            string second = PhotoStore.Import(source);

            Assert.Equal(first, second);
            Assert.Single(Directory.GetFiles(_db.PhotoFolder));
        }

        [Fact]
        public void Import_AlreadyStoredPhoto_IsANoOp()
        {
            string source = TempDatabase.WritePngInNewFolder("me.png");
            string stored = PhotoStore.Import(source);
            string resolved = PhotoStore.Resolve(stored);
            int countAfterFirstImport = Directory.GetFiles(_db.PhotoFolder).Length;

            // Re-importing the copy that is already inside the profile folder must not
            // create me_1.png.
            string again = PhotoStore.Import(resolved);

            Assert.Equal(stored, again);
            Assert.Equal(countAfterFirstImport, Directory.GetFiles(_db.PhotoFolder).Length);
        }

        [Fact]
        public void Import_SameFileNameFromDifferentSource_GetsAUniqueName()
        {
            string first = TempDatabase.WritePngInNewFolder("photo.png");

            // A genuinely different picture that happens to share the file name.
            string secondFolder = Path.Combine(Path.GetTempPath(), "cvpics_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(secondFolder);
            string second = Path.Combine(secondFolder, "photo.png");
            File.WriteAllBytes(second, TestData.OnePixelPng().Concat(new byte[] { 0 }).ToArray());

            string storedFirst = PhotoStore.Import(first);
            string storedSecond = PhotoStore.Import(second);

            Assert.NotEqual(storedFirst, storedSecond);
            Assert.Equal(2, Directory.GetFiles(_db.PhotoFolder).Length);
        }

        [Fact]
        public void Import_SamePictureUnderDifferentFileName_KeepsThemSeparate()
        {
            string first = TempDatabase.WritePngInNewFolder("one.png");
            string second = TempDatabase.WritePngInNewFolder("two.png");

            string storedFirst = PhotoStore.Import(first);
            string storedSecond = PhotoStore.Import(second);

            // Two different names are treated as two different photos on purpose: two
            // clients must not end up sharing one file (deleting one would break the other).
            Assert.NotEqual(storedFirst, storedSecond);
            Assert.Equal(2, Directory.GetFiles(_db.PhotoFolder).Length);
        }

        [Fact]
        public void Import_MissingFile_ThrowsFileNotFound()
        {
            string missing = Path.Combine(_db.RootPath, "nope.png");

            Assert.Throws<FileNotFoundException>(() => PhotoStore.Import(missing));
        }

        [Fact]
        public void Import_EmptyPath_Throws()
        {
            Assert.Throws<ArgumentException>(() => PhotoStore.Import("   "));
        }

        [Fact]
        public void Resolve_EmptyPath_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, PhotoStore.Resolve(null));
            Assert.Equal(string.Empty, PhotoStore.Resolve(""));
        }

        [Fact]
        public void Resolve_AbsolutePath_IsReturnedUnchanged()
        {
            string absolute = Path.Combine(_db.RootPath, "somewhere", "a.png");

            Assert.Equal(absolute, PhotoStore.Resolve(absolute));
        }

        [Fact]
        public void ProfilePicsFolder_IsTheConfiguredFolder()
        {
            Assert.Equal(Path.GetFullPath(_db.PhotoFolder), PhotoStore.ProfilePicsFolder);
        }
    }

    public class PdfHelperTests : IDisposable
    {
        private readonly TempDatabase _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void LoadPhotoBytes_NoPath_ReturnsNull()
        {
            Assert.Null(PdfHelper.LoadPhotoBytes(null));
            Assert.Null(PdfHelper.LoadPhotoBytes(""));
            Assert.Null(PdfHelper.LoadPhotoBytes("   "));
        }

        [Fact]
        public void LoadPhotoBytes_MissingFile_ReturnsNull()
        {
            Assert.Null(PdfHelper.LoadPhotoBytes(Path.Combine(_db.RootPath, "missing.png")));
        }

        [Fact]
        public void LoadPhotoBytes_ExistingAbsoluteFile_ReturnsItsBytes()
        {
            string path = _db.WritePng();

            byte[]? bytes = PdfHelper.LoadPhotoBytes(path);

            Assert.NotNull(bytes);
            Assert.Equal(TestData.OnePixelPng(), bytes);
        }

        [Fact]
        public void LoadPhotoBytes_RelativeStoredPath_IsResolvedAgainstTheDataRoot()
        {
            // A relative stored path is resolved against the data root (%AppData%\OfflineCvGenerator),
            // which is what makes "ProfilePics\photo.png" survive an application update.
            string relativeName = "rel_" + Guid.NewGuid().ToString("N") + ".png";
            string fullPath = Path.Combine(AppPaths.Root, relativeName);
            File.WriteAllBytes(fullPath, TestData.OnePixelPng());

            try
            {
                byte[]? bytes = PdfHelper.LoadPhotoBytes(relativeName);

                Assert.NotNull(bytes);
                Assert.Equal(TestData.OnePixelPng(), bytes);
            }
            finally
            {
                File.Delete(fullPath);
            }
        }

        [Fact]
        public void LoadPhotoBytes_ImportedPhoto_RoundTrips()
        {
            string stored = PhotoStore.Import(TempDatabase.WritePngInNewFolder("roundtrip.png"));

            byte[]? bytes = PdfHelper.LoadPhotoBytes(stored);

            Assert.NotNull(bytes);
            Assert.Equal(TestData.OnePixelPng(), bytes);
        }
    }
}
