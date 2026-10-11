using Android.App;

// Index declares RECEIVE_USAGE at protectionLevel="signature"; a sender must hold it too, and every
// head shares one keystore, so this costs nothing and stops any other app feeding Index fake usage.
[assembly: UsesPermission(Name = "com.storyworthtelling.ledger.RECEIVE_USAGE")]
