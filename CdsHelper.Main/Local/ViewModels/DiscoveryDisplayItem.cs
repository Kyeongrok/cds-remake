using CommunityToolkit.Mvvm.ComponentModel;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.Local.ViewModels;

/// <summary>
/// 발견물 한 줄 — 세계지도(<c>WorldMapContent</c>)의 좌표 편집(<c>EditDiscoveryDialog</c>)이 쓴다.
/// </summary>
/// <remarks>발견물 목록 화면을 없애면서 그 ViewModel 파일에서 떼어 냈다.</remarks>
public partial class DiscoveryDisplayItem : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? HintId { get; set; }
    public string HintName { get; set; } = "";
    public string AppearCondition { get; set; } = "";
    public string BookName { get; set; } = "";
    public string ParentNames { get; set; } = "";
    private string _coordinateDisplay = "";
    public string CoordinateDisplay
    {
        get => _coordinateDisplay;
        set => SetProperty(ref _coordinateDisplay, value);
    }
    public int? LatFrom { get; set; }
    public int? LatTo { get; set; }
    public int? LonFrom { get; set; }
    public int? LonTo { get; set; }

    public bool IsHintObtained { get; set; }

    public bool IsDiscoveryFound { get; set; }

    public string DiscoveryStatusDisplay => IsDiscoveryFound ? "O" : "";

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (SetProperty(ref _isChecked, value))
                AppSettings.SetDiscoveryChecked(Id, value);
        }
    }

    public void SetCheckedWithoutSave(bool value)
    {
        _isChecked = value;
    }

    // 슬롯 상태 편집 — 선택 시 세이브 파일에 즉시 반영
    public const string StateUndiscovered = "미발견";
    public const string StateFound = "발견";
    public const string StateAnnounced = "발표";
    public static IReadOnlyList<string> SlotStateOptions { get; } = new[]
    {
        StateUndiscovered, StateFound, StateAnnounced
    };

    // DB Id와 세이브 슬롯 인덱스 사이 오프셋.
    //
    // 세이브 슬롯 번호는 게임 발견물 번호(0~273)와 그대로 같다. DB Id 가 그보다 1 크므로 -1 이다.
    // 예전 값은 +18 이었는데("슬롯 1~18은 특수 슬롯으로 추정") 근거가 없었고, 실제 세이브로
    // 확인해 보면 틀렸다 — SAVEDATA.CDS 0x1AA81 부터 164바이트씩 훑어 +0x15 의 bit6 을 보면
    // 1~8번이 신대륙·인도·말라카해협·향료제도·중국·마젤란해협·세계일주항로·지팡그로,
    // CDS_95.EXE 발견물 표(GameMapCoords)의 1~8번과 이름까지 그대로 맞는다.
    // +18 이면 그 칸들이 음수 색인을 가리켜 발견 여부가 통째로 어긋난다.
    // (cds95-mod HintUtilKR 의 disc.c 도 슬롯 i 를 발견물 i 로 그냥 읽는다.)
    public const int SaveSlotOffset = -GameMapCoords.DiscoveryIdOffset;
    public int SaveSlotIndex => Id + SaveSlotOffset;

    // (slotIndex, newStateByte) → 세이브 파일에 저장
    public static Action<int, byte>? OnSlotStateChanged;

    // 현재 슬롯 state 바이트 (하위 6비트 = 카테고리 base, 변경 시 보존)
    public byte CurrentStateByte { get; set; }

    private string _slotState = StateUndiscovered;
    public string SlotState
    {
        get => _slotState;
        set
        {
            if (!SetProperty(ref _slotState, value)) return;
            byte baseBits = (byte)(CurrentStateByte & 0x3F);
            byte newByte = value switch
            {
                StateAnnounced => (byte)(baseBits | 0xC0),
                StateFound => (byte)(baseBits | 0x40),
                _ => baseBits
            };
            CurrentStateByte = newByte;
            OnSlotStateChanged?.Invoke(SaveSlotIndex, newByte);
        }
    }

    public void SetSlotStateWithoutSave(byte stateByte)
    {
        CurrentStateByte = stateByte;
        bool announced = (stateByte & 0x80) != 0;
        bool found = (stateByte & 0x40) != 0;
        _slotState = announced ? StateAnnounced : (found ? StateFound : StateUndiscovered);
        OnPropertyChanged(nameof(SlotState));
    }
}
