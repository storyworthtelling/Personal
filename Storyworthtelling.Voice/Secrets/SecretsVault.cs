using System.Text.Json.Serialization;

namespace Storyworthtelling.Voice.Secrets;

// The single cross-app secrets vault: every com.storyworthtelling app's keys and durable "magic strings"
// in one record, serialized to JSON, encrypted (see SecretsCrypto), and stored as ONE file in the owner's
// Google Drive (DriveSecretsVaultStore). It exists so the FocusPhone cutover's factory reset — which wipes
// every on-device store — no longer means re-entering every credential by hand: after the reset the owner
// enters one passphrase per app and each app restores its own section from Drive.
//
// Each app owns exactly one section. Publishing is a per-section merge (load the vault, replace only your
// section with `with`, save) so one app's write never clobbers another's. Reading takes only your section.
// Sections carry empty defaults so a vault that has only ever been written by one app still round-trips.
public sealed record SecretsVault
{
    public ClaudeSecrets Claude { get; init; } = new();
    public GeminiSecrets Gemini { get; init; } = new();
    public PlaidSecrets Plaid { get; init; } = new();
    public NytSecrets Nyt { get; init; } = new();
    public RemarkableSecrets Remarkable { get; init; } = new();

    // Foreword's own Anthropic key for handwriting extraction, separate from the shared Claude voice key.
    //
    // The WIRE KEY stays "DailyLoader" and always will. There is no naming policy (the only other
    // [JsonPropertyName] here is Pulp's, below), so a property name here is literally the key written to the
    // owner's Drive — renaming it would make every existing vault deserialise this section to its
    // empty default, silently, on every phone at once. The calendar mapping JSON is the reason that
    // would hurt: it lives in the vault precisely because it is tedious to reconstruct by hand.
    [JsonPropertyName("DailyLoader")]
    public ForewordSecrets Foreword { get; init; } = new();

    public GmailSecrets Gmail { get; init; } = new();

    // The FocusPhone launcher's GitHub token for the self-update channel.
    public string GitHubUpdateToken { get; init; } = string.Empty;

    public MusicSecrets Music { get; init; } = new();

    // Google Maps Platform. ONE key, because Maps Platform bills a project rather than a product:
    // routes, weather, pollen and air quality are the same credential with different APIs enabled on
    // it. Four fields would invite four places to paste the same string and three of them to go
    // stale. The house is here beside it because it is the other thing every one of those calls
    // needs — where the family's day starts, and where the weather is.
    public MapsSecrets Maps { get; init; } = new();

    // Score's TIDAL APP credentials — the registered developer app, which is one thing
    // the household owns however many people listen. The per-person half of TIDAL (the
    // refresh token that carries someone's subscription) is deliberately NOT here: see
    // PersonalVault.
    public TidalSecrets Tidal { get; init; } = new();

    // Pulp's three household API keys: the streaming-availability lookups (what is on which of the
    // household's services), the rating chip, and the trailers and league highlights. Household
    // facts, not per-person ones — one key per service however many people watch — so they live
    // here rather than in PersonalVault.
    //
    // The WIRE KEY is "Pulp" and is frozen, pinned with [JsonPropertyName] for the same reason as
    // Foreword's section above: with no naming policy a property name is the key written to Drive,
    // so renaming the property would load every existing vault's section as empty, silently.
    [JsonPropertyName("Pulp")]
    public PulpSecrets Pulp { get; init; } = new();

    // Constitution's one shared fact: the household's Sleep Number login. One bed account serves both
    // adults, so it is a household fact and lives here. Per-install facts (Health Connect choices, the
    // work schedule) are NOT here — restored from the first phone onto the second they would be
    // Foreword Release I's bug again — they belong in PersonalVault.
    //
    // The WIRE KEY is "Constitution" and is frozen, pinned with [JsonPropertyName] for the same reason
    // as Foreword's and Pulp's sections above: with no naming policy a property name is the key written
    // to Drive, so a rename would load every existing vault's section as empty, silently.
    [JsonPropertyName("Constitution")]
    public ConstitutionSecrets Constitution { get; init; } = new();

