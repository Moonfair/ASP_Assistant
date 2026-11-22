namespace ASPAssistant.Core.Services;

public sealed record GarrisonPhase(int State, int SubState, string? DraftChoiceId)
{
    public const int BossBattle = 6;
}

internal sealed partial class GarrisonMemoryReader
{
    private GarrisonPhase? ReadPhase(long manager, long center)
    {
        // PC 2.7.91: use the current client state, not the server's next state or a scheduled round.
        long Child(long parent, int field, string type, string ns)
        {
            long obj = parent == 0 ? 0 : Pointer(parent + field);
            if (obj != 0) CheckType(obj, type, ns);
            return obj;
        }

        var status = Child(manager, 56, "AutoChessGameStatus", Chess);
        var model = Child(status, 16, "AutoChessGameStateModel", "");
        var machine = Child(model, 24, "AutoChessGameStateMachine", "");
        var current = Child(machine, 96, "AutoChessGameState", "");
        if (current == 0) return null;
        int state = Number(machine + 92), subState = Number(current + 24);
        if (state != Number(current + 20)) return null;

        // TryGetDisplayContext: shared draft = SP_PREPARE + MAIN/END.
        // The other branch is a personal item choice during normal preparation, not a draft.
        string? choiceId = null;
        if (state == 2 && subState is 3 or 4)
        {
            var choice = Child(center, 264, "AutoChessEffectChooseDataModel", Chess);
            var options = Child(choice, 32, "List`1", Generic);
            if (options != 0 && Number(options + 24) is > 0 and <= 256)
                choiceId = Identifier(choice + 24);
        }
        return new(state, subState, choiceId);
    }
}
