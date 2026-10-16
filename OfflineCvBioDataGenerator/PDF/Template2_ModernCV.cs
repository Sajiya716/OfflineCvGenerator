using System;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using OfflineCvBioDataGenerator.Models;

namespace OfflineCvBioDataGenerator.PDF
{
    public class Template2_ModernCV : IDocument
    {
        private readonly ClientDetail _clientDetail;

        public Template2_ModernCV(ClientDetail clientDetail)
        {
            _clientDetail = clientDetail;
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
        public DocumentSettings GetSettings() => DocumentSettings.Default;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(40);
                page.Size(PageSizes.A4);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Content().Row(row =>
                {
                    row.RelativeItem(0.4f).Background(Colors.Blue.Darken2).Padding(20).Element(ComposeLeftColumn);
                    row.RelativeItem(0.6f).Padding(20).Element(ComposeRightColumn);
                });
            });
        }

        private void ComposeLeftColumn(IContainer container)
        {
            container.Column(column =>
            {
                byte[]? photoBytes = PdfHelper.LoadPhotoBytes(_clientDetail.Client.PhotoPath);
                if (photoBytes != null)
                {
                    column.Item().Width(120).Height(120).Image(photoBytes);
                    column.Item().PaddingBottom(10);
                }

                column.Item().Text(_clientDetail.Client.FullName).FontSize(16).Bold().FontColor(Colors.White);
                column.Item().PaddingBottom(10).Text("Professional CV").FontSize(11).FontColor(Colors.Grey.Lighten3);

                column.Item().PaddingTop(20).Text("CONTACT").FontSize(11).Bold().FontColor(Colors.White);
                column.Item().PaddingTop(5).Text(_clientDetail.Client.Phone).FontColor(Colors.Grey.Lighten3);
                column.Item().Text(_clientDetail.Client.Email).FontColor(Colors.Grey.Lighten3);
                column.Item().Text(_clientDetail.Client.Address).FontColor(Colors.Grey.Lighten3).BreakAnywhere();

                if (!string.IsNullOrEmpty(_clientDetail.Client.NIC) || _clientDetail.Client.DOB.HasValue)
                {
                    column.Item().PaddingTop(15).Text("PERSONAL DETAILS").FontSize(11).Bold().FontColor(Colors.White);
                    if (_clientDetail.Client.DOB.HasValue)
                        column.Item().PaddingTop(5).Text($"DOB: {_clientDetail.Client.DOB.Value.ToShortDateString()}").FontColor(Colors.Grey.Lighten3);
                    if (!string.IsNullOrEmpty(_clientDetail.Client.NIC))
                        column.Item().Text($"NIC: {_clientDetail.Client.NIC}").FontColor(Colors.Grey.Lighten3);
                }
            });
        }

        private void ComposeRightColumn(IContainer container)
        {
            container.Column(column =>
            {
                if (_clientDetail.EducationList.Any())
                {
                    column.Item().Text("EDUCATION").FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
                    column.Item().PaddingBottom(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    foreach (var edu in _clientDetail.EducationList)
                    {
                        column.Item().PaddingTop(3).Text(edu.Qualification).Bold();
                        column.Item().Text($"{edu.Institute} | {edu.Year} | {edu.Results}");
                        column.Item().PaddingBottom(5);
                    }
                }

                if (_clientDetail.ExperienceList.Any())
                {
                    column.Item().PaddingTop(15).Text("WORK EXPERIENCE").FontSize(12).Bold().FontColor(Colors.Blue.Darken2);
                    column.Item().PaddingBottom(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    foreach (var exp in _clientDetail.ExperienceList)
                    {
                        column.Item().PaddingTop(3).Text(exp.JobTitle).Bold();
                        column.Item().Text($"{exp.Company} | {exp.Duration}");
                        column.Item().PaddingBottom(5);
                    }
                }
            });
        }
    }
}
