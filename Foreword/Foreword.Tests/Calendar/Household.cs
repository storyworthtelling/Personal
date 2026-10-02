using Foreword.Domain.Calendar;
using Foreword.Domain.Models;

namespace Foreword.Tests.Calendar;

/// <summary>
/// The household every test uses: four invented people, two with a Google account of their own and
/// two whose days arrive on calendars somebody else owns.
///
/// <para>Invented on purpose. The repository is public, and the columns are the owner's own choices,
/// set up in Foreword and kept in their vault — the code and its tests never know who anybody
/// is.</para>
/// </summary>
internal static class Household
{
    public static readonly PersonColumn Alex = new("Alex");

    public static readonly PersonColumn Sam = new("Sam");

    public static readonly PersonColumn Riley = new("Riley");

    public static readonly PersonColumn Jo = new("Jo");

    public static readonly ColumnLayout Layout = new([Alex, Sam, Riley, Jo], withAccounts: [Alex, Sam]);
}
