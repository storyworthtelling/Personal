namespace NytRemarkable.Domain.Interfaces;

public interface IRemarkableClient
{
    Task Upload(string title, byte[] pdfBytes);
}
