using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Engine.Town;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using Lines = CdsHelper.Game.Engine.Town.Poker.Lines;

namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 술집 미니 게임 「포카」의 카드판. 규칙은 <see cref="Poker"/> 에 모아 두었다.
/// </summary>
/// <remarks>
/// 게임의 들머리는 <c>0x0042F690</c> → <c>0x00406470</c>(<see cref="Play"/>), 판을 여는 손은
/// <c>0x004059E0</c>, 한 판은 <c>0x00405CF7</c> 이다. 볼트 67 §19 의 상태 기계를 그대로 따라
/// <b>차례대로 적힌 코드</b>로 돌린다 — 기다림(<c>0x00482D50(n)</c> = n x 500ms)과 단추 입력은
/// 속 고리(<see cref="DispatcherFrame"/>)로 기다린다.
///
/// <b>그림은 TRAMP.CDS 에서 그때 푼다</b>(<see cref="TrampArt"/>). 판 좌표는 <c>0x00401040</c>,
/// 곁창 좌표는 <c>0x004832D0</c>·<c>0x00482FA0</c>·<c>0x004856E0</c>·<c>0x00483970</c>·
/// <c>0x00484920</c>·<c>0x00484240</c>·<c>0x00485CB0</c> 에서 옮겼다.
///
/// <b>소리는 없다</b> — 판 안에서 효과음·음악을 부르는 곳이 없다. 나올 때 술집 음악만 되돌린다.
///
/// 원본에는 판 중에 나갈 길이 없는데, 미니 게임 창 버릇대로 금빛 테에 닫기 단추를 얹었다.
/// 판 중에 닫으면 그때까지 낸 판돈은 돌려받지 못한다.
/// </remarks>
internal sealed class PokerDialog : GameWindow
{
    /// <summary>술집 건물 코드 — 화자표에서 주인 얼굴을 찾는다(문화권 0~2 는 모두 MALE #32).</summary>
    private const int BuildingCode = 4;

    /// <summary>기다림 한 눈(<c>0x00482D50</c>).</summary>
    private const int HalfSecond = 500;

    /// <summary>글자 검정 0x49 · 창 바탕 10 · 카드 띠 옅은 파랑 0xCC.</summary>
    private const byte InkIndex = 0x49, PaperIndex = 10, StripIndex = 0xCC;

    /// <summary>카드 사이.</summary>
    private const int Pitch = 48;

    private const int DeckX = 248, DeckY = 160;
    private const int MyFaceX = 48, MyFaceY = 288, TheirFaceX = 448, TheirFaceY = 32;
    private const int BubbleX = 274, BubbleY = 32, BubbleW = 160, BubbleH = 96;
    private const int PanelX = 280, PanelY = 288, PanelW = 264, PanelShort = 104, PanelTall = 128;
    private const int ButtonY = 104, ButtonH = 24, BetButtonW = 80;
    private const int ResultW = 264, ResultH = 240;

    // ── 단추가 내는 답 ──────────────────────────────────────────────────────
    private const int Raise = 1, Call = 2, Fold = 3, Open = 4, SuitSort = 5, RankSort = 6,
                      Undo = 7, Decide = 8, Ok = 9, CardClick = 10, CardAsk = 20, Moved = 30;

    private readonly Poker _poker;
    private readonly TrampArt _art;
    private readonly Player _player;
    private readonly uint[]? _face;
    private readonly int _luck;

    private readonly Brush _ink, _paper, _stripBack;

    private readonly Canvas _scene = new()
    {
        Width = TrampArt.BoardWidth,
        Height = TrampArt.BoardHeight,
        ClipToBounds = true,
    };

    private readonly Image[] _mySlot = new Image[Poker.HandSize], _theirSlot = new Image[Poker.HandSize];

    /// <summary>판 위 칸 상태(<c>+0xB1..</c> 사람 · <c>+0xA6..</c> 상대) — 0 빈 칸 · 1 뒷면 · 2 앞면.</summary>
    private readonly byte[] _myState = new byte[Poker.HandSize], _theirState = new byte[Poker.HandSize];

    private readonly List<Image> _piles = [];
    private int _myPile, _theirPile;

    private readonly Image _coin;
    private readonly Canvas _bubble = new() { Width = BubbleW, Height = BubbleH };
    private readonly Canvas _words = new();
    private readonly Canvas _panel = new() { Width = PanelW, Height = PanelShort };
    private readonly Canvas _result = new() { Width = ResultW, Height = ResultH };
    private readonly Canvas _hover = new() { Width = 64, Height = 24, IsHitTestVisible = false };
    private readonly Image _hoverMark;
    private readonly GameUi.GameLabel _hoverRank;

    private readonly GameUi.GameLabel _purseText, _stakeText, _myPotText, _theirPotText;

    private DispatcherFrame? _asking, _sleeping;
    private int _choice;
    private bool _closed;

    /// <summary>카드 띠에 선 장수와 키 커서. 커서가 −1 이면 띠를 눌러도 답이 없다.</summary>
    private int _stripCount, _cursor = -1;

    /// <summary>Enter 가 「확인」인지(여는 카드 고르기·결과창). 아니면 커서 카드를 누른다.</summary>
    private bool _enterIsOk;

    /// <summary>창이 닫혀 판을 걷는다.</summary>
    private sealed class TableClosed : Exception;

