using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Settings;

namespace CdsHelper.Main.UI.Views;

/// <summary>
/// 게임 동영상(<c>AVI\I??_0000.AVI</c> · <c>AVI\S??_0001.AVI</c>)을 늘어놓고, 틀어 보고,
/// <b>갈아 끼울 동영상을 올리는</b> 창.
/// </summary>
/// <remarks>
/// 올린 것은 같은 이름 줄기로 <c>asset/movie</c> 에 들어간다(<see cref="MovieFiles"/>). 놀이는
/// 그쪽을 먼저 보고, 없으면 게임 폴더 원본을 튼다 — 발견·보고·발표·DISEV·조선소가 다 그렇다.
///
/// 발견물 표에 원래 없던 번호(70 이상)를 적어 둔 줄이 있으면 그 번호도 줄로 낸다 — 원본이 없으니
/// 올린 것만 틀린다.
///
/// 미리 보기 <see cref="MediaElement"/> 가 파일을 물고 있으면 지우거나 덮어쓰지 못한다. 그래서
/// 올리기·지우기 앞에서 늘 닫는다.
/// </remarks>
public sealed class MovieBankDialog : Window
{
    /// <summary>원본 발견물 동영상 수(<c>I00</c>~<c>I69</c>).</summary>
    private const int DiscoveryMovies = MovieFiles.OriginalDiscoveryMovies;

    /// <summary>발견물 표가 쓰는 동영상 번호 — 새 번호를 줄 때 피한다.</summary>
    private readonly HashSet<int> _usedByTable = [];

    private readonly DataGrid _grid;
    private readonly TextBlock _status;
    private readonly MediaElement _preview;
    private readonly TextBlock _previewLabel;
    private string _gameDirectory = "";

    public MovieBankDialog()
    {
        Title = "동영상 (AVI · asset/movie)";
        Width = 1040;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        AllowDrop = true;

        _status = new TextBlock { Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
        _grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            AlternatingRowBackground = Brushes.WhiteSmoke,
        };
        AddColumns();
        _grid.MouseDoubleClick += (_, _) => PlaySelected(original: false);

        _preview = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Close,
            Stretch = Stretch.Uniform,
        };
        _preview.MediaFailed += (_, e) =>
            _status.Text = $"틀지 못했습니다 — {e.ErrorException?.Message}";
        _previewLabel = new TextBlock
        {
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

        // 파일을 끌어다 놓으면 고른 줄로 올린다.
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
                Upload(files[0]);
        };

