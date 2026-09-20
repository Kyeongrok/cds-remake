namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 한 마디 알리고 확인만 받는 창 — <see cref="ConfirmDialog"/> 를 부르는 <b>이름 하나</b>다.
/// </summary>
/// <remarks>
/// 예전에는 제 창을 따로 지었다. 그래서 같은 말이 이 창과 물음창에서 <b>다른 폭 · 다른
/// 글꼴</b>로 떴다 — 이쪽은 WPF 글자에 여백을 손으로 박았고, 물음창은 게임 셈
/// (칸수 = max(30, 가장 긴 줄) · 너비 = 칸수 x 8 + 32)으로 게임 글꼴을 찍는다.
///
/// 게임은 알림과 물음이 <b>한 함수</b>다(<c>0x00469060</c> — 첫 인자가 0 이면 확인,
/// 2 면 YES/NO). 우리도 한 벌만 두고, 이 이름은 부르는 자리 여든 남짓을 그대로 두려고
/// 남겨 둔 껍데기다.
/// </remarks>
public static class NoticeDialog
{
    /// <param name="owner">알림을 얹을 창.</param>
    /// <param name="text">할 말.</param>
    /// <param name="title">제목 띠에 얹을 글. 비우면 띠를 안 단다.</param>
    public static void Show(System.Windows.Window owner, string text, string? title = null) =>
        ConfirmDialog.Tell(owner, text, title);

    /// <summary>
    /// 미니게임의 <b>「게임 설명」</b> 창. 글이 왼쪽 테에서 한 뼘 떨어져 시작한다.
    /// </summary>
    /// <remarks>
    /// 여느 알림은 테 바로 옆(<c>7</c>점)에서 글이 시작하는데 설명 글은 그렇게 두면
    /// 왼쪽이 답답하다. 게임 글꼴 한 자가 <see cref="GameUi.CellWidth"/> 이므로
    /// <see cref="ExplainCells"/> 자만큼 들인다.
    /// </remarks>
    public static void Explain(System.Windows.Window owner, string text,
                               string title = "게임 설명") =>
        ConfirmDialog.Tell(owner, Wrap(text, ExplainWidth), title, null,
                           ExplainCells * GameUi.CellWidth);

    /// <summary>설명 글을 들이는 글자 수.</summary>
    private const int ExplainCells = 3;

    /// <summary>
    /// 설명 글 한 줄이 담는 <b>반각 글자 수</b>. 원본 창이 이 자리에서 글을 접는다.
    /// </summary>
    /// <remarks>
    /// 게임 화면이 640점이고 설명 창이 그 안에 드는지라 한 줄이 예순 자 남짓이다.
    /// <b>접지 않으면 창이 가로로 한없이 길어진다</b> — 미궁 64의 설명은 한 줄이 백 자를
    /// 넘어, 우리 창이 화면을 꽉 채웠다.
    /// </remarks>
    private const int ExplainWidth = 62;

    /// <summary>
    /// 글을 반각 <paramref name="cells"/> 자에서 접는다. 한글·기호는 두 칸으로 센다 —
    /// 원본 글꼴이 반각·전각 둘뿐이라 그것으로 족하다. <b>낱말을 가리지 않고 접는다</b>(원본도
    /// 「이동합 / 니다.」처럼 글자 자리에서 끊는다).
    /// </summary>
    private static string Wrap(string text, int cells)
    {
        var made = new System.Text.StringBuilder(text.Length + 16);

        foreach (string line in text.Split(System.Environment.NewLine))
        {
            int width = 0;
            foreach (char c in line)
            {
                int w = c > 0x7F ? 2 : 1;
                if (width + w > cells)
                {
                    made.Append(System.Environment.NewLine);
                    width = 0;
                }
                made.Append(c);
                width += w;
            }
            made.Append(System.Environment.NewLine);
        }

        return made.ToString().TrimEnd();
    }
}
