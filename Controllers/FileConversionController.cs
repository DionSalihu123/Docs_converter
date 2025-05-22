// File: Controllers/FileConversionController.cs
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using PdfSharpCore.Pdf;
using PdfSharpCore.Drawing;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FileConverterApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FileConversionController : ControllerBase
    {
        [HttpPost("convert")]
        public IActionResult ConvertFile(IFormFile file, [FromQuery] string toFormat)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            if (string.IsNullOrWhiteSpace(toFormat))
                return BadRequest("Target format not specified.");

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant().TrimStart('.');
            toFormat = toFormat.ToLowerInvariant();

            if (ext == toFormat)
                return BadRequest("Cannot convert to the same format.");

            // Save input file
            string inputPath = Path.GetTempFileName();
            using (var inStream = System.IO.File.Create(inputPath))
                file.CopyTo(inStream);

            // Extract text
            string text;
            try
            {
                text = ext switch
                {
                    "txt" => System.IO.File.ReadAllText(inputPath),
                    "docx" => ExtractFromDocx(inputPath),
                    "pdf" => ExtractFromPdf(inputPath),
                    _ => throw new NotSupportedException($"Unsupported input format: {ext}")
                };
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }

            // Prepare output
            string outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + $".{toFormat}");
            byte[] result;
            string contentType;
            string downloadName;

            try
            {
                switch (toFormat)
                {
                    case "txt":
                        System.IO.File.WriteAllText(outputPath, text);
                        result = System.IO.File.ReadAllBytes(outputPath);
                        contentType = "text/plain";
                        downloadName = "converted.txt";
                        break;

                    case "docx":
                        CreateDocx(text, outputPath);
                        result = System.IO.File.ReadAllBytes(outputPath);
                        contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
                        downloadName = "converted.docx";
                        break;

                    case "pdf":
                        CreatePdf(text, outputPath);
                        result = System.IO.File.ReadAllBytes(outputPath);
                        contentType = "application/pdf";
                        downloadName = "converted.pdf";
                        break;

                    default:
                        return BadRequest($"Unsupported target format: {toFormat}");
                }
                return File(result, contentType, downloadName);
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Conversion failed: " + ex.Message);
            }
        }

        private string ExtractFromDocx(string path)
        {
            using var doc = WordprocessingDocument.Open(path, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body == null)
                throw new InvalidDataException("Invalid DOCX file.");
            return string.Join("\n", body.Descendants<Text>().Select(t => t.Text));
        }

        private string ExtractFromPdf(string path)
        {
            var sb = new StringBuilder();
            using var pdf = UglyToad.PdfPig.PdfDocument.Open(path);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }

        private void CreateDocx(string text, string path)
        {
            using var doc = WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body());
            foreach (var line in text.Split('\n'))
            {
                main.Document.Body!.AppendChild(new Paragraph(new Run(new Text(line))));
            }
            main.Document.Save();
        }

        private void CreatePdf(string text, string path)
        {
            var pdf = new PdfSharpCore.Pdf.PdfDocument();
            var page = pdf.AddPage();
            var gfx = XGraphics.FromPdfPage(page);
            // Use Liberation Sans, a common free font on Linux
            var font = new XFont("Liberation Sans", 12);
            double y = 40;
            foreach (var line in text.Split('\n'))
            {
                if (y > page.Height - 40)
                {
                    page = pdf.AddPage();
                    gfx = XGraphics.FromPdfPage(page);
                    y = 40;
                }
                gfx.DrawString(line, font, XBrushes.Black,
                    new XRect(40, y, page.Width - 80, page.Height - 80), XStringFormats.TopLeft);
                y += font.GetHeight();
            }
            using var fs = System.IO.File.Create(path);
            pdf.Save(fs, false);
        }

    }
}
