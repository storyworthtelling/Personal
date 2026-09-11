using Foreword.Domain.Models;

namespace Foreword.Domain.Interfaces;

/// <summary>The one document delivered each morning: Day, Weather, Outlook, Checklist.</summary>
public interface IDayPdfComposer
{
    byte[] Compose(DayDocument document);
}