    // Stable Drive file ids for the suite's well-known JSON files (voice profile/config, this vault, taste
    // profile, audio library), keyed by file name. Lets those files be MOVED in Drive without breaking the
    // apps (thread #13): the app resolves by remembered id first, falling back to a name search. Cross-app,
    // not owned by any one section.
    public IReadOnlyDictionary<string, string> DriveFileIds { get; init; } = new Dictionary<string, string>();

    // The account that first published to this vault, and therefore the only one that
    // writes it. Stamped by VaultOwnership.Claim on the first publish; every other
    // install compares itself against this and settles for reading. Empty on a vault
    // written before ownership existed, which reads as unclaimed — the owner's phone
    // is the one that publishes most, so it takes it back on the next save.
    public string OwnerAccount { get; init; } = string.Empty;

    public static SecretsVault Empty { get; } = new();
}

// The other vault: one file per PERSON, in that person's own Drive, holding the
// credentials that are theirs rather than the household's.
//
// It exists because the shared vault is exactly that — shared — and some credentials
// must not be. A TIDAL refresh token carries one member's Family entitlement; sharing
// it would put both installs on one membership and back into the single-stream problem
// Family was bought to solve, and it would mean either person's sign-out silently
// signing the other out too.
//
// Same shape and same crypto as SecretsVault, deliberately: one passphrase, one
// envelope format, one thing to understand. What differs is only where the file lives
// and who may write it — the shared vault is read-only on the second install, this one
// is not.
public sealed record PersonalVault
{
    // Whose listening account this install signs in as. The refresh token is what
    // survives a factory reset, exactly as RemarkableSecrets.DeviceToken does for the
    // tablet — without it the owner signs in again on a fresh phone, with it they do
    // not.
    public TidalAccount TidalAccount { get; init; } = new();

    // Which of the household's columns this install belongs to, as the word rather than the number
    // — the same lesson the calendar mapping learned, and for the same reason: a stored enum that
    // serialises as an index is one reordering away from meaning somebody else.
    //
    // It is here rather than in the shared vault because it is the one fact that MUST differ
    // between the two installs. In the shared file it would be restored from the first phone onto
    // the second, and its owner's handwriting would start filing under somebody else's name.
    public string OwnerColumn { get; init; } = string.Empty;

    // The photo mark an install once signed its documents with, as a base64 PNG. No longer written:
    // documents now carry the owner's drawn mark (Storyworthtelling.Paper.OwnersMark). Kept, like
    // OwnerColumn above (Foreword now infers that instead of storing it), so a file already in
    // somebody's Drive still round-trips.
    public string OwnerPhoto { get; init; } = string.Empty;

    // Foreword's calendar choices: the columns this account prints, which calendar feeds which, the
    // words and addresses that route to each person, and the work blocks. They are this account's
    // own — another account on the same household sets up its own — so they are kept here, in this
    // person's Drive, rather than in the shared vault, whose copy only ever held the first install's.
    // Stored as the same JSON the app keeps on the phone, so restoring is a copy, not a translation.
    public string CalendarMappingJson { get; init; } = string.Empty;

    public string WorkScheduleJson { get; init; } = string.Empty;

    // Constitution's Health Connect choices: which signals this person agreed to share with the app.
    // A per-install fact, so it is here and never in the shared vault — restored from the first phone
    // onto the second it would hand one adult's choices to the other. Stored as the same JSON the app
    // keeps on the phone, so restoring is a copy. The WIRE KEY is this property's name, with no naming
    // policy, so it is frozen from the day it is first written.
    public string HealthConnectChoicesJson { get; init; } = string.Empty;

