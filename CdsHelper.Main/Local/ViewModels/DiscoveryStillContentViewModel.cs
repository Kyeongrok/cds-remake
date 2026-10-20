using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CdsHelper.Main.Local.ViewModels;

/// <summary>
/// 발견물 그림 뷰어 — <c>asset/dstill</c> 의 그림(<c>D{번호}.png</c>)을 늘어놓고 골라 본다.
/// </summary>
/// <remarks>
/// 그림은 게임 쪽 읽개를 그대로 쓴다(<see cref="DiscoveryStillFiles"/>) — 게임 폴더의 DSTILL.CDS 는 안 읽는다. 어느 그림이 어느
/// 발견물인지는 발견물 표의 <c>+0x0C</c> 가 들고 있어서(<see cref="DiscoveryTable"/>),
/// 표가 열리면 이름을 붙이고 안 열리면 번호만 낸다.
///
/// 게임 폴더는 마지막으로 연 세이브 파일이 있는 자리로 잡는다 — 개발도구의 다른 화면들과
/// 같은 길이다(<see cref="AppSettings.LastSaveFilePath"/>).
/// </remarks>
public partial class DiscoveryStillContentViewModel : ObservableObject
{
    /// <summary>목록 한 줄.</summary>
    /// <param name="Number">그림 번호(<c>D{번호}.png</c>).</param>
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

    /// <summary>발견물 표가 이미 쓰는 그림 번호 — 새 그림은 이 번호를 피해 받는다.</summary>
    private readonly HashSet<int> _taken = [];

    public DiscoveryStillContentViewModel() => Reload();

    /// <summary>
    /// 그림 파일을 골라 <b>발견물 그림처럼 바꿔</b>(<see cref="DiscoveryStillFilter"/>) 다음 빈 번호로 넣는다.
    /// </summary>
    /// <remarks>
    /// 여러 장을 한꺼번에 골라도 된다. 넣은 번호를 발견물 표의 그림 칸에 적으면 그 발견물의 그림이 된다.
    /// </remarks>
    [RelayCommand]
    private void AddFiltered()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "발견물 그림으로 바꿀 그림",
            Filter = DiscoveryStillFiles.OpenFilter,
            Multiselect = true,
        };
        if (dialog.ShowDialog() != true) return;

        int last = -1;
        var failed = new List<string>();
        foreach (string path in dialog.FileNames)
        {
            try
            {
                if (DiscoveryStillFilter.Apply(path, out int w, out int h) is not { } px) { failed.Add(Path.GetFileName(path)); continue; }
                last = DiscoveryStillFiles.NextFreeNumber(_taken);
                DiscoveryStillFiles.Upload(px, w, h, last);
            }
            catch (Exception) { failed.Add(Path.GetFileName(path)); }
        }

        Reload();
        if (last >= 0) Selected = Stills.FirstOrDefault(s => s.Number == last) ?? Selected;
        if (failed.Count > 0) Note = $"못 읽은 그림: {string.Join(", ", failed)}";
        else if (last >= 0) Note = $"D{last}.png 로 넣었습니다 — {DiscoveryStillFiles.UploadDirectory()}";
    }

    /// <summary>
    /// 세기와 자르기를 고르며 한 장을 다듬어 넣는다(<see cref="CdsHelper.Game.UI.Views.DiscoveryStillTuneDialog"/>).
    /// 번호는 고른 그림이 있으면 그 번호(바꾸기), 없으면 다음 빈 번호로 적어 둔다.
    /// </summary>
    [RelayCommand]
    private void Tune()
    {
        int suggested = Selected?.Number ?? DiscoveryStillFiles.NextFreeNumber(_taken);
        int saved = CdsHelper.Game.UI.Views.DiscoveryStillTuneDialog.Show(System.Windows.Application.Current?.MainWindow, suggested);
        if (saved < 0) return;
        Reload();
        Selected = Stills.FirstOrDefault(s => s.Number == saved) ?? Selected;
        Note = $"D{saved}.png 로 넣었습니다 — {DiscoveryStillFiles.UploadDirectory()}";
    }

    /// <summary>그림 폴더에서 그림을 다시 읽는다.</summary>
    public void Reload()
    {
        Stills.Clear();
        Selected = null;

        // 발견물 표를 열 폴더 — 세이브를 한 번도 안 열었으면 실행 폴더를 대신 준다.
        string? save = AppSettings.LastSaveFilePath;
        string dir = !string.IsNullOrEmpty(save) && Directory.Exists(Path.GetDirectoryName(save))
            ? Path.GetDirectoryName(save)!
            : AppDomain.CurrentDomain.BaseDirectory;

        var numbers = DiscoveryStillFiles.UploadedNumbers();
        if (numbers.Count == 0)
        {
            Note = $"{DiscoveryStillFiles.AssetDirectory} 에 그림(D{{번호}}.png)이 없습니다";
            return;
        }

        // 그림 번호 -> 발견물 이름. 표를 못 열어도 그림은 다 보인다.
        var names = new Dictionary<int, string>();
        _taken.Clear();
        if (DiscoveryTable.Open(dir) is { } table)
            foreach (var row in table.Discoveries)
            {
                if (row.Picture < 0) continue;
                _taken.Add(row.Picture);
                if (!names.ContainsKey(row.Picture)) names[row.Picture] = row.Name;
            }

        foreach (int i in numbers)
        {
            var px = DiscoveryStillFiles.TryGetBgra(i, out int w, out int h);
            BitmapSource? bmp = null;
            if (px != null && w > 0 && h > 0)
            {
                bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, px, w * 4);
                bmp.Freeze();
            }
            Stills.Add(new Still(i, names.TryGetValue(i, out var name) ? $"{i,3}  {name}"
                                                                      : $"{i,3}  (이름 모름)", bmp));
        }

        Note = $"{DiscoveryStillFiles.AssetDirectory} · 그림 {Stills.Count}장";
        Selected = Stills.FirstOrDefault();
    }
}
