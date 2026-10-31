namespace Constitution.WorkoutsTab;

/// <summary>
/// An area that can raise an undo on the shell's one dog-ear. The shell hands it the act key when the fold is turned, so
/// the area can reverse the act (the board has already closed the window by then).
/// </summary>
public interface IUndoableArea
{
    void UndoTurned(string actKey);
}
