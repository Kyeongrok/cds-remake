using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using CdsHelper.Support.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// WAVES.CDS 에 든 게임 효과음을 늘어놓고 들어 보는 창.
/// </summary>
/// <remarks>
/// 파트 하나가 RIFF WAVE 파일 통째라 <see cref="SoundPlayer"/> 에 메모리 스트림으로
/// 그대로 넘길 수 있다. 게임 폴더는 마지막으로 연 세이브 파일 자리에서 찾는다.
/// </remarks>
public sealed class WaveBankDialog : Window
{
    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly SoundPlayer _player = new();
    private WaveBank? _bank;

    public WaveBankDialog()
    {
        Title = "효과음 (WAVES.CDS)";
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            // 비고 한 칸만 고칠 수 있게 하고, 나머지 칸은 각각 읽기 전용으로 잠근다.
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Extended,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            AlternatingRowBackground = System.Windows.Media.Brushes.WhiteSmoke,
        };
        AddColumns();
        // 줄을 두 번 찍으면 바로 들려준다 — 비고 칸을 고치는 중이면 건드리지 않는다.
        _grid.MouseDoubleClick += (_, e) =>
        {
            if (!IsInNoteCell(e.OriginalSource as DependencyObject)) PlaySelected();
        };

        // 비고를 고치고 칸을 떠나면 바로 쓴다 — 창을 닫아도 남게.
        _grid.CellEditEnding += (_, e) =>
        {
            if (e.EditAction != DataGridEditAction.Commit) return;
            Dispatcher.BeginInvoke(NoteSaved);
        };

