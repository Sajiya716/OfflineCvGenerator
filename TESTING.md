# Automated tests

The test project lives next to the application:

```
cv project\
├─ CvGenerator.sln
├─ OfflineCvBioDataGenerator\              <- WinForms application
└─ OfflineCvBioDataGenerator.Tests\        <- xUnit test project
```

## One-time setup (only needed if you recreate the project)

```powershell
cd "C:\Users\Saj\Documents\cv project"

dotnet new xunit -n OfflineCvBioDataGenerator.Tests
dotnet add OfflineCvBioDataGenerator.Tests\OfflineCvBioDataGenerator.Tests.csproj `
    reference OfflineCvBioDataGenerator\OfflineCvBioDataGenerator.csproj
```

Then edit `OfflineCvBioDataGenerator.Tests.csproj` so the target framework matches the
application (the WinForms project can only be referenced by another `-windows` project):

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```

## Running the tests

```powershell
cd "C:\Users\Saj\Documents\cv project"

# everything (build + test)
dotnet test CvGenerator.sln

# just the test project, skipping a rebuild
dotnet test OfflineCvBioDataGenerator.Tests

# one class
dotnet test --filter "FullyQualifiedName~ClientRepositoryTests"

# one test
dotnet test --filter "FullyQualifiedName~SearchClients_EscapesUnderscoreWildcard"

# see every test name
dotnet test -v normal
```

> If `dotnet` is not on your PATH, use the per-user install explicitly:
> ```powershell
> $u = "$env:USERPROFILE\.dotnet\dotnet.exe"
> & $u test CvGenerator.sln
> ```

## What is covered

| Test file | Covers |
|---|---|
| `DatabaseHelperTests` | schema creation, `PhotoPath` migration from an old database, foreign keys, idempotent `InitializeDatabase` |
| `ClientRepositoryTests` | insert/read/update/delete, unique NIC, transactional `Save`, LIKE-wildcard escaping in search |
| `EducationExperienceRepositoryTests` | CRUD per client, `AddRange`, delete isolation, cascade delete |
| `ClientValidatorTests` | mandatory Name/NIC, email/phone/name-length rules, future DOB |
| `SearchTermTests` | escaping of `%`, `_` and `\` |
| `PhotoStoreTests` / `PdfHelperTests` | photo import, no duplicate copies, path resolution, missing/corrupt photos |
| `PdfGeneratorTests` | all five QuestPDF templates produce valid PDFs, empty lists, photos, long/multi-page content, template-name/enum sync, `FromIndex` mapping, safe file names, template fonts registered |

## How isolation works

`DatabaseHelper` and `PhotoStore` hold static state, so each test gets a fresh temporary
database and `ProfilePics` folder via `Infrastructure\TempDatabase.cs`. Tests therefore
never touch your real `cvdb.sqlite`, and `AssemblyInfo.cs` disables parallel execution so
the two statics cannot clash.

## Fonts

The templates use normal Windows fonts (Georgia, Calibri, Cambria, Segoe UI, Verdana).
QuestPDF only ships the Lato font and does **not** auto-discover the installed Windows
fonts on this machine, so `PdfGenerator.RegisterFonts()` registers the needed font files
from `%WINDIR%\Fonts` at startup (see `PdfGenerator.EnsureLicense()`).

Consequences:

* The generated PDFs embed a subset of the real font, so they render identically on any PC.
* If a font file is missing, that family silently falls back to Lato - the PDF is still
  produced.
* `RegisterFonts_MakesTheTemplateFontsAvailable` fails loudly if registration regresses
  (which would otherwise make every template come out in Lato).

## Sample output

`TemplateSamples\` contains a rendered sample of every template (one `PDF` plus a `preview_*.png`
image per page) using realistic data, including an "empty" variant with no photo and no
education/experience. Regenerate them by re-running the template tests, or simply use the
app. The folder is not part of the build and can be deleted at any time.
