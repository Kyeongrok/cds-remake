using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace CdsHelper.Support.UI.Units;

/// <summary>
/// 스킬 표시용 그룹 박스 컨트롤
/// 5개 열 (플레이어, 부관, 항해사, 측량사, 통역)의 스킬 레벨을 표시
/// </summary>
public class SkillGroupBox : Control
{
    #region Dependency Properties

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("스킬"));

    public static readonly DependencyProperty SkillsProperty =
        DependencyProperty.Register(nameof(Skills), typeof(IEnumerable), typeof(SkillGroupBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty LanguagesProperty =
        DependencyProperty.Register(nameof(Languages), typeof(IEnumerable), typeof(SkillGroupBox),
            new PropertyMetadata(null));

    public static readonly DependencyProperty Column1LabelProperty =
        DependencyProperty.Register(nameof(Column1Label), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("플레이어"));

    public static readonly DependencyProperty Column2LabelProperty =
        DependencyProperty.Register(nameof(Column2Label), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("부관"));

    public static readonly DependencyProperty Column3LabelProperty =
        DependencyProperty.Register(nameof(Column3Label), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("항해사"));

    public static readonly DependencyProperty Column4LabelProperty =
        DependencyProperty.Register(nameof(Column4Label), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("측량사"));

    public static readonly DependencyProperty Column5LabelProperty =
        DependencyProperty.Register(nameof(Column5Label), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("통역"));

    public static readonly DependencyProperty Column1ColorProperty =
        DependencyProperty.Register(nameof(Column1Color), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("#2196F3"));

    public static readonly DependencyProperty Column2ColorProperty =
        DependencyProperty.Register(nameof(Column2Color), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("#4CAF50"));

    public static readonly DependencyProperty Column3ColorProperty =
        DependencyProperty.Register(nameof(Column3Color), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("#FF9800"));

    public static readonly DependencyProperty Column4ColorProperty =
        DependencyProperty.Register(nameof(Column4Color), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("#9C27B0"));

    public static readonly DependencyProperty Column5ColorProperty =
        DependencyProperty.Register(nameof(Column5Color), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("#F44336"));

    public static readonly DependencyProperty SkillsHeaderProperty =
        DependencyProperty.Register(nameof(SkillsHeader), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("기능 스킬"));

    public static readonly DependencyProperty LanguagesHeaderProperty =
        DependencyProperty.Register(nameof(LanguagesHeader), typeof(string), typeof(SkillGroupBox),
            new PropertyMetadata("언어 스킬"));

    #endregion

    #region Properties

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public IEnumerable Skills
    {
        get => (IEnumerable)GetValue(SkillsProperty);
        set => SetValue(SkillsProperty, value);
    }

    public IEnumerable Languages
    {
        get => (IEnumerable)GetValue(LanguagesProperty);
        set => SetValue(LanguagesProperty, value);
    }

    public string Column1Label
    {
        get => (string)GetValue(Column1LabelProperty);
        set => SetValue(Column1LabelProperty, value);
    }

    public string Column2Label
    {
        get => (string)GetValue(Column2LabelProperty);
        set => SetValue(Column2LabelProperty, value);
    }

    public string Column3Label
    {
        get => (string)GetValue(Column3LabelProperty);
        set => SetValue(Column3LabelProperty, value);
    }

    public string Column4Label
    {
        get => (string)GetValue(Column4LabelProperty);
        set => SetValue(Column4LabelProperty, value);
    }

    public string Column5Label
    {
        get => (string)GetValue(Column5LabelProperty);
        set => SetValue(Column5LabelProperty, value);
    }

    public string Column1Color
    {
        get => (string)GetValue(Column1ColorProperty);
        set => SetValue(Column1ColorProperty, value);
    }

    public string Column2Color
    {
        get => (string)GetValue(Column2ColorProperty);
        set => SetValue(Column2ColorProperty, value);
    }

    public string Column3Color
    {
        get => (string)GetValue(Column3ColorProperty);
        set => SetValue(Column3ColorProperty, value);
    }

    public string Column4Color
    {
        get => (string)GetValue(Column4ColorProperty);
        set => SetValue(Column4ColorProperty, value);
    }

    public string Column5Color
    {
        get => (string)GetValue(Column5ColorProperty);
        set => SetValue(Column5ColorProperty, value);
    }

    public string SkillsHeader
    {
        get => (string)GetValue(SkillsHeaderProperty);
        set => SetValue(SkillsHeaderProperty, value);
    }

    public string LanguagesHeader
    {
        get => (string)GetValue(LanguagesHeaderProperty);
        set => SetValue(LanguagesHeaderProperty, value);
    }

    #endregion

    static SkillGroupBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SkillGroupBox),
            new FrameworkPropertyMetadata(typeof(SkillGroupBox)));
    }
}
