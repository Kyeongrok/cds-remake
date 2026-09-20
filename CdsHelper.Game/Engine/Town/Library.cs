using CdsHelper.Game.Local.Helpers;

namespace CdsHelper.Game.Engine.Town;

/// <summary>
/// 도서관 서가를 채우는 규칙 — 진짜 책 사이사이에 <b>읽을 수 없는 책</b>을 끼운다.
/// </summary>
/// <remarks>
/// 그 마을에 놓인 책만 꽂으면 서가가 휑하다. 게임은 진짜 책 한 권을 꽂기 전에 가짜를
/// 몇 권 끼워 넣어 책장을 채운다(<c>0x004721E3</c>).
/// <code>
///   4721e3  남은 자리가 없으면 그냥 진짜 책을 꽂는다
///   4721e9  rand(3) 이 0 이 아니면 → 끼울 자리 = rand(3) 이 0 이면 0, 아니면 1
///   472209  rand(3) 이 0 이면      → 끼울 자리 = min(rand(남은자리 + 1), 8)   (뭉텅이)
///   472228  그 자리마다 <b>rand(3) 이 0 이 아닐 때</b> 실제로 꽂는다(<c>test eax,eax</c>
///           <c>je</c> 가 <b>0 일 때 건너뛴다</b>) — 안 꽂아도 자리는 넘어가므로
///           <b>세 자리에 하나꼴</b>로 빈 자리가 생긴다
///   472236  꽂을 때 책 번호가 <b>-1</b> 이다
///   472245  쓴 만큼 남은 자리를 깎는다
/// </code>
/// <c>4721f7</c> 의 <c>cmp eax,1 / sbb edi,edi / inc edi</c> 는 <b>rand(3) 이 0 일 때 0,
/// 아니면 1</b> 이다 — 세 번에 <b>두 번</b>은 자리를 하나 띄운다. 예전에는 이것을 뒤집어
/// 읽어 세 번에 한 번만 띄웠고, 그래서 서가가 게임보다 빽빽했다.
///
/// <b>가짜를 꽂는 쪽도 한 번 뒤집어 읽었다.</b> <c>0x00472234</c> 의 <c>je</c> 는 주사위가
/// <b>0 일 때 건너뛰라</b>는 것이라, 꽂는 것은 세 번에 <b>두 번</b>이다. 반대로 읽어 두어서
/// 서가가 구멍투성이였다 — 게임 서가는 훨씬 촘촘하다.
///
/// 책 번호 -1 은 책등 색을 정하는 <c>0x004716A0</c> 이 맨 앞에서 걸러 <c>0</c>(초록)을
/// 돌려준다(<c>0x00471768</c>). 그래서 가짜는 늘 초록이다. <b>진짜 책도 남은 힌트가
/// 없으면 같은 초록</b>이라 둘은 서가에서 구별되지 않는다 — 초록이 잔뜩 선 서가를 보고
/// "쓸모없는 책은 붙여 꽂나" 싶어지는 까닭이다.
///
/// 무작위는 <b>씨앗을 박고</b> 돌린다(<c>0x004721C9</c> 가 srand). 그래서 같은 마을에
/// 같은 때 들어가면 책장이 늘 같은 모양이다 — 들어갈 때마다 뒤바뀌지 않는다.
/// </remarks>
public static class Library
{
    /// <summary>책장 자리 수. 게임도 이만큼에서 그만 꽂는다(<c>0x004715E0</c> 의 <c>cmp 0x33</c>).</summary>
    public const int Slots = 51;

    /// <summary>한 번에 끼우는 가짜 책의 가장 많은 수(<c>0x00472216</c> 의 <c>cmp eax,8</c>).</summary>
    public const int MaxFillers = 8;

    /// <summary>세 번에 한 번 꼴로 일어난다는 뜻 — 게임이 <c>rand(3)</c> 로 던지는 주사위다.</summary>
    private const int Dice = 3;

    /// <summary>놀이가 시작하는 해. 씨앗을 짓는 데 쓴다(<c>0x004721AE</c> 의 <c>sub eax,0x5c8</c>).</summary>
    private const int FirstYear = 1480;

    /// <summary>서가가 바뀌는 주기(해). 씨앗이 <c>(해-1480)/4</c> 라 넉 해 동안은 그대로다.</summary>
    private const int Period = 4;

    /// <summary>
    /// 책장 한 자리 — 진짜 책이거나, 읽을 수 없는 초록 책이거나, 빈 자리다.
    /// </summary>
    /// <param name="Book">진짜 책. 가짜이거나 빈 자리면 null.</param>
    /// <param name="Filler">읽을 수 없는 초록 책이면 true.</param>
    public readonly record struct Slot(BookTable.Book? Book, bool Filler);

    /// <summary>
    /// 그 마을·그 무렵의 책장은 늘 같은 모양이다. 씨앗은 게임 것을 그대로 쓴다
    /// (<c>0x004721A9</c>) — <b>도시 번호 + (해 - 1480) / 4</b> 다.
    /// </summary>
    /// <remarks>
    /// 해를 넷으로 나눠 쓰므로 서가는 <b>넉 해에 한 번</b> 바뀐다. 주사위까지 게임 것이라
    /// (<see cref="GameRandom"/>) 어느 자리에 무엇이 꽂히는지가 게임과 똑같다.
    /// </remarks>
    public static GameRandom RandomFor(int cityId, int year) =>
        new(cityId + (year - FirstYear) / Period);

    /// <summary>
    /// 책장에 꽂을 차례를 짓는다. 자리 차례대로 <see cref="Slots"/> 칸까지다.
    /// </summary>
    /// <param name="books">그 마을에 놓인 진짜 책.</param>
    /// <param name="random">씨앗을 박은 주사위(<see cref="RandomFor"/>).</param>
    public static List<Slot> Shelve(IReadOnlyList<BookTable.Book> books, GameRandom random)
    {
        var shelf = new List<Slot>(Slots);
        int spare = Slots - books.Count;      // 가짜로 채울 수 있는 자리

        foreach (var book in books)
        {
            if (shelf.Count >= Slots) break;

            if (spare > 0)
            {
                // 세 번에 두 번은 한 자리만 띄우고, 한 번은 뭉텅이로 띄운다.
                int gap = random.Next(Dice) != 0
                    ? (random.Next(Dice) == 0 ? 0 : 1)
                    : Math.Min(random.Next(spare + 1), MaxFillers);

                for (int i = 0; i < gap && shelf.Count < Slots; i++)
                    // 자리는 넘어가되 세 번에 <b>두 번</b> 실제로 꽂는다 — 나머지 한 번이 빈 자리다.
                    shelf.Add(new Slot(null, random.Next(Dice) != 0));

                spare -= gap;
            }

            shelf.Add(new Slot(book, false));
        }
        return shelf;
    }
}
