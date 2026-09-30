using System.Windows.Controls;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 본문 칸에 싣는 대본 편집기(발견 이벤트 · 이야기) — 개발도구의 첫 화면이다.
/// 편집기는 <see cref="CdsHelper.Game.UI.Views.DisevEditorDialog"/> 의 화면을 떼어 그대로 쓴다.
/// </summary>
public class DisevEditorContent : ContentControl
{
    public DisevEditorContent() => Content = CdsHelper.Game.UI.Views.DisevEditorDialog.CreateView();
}
