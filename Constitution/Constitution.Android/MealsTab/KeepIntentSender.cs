using Android.Content;
using Constitution.Domain.Groceries;

namespace Constitution.MealsTab;

/// <summary>
/// Starts the one intent that hands the grocery list to Google Keep (D112): <c>ACTION_SEND</c> text, aimed at the package <see cref="GroceryShare.Plan"/> chose and never at a
/// chooser, since under the lockdown a chooser would offer only whatever survived it. Decides nothing: what to send is <see cref="KeepShareRun"/>'s, and how the phone answered
/// becomes a sentence there. <b>Package visibility is not asked for</b> (no <c>queries</c> entry, no permission): an explicit-package start needs none, so "is Keep here" is
/// found out by starting it, and a phone without Keep answers <see cref="ActivityNotFoundException"/>.
/// </summary>
internal static class KeepIntentSender
{
    public static KeepStart Start(Context context, GroceryShareDecision decision)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Outcome != GroceryShareOutcome.Send)
        {
            throw new ArgumentException("Only a decision to send is started; this one is " + decision.Outcome + ".", nameof(decision));
        }

        var intent = new Intent(Intent.ActionSend);
        intent.SetType("text/plain");
        intent.SetPackage(decision.Package);
        intent.PutExtra(Intent.ExtraSubject, decision.Title);
        intent.PutExtra(Intent.ExtraText, decision.Body);
        try
        {
            context.StartActivity(intent);
            return KeepStart.Started;
        }
        catch (ActivityNotFoundException)
        {
            // Keep is not on this phone (or is disabled): the plain sentence says so, nothing else is offered.
            return KeepStart.NotFound;
        }
        catch (Java.Lang.SecurityException)
        {
            // Keep (or the device policy) would not take it.
            return KeepStart.Refused;
        }
    }
}