        Content = BuildContent();
        Loaded += (_, _) => Load();
        Closed += (_, _) => { _player.Stop(); _player.Dispose(); };
    }

    private void AddColumns()
    {
        void Col(string header, string path, double width) => _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = new DataGridLength(width),
            IsReadOnly = true,
        });

        Col("파트", nameof(WaveInfo.Part), 50);
        Col("사운드 ID", nameof(WaveInfo.SoundId), 70);
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "길이(초)",
            Binding = new Binding(nameof(WaveInfo.Seconds)) { StringFormat = "F2" },
            Width = new DataGridLength(70),
            IsReadOnly = true,
        });
        Col("형식", nameof(WaveInfo.FormatText), 160);
        Col("data 크기", nameof(WaveInfo.DataBytes), 80);
        Col("푼 크기", nameof(WaveInfo.RawSize), 80);
        Col("겹침", nameof(WaveInfo.DuplicateText), 110);
        Col("문제", nameof(WaveInfo.Error), 90);

        // 비고 — 여기만 고칠 수 있다. 칸을 떠나는 즉시 파일에 쓴다.
        _grid.Columns.Add(new DataGridTextColumn
        {
            Header = "비고 (편집 가능)",
            Binding = new Binding(nameof(WaveInfo.Note))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.LostFocus,
            },
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
        });
    }

    private UIElement BuildContent()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(MakeButton("▶ 재생", PlaySelected));
        bar.Children.Add(MakeButton("■ 멈춤", () => _player.Stop()));
        bar.Children.Add(MakeButton("선택 저장…", SaveSelected));
        bar.Children.Add(MakeButton("전체 내보내기…", ExportAll));
        bar.Children.Add(new TextBlock
        {
            Text = "줄을 두 번 찍어도 들립니다 · 비고 칸은 두 번 찍어 고칩니다",
            Foreground = System.Windows.Media.Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bar, 0);
        Grid.SetRow(_grid, 1);
        Grid.SetRow(_status, 2);
        grid.Children.Add(bar);
        grid.Children.Add(_grid);
        grid.Children.Add(_status);
        return grid;
    }

    private static Button MakeButton(string text, Action run)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => run();
        return b;
    }

    private void Load()
    {
        var dir = Path.GetDirectoryName(AppSettings.LastSaveFilePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
        {
            _status.Text = "게임 폴더를 모릅니다 — 먼저 세이브 파일을 한 번 열어 주세요.";
            return;
        }

        _bank = WaveBank.LoadFromDirectory(dir);
        if (_bank == null)
        {
            _status.Text = $"{Path.Combine(dir, WaveBank.FileName)} 를 읽지 못했습니다 — {WaveBank.LastError}";
            return;
        }

        _grid.ItemsSource = _bank.Items;
        int dup = _bank.Items.Count(i => i.DuplicateOf >= 0);
        _status.Text = $"{Path.Combine(dir, WaveBank.FileName)} · 효과음 {_bank.Count}개" +
                       (dup > 0 ? $" (겹치는 소리 {dup}개)" : "") +
                       $" · 게임 사운드 ID {WaveBank.FirstSoundId}~{WaveBank.FirstSoundId + _bank.Count - 1}" +
                       $" (그 앞 ID 0~{WaveBank.CdTrackCount - 1} 은 CD 트랙 " +
                       $"{WaveBank.FirstCdTrack}~{WaveBank.FirstCdTrack + WaveBank.CdTrackCount - 1} 자리다)";
    }

    private WaveInfo? Selected => _grid.SelectedItem as WaveInfo;

    /// <summary>비고 칸(마지막 칸) 안에서 벌어진 일인지.</summary>
    private bool IsInNoteCell(DependencyObject? hit)
    {
        while (hit != null && hit is not DataGridCell)
            hit = System.Windows.Media.VisualTreeHelper.GetParent(hit);
        return hit is DataGridCell cell && cell.Column == _grid.Columns[^1];
    }

    /// <summary>비고를 쓴 뒤 한 줄 알린다 — 못 썼으면 까닭까지.</summary>
    private void NoteSaved() =>
        _status.Text = WaveNotes.LastError.Length == 0
            ? "비고를 적어 두었습니다"
            : $"비고를 쓰지 못했습니다 — {WaveNotes.LastError}";

    private void PlaySelected()
    {
        if (_bank == null || Selected is not { } info) return;

        var wav = _bank.Wav(info.Part);
        if (wav == null) { _status.Text = $"파트 {info.Part} 를 풀지 못했습니다"; return; }

        try
        {
            // SoundPlayer 는 스트림을 물고 있으므로 재생마다 새로 잡아 넘긴다.
            _player.Stop();
            _player.Stream = new MemoryStream(wav);
            _player.Play();
            _status.Text = $"파트 {info.Part} (사운드 ID {info.SoundId}) · {info.Seconds:F2}초 · {info.FormatText}";
        }
        catch (Exception ex)
        {
            _status.Text = $"파트 {info.Part} 를 틀지 못했습니다 — {ex.Message}";
        }
    }

    private void SaveSelected()
    {
        if (_bank == null || Selected is not { } info) return;

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"waves-{info.Part:D2}.wav",
            Filter = "WAV 파일 (*.wav)|*.wav",
        };
        if (dlg.ShowDialog(this) != true) return;

        try
        {
            _status.Text = _bank.Save(info.Part, dlg.FileName)
                ? $"{dlg.FileName} 에 저장했습니다"
                : $"파트 {info.Part} 를 풀지 못했습니다";
        }
        catch (Exception ex)
        {
            _status.Text = $"저장하지 못했습니다 — {ex.Message}";
        }
    }

    private void ExportAll()
    {
        if (_bank == null) return;

        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "효과음을 풀어 놓을 폴더" };
        if (dlg.ShowDialog(this) != true) return;

        int done = 0;
        try
        {
            foreach (var info in _bank.Items)
                if (_bank.Save(info.Part, Path.Combine(dlg.FolderName, $"waves-{info.Part:D2}.wav")))
                    done++;
            _status.Text = $"{dlg.FolderName} 에 {done}개를 풀어 놓았습니다";
        }
        catch (Exception ex)
        {
            _status.Text = $"{done}개까지 쓰고 멈췄습니다 — {ex.Message}";
        }
    }
}
