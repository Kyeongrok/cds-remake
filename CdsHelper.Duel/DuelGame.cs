using System.Windows;
using CdsHelper.Game.UI.Views;

namespace CdsHelper.Duel;

/// <summary>
/// 일기토의 바깥 문. <b>이 덩이에서 밖으로 열린 것은 이것 하나뿐</b>이다.
/// </summary>
/// <remarks>
/// 화면(<see cref="DuelDialog"/>)은 CdsHelper.Game 의 밤색 판을 물려받는데 그 판이
/// 안쪽 것이라 화면도 안쪽으로 둘 수밖에 없다. 그래서 부르는 자리만 이렇게 따로
/// 낸다 — 띄우는 쪽(CdsHelper.Form)이 <c>ShipMapWindow.DuelGame = DuelGame.Play</c>
/// 로 걸어 준다. <see cref="CdsHelper.Maze.MazeGame"/> 과 같은 꼴이다.
/// </remarks>
public static class DuelGame
{
    /// <summary>
    /// 상대의 세기 눈금. 값은 게임의 괴물·적장 벌(<c>0x00440DC1</c>)에서 따 왔다.
    /// </summary>
    /// <summary>얼굴 번호는 MALE.CDS 에서 눈으로 골랐다 — 인물 표에 매인 것이 아니다.</summary>
    /// <summary>
    /// 몸짓 벌(<c>Kit</c>)은 <b>FIGHTER.CDS 의 파트를 반으로 나눈 번호</b>다.
    /// </summary>
    /// <remarks>
    /// 파일은 파트 서른셋이 <b>그림·팔레트 짝</b>으로 갈마든다 — 짝수가 646272바이트짜리
    /// 그림 한 장, 홀수가 768바이트 팔레트다. 게임은 통째로 안 읽고 <b>한 벌만</b> 읽는다
    /// (잡는 버퍼 <c>[0x00572A64]</c> = 646272 가 딱 한 파트 크기다).
    /// <code>
    ///   004a8e4b  파트 0            → 주인공 그림  (this + 0x21C)   ; 늘 0 이다
    ///   004a8f04  파트 1            → 주인공 팔레트
    ///   004a8f62  eax = [this+0x15C]                                ; ← 상대 몸짓 벌
    ///   004a8f6e  파트 = 벌 x 2     → 상대 그림    (this + 0x230)
    ///   004a9194  파트 = 벌 x 2 + 1 → 상대 팔레트
    ///   004a9200  eax = [this+0x1D0]
    ///   004a920c  파트 = 그것 x 2 + 0x12                            ; 파트 18부터의 둘째 무리
    /// </code>
    /// 그래서 <b>주인공은 늘 벌 0</b> 이고(여기서도 <c>kit</c> 을 안 넘겨 0 이 된다), 상대만
    /// 골라 쓴다. 벌 0~8 이 파트 0~17 이라 뽑아 둔 아홉 벌과 맞는다.
    ///
    /// <b>아직 못 짚은 것.</b> <c>+0x15C</c> 에 <b>쓰는</b> 자리는 <c>.text</c> 를 네 정렬로
    /// 다 훑어도 안 나온다 — 포인터를 미리 더해 놓고(<c>edi = this + 0x100</c> 꼴) 쓰는 것으로
    /// 보인다. 그래서 아래 벌 번호는 아직 눈으로 고른 것이다.
    /// </remarks>
    private static readonly (string Name, int Body, int Might, int Sword, int Face, int Kit)[] Foes =
    [
        ("해적 두목", 70, 70, 1, 41, 3),
        ("이슬람 제독", 80, 80, 2, 63, 5),
        ("토벌대장", 90, 90, 2, 12, 7),
        ("전설의 검객", 100, 100, 3, 88, 1),
    ];

    /// <summary>
    /// 한 판 한다.
    /// </summary>
    /// <remarks>
    /// 게임에서는 <b>해전에서 기함끼리 붙었을 때만</b> 열린다(<c>0x0043A347</c>).
    /// 아직 해전이 없으니 여기서는 상대를 골라 바로 붙는다.
    ///
    /// 내 값은 <c>0x004A87DE</c> 가 하듯 인물 레코드에서 그대로 가져온다 —
    /// 무력 <c>+0x28</c>, 검술 <c>+0x48</c> 에 1 을 더하는 자리까지 같다.
    /// </remarks>
    public static void Play(Window owner, Random rng)
    {
        var names = Foes.Select(foe => foe.Name).ToList();
        int pick = MapPointDialog.Ask(owner, names, "일기토");
        if (pick < 0) return;

        var (name, body, might, sword, face, kit) = Foes[pick];

        // 아직 인물을 물리지 않았으니 웬만한 모험가 하나를 세운다.
        var mine = new Duel.Fighter("나", 100, 60, 2, 40, weapon: 34, armour: 25, face: 0);
        var theirs = new Duel.Fighter(name, body, might, sword, might,
                                      weapon: 22 + pick * 10, armour: 15 + pick * 8,
                                      face: face, kit: kit);

        bool? won = DuelDialog.Play(owner, new Duel(mine, theirs, rng));
        if (won == null) return;

        NoticeDialog.Show(owner,
            won.Value
                ? "이겼다! 상대를 쓰러뜨렸다." + Environment.NewLine +
                  "「처형한다 · 놓아 준다 · 모두 뺏는다」 는 아직 안 옮겼다."
                : "졌다. 상대의 칼끝이 먼저 닿았다.",
            "일기토");
    }
}
