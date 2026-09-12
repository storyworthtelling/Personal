using Foreword.Domain.Prayer;

namespace Foreword.Domain.Interfaces;

/// <summary>The third document delivered each morning: the prayer list, then the journal.</summary>
public interface IPrayerPdfComposer
{
    byte[] Compose(PrayerDocument document);
}
