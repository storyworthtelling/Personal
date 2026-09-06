using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Foreword.Tests.Pdf;

public class ChecklistPdfComposerTests
{
    private readonly ChecklistPdfComposer composer = new();
    private static readonly DateOnly Today = new(2026, 6, 1);

    private static int PageCount(byte[] pdf)
    {
        using var stream = new MemoryStream(pdf);
        return PdfReader.Open(stream, PdfDocumentOpenMode.InformationOnly).PageCount;
    }

    [Fact]
    public void Compose_ProducesPdf()
    {
        var tasks = new[]
        {
            new TodoItem { Title = "Buy milk" },
            new TodoItem { Title = "Call dentist", Due = Today }
        };

        var pdf = composer.Compose(tasks, Today, []);

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(PageCount(pdf) >= 1);
    }

    [Fact]
    public void Compose_HandlesNoTasks()
    {
        var pdf = composer.Compose([], Today, []);

        Assert.True(pdf.Length > 0);
        Assert.True(PageCount(pdf) >= 1);
    }

    [Fact]
    public void Compose_WithCarryoverNotes_DoesNotThrow()
    {
        var carryovers = new[] { new ParsedNote { Text = "messy task", IsTask = true, Confidence = 0.3 } };

        var pdf = composer.Compose([], Today, carryovers);

        Assert.True(pdf.Length > 0);
    }
}
