namespace CdsHelper.Game.UI.Views;

/// <summary>
/// 동영상이 도는 동안 <b>겹쳐 보기 쪽지</b>(함대 · 힌트 · 발견물 수 · 배 속도 · 기능·언어)를 걷어 둔다.
/// </summary>
/// <remarks>
/// 쪽지들은 도시 그림 창이나 함대 창에 딸린 제 창이라, 화면을 덮는 동영상 창(<see cref="MoviePlayer"/>)보다
/// 위에 뜰 수 있다 — 보고하며 동영상을 틀면 쪽지가 영상 위에 그대로 얹혀 보였다. 그래서 동영상을 트는 손이
/// <see cref="Hold"/> 로 잡아 두는 동안 쪽지들은 보여야 할 때도 감춰 두고, 놓으면 그때 보여야 할 것만 도로 낸다.
/// 잡아 둔 동안 글이 바뀌어도(<see cref="FleetLabelWindow.Set"/>) 보일지는 놓을 때 정해진다.
/// </remarks>
internal static class OverlayNotes
{
    private static int _held;

    /// <summary>지금 걷어 두는 중인지.</summary>
    public static bool Held => _held > 0;

    /// <summary>잡거나 놓을 때 알린다 — 쪽지들이 제 보임을 다시 맞춘다.</summary>
    public static event Action? Changed;

    /// <summary>쪽지들을 걷는다. 돌려받은 것을 <c>Dispose</c> 하면 도로 낸다. 겹쳐 잡아도 된다.</summary>
    public static IDisposable Hold()
    {
        _held++;
        Changed?.Invoke();
        return new Release();
    }

    private sealed class Release : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done) return;
            _done = true;
            _held--;
            Changed?.Invoke();
        }
    }
}
