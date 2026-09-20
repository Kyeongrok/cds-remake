using CdsHelper.Support.Local.Models;
using Prism.Events;

namespace CdsHelper.Support.Local.Events;

/// <summary>
/// 세이브 데이터가 로드되었을 때 발생하는 이벤트
/// </summary>
public class SaveDataLoadedEvent : PubSubEvent<SaveDataLoadedEventArgs>
{
}

/// <summary>
/// 세이브 데이터 로드 이벤트 인자
/// </summary>
public class SaveDataLoadedEventArgs
{
    public SaveGameInfo SaveGameInfo { get; set; } = null!;
    public PlayerData? PlayerData { get; set; }
    public string FilePath { get; set; } = string.Empty;
}
