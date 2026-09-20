using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CdsHelper.Main.Local.ViewModels;

/// <summary>
/// 발견물 그림 뷰어 — <c>DSTILL.CDS</c> 의 그림 여든넉 장을 늘어놓고 골라 본다.
/// </summary>
/// <remarks>
/// 그림은 놀이 쪽 읽개를 그대로 쓴다(<see cref="DiscoveryStills"/>). 어느 그림이 어느
/// 발견물인지는 발견물 표의 <c>+0x0C</c> 가 들고 있어서(<see cref="DiscoveryTable"/>),
/// 표가 열리면 이름을 붙이고 안 열리면 번호만 낸다.
///
/// 게임 폴더는 마지막으로 연 세이브 파일이 있는 자리로 잡는다 — 뷰어의 다른 화면들과
/// 같은 길이다(<see cref="AppSettings.LastSaveFilePath"/>).
/// </remarks>
public partial class DiscoveryStillContentViewModel : ObservableObject
{
    /// <summary>목록 한 줄.</summary>
    /// <param name="Number">DSTILL.CDS 안의 그림 번호.</param>
    /// <param name="Title">줄에 적을 글 — 이름을 알면 이름, 모르면 번호다.</param>
    /// <param name="Image">그림. 못 풀었으면 null 이라 줄만 남는다.</param>
    public sealed record Still(int Number, string Title, BitmapSource? Image)
    {
        /// <summary>그림 크기 한 줄 — 선 것과 누운 것이 섞여 있다.</summary>
        public string Size => Image == null ? "" : $"{Image.PixelWidth} x {Image.PixelHeight}";
    }

    /// <summary>읽어 온 그림들.</summary>
    public ObservableCollection<Still> Stills { get; } = [];

    [ObservableProperty]
    private Still? _selected;

    /// <summary>왜 못 읽었는지. 잘 읽었으면 빈 글이다.</summary>
    [ObservableProperty]
    private string _note = "";

    public DiscoveryStillContentViewModel() => Reload();

    /// <summary>게임 폴더에서 그림을 다시 읽는다.</summary>
    public void Reload()
    {
        Stills.Clear();
        Selected = null;

        string? save = AppSettings.LastSaveFilePath;
        string dir = string.IsNullOrEmpty(save) ? "" : Path.GetDirectoryName(save) ?? "";
        if (dir.Length == 0)
        {
            Note = "게임 폴더를 아직 모릅니다 — 세이브 파일을 한 번 열어 주세요.";
            return;
        }

        var art = DiscoveryStills.Open(dir);
        if (art == null)
        {
            Note = DiscoveryStills.LastError;
            return;
        }

        // 그림 번호 -> 발견물 이름. 표를 못 열어도 그림은 다 보인다.
        var names = new Dictionary<int, string>();
        if (DiscoveryTable.Open(dir) is { } table)
            foreach (var row in table.Discoveries)
                if (row.Picture >= 0 && !names.ContainsKey(row.Picture))
                    names[row.Picture] = row.Name;

        for (int i = 0; i < art.Count; i++)
        {
            var px = art.TryGetBgra(i, out int w, out int h);
            BitmapSource? bmp = null;
            if (px != null && w > 0 && h > 0)
            {
                bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                bmp.Freeze();
            }
            Stills.Add(new Still(i, names.TryGetValue(i, out var name) ? $"{i,3}  {name}"
                                                                      : $"{i,3}  (이름 모름)", bmp));
        }

        Note = $"{dir} · 그림 {Stills.Count}장";
        Selected = Stills.FirstOrDefault();
    }
}