    // Constitution's bed side: which side of the household's Sleep Number bed is this person's, as the
    // word "left" or "right", or "none" when they chose the bed is not theirs to read. Empty means never
    // chosen. The household shares one bed login, so the side is the one fact that must differ between
    // the two installs: here it is per person, and never in the shared vault. The WIRE KEY is this
    // property's name, with no naming policy, so it is frozen from the day it is first written.
    public string SleeperSide { get; init; } = string.Empty;

    public static PersonalVault Empty { get; } = new();
}

// One person's TIDAL sign-in. UserId is kept beside the token because a token alone
// cannot tell you who it belongs to, and an install that cannot name its own account
// cannot say anything useful when the sign-in stops working.
public sealed record TidalAccount(string UserId = "", string RefreshToken = "");

// The shared Claude/Anthropic key that drives the owner's-voice messaging across every app. Canonical
// source of truth once the vault is live (replaces the plaintext storyworthtelling-voice-config.json).
public sealed record ClaudeSecrets(string ApiKey = "");

public sealed record GeminiSecrets(string ApiKey = "");

// Plaid's app credentials plus the per-institution access tokens, which are generated on-device by the
// Hosted-Link flow and otherwise lost on a reset (each bank would have to be re-linked by hand).
public sealed record PlaidSecrets(
    string ClientId = "",
    string Secret = "",
    string Environment = "sandbox")
{
    public IReadOnlyList<PlaidItem> Items { get; init; } = [];
}

// One connected Plaid Item (= one bank login): the long-lived access token, its item id, and the display
// name, mirroring the fields Ledger keeps on Ledger.Domain.Model.Institution.
public sealed record PlaidItem(string ItemId = "", string AccessToken = "", string Name = "");

public sealed record NytSecrets(string SessionCookie = "", string ApiKey = "");

// The Maps Platform key and the household's address. Coordinates rather than only the address
// because the weather, pollen and air-quality APIs take a point and the Routes API takes text: the
// address is what a drive is measured from, the point is what the forecast is for.
public sealed record MapsSecrets(
    string ApiKey = "",
    string HomeAddress = "",
    double HomeLatitude = 0,
    double HomeLongitude = 0);

public sealed record RemarkableSecrets(string DeviceToken = "");

// Foreword's handwriting Anthropic key plus the owner-authored calendar mapping JSON (a magic string
// that is tedious to reconstruct by hand). Written to Drive under the key "DailyLoader" — see above.
public sealed record ForewordSecrets(string AnthropicApiKey = "", string CalendarMappingJson = "");

// The FocusPhone Communication head's Gmail credentials: address, the IMAP/SMTP app password, and the
// Messages-for-Web (gmbridge) session blob, which is generated by pairing and otherwise lost on a reset.
public sealed record GmailSecrets(string Address = "", string AppPassword = "", string BridgeSession = "");

// The FocusPhone Audio head's chosen music Drive-folder id.
public sealed record MusicSecrets(string FolderId = "");

// Score's TIDAL app credentials: the registered developer app's id and secret. These
// identify the SOFTWARE, not a listener — TIDAL's own Credentials model draws the same
// line, carrying a clientId always and a userId only once somebody has signed in.
//
// The app is free and shared; the subscription is not. So this section holds only the
// half that is genuinely common to both installs, and the refresh token that carries a
// person's Family membership lives in their own PersonalVault.
public sealed record TidalSecrets(
    string ClientId = "",
    string ClientSecret = "");

// Pulp's household API keys: the streaming-availability service, the rating service behind the
// rating chip, and the video service for trailers and league highlights. Written to Drive under the
// key "Pulp", with these three inner names exactly — see above.
public sealed record PulpSecrets(
    string StreamingAvailabilityKey = "",
    string OmdbKey = "",
    string YouTubeKey = "");

// Constitution's household Sleep Number login: the account's sign-in name (an email address) and its
// password. Written to Drive under the key "Constitution", with these two inner names exactly — see above.
public sealed record ConstitutionSecrets(
    string SleepNumberLogin = "",
    string SleepNumberPassword = "");
