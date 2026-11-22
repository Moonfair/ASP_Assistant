namespace ASPAssistant.Core.Services;

public sealed record GameRoomSeat(string Uid, string Nickname, bool Ready);

public sealed record GameRoomObservation(string SelfUid, string Nickname, string? RoomId,
    string? HostUid, string Phase, GameRoomSeat[] Members);
