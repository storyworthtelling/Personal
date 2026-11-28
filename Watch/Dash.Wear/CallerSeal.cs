using Android.Security.Keystore;
using Java.Security;
using Javax.Crypto;
using Watch.Domain;

namespace Dash.Wear;

/// <summary>
/// The key that seals an unknown caller on the wrist (P14 B8, W-5.1.9): an HMAC-SHA256 key the platform's keystore makes
/// and keeps, and never gives out, so the call screening's kept digests can't be turned back into a number anywhere but
/// on this watch, and not by reading anything Dash wrote down. Made once, on the first call it is asked for.
/// </summary>
internal static class CallerSeal
{
    private const string Store = "AndroidKeyStore"; // not copy: the platform keystore's provider name
    private const string Algorithm = "HmacSHA256"; // not copy: the JCA name of the keystore key's MAC

    private static readonly object Gate = new();

    /// <summary>The digits sealed by the keystore's key.</summary>
    public static byte[] Digest(byte[] digits)
    {
        var mac = Mac.GetInstance(Algorithm)!;
        mac.Init(Key());
        return mac.DoFinal(digits)!;
    }

    private static IKey Key()
    {
        lock (Gate)
        {
            var store = KeyStore.GetInstance(Store)!;
            store.Load(null);
            if (store.GetKey(WatchIdentity.DashCallerSealAlias, null) is { } kept)
            {
                return kept;
            }

            var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmHmacSha256, Store)!;
            generator.Init(new KeyGenParameterSpec.Builder(WatchIdentity.DashCallerSealAlias, KeyStorePurpose.Sign).Build());
            return generator.GenerateKey()!;
        }
    }
}
