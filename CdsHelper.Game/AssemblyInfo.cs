using System.Runtime.CompilerServices;

// 미궁 64 퍼즐은 CdsHelper.Maze 에, 일기토는 CdsHelper.Duel 에 따로 낸다. 두 화면 다
// 이쪽 밤색 판(InfoDialog)·게임 글꼴·띠 단추를 그대로 쓰므로 안쪽을 열어 준다.
[assembly: InternalsVisibleTo("CdsHelper.Maze")]
[assembly: InternalsVisibleTo("CdsHelper.Duel")]
