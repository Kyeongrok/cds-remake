using System.Net.Http;

namespace CdsHelper.Game.Local.Helpers;

/// <summary>
/// 통계 받는 곳(<c>tools/stats-worker</c>)에 못 닿았을 때 사용자에게 보일 까닭 — 인터넷이 끊긴 것과 서버가 막힌 것을 가른다.
/// 원본에 없는 것이다.
/// </summary>
/// <remarks>
/// 받는 곳은 Cloudflare D1 무료 한도(하루 읽기 오백만 줄)가 차면 그날 UTC 자정까지 503(<c>{"error":"quota"}</c>)을 돌려준다.
/// 예전에는 무엇이 틀리든 「인터넷 연결을 확인해 주세요」라고만 해서, 인터넷이 멀쩡한 분을 헷갈리게 했다.
/// </remarks>
public static class ServerReply
{
    /// <summary>서버가 답은 했는데 실패한 까닭.</summary>
    public static string Why(System.Net.HttpStatusCode code) => (int)code == 503
        ? Lang.T("서버의 오늘 사용량이 다 찼습니다 — 한국 시각 오전 9시가 지나면 다시 됩니다.")
        : string.Format(Lang.T("서버 오류입니다(응답 {0}) — 잠시 뒤 다시 해 주세요."), (int)code);

    /// <summary>서버에 닿지도 못했을 때(또는 답을 못 읽었을 때)의 까닭.</summary>
    public static string Why(Exception ex) => ex is HttpRequestException or TaskCanceledException
        ? Lang.T("서버에 닿지 못했습니다 — 인터넷 연결을 확인해 주세요.")
        : Lang.T("서버의 답을 읽지 못했습니다 — 잠시 뒤 다시 해 주세요.");
}
