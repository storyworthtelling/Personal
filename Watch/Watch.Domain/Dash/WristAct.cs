namespace Watch.Domain.Dash;

/// <summary>What the wrist does to a correspondence (W-5.1.4; A14).</summary>
public enum WristActKind
{
    File,

    /// <summary>Undo-first, as on the phone: Post holds it for the window before it deletes at the source.</summary>
    Delete,
}

/// <summary>Where an act from the wrist stands (W-5.1.4).</summary>
public enum WristActState
{
    /// <summary>The act is on its way to the phone, and the ring is open.</summary>
    Telling,

    /// <summary>The phone has the act, and the ring is open.</summary>
    Open,

    /// <summary>Undo was tapped: the undo is on its way, or waits for the act to have reached the phone first.</summary>
    Undoing,

    /// <summary>Taken back, or never done: the correspondence is as it was.</summary>
    TakenBack,

    /// <summary>The ring emptied with the act at the phone: the act is final.</summary>
    Final,

    /// <summary>The act never reached the phone: nothing changed, and the quiet line says why.</summary>
    Failed,

    /// <summary>The undo never reached the phone: the act stands, and the quiet line says so.</summary>
    UndoLost,
}

/// <summary>
/// Filing or deleting a correspondence from the wrist, undo-first (P14 W-5.1.4; A14, WD §9, O5; mock: Filed, with
/// Undo). There is no confirmation: the act goes to the phone at once (<see cref="Message"/>), under the watch's own
/// name for it and with the window the wrist is showing, so Post holds it at least that long (B1, B2), and the Undo ring
/// opens. An undo inside the window takes back exactly that act, once; an undo tapped before the phone has the act waits
/// for it, so it never arrives first; when the ring empties the act is final. Nothing is claimed that did not happen: an
/// act that never reached the phone left the correspondence where it was, and an undo that never reached it left the act
/// standing, each said as a quiet line (WD §6). The ring's tap and the phone's answers come on different threads, so
/// every change is locked.
/// </summary>
public sealed class WristAct
{
    private const string Prefix = "act-";

    private readonly object gate = new();
    private WristActState state = WristActState.Telling;
    private DashTrouble? trouble;
    private bool told;
    private bool closed;
    private bool undoSent;

    private WristAct(WristActKind kind, string key, string name, TimeSpan window, Guid id)
    {
        if (!DashIds.IsCorrespondence(key))
        {
            throw new ArgumentException("An act from the wrist names a correspondence by Post's key for it.", nameof(key));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An act from the wrist says whose correspondence it took, for its Undo line.", nameof(name));
        }
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero);

        Kind = kind;
        Key = key;
        Name = name.Trim();
        Window = window > DashLimits.MaxWindow ? DashLimits.MaxWindow : window;
        Id = Prefix + id.ToString("N");
    }

    /// <summary>The watch's own name for the act: "act-" and 32 lowercase hex digits (<see cref="DashIds.IsAct"/>).</summary>
    public string Id { get; }

    public string Key { get; }

    public string Name { get; }

    public WristActKind Kind { get; }

    /// <summary>The window the ring shows and the act carries: the watch's undo window, at most what the wire carries.</summary>
    public TimeSpan Window { get; }

    public WristActState State
    {
        get
        {
            lock (gate)
            {
                return state;
            }
        }
    }

    /// <summary>The act for the phone: the whole correspondence, by its key.</summary>
    public DashMessage Message => Kind == WristActKind.File ? new DashFile(Id, Key, [], Window) : new DashDelete(Id, Key, [], Window);

    /// <summary>Whether a tap on the ring still takes the act back.</summary>
    public bool CanUndo
    {
        get
        {
            lock (gate)
            {
                return !closed && state is WristActState.Telling or WristActState.Open;
            }
        }
    }

    /// <summary>Whether the screen has nothing left to say and goes: the act is final, or taken back.</summary>
    public bool Finished => State is WristActState.TakenBack or WristActState.Final;

    /// <summary>Whether the correspondence has left People waiting: the act went, or is going, and was not taken back.</summary>
    public bool Leaves => State is WristActState.Telling or WristActState.Open or WristActState.Final or WristActState.UndoLost;

    /// <summary>"Lee filed" (mock), or only the name when the act never reached the phone.</summary>
    public string Heading =>
        State == WristActState.Failed ? Name
        : Kind == WristActKind.File ? DashWords.Filed(Name)
        : DashWords.Deleted(Name);

    /// <summary>"Tap to undo" while a tap still takes it back.</summary>
    public string? Prompt => CanUndo ? DashWords.TapToUndo : null;

    /// <summary>The quiet line: what stood in the way, or nothing.</summary>
    public string? Line
    {
        get
        {
            lock (gate)
            {
                return state switch
                {
                    WristActState.Failed => DashWords.For(trouble ?? DashTrouble.NoPhone),
                    WristActState.UndoLost => DashWords.UndoLost,
                    _ => null,
                };
            }
        }
    }

    /// <param name="id">A fresh name for the act; any Guid, written as 32 lowercase hex digits.</param>
    public static WristAct File(string key, string name, TimeSpan window, Guid id) => new(WristActKind.File, key, name, window, id);

    /// <param name="id">A fresh name for the act; any Guid, written as 32 lowercase hex digits.</param>
    public static WristAct Delete(string key, string name, TimeSpan window, Guid id) => new(WristActKind.Delete, key, name, window, id);

    /// <summary>
    /// How handing the act to the phone went (null: it has it). Returns the undo to tell now if one was tapped while the
    /// act was on its way; an act that never got there needs no undo. Heard once.
    /// </summary>
    public DashUndo? Told(DashTrouble? said)
    {
        lock (gate)
        {
            if (told)
            {
                return null;
            }
            told = true;
            switch (state)
            {
                case WristActState.Telling when said is not null:
                    state = WristActState.Failed;
                    trouble = said;
                    return null;
                case WristActState.Telling:
                    state = closed ? WristActState.Final : WristActState.Open;
                    return null;
                case WristActState.Undoing when said is not null:
                    // The person took it back and the phone never had it: the correspondence is as it was.
                    state = WristActState.TakenBack;
                    return null;
                case WristActState.Undoing:
                    undoSent = true;
                    return new DashUndo(Id);
                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// A tap on the ring, <paramref name="elapsed"/> after it opened: the undo to tell the phone, inside the window, once;
    /// none while the act is still on its way (it goes when <see cref="Told"/> hears it landed), and none after.
    /// </summary>
    public DashUndo? Undo(TimeSpan elapsed)
    {
        lock (gate)
        {
            if (closed || elapsed >= Window || state is not (WristActState.Telling or WristActState.Open))
            {
                return null;
            }

            var landed = state == WristActState.Open;
            state = WristActState.Undoing;
            if (!landed)
            {
                return null;
            }
            undoSent = true;
            return new DashUndo(Id);
        }
    }

    /// <summary>How handing the undo to the phone went (null: it has it).</summary>
    public void UndoTold(DashTrouble? said)
    {
        lock (gate)
        {
            if (state != WristActState.Undoing || !undoSent)
            {
                return;
            }
            if (said is null)
            {
                state = WristActState.TakenBack;
                return;
            }
            state = WristActState.UndoLost;
            trouble = said;
        }
    }

    /// <summary>The ring emptied, or the screen went: no tap takes the act back now.</summary>
    public void Closed()
    {
        lock (gate)
        {
            closed = true;
            if (state == WristActState.Open)
            {
                state = WristActState.Final;
            }
        }
    }
}