        Content = BuildContent();
        Loaded += (_, _) => Load();
        Closed += (_, _) => ClosePreview();
    }

    private void AddColumns()
    {
        void Col(string header, string path, DataGridLength width) => _grid.Columns.Add(new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
        });

        Col("갈래", nameof(Row.Kind), new DataGridLength(56));
        Col("이름 줄기", nameof(Row.Stem), new DataGridLength(90));
        Col("쓰는 곳", nameof(Row.Users), new DataGridLength(1, DataGridLengthUnitType.Star));
        Col("원본", nameof(Row.OriginalText), new DataGridLength(70));
        Col("올린 것", nameof(Row.UploadedText), new DataGridLength(150));
        Col("틀 것", nameof(Row.PlaysText), new DataGridLength(60));
    }

    private UIElement BuildContent()
    {
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(MakeButton("▶ 재생", () => PlaySelected(original: false)));
        bar.Children.Add(MakeButton("▶ 원본 재생", () => PlaySelected(original: true)));
        bar.Children.Add(MakeButton("■ 멈춤", ClosePreview));
        bar.Children.Add(MakeButton("새 동영상 추가…", AddNew));
        bar.Children.Add(MakeButton("올리기…", PickAndUpload));
        bar.Children.Add(MakeButton("올린 것 지우기", RemoveSelected));
        bar.Children.Add(MakeButton("폴더 열기", OpenFolder));
        bar.Children.Add(new TextBlock
        {
            Text = "두 번 찍으면 틉니다 · 끌어다 놓으면 고른 줄로(고른 줄이 없으면 새 번호로) 올라갑니다",
            Foreground = Brushes.Gray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        });

        var screen = new Border
        {
            Background = Brushes.Black,
            Width = 320,
            Height = 240,
            Child = _preview,
        };
        var side = new StackPanel { Margin = new Thickness(12, 0, 0, 0), Width = 320 };
        side.Children.Add(screen);
        side.Children.Add(_previewLabel);

        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetRow(bar, 0);
        Grid.SetColumnSpan(bar, 2);
        Grid.SetRow(_grid, 1);
        Grid.SetRow(side, 1);
        Grid.SetColumn(side, 1);
        Grid.SetRow(_status, 2);
        Grid.SetColumnSpan(_status, 2);
        grid.Children.Add(bar);
        grid.Children.Add(_grid);
        grid.Children.Add(side);
        grid.Children.Add(_status);
        return grid;
    }

    private static Button MakeButton(string text, Action run)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0) };
        b.Click += (_, _) => run();
        return b;
    }

    // ── 목록 ────────────────────────────────────────────────────────────────

    private void Load()
    {
        _gameDirectory = Path.GetDirectoryName(AppSettings.LastSaveFilePath) ?? "";
        if (!Directory.Exists(_gameDirectory)) _gameDirectory = "";
        Refill();
    }

    /// <summary>줄을 다시 짓는다. 고른 줄은 지킨다.</summary>
    private void Refill()
    {
        string? keep = Selected?.Stem;

        // 발견물 동영상 번호마다 그것을 쓰는 발견물 이름을 모은다 — 고치거나 더한 줄까지.
        var users = new Dictionary<int, List<string>>();
        var table = DiscoveryTable.Open(_gameDirectory);
        if (table != null)
            foreach (var d in table.Discoveries)
                if (d.Movie >= 0)
                {
                    if (!users.TryGetValue(d.Movie, out var names)) users[d.Movie] = names = [];
                    names.Add(d.Name);
                }

        var rows = new List<Row>();
        _usedByTable.Clear();
        _usedByTable.UnionWith(users.Keys);

        // 새로 더한 번호(70 이상)는 발견물 표에 없어도 올린 파일이 있으면 줄로 낸다 — 대본의 「동영상 재생」이 부른다.
        foreach (int n in Enumerable.Range(0, DiscoveryMovies).Union(users.Keys)
                     .Union(MovieFiles.UploadedDiscoveryNumbers()).OrderBy(n => n))
            rows.Add(MakeRow(n < DiscoveryMovies ? "발견물" : "추가", MovieFiles.DiscoveryStem(n),
                             users.TryGetValue(n, out var names) ? string.Join(", ", names)
                             : n >= DiscoveryMovies ? $"대본 「동영상 재생 {n}」" : ""));

        var hulls = MovieFiles.Hulls;
        for (int h = 0; h < hulls.Count; h++)
            rows.Add(MakeRow("선체", MovieFiles.HullStem(h), hulls[h]));

        _grid.ItemsSource = rows;
        if (keep != null) _grid.SelectedItem = rows.FirstOrDefault(r => r.Stem == keep);

        int uploaded = rows.Count(r => r.Uploaded != null);
        int original = rows.Count(r => r.Original != null);
        _status.Text = (_gameDirectory.Length == 0
                           ? "게임 폴더를 모릅니다 — 원본은 안 보이고 올린 것만 틀립니다. "
                           : $"{Path.Combine(_gameDirectory, MovieFiles.GameFolder)} · ") +
                       $"원본 {original}편 · 올린 것 {uploaded}편 → {MovieFiles.UploadDirectory()}";
    }

    private Row MakeRow(string kind, string stem, string users) => new(
        kind, stem, users,
        MovieFiles.Original(_gameDirectory, stem),
        MovieFiles.Uploaded(stem));

    private Row? Selected => _grid.SelectedItem as Row;

    // ── 재생 ────────────────────────────────────────────────────────────────

    private void PlaySelected(bool original)
    {
        if (Selected is not { } row) return;

        string? path = original ? row.Original : row.Uploaded ?? row.Original;
        if (path == null)
        {
            _status.Text = original ? $"{row.Stem} 원본이 없습니다" : $"{row.Stem} 는 틀 파일이 없습니다";
            return;
        }

        ClosePreview();
        _preview.Source = new Uri(path);
        _preview.Play();
        _previewLabel.Text = $"{row.Stem} · {(path == row.Uploaded ? "올린 것" : "원본")}\n{path}";
        _status.Text = $"{Path.GetFileName(path)} 를 틉니다";
    }

    /// <summary>미리 보기를 멈추고 파일을 놓는다 — 그래야 지우거나 덮어쓸 수 있다.</summary>
    private void ClosePreview()
    {
        _preview.Stop();
        _preview.Close();
        _preview.Source = null;
        _previewLabel.Text = "";
    }

    // ── 올리기 · 지우기 ──────────────────────────────────────────────────────

    private void PickAndUpload()
    {
        if (Selected is not { } row)
        {
            _status.Text = "먼저 갈아 끼울 줄을 고르세요";
            return;
        }

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = $"{row.Stem} 자리에 올릴 동영상",
            Filter = MovieFiles.OpenFilter,
        };
        if (dlg.ShowDialog(this) == true) Upload(dlg.FileName);
    }

    /// <summary>
    /// 새 동영상을 <b>빈 번호</b>(70 부터)로 더한다. 발견 이벤트 편집기의 「동영상 넣기」가 그 번호로 부른다.
    /// </summary>
    private void AddNew()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "새로 더할 동영상",
            Filter = MovieFiles.OpenFilter,
        };
        if (dlg.ShowDialog(this) == true) AddNew(dlg.FileName);
    }

    private void AddNew(string source)
    {
        ClosePreview();
        try
        {
            int n = MovieFiles.NextFreeDiscoveryNumber(_usedByTable);
            string stem = MovieFiles.DiscoveryStem(n);
            var target = MovieFiles.Upload(source, stem);
            Refill();
            _grid.SelectedItem = (_grid.ItemsSource as List<Row>)?.FirstOrDefault(r => r.Stem == stem);
            _grid.ScrollIntoView(_grid.SelectedItem);
            _status.Text = $"{Path.GetFileName(source)} → 동영상 {n} ({target}) — " +
                           "발견 이벤트 편집기에서 「동영상 넣기」로 이 번호를 고르면 대본 사이에 틉니다";
        }
        catch (Exception ex)
        {
            _status.Text = $"더하지 못했습니다 — {ex.Message}";
        }
    }

    private void Upload(string source)
    {
        if (Selected is not { } row)
        {
            AddNew(source);
            return;
        }

        ClosePreview();
        try
        {
            var target = MovieFiles.Upload(source, row.Stem);
            Refill();
            _status.Text = $"{Path.GetFileName(source)} → {target}";
        }
        catch (Exception ex)
        {
            _status.Text = $"올리지 못했습니다 — {ex.Message}";
        }
    }

    private void RemoveSelected()
    {
        if (Selected is not { Uploaded: not null } row) return;

        ClosePreview();
        try
        {
            MovieFiles.Remove(row.Stem);
            Refill();
            _status.Text = $"{row.Stem} 올린 것을 지웠습니다 — 이제 원본을 틉니다";
        }
        catch (Exception ex)
        {
            _status.Text = $"지우지 못했습니다 — {ex.Message}";
        }
    }

    private void OpenFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo(MovieFiles.UploadDirectory()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _status.Text = $"폴더를 열지 못했습니다 — {ex.Message}";
        }
    }

    /// <summary>표 한 줄.</summary>
    private sealed record Row(string Kind, string Stem, string Users, string? Original, string? Uploaded)
    {
        public string OriginalText => Original != null ? "있음" : "—";
        public string UploadedText => Uploaded != null ? Path.GetFileName(Uploaded) : "—";
        public string PlaysText => Uploaded != null ? "올린 것" : Original != null ? "원본" : "없음";
    }
}
