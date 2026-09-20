using System.IO;
using System.Reflection;

namespace CdsHelper.Support.Local.Helpers;

/// <summary>
/// 초상화 벌(<c>MALE.CDS</c> · <c>FEMALE.CDS</c>)이 놓인 자리를 잡아 준다.
/// </summary>
/// <remarks>
/// <b>원본은 이 DLL 안에 박혀 있다</b>(<c>EmbeddedResource</c>). 예전에는 저장소의
/// <c>asset</c> 폴더에 두고 빌드가 앱마다 <c>bin</c> 으로 실어 날랐는데, 얼굴을
/// 새로 넣는 기능이 그 <c>bin</c> 사본을 고치는 바람에 탈이 셋이었다.
/// <list type="number">
///   <item>다시 구우면 넣은 얼굴이 원본으로 덮여 날아간다.</item>
///   <item>앱마다 사본이 따로라 <c>CostaDelSol</c> 에서 넣은 얼굴을
///         <c>CdsHelper</c> 가 못 본다.</item>
///   <item>저장소에 이진 파일이 남아 얼굴을 넣을 때마다 <c>git</c> 이 흔들린다.</item>
/// </list>
/// 그래서 <b>고칠 수 있는 한 벌</b>은 <see cref="Directory"/> 아래 하나만 둔다.
/// 없으면 박아 둔 원본을 그리로 꺼내 놓는다(<see cref="PathOf"/>). 원래대로
/// 돌리려면 그 파일을 지우면 된다 — 다음에 열 때 다시 꺼내 놓는다.
/// </remarks>
public static class PortraitStore
{
    /// <summary>고칠 수 있는 초상화 벌이 사는 자리.</summary>
    public static string Directory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CdsHelper", "asset");

    /// <summary>남자·여자 벌의 파일 이름.</summary>
    public static string NameOf(bool female) => female ? "FEMALE.CDS" : "MALE.CDS";

    /// <summary>
    /// 그 벌이 놓인 자리. 아직 없으면 박아 둔 원본을 꺼내 놓고 그 자리를 낸다.
    /// 꺼내지 못하면 빈 글이다.
    /// </summary>
    public static string PathOf(bool female)
    {
        string path = Path.Combine(Directory, NameOf(female));
        if (File.Exists(path)) return path;

        if (Unpack(female, path)) return path;
        return CdsAssetPath.Resolve("", NameOf(female)) is var fallback && File.Exists(fallback)
            ? fallback : "";
    }

    /// <summary>왜 못 꺼냈는지. 잘 됐으면 빈 글.</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>
    /// 그 벌을 박아 둔 원본으로 되돌린다. 넣은 얼굴이 죄다 사라진다.
    /// </summary>
    public static bool Reset(bool female)
    {
        string path = Path.Combine(Directory, NameOf(female));
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception e) { LastError = e.Message; return false; }

        return Unpack(female, path);
    }

    /// <summary>박아 둔 원본을 그 자리에 꺼내 놓는다.</summary>
    private static bool Unpack(bool female, string path)
    {
        LastError = "";
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string name = asm.GetManifestResourceNames()
                             .First(n => n.EndsWith(NameOf(female), StringComparison.OrdinalIgnoreCase));

            System.IO.Directory.CreateDirectory(Directory);

            using var from = asm.GetManifestResourceStream(name)!;
            using var to = File.Create(path);
            from.CopyTo(to);
            return true;
        }
        catch (Exception e)
        {
            LastError = $"{NameOf(female)} 를 꺼내지 못했습니다 — {e.Message}";
            return false;
        }
    }
}
