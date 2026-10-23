using Constitution.Domain.ReadBack;
using Constitution.Infrastructure.ReadBack;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// The delivered documents as the morning read-back should see them: only those whose day has ended.
///
/// <para><b>Why the read-back needs this.</b> <c>ReadBackService</c> reads every outstanding document whatever its day. A
/// workout page delivered this morning is blank until the owner has done the workout, and once pen ink has been proven to
/// come through the cloud's PDF, a blank page is read as a skip: the plan would record a skip, for a session that has not
/// happened, at 7:15 on the morning of the day. Recording a document for a later day is therefore harmless only while the
/// read-back is shown this view; hand it this in place of the raw store.</para>
///
/// <para>Recording, saving and retiring go straight to the store: the filter is about what is read, not what is kept. The day
/// is the date on the wall clock of the offset the clock gives, as the read-back itself reads it.</para>
/// </summary>
public sealed class DueExpectedDocuments(IExpectedDocuments inner, Func<DateTimeOffset> clock) : IExpectedDocuments
{
    private readonly IExpectedDocuments inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly Func<DateTimeOffset> clock = clock ?? throw new ArgumentNullException(nameof(clock));

    public IReadOnlyList<OutstandingDocument> Outstanding()
    {
        var today = DateOnly.FromDateTime(clock().DateTime);
        return inner.Outstanding().Where(item => item.Document.Day < today).ToList();
    }

    public void Record(ExpectedDocument document) => inner.Record(document);

    public void MarkSaved(string documentName) => inner.MarkSaved(documentName);

    public void Retire(string documentName) => inner.Retire(documentName);
}
