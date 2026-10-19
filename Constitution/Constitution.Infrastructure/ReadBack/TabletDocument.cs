namespace Constitution.Infrastructure.ReadBack;

/// <summary>A document found on the tablet: its id (to trash it by), its title, and the cloud's flattened PDF of it.</summary>
public sealed record TabletDocument(string Id, string Title, byte[] PdfBytes);