    private PokerDialog(Window owner, Engine.Game game, TrampArt art, Poker poker, uint[]? face)
    {
        _poker = poker;
        _art = art;
        _player = game.Player;
        _face = face;
        _luck = _player.AbilityOf(Ability.Luck);

        _ink = Frozen(art.ColorOf(InkIndex));
        _paper = Frozen(art.ColorOf(PaperIndex));
        _stripBack = Frozen(art.ColorOf(StripIndex));

        Place(_scene, Sprite(art.Board, TrampArt.BoardWidth, TrampArt.BoardHeight), 0, 0);
        Place(_scene, Sprite(art.Deck, TrampArt.DeckWidth, TrampArt.DeckHeight), DeckX, DeckY, 5);
        Place(_scene, Sprite(FaceArt(face), Portraits.Width, Portraits.Height), TheirFaceX, TheirFaceY, 5);
        var mine = game.Faces?.TryGetBgra(PortraitAges.At(_player.Face, _player.Age, false, game.Faces),
                                          female: false);
        Place(_scene, Sprite(FaceArt(mine), Portraits.Width, Portraits.Height), MyFaceX, MyFaceY, 5);

        // 상태 창(0x004832D0) — 소지금과 「시 세」(= 내기돈).
        var status = Box(_scene, 24, 24, 128, 57, 30);
        Letter(status, "소지금", 8, 8);
        _purseText = Letter(status, "", 64, 8);
        Place(status, new Border { Width = 120, Height = 1, Background = _ink }, 4, 28);
        Letter(status, "시 세", 8, 32);
        _stakeText = Letter(status, "", 64, 32);

        // 판돈 창 둘(0x00482FA0).
        var myPot = Box(_scene, 32, 248, 112, 32, 30);
        Letter(myPot, "내기돈", 8, 8);
        _myPotText = Letter(myPot, "", 62, 8);
        var theirPot = Box(_scene, 432, 136, 112, 32, 30);
        Letter(theirPot, "내기돈", 8, 8);
        _theirPotText = Letter(theirPot, "", 62, 8);

        // 패 칸 열.
        for (int i = 0; i < Poker.HandSize; i++)
        {
            int k = i;
            _mySlot[i] = Sprite(null, TrampArt.TiltSize, TrampArt.TiltSize);
            Place(_scene, _mySlot[i], 32 + Pitch * i, 112 + Pitch * i, 20);
            HookHover(_mySlot[i], () => _poker.Mine[k], () => _myState[k]);

            _theirSlot[i] = Sprite(null, TrampArt.TiltSize, TrampArt.TiltSize);
            Place(_scene, _theirSlot[i], 464 - Pitch * i, 224 - Pitch * i, 20);
            HookHover(_theirSlot[i], () => _poker.Theirs[k], () => _theirState[k]);
        }

        _coin = Sprite(art.Coin, TrampArt.CoinSize, TrampArt.CoinSize);
        Place(_scene, _coin, 0, 0, 35);

        BuildBubble();
        Place(_scene, _bubble, BubbleX, BubbleY, 50);
        _bubble.Visibility = Visibility.Collapsed;

        Place(_scene, _panel, PanelX, PanelY, 40);

        // 카드 풍선(0x00401590) — 무늬 (8,4) · 끗수 이름 (24,4).
        Frame(_hover, 64, 24);
        _hoverMark = Sprite(null, TrampArt.MarkSize, TrampArt.MarkSize);
        Place(_hover, _hoverMark, 8, 4);
        _hoverRank = Letter(_hover, "", 24, 4);
        Place(_scene, _hover, 0, 0, 60);
        _hover.Visibility = Visibility.Collapsed;

        Place(_scene, _result, (TrampArt.BoardWidth - ResultW) / 2.0, (TrampArt.BoardHeight - ResultH) / 2.0, 70);
        _result.Visibility = Visibility.Collapsed;

        double zoom = GameUi.PixelZoom(owner, GameUi.PixelFit(owner, TrampArt.BoardWidth, TrampArt.BoardHeight, 2));
        _scene.LayoutTransform = new ScaleTransform(zoom, zoom);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = GameUi.Back;
        Content = GameUi.GoldFrame(_scene, Close);
        GameUi.EnableDrag(this, _scene);

        KeyDown += OnKey;
        Closed += (_, _) =>
        {
            _closed = true;
            if (_asking != null) _asking.Continue = false;
            if (_sleeping != null) _sleeping.Continue = false;
        };
        Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Run));

        ClearTable();
        Refresh();
    }

    /// <summary>
    /// 「포카를 권한다」 — 물음과 거절을 치르고 판을 연다(<c>0x0042F690</c> → <c>0x00406470</c>).
    /// </summary>
    /// <remarks>
    /// <code>
    ///   그림을 못 읽었으면 조용히 끝                          [+0x4C0] == 0
    ///   ① 「내기돈은 금화 %d닢이네. 승부하겠나?」 (예/아니오)   0x535B00[rand2]
    ///        아니오 → 「쳇, 겁주는 거냐.」 따위                 0x535B20[rand5]
    ///   ② 소지금 &lt; 10000 이면 한 번 더 묻는다               0x535B50[rand5], 아니오면 조용히 끝
    ///   ③ 소지금 &lt; 내기돈 이면 「돈이 없는 녀석은…」          0x535B08[rand5]
    ///   ④ 판을 연다                                          0x004059E0
    ///   나오면 술집 음악으로 되돌린다                          0x0047E360(0x5B63D8, 4)
    /// </code>
    /// 말하는 이는 늘 <b>술집 주인</b>(<c>[술집+0x80]</c>)이고 제목 띠는 빈 문자열(<c>0x0052F650</c>)이라 비어 있다.
    /// 시각·요일·이미 한 판 했는지 따위는 안 본다.
    /// </remarks>
    public static void Play(Window owner, Engine.Game game, int culture)
    {
        if (!Poker.CanPlayIn(culture)) return;
        var art = TrampArt.Open(game.Directory);
        if (art == null) return;

        try
        {
            var face = game.SpeakerFace(BuildingCode, culture);
            var poker = new Poker(new GameRandom(Environment.TickCount), game.Player.Gold);
            string Pick(string[] table) => table[poker.Dice.Next(table.Length)];

            if (!ConfirmDialog.Ask(owner, Money(Pick(Lines.AskStake), poker.Stake), null, face))
            {
                ConfirmDialog.Tell(owner, Pick(Lines.Refused), null, face);
                return;
            }
            if (poker.Purse < Poker.PoorGold && !ConfirmDialog.Ask(owner, Pick(Lines.Poor), null, face))
                return;
            if (poker.Purse < poker.Stake)
            {
                ConfirmDialog.Tell(owner, Pick(Lines.NoMoney), null, face);
                return;
            }

            new PokerDialog(owner, game, art, poker, face) { Owner = owner }.ShowDialog();
        }
        finally
        {
            game.Bgm.Play(BgmPlayer.TavernTrack);
        }
    }

    // ── 흐름 ────────────────────────────────────────────────────────────────

    /// <summary>판을 거듭 돌린다. 판 사이에서 그만두면 창을 닫는다.</summary>
    private void Run()
    {
        try
        {
            do PlayHand();
            while (NextHand());
        }
        catch (TableClosed)
        {
            // 창이 닫혔다 — 걷기만 한다.
        }
        finally
        {
            SyncGold();
            if (!_closed) Close();
        }
    }

    /// <summary>
    /// 한 판(<c>0x00405CF7</c>) — 밑돈 · 딜 · 혼잣말 · 교환 둘 · 여는 차례 · 베팅 네 바퀴 · 까기 · 정산.
    /// </summary>
    private void PlayHand()
    {
        ClearTable();
        Say(Pick(_poker.TheyDeal ? Lines.TheyDeal : Lines.YouDeal), 2);
        _poker.BeginHand();
        Refresh();

        Deal();
        Say(Mood(), 2);

        // 선이 상대면 사람이 먼저 바꾼다(0x004049F0 → 0x00404B00).
        if (_poker.TheyDeal)
        {
            YourExchange(Pick(Lines.YouSwapFirst));
            TheirExchange(Pick(Lines.TheySwapAfter));
        }
        else
        {
            TheirExchange(Pick(Lines.TheySwapFirst));
            YourExchange(Pick(Lines.YouSwapAfter));
        }
        _poker.ArrangeOpenOrder();

        // drop 1 = 선이 접음, 2 = 받는 쪽이 접음.
        int drop = 0;
        for (int n = 0; n < Poker.Rounds && drop == 0; n++)
        {
            if (Lead(n) < 0) drop = 1;
            else if (Reply(n) < 0) drop = 2;
        }

        if (drop == 0)
        {
            bool fold = _poker.TheyDeal ? _poker.TheyFoldAtShowdown() : !YouOpen();
            if (fold)
            {
                drop = 1;
            }
            else
            {
                _poker.CatchUp();
                Refresh();
            }
        }

        ShowHand(Poker.HandSize);
        Settle(drop);
    }

    /// <summary>
    /// 딜(<c>0x00402340</c>) — 한 쪽에 한 장씩 뒷면으로 놓고 0.5초. 선이 상대면 사람부터 받는다.
    /// 끝나면 판 위 칸을 모두 비운다(<c>0x00402490</c>) — 카드는 사람 손패 창에만 남는다.
    /// </summary>
    private void Deal()
    {
        for (int i = 0; i < Poker.HandSize; i++)
        {
            if (_poker.TheyDeal) { DealYou(i); DealThem(i); }
            else { DealThem(i); DealYou(i); }
        }
        ClearSlots();
    }

    private void DealYou(int i)
    {
        _poker.Mine[i] = _poker.Draw();
        _myState[i] = 1;
        DrawSlots();
        ShowHand(i + 1);
        Wait(1);
    }

    private void DealThem(int i)
    {
        _poker.Theirs[i] = _poker.Draw();
        _theirState[i] = 1;
        DrawSlots();
        Wait(1);
    }

    /// <summary>딜 뒤 상대의 혼잣말(<c>0x004024D0</c>) — 제 족보가 그대로 드러난다.</summary>
    private string Mood()
    {
        var dice = _poker.Dice;
        return Poker.Evaluate(_poker.Theirs) switch
        {
            >= Poker.Straight => Pick(Lines.MoodGreat),
            >= Poker.TwoPair => Pick(Lines.MoodGood),
            Poker.OnePair => dice.Next(2) != 0 ? Pick(Lines.MoodPairA) : Pick(Lines.MoodPairB),
            _ => dice.Next(2) != 0 ? Pick(Lines.MoodNoneA) : Pick(Lines.MoodNoneB),
        };
    }

    /// <summary>
    /// 상대 교환(<c>0x00402970</c>) — 말 1.5초, 버린 장마다 더미에 뒷면 0.5초, 채운 장마다 0.5초.
    /// </summary>
    private void TheirExchange(string line)
    {
        Say(line, 3);
        int thrown = Poker.Discard(_poker.Theirs, _poker.TheirDiscards());
        for (int r = 0; r < thrown; r++)
        {
            AddPile(mine: false);
            Wait(1);
        }
        for (int j = 0; j < thrown; j++)
        {
            int k = Poker.HandSize - thrown + j;
            _poker.DrawInto(_poker.Theirs, k, burn: true);
            _theirState[k] = 1;
            DrawSlots();
            Wait(1);
        }
        ClearSlots();
    }

    /// <summary>
    /// 사람 교환 창(<c>0x00484920</c>·<c>0x00484D20</c>) — 처음엔 다섯 장 모두 「바꿈」이다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   카드 왼쪽 누름     바꿈/HOLD 뒤집기
    ///   카드 오른쪽 누름   「HOLD 하시겠습니까?」·「HOLD 해제하시겠습니까?」(제목 「한다.」) 예 → 뒤집기
    ///   슈트소트           카드 바이트 오름차순, 표시도 같이 옮긴다
    ///   소트               끗수 오름차순, 표시도 같이
    ///   언두               원래 차례로 — 표시는 카드를 따라간다
    ///   결정               바꾼다 (0~5장, 제한 없음)
    /// </code>
    /// 버린 장마다 더미에 뒷면 한 장 0.5초, 채운 장마다 0.5초(<c>0x004025C0</c>).
    /// </remarks>
    private void YourExchange(string line)
    {
        SayOn(line);

        var hand = _poker.Mine;
        var original = (byte[])hand.Clone();
        var swap = new bool[Poker.HandSize];
        Array.Fill(swap, true);
        int cursor = 0;

        while (true)
        {
            _enterIsOk = false;
            DrawPanel(0, Poker.HandSize, swap, cursor,
                      ("슈트소트", 0, 64, SuitSort), ("소트", 64, 64, RankSort),
                      ("언두", 128, 64, Undo), ("결정", 200, 64, Decide));
            int c = Await();
            if (c == Decide) break;

            switch (c)
            {
                case SuitSort: SortWith(hand, swap, card => card); break;
                case RankSort: SortWith(hand, swap, Poker.RankOf); break;
                case Undo: SortWith(hand, swap, card => Array.IndexOf(original, card)); break;
                case Moved: cursor = _cursor; break;

                case >= CardAsk and < CardAsk + Poker.HandSize:
                    cursor = c - CardAsk;
                    bool yes = ConfirmDialog.Ask(this, swap[cursor] ? "HOLD 하시겠습니까?" : "HOLD 해제하시겠습니까?",
                                                 "한다.");
                    CheckClosed();
                    if (yes) swap[cursor] = !swap[cursor];
                    break;

                case >= CardClick and < CardClick + Poker.HandSize:
                    cursor = c - CardClick;
                    swap[cursor] = !swap[cursor];
                    break;
            }
        }

        Hush();
        int thrown = Poker.Discard(hand, swap);
        ShowHand(Poker.HandSize - thrown);
        for (int r = 0; r < thrown; r++)
        {
            AddPile(mine: true);
            Wait(1);
        }
        for (int j = 0; j < thrown; j++)
        {
            int k = Poker.HandSize - thrown + j;
            _poker.DrawInto(hand, k, burn: false);
            _myState[k] = 1;
            DrawSlots();
            ShowHand(k + 1);
            Wait(1);
        }
        ClearSlots();
    }

    /// <summary>카드와 바꿈 표시를 함께 줄 세운다(선택 정렬, <c>0x00484530</c>).</summary>
    private static void SortWith(byte[] cards, bool[] flags, Func<byte, int> key)
    {
        for (int i = 0; i < cards.Length - 1; i++)
        {
            int low = i;
            for (int j = i + 1; j < cards.Length; j++)
                if (key(cards[j]) < key(cards[low])) low = j;
            (cards[i], cards[low]) = (cards[low], cards[i]);
            (flags[i], flags[low]) = (flags[low], flags[i]);
        }
    }

    /// <summary>먼저 거는 쪽(<c>0x00404290</c>). −1 이면 접었다.</summary>
    private int Lead(int n) => _poker.TheyDeal ? TheyLead(n) : YouLead(n);

    /// <summary>받는 쪽(<c>0x00404540</c>). −1 이면 접었다.</summary>
    private int Reply(int n) => _poker.TheyDeal ? YouReply(n) : TheyReply(n);

    /// <summary>선인 상대 — 레이즈하며 카드 한 장을 열거나 접는다. 콜은 없다.</summary>
    private int TheyLead(int n)
    {
        int amount = _poker.TheirBet(n);
        if (amount <= 0)
        {
            Say(Pick(Lines.TheyDrop), 5);
            return -1;
        }

        _poker.TheyLead(n, amount);
        OpenTheirs(n);
        Refresh();
        Say(Money(Pick(Lines.TheyRaise), amount), 3);
        return amount;
    }

    /// <summary>받는 쪽 상대 — 레이즈·콜이면 카드 한 장을 연다.</summary>
    private int TheyReply(int n)
    {
        int amount = _poker.TheirBet(n);
        if (amount < 0)
        {
            Say(Pick(Lines.TheyDrop), 3);
            return -1;
        }

        _poker.TheyAnswer(amount);
        OpenTheirs(n);
        Refresh();
        Say(amount > 0 ? Money(Pick(Lines.TheyRaise), amount) : Pick(Lines.TheyCall), 3);
        return amount;
    }

    /// <summary>
    /// 선인 사람 — 「레이즈 · 드롭」, 걸 돈이 없으면 「드롭」만(<c>0x00483970</c> 모드 1·3).
    /// </summary>
    /// <remarks>
    /// 한도는 <c>min(2 x 내기돈, 낼 수 있는 돈)</c>(<c>0x00482900</c>). 금액 창에서 물리면 단추로 돌아간다.
    /// </remarks>
    private int YouLead(int n)
    {
        while (true)
        {
            int need = n > 0 ? _poker.YouOwe : _poker.Stake;
            int c = _poker.Purse > need
                ? Buttons(("레이즈", 4, Raise), ("드롭", 92, Fold))
                : Buttons(("드롭", 4, Fold));
            if (c == Fold) return -1;
            if (c != Raise) continue;

            int most = Math.Min(2 * _poker.Stake, n > 0 ? _poker.Purse - _poker.YouOwe : _poker.Purse);
            if (AskAmount(most) is not { } amount) continue;

            _poker.YouLead(n, amount);
            Refresh();
            PickOpen(n);
            return amount;
        }
    }

    /// <summary>받는 쪽 사람 — 「콜 · 레이즈 · 드롭」, 모자라면 「드롭」만(모드 0·3).</summary>
    private int YouReply(int n)
    {
        while (true)
        {
            int owe = _poker.YouOwe;
            int c = owe < _poker.Purse
                ? Buttons(("콜", 4, Call), ("레이즈", 92, Raise), ("드롭", 180, Fold))
                : Buttons(("드롭", 4, Fold));
            if (c == Fold) return -1;

            if (c == Call)
            {
                _poker.YouAnswer(0);
                Refresh();
                PickOpen(n);
                return 0;
            }
            if (c != Raise) continue;

            if (AskAmount(Math.Min(2 * _poker.Stake, _poker.Purse - owe)) is not { } amount) continue;
            _poker.YouAnswer(amount);
            Refresh();
            PickOpen(n);
            return amount;
        }
    }

    /// <summary>레이즈 금액 창(<c>0x00482900(1, 한도, 1)</c> → <c>0x00482950</c>). 물리면 null.</summary>
    private int? AskAmount(int most)
    {
        if (most < 1) return null;
        int? amount = NumberPadDialog.Ask(this, 1, 1, most);
        CheckClosed();
        return amount is > 0 ? Math.Min(amount.Value, most) : null;
    }

    /// <summary>
    /// 선인 사람의 까기 — 「오픈 · 드롭」(모드 2). 손패 창에는 아직 안 연 다섯째 장이 뜬다.
    /// </summary>
    /// <returns>오픈(승부)이면 true.</returns>
    private bool YouOpen()
    {
        while (true)
        {
            DrawPanel(Poker.HandSize - 1, 1, null, -1, ("오픈", 4, BetButtonW, Open), ("드롭", 92, BetButtonW, Fold));
            int c = Await();
            if (c == Open) return true;
            if (c == Fold) return false;
        }
    }

    /// <summary>
    /// 여는 카드 고르기(<c>0x00403380</c> → <c>0x00484240</c>) — 아직 안 연 5-n 장에서 하나를 골라
    /// 칸 n 으로 맞바꾸고 앞면으로 둔다.
    /// </summary>
    /// <remarks>
    /// 왼쪽 누름은 커서만 옮기고 ＯＫ 로 정한다. 오른쪽 누름은 「OPEN 하겠습니까?」(제목 「OPEN」)를
    /// 묻고 예면 곧장 정한다. 메시지 1/2 가 왼쪽/오른쪽 누름이라는 것은 볼트도 짐작이다.
    /// </remarks>
    private void PickOpen(int n)
    {
        int count = Poker.HandSize - n;
        int cursor = 0;

        while (true)
        {
            _enterIsOk = true;
            DrawPanel(n, count, null, cursor, ("ＯＫ", 200, 64, Ok));
            int c = Await();
            if (c == Ok) break;
            if (c == Moved) { cursor = _cursor; continue; }

            if (c is >= CardAsk and < CardAsk + Poker.HandSize)
            {
                cursor = c - CardAsk;
                bool yes = ConfirmDialog.Ask(this, "OPEN 하겠습니까?", "OPEN");
                CheckClosed();
                if (yes) break;
                continue;
            }
            if (c is >= CardClick and < CardClick + Poker.HandSize) cursor = c - CardClick;
        }
        _enterIsOk = false;

        var hand = _poker.Mine;
        int at = n + cursor;
        (hand[n], hand[at]) = (hand[at], hand[n]);
        _myState[n] = 2;
        DrawSlots();
        ShowHand(Poker.HandSize);
    }

    private void OpenTheirs(int n)
    {
        _theirState[n] = 2;
        DrawSlots();
    }

    /// <summary>
    /// 정산(<c>0x00405E97</c>~<c>0x004060EF</c>) 뒤 결과창을 띄운다.
    /// </summary>
    /// <remarks>
    /// <code>
    ///   드롭    열 칸을 한꺼번에 연다
    ///           상대가 접었으면 잘 접었는지 한마디(3초)
    ///           진 사람이 판 중에 모자랐으면 「뭐야, 돈이 없나?」 뒤 0 으로 (3초)
    ///           결과 0 이김(드롭) · 1 짐(드롭)
    ///   까기    「자, 승부다.」(3초) → 양쪽 칸 4 를 연다 → 승패 한마디(1.5초)
    ///           결과 2 이김 · 3 짐
    ///   딴 돈은 백만 닢에서 자르고 넘치면 「경 고」(0x004059B0)
    /// </code>
    /// </remarks>
    private void Settle(int drop)
    {
        int result;
        bool capped;

        if (drop != 0)
        {
            Array.Fill(_myState, (byte)2);
            Array.Fill(_theirState, (byte)2);
            DrawSlots();

            if (_poker.TheyFolded(drop))
                Say(Poker.Beats(_poker.Mine, _poker.Theirs) ? Pick(Lines.FoldedWisely) : Pick(Lines.FoldedBadly), 6);

            // drop 1 은 선이 접었으니 받는 쪽이, drop 2 는 선이 이긴다.
            bool youWon = drop == 1 ? _poker.TheyDeal : !_poker.TheyDeal;
            if (!youWon && _poker.Purse < 0)
            {
                Say(Lines.Seized, 6);
                _poker.SeizeDebt();
            }
            capped = _poker.Award(youWon);
            result = youWon ? 0 : 1;
        }
        else
        {
            Say(Pick(Lines.Showdown), 6);
            _theirState[Poker.HandSize - 1] = 2;
            _myState[Poker.HandSize - 1] = 2;
            DrawSlots();

            bool youWon = Poker.Beats(_poker.Mine, _poker.Theirs);
            Say(Pick(youWon ? Lines.YouWon : Lines.TheyWon), 3);
            capped = _poker.Award(youWon);
            result = youWon ? 2 : 3;
        }

        Refresh();
        if (capped)
        {
            NoticeDialog.Show(this, "금화는 더 이상 늘릴 수 없습니다", "경 고");
            CheckClosed();
        }
        ShowResult(result);
    }

    /// <summary>
    /// 판과 판 사이(<c>0x00406430</c>). 이어 가면 true.
    /// </summary>
    /// <remarks>
    /// 사람에게 묻는 갈래와 「빈털터리에게는…」은 술집 주인 얼굴을 건 대사 창이고, 상대가 가르는
    /// 갈래의 말(줄바꿈이 든 표)은 카드판 말풍선에 1.5초 뜬다.
    /// </remarks>
    private bool NextHand()
    {
        switch (_poker.NextHand(_luck))
        {
            case Poker.Parting.YouBroke:
                Tell(Lines.YouBroke);
                return false;

            case Poker.Parting.AskYou:
                bool yes = ConfirmDialog.Ask(this, Pick(Lines.Again), null, _face);
                CheckClosed();
                Tell(Pick(yes ? Lines.AgainYes : Lines.AgainNo));
                return yes;

            case Poker.Parting.TheyBroke:
                Say(Pick(Lines.TheyBroke), 3);
                return false;

            case Poker.Parting.TheyShort:
                Say(Pick(Lines.TheyShort), 3);
                return false;

            case Poker.Parting.TheyLeave:
                Say(Pick(Lines.TheyLeave), 3);
                return false;

            default:
                Say(Pick(Lines.GoOn), 3);
                return true;
        }
    }

    /// <summary>
    /// 결과창(<c>0x00485CB0</c>) — 264x240 가운데. 상대 패 (16+48i,16)·족보 y=88,
    /// 사람 패 (16+48i,112)·족보 y=184, 승패 (16,208), 확인 (208,208).
    /// </summary>
    private void ShowResult(int result)
    {
        _result.Children.Clear();
        Frame(_result, ResultW, ResultH);
        Row(_poker.Theirs, 16, 88);
        Row(_poker.Mine, 112, 184);
        Letter(_result, (result % 2 == 0 ? "이김" : "짐") + (result < 2 ? "(드롭에 의한다)" : ""), 16, 208);
        Place(_result, Push("확인", 48, Ok), 208, 208);
        _result.Visibility = Visibility.Visible;

        _enterIsOk = true;
        while (Await() != Ok) { }
        _enterIsOk = false;
        _result.Visibility = Visibility.Collapsed;

        void Row(byte[] cards, int y, int nameY)
        {
            for (int i = 0; i < Poker.HandSize; i++)
                Place(_result, Sprite(_art.Cards[Poker.PictureOf(cards[i])], TrampArt.CardWidth, TrampArt.CardHeight),
                      16 + Pitch * i, y);

            // 이름을 바이트 길이로 가운데에 둔다: x = 23 + 4*((27-len) & ~1).
            string name = Poker.HandNames[Poker.Evaluate(cards)];
            int bytes = name.Sum(ch => ch > 0x7F ? 2 : 1);
            Letter(_result, name, 23 + 4 * ((27 - bytes) & ~1), nameY);
        }
    }

    // ── 기다림과 입력 ────────────────────────────────────────────────────────

    /// <summary>n x 0.5초 기다린다(<c>0x00482D50</c>). 메시지는 돌린다.</summary>
    private void Wait(int halves)
    {
        if (halves <= 0) return;

        var frame = new DispatcherFrame();
        _sleeping = frame;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(halves * HalfSecond), DispatcherPriority.Normal,
                                        (_, _) => frame.Continue = false, Dispatcher);
        try { Dispatcher.PushFrame(frame); }
        finally
        {
            timer.Stop();
            _sleeping = null;
        }
        CheckClosed();
    }

    /// <summary>단추·카드 누름 하나를 기다려 그 답을 낸다.</summary>
    private int Await()
    {
        _choice = -1;
        var frame = new DispatcherFrame();
        _asking = frame;
        try { Dispatcher.PushFrame(frame); }
        finally { _asking = null; }
        CheckClosed();
        return _choice;
    }

    private void Answer(int choice)
    {
        if (_asking == null) return;
        _choice = choice;
        _asking.Continue = false;
    }

    private void CheckClosed()
    {
        if (_closed) throw new TableClosed();
    }

    /// <summary>단추 줄을 세우고(80x24, y=104) 누름을 기다린다.</summary>
    private int Buttons(params (string Text, double X, int Choice)[] buttons)
    {
        var row = new (string, double, double, int)[buttons.Length];
        for (int i = 0; i < buttons.Length; i++)
            row[i] = (buttons[i].Text, buttons[i].X, BetButtonW, buttons[i].Choice);
        DrawPanel(0, Poker.HandSize, null, -1, row);
        return Await();
    }

    /// <summary>방향키는 카드 커서를 옮기고, Enter·Space 는 커서 카드를 누르거나 확인한다.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_asking == null) return;

        if (e.Key is Key.Enter or Key.Space)
        {
            if (_enterIsOk) Answer(Ok);
            else if (_cursor >= 0) Answer(CardClick + _cursor);
            e.Handled = true;
            return;
        }

        if (_cursor < 0 || _stripCount == 0) return;
        if (e.Key == Key.Left) _cursor = (_cursor + _stripCount - 1) % _stripCount;
        else if (e.Key == Key.Right) _cursor = (_cursor + 1) % _stripCount;
        else return;

        e.Handled = true;
        Answer(Moved);
    }

    /// <summary>
    /// 카드 띠를 눌렀다(<c>0x004851A0</c>) — x∈[8,240), y∈[8,72), (x-8)%48 &lt; 40 이면 i=(x-8)/48.
    /// </summary>
    private void OnStrip(Point at, bool right)
    {
        if (_cursor < 0) return;
        int x = (int)at.X, y = (int)at.Y;
        if (x < 8 || x >= 240 || y < 8 || y >= 72 || (x - 8) % Pitch >= TrampArt.CardWidth) return;

        int i = (x - 8) / Pitch;
        if (i < _stripCount) Answer((right ? CardAsk : CardClick) + i);
    }

    // ── 그리기 ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 손패 창 자리(280,288)를 다시 짓는다 — 카드 띠 248x89 @(8,8), 선 카드 (8+48i, 8),
    /// 커서 (7+48i,7) 48x72, 「Hold」 (12+48i, 73), 단추는 y=104 줄. 단추가 있으면 높이 128 이다.
    /// </summary>
    private void DrawPanel(int from, int count, bool[]? swap, int cursor,
                           params (string Text, double X, double Width, int Choice)[] buttons)
    {
        int height = buttons.Length > 0 ? PanelTall : PanelShort;
        _panel.Children.Clear();
        _panel.Height = height;
        Frame(_panel, PanelW, height);

        _stripCount = count;
        _cursor = cursor;

        var strip = new Border { Width = 248, Height = 89, Background = _stripBack };
        strip.MouseLeftButtonDown += (_, e) => e.Handled = true;
        strip.MouseRightButtonDown += (_, e) => e.Handled = true;
        strip.MouseLeftButtonUp += (_, e) => { e.Handled = true; OnStrip(e.GetPosition(_panel), right: false); };
        strip.MouseRightButtonUp += (_, e) => { e.Handled = true; OnStrip(e.GetPosition(_panel), right: true); };
        Place(_panel, strip, 8, 8);

        for (int i = 0; i < count; i++)
        {
            byte card = _poker.Mine[from + i];
            if (card == Poker.NoCard) continue;
            Place(_panel, Sprite(_art.Cards[Poker.PictureOf(card)], TrampArt.CardWidth, TrampArt.CardHeight),
                  8 + Pitch * i, 8);
            if (swap != null && !swap[i]) Letter(_panel, "Hold", 12 + Pitch * i, 73);
        }

        if (cursor >= 0)
            Place(_panel, new Border
            {
                Width = 48,
                Height = 72,
                BorderBrush = _ink,
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
            }, 7 + Pitch * cursor, 7);

        foreach (var b in buttons) Place(_panel, Push(b.Text, b.Width, b.Choice), b.X, ButtonY);
    }

    /// <summary>단추 없는 손패 창(<c>0x004856E0</c>) — 앞 <paramref name="count"/> 장만.</summary>
    private void ShowHand(int count) => DrawPanel(0, count, null, -1);

    private GameButton Push(string text, double width, int choice) =>
        new(text, () => Answer(choice), BandStyle.Button, width) { Margin = default, Height = ButtonH };

    /// <summary>판 위 열 칸을 상태대로 그린다 — 1 뒷면(파트 1 0x1E00), 2 기울인 앞면(파트 3).</summary>
    private void DrawSlots()
    {
        for (int i = 0; i < Poker.HandSize; i++)
        {
            _mySlot[i].Source = SlotArt(_poker.Mine[i], _myState[i]);
            _theirSlot[i].Source = SlotArt(_poker.Theirs[i], _theirState[i]);
        }
    }

    private BitmapSource? SlotArt(byte card, byte state) => state switch
    {
        1 => _art.Back,
        2 when card != Poker.NoCard => _art.Tilted[Poker.PictureOf(card)],
        _ => null,
    };

    private void ClearSlots()
    {
        Array.Clear(_myState);
        Array.Clear(_theirState);
        DrawSlots();
    }

    /// <summary>새 판 앞에 판을 걷는다 — 칸·버린 더미(<c>0x00401000</c>)·말풍선·결과창.</summary>
    private void ClearTable()
    {
        ClearSlots();
        foreach (var pile in _piles) _scene.Children.Remove(pile);
        _piles.Clear();
        _myPile = _theirPile = 0;
        Hush();
        _result.Visibility = Visibility.Collapsed;
        _hover.Visibility = Visibility.Collapsed;
        ShowHand(0);
    }

    /// <summary>버린 더미에 뒷면 한 장 — 사람 (304+8i, 272+8i) · 상대 (192-8i, 64-8i).</summary>
    private void AddPile(bool mine)
    {
        int i = mine ? _myPile++ : _theirPile++;
        var image = Sprite(_art.Back, TrampArt.TiltSize, TrampArt.TiltSize);
        Place(_scene, image, mine ? 304 + 8 * i : 192 - 8 * i, mine ? 272 + 8 * i : 64 - 8 * i, 10);
        _piles.Add(image);
    }

    /// <summary>글자판·선 금화·돈을 다시 적는다. 소지금은 곧바로 제독 금화에 옮긴다.</summary>
    private void Refresh()
    {
        _purseText.Text = $"{_poker.Purse,7}";
        _stakeText.Text = $"{_poker.Stake,7}";
        _myPotText.Text = $"{_poker.MyPot,5}";
        _theirPotText.Text = $"{_poker.TheirPot,5}";

        // 선 표시 — 상대면 (536,40), 사람이면 (24,296).
        Canvas.SetLeft(_coin, _poker.TheyDeal ? 536 : 24);
        Canvas.SetTop(_coin, _poker.TheyDeal ? 40 : 296);
        SyncGold();
    }

    /// <summary>게임은 <c>+0x5C8</c> 과 전역 금화 <c>0x005B6194</c> 를 늘 같이 쓴다.</summary>
    private void SyncGold() => _player.SetGold(Math.Max(0, _poker.Purse));

    /// <summary>말풍선에 띄우고 n x 0.5초 뒤 걷는다(<c>0x00402F70(글, t)</c>).</summary>
    private void Say(string text, int halves)
    {
        SayOn(text);
        Wait(halves);
        Hush();
    }

    /// <summary>
    /// 말풍선에 띄워 둔다 — 글은 x=284, y = 40 + (4 - 줄바꿈 수) x 8 부터 줄마다 16.
    /// </summary>
    private void SayOn(string text)
    {
        _words.Children.Clear();
        var lines = text.Split('\n');
        double top = 40 + (4 - (lines.Length - 1)) * 8 - BubbleY;
        for (int k = 0; k < lines.Length; k++)
            Letter(_words, lines[k], 284 - BubbleX, top + 16 * k);
        _bubble.Visibility = Visibility.Visible;
    }

    private void Hush() => _bubble.Visibility = Visibility.Collapsed;

    /// <summary>
    /// 말풍선 틀 — 모서리 넷, 위아래 변 x=290..402, 좌우 변 y=48..96, 속 (290,48)-(415,111) 색 10.
    /// </summary>
    private void BuildBubble()
    {
        Place(_bubble, new Border { Width = 126, Height = 64, Background = _paper }, 16, 16);

        void Tile(int k, double x, double y) =>
            Place(_bubble, Sprite(_art.Tiles[k], TrampArt.TileSize, TrampArt.TileSize), x, y);

        for (int x = 16; x <= 128; x += 16)
        {
            Tile(0, x, 0);
            Tile(1, x, 80);
        }
        for (int y = 16; y <= 64; y += 16)
        {
            Tile(2, 0, y);
            Tile(3, 142, y);
        }
        Tile(4, 0, 0);
        Tile(5, 0, 80);
        Tile(6, 142, 0);
        Tile(7, 142, 80);

        _bubble.IsHitTestVisible = false;
        _bubble.Children.Add(_words);
    }

    /// <summary>
    /// 앞면 칸에 커서를 올리면 무늬와 끗수 이름을 띄운다(<c>0x00401590</c>).
    /// </summary>
    /// <remarks>원본은 45° 띠로 칸을 판정하는데, 여기서는 칸 그림에 마우스가 들었는지로 가린다.</remarks>
    private void HookHover(Image slot, Func<byte> card, Func<byte> state)
    {
        slot.IsHitTestVisible = true;
        slot.MouseEnter += (_, _) =>
        {
            byte c = card();
            if (state() != 2 || c == Poker.NoCard) return;
            _hoverMark.Source = _art.Marks[Poker.SuitOf(c)];
            _hoverRank.Text = Poker.RankNames[Poker.RankOf(c)];
            Canvas.SetLeft(_hover, Canvas.GetLeft(slot) + 8);
            Canvas.SetTop(_hover, Math.Max(0, Canvas.GetTop(slot) - 24));
            _hover.Visibility = Visibility.Visible;
        };
        slot.MouseLeave += (_, _) => _hover.Visibility = Visibility.Collapsed;
    }

    /// <summary>색 10 바탕에 검정 겹테를 두른 창 하나를 판에 얹는다.</summary>
    private Canvas Box(Canvas parent, double x, double y, double width, double height, int z)
    {
        var box = new Canvas { Width = width, Height = height };
        Frame(box, width, height);
        Place(parent, box, x, y, z);
        return box;
    }

    private void Frame(Canvas box, double width, double height)
    {
        box.Background = _paper;
        box.Children.Add(new Border
        {
            Width = width,
            Height = height,
            BorderBrush = _ink,
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Child = new Border
            {
                Margin = new Thickness(1),
                BorderBrush = _ink,
                BorderThickness = new Thickness(1),
            },
        });
    }

    /// <summary>게임 글꼴 검정(0x49) 글자 한 줄.</summary>
    private GameUi.GameLabel Letter(Canvas on, string text, double x, double y)
    {
        var label = new GameUi.GameLabel(InkIndex)
        {
            Text = text,
            Bold = false,
            FallbackBrush = _ink,
            IsHitTestVisible = false,
        };
        Place(on, label, x, y);
        return label;
    }

    private static Image Sprite(BitmapSource? source, double width, double height)
    {
        var image = new Image
        {
            Source = source,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(image, GameUi.SpriteScaling);
        return image;
    }

    private static void Place(Canvas on, UIElement element, double x, double y, int z = 0)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        if (z != 0) Panel.SetZIndex(element, z);
        on.Children.Add(element);
    }

    private static BitmapSource? FaceArt(uint[]? bgra)
    {
        if (bgra == null) return null;
        var bmp = BitmapSource.Create(Portraits.Width, Portraits.Height, 96, 96, PixelFormats.Bgra32, null,
                                      bgra, Portraits.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private string Pick(string[] table) => table[_poker.Dice.Next(table.Length)];

    private void Tell(string text)
    {
        ConfirmDialog.Tell(this, text, null, _face);
        CheckClosed();
    }

    private static string Money(string text, int amount) => text.Replace("%d", amount.ToString());
}
