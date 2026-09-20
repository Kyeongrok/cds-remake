using CommunityToolkit.Mvvm.ComponentModel;

namespace CdsHelper.Main.Local.ViewModels;

public partial class SphinxCalculatorContentViewModel : ObservableObject
{
    // === Type 1: 기본형 ===
    private int _legsPerTypeA = 4;
    private int _legsPerTypeB = 2;
    private int _totalCreatures = 13;
    private int _totalLegs = 42;
    private int _typeACount;
    private int _typeBCount;
    private string _solutionText = "";

    // === Type 2: 변환형 ===
    private int _t2_initialLegsA = 4;
    private int _t2_initialLegsB = 2;
    private int _t2_initialTotalLegs = 42;
    private int _t2_afterLegsA = 2;
    private int _t2_transformCountB = 3;
    private int _t2_afterLegsB = 3;
    private int _t2_finalTotalLegs = 33;
    private int _t2_typeACount;
    private int _t2_typeBCount;
    private string _t2_solutionText = "";

    // === Type 3: 변환+탄생형 ===
    private int _t3_initialLegsA = 4;
    private int _t3_initialLegsB = 2;
    private int _t3_initialTotalLegs = 42;
    private int _t3_afterLegsA = 2;
    private int _t3_transformCountB = 4;
    private int _t3_afterLegsB = 3;
    private int _t3_newCreatureMultiplier = 2;
    private int _t3_newCreatureLegs = 4;
    private int _t3_finalTotalLegs = 94;
    private int _t3_typeACount;
    private int _t3_typeBCount;
    private string _t3_solutionText = "";

    public int LegsPerTypeA
    {
        get => _legsPerTypeA;
        set { if (SetProperty(ref _legsPerTypeA, value)) Calculate(); }
    }

    public int LegsPerTypeB
    {
        get => _legsPerTypeB;
        set { if (SetProperty(ref _legsPerTypeB, value)) Calculate(); }
    }

    public int TotalCreatures
    {
        get => _totalCreatures;
        set { if (SetProperty(ref _totalCreatures, value)) Calculate(); }
    }

    public int TotalLegs
    {
        get => _totalLegs;
        set { if (SetProperty(ref _totalLegs, value)) Calculate(); }
    }

    public int TypeACount
    {
        get => _typeACount;
        private set => SetProperty(ref _typeACount, value);
    }

    public int TypeBCount
    {
        get => _typeBCount;
        private set => SetProperty(ref _typeBCount, value);
    }

    public string SolutionText
    {
        get => _solutionText;
        private set => SetProperty(ref _solutionText, value);
    }

    // === Type 2 Properties ===
    public int T2_InitialLegsA
    {
        get => _t2_initialLegsA;
        set { if (SetProperty(ref _t2_initialLegsA, value)) CalculateType2(); }
    }

    public int T2_InitialLegsB
    {
        get => _t2_initialLegsB;
        set { if (SetProperty(ref _t2_initialLegsB, value)) CalculateType2(); }
    }

    public int T2_InitialTotalLegs
    {
        get => _t2_initialTotalLegs;
        set { if (SetProperty(ref _t2_initialTotalLegs, value)) CalculateType2(); }
    }

    public int T2_AfterLegsA
    {
        get => _t2_afterLegsA;
        set { if (SetProperty(ref _t2_afterLegsA, value)) CalculateType2(); }
    }

    public int T2_TransformCountB
    {
        get => _t2_transformCountB;
        set { if (SetProperty(ref _t2_transformCountB, value)) CalculateType2(); }
    }

    public int T2_AfterLegsB
    {
        get => _t2_afterLegsB;
        set { if (SetProperty(ref _t2_afterLegsB, value)) CalculateType2(); }
    }

    public int T2_FinalTotalLegs
    {
        get => _t2_finalTotalLegs;
        set { if (SetProperty(ref _t2_finalTotalLegs, value)) CalculateType2(); }
    }

    public int T2_TypeACount
    {
        get => _t2_typeACount;
        private set => SetProperty(ref _t2_typeACount, value);
    }

    public int T2_TypeBCount
    {
        get => _t2_typeBCount;
        private set => SetProperty(ref _t2_typeBCount, value);
    }

    public string T2_SolutionText
    {
        get => _t2_solutionText;
        private set => SetProperty(ref _t2_solutionText, value);
    }

    // === Type 3 Properties ===
    public int T3_InitialLegsA
    {
        get => _t3_initialLegsA;
        set { if (SetProperty(ref _t3_initialLegsA, value)) CalculateType3(); }
    }

    public int T3_InitialLegsB
    {
        get => _t3_initialLegsB;
        set { if (SetProperty(ref _t3_initialLegsB, value)) CalculateType3(); }
    }

    public int T3_InitialTotalLegs
    {
        get => _t3_initialTotalLegs;
        set { if (SetProperty(ref _t3_initialTotalLegs, value)) CalculateType3(); }
    }

    public int T3_AfterLegsA
    {
        get => _t3_afterLegsA;
        set { if (SetProperty(ref _t3_afterLegsA, value)) CalculateType3(); }
    }

    public int T3_TransformCountB
    {
        get => _t3_transformCountB;
        set { if (SetProperty(ref _t3_transformCountB, value)) CalculateType3(); }
    }

    public int T3_AfterLegsB
    {
        get => _t3_afterLegsB;
        set { if (SetProperty(ref _t3_afterLegsB, value)) CalculateType3(); }
    }

    public int T3_NewCreatureMultiplier
    {
        get => _t3_newCreatureMultiplier;
        set { if (SetProperty(ref _t3_newCreatureMultiplier, value)) CalculateType3(); }
    }

    public int T3_NewCreatureLegs
    {
        get => _t3_newCreatureLegs;
        set { if (SetProperty(ref _t3_newCreatureLegs, value)) CalculateType3(); }
    }

    public int T3_FinalTotalLegs
    {
        get => _t3_finalTotalLegs;
        set { if (SetProperty(ref _t3_finalTotalLegs, value)) CalculateType3(); }
    }

    public int T3_TypeACount
    {
        get => _t3_typeACount;
        private set => SetProperty(ref _t3_typeACount, value);
    }

    public int T3_TypeBCount
    {
        get => _t3_typeBCount;
        private set => SetProperty(ref _t3_typeBCount, value);
    }

    public string T3_SolutionText
    {
        get => _t3_solutionText;
        private set => SetProperty(ref _t3_solutionText, value);
    }

    public SphinxCalculatorContentViewModel()
    {
        Calculate();
        CalculateType2();
        CalculateType3();
    }

    private void Calculate()
    {
        var a = LegsPerTypeA;
        var b = LegsPerTypeB;

        if (a == b)
        {
            SolutionText = "A와 B의 다리 수가 같으면 계산할 수 없습니다.";
            TypeACount = 0;
            TypeBCount = 0;
            return;
        }

        var numerator = TotalLegs - b * TotalCreatures;
        var denominator = a - b;

        if (numerator % denominator != 0)
        {
            SolutionText = "해가 없습니다. 입력값을 확인하세요.";
            TypeACount = 0;
            TypeBCount = 0;
            return;
        }

        var x = numerator / denominator;
        var y = TotalCreatures - x;

        if (x < 0 || y < 0)
        {
            SolutionText = "해가 없습니다. (음수 결과)";
            TypeACount = 0;
            TypeBCount = 0;
            return;
        }

        TypeACount = x;
        TypeBCount = y;

        SolutionText = $"""
            [풀이]
            {a}다리 괴물 = x, {b}다리 괴물 = y

            ▶ x + y = {TotalCreatures} (괴물의 수)
            ▶ {a}x + {b}y = {TotalLegs} (다리의 총 수)

            → {b}x + {b}y = {b * TotalCreatures}
            → ({a}x + {b}y) - ({b}x + {b}y) = {TotalLegs} - {b * TotalCreatures}
            → {a - b}x = {numerator}
            → x = {x}

            y = {TotalCreatures} - {x} = {y}

            [정답]
            {a}다리 괴물: {x}마리
            {b}다리 괴물: {y}마리
            """;
    }

    private void CalculateType2()
    {
        var a = T2_InitialLegsA;
        var b = T2_InitialLegsB;
        var a2 = T2_AfterLegsA;
        var n = T2_TransformCountB;
        var b2 = T2_AfterLegsB;
        var initLegs = T2_InitialTotalLegs;
        var finalLegs = T2_FinalTotalLegs;

        var denominator = a - a2;
        if (denominator == 0)
        {
            T2_SolutionText = "A의 다리 수 변화가 없으면 계산할 수 없습니다.";
            T2_TypeACount = 0;
            T2_TypeBCount = 0;
            return;
        }

        var numerator = initLegs - finalLegs + n * (b2 - b);

        if (numerator % denominator != 0)
        {
            T2_SolutionText = "해가 없습니다. 입력값을 확인하세요.";
            T2_TypeACount = 0;
            T2_TypeBCount = 0;
            return;
        }

        var x = numerator / denominator;

        var byValue = initLegs - a * x;
        if (byValue % b != 0 || byValue < 0)
        {
            T2_SolutionText = "해가 없습니다. 입력값을 확인하세요.";
            T2_TypeACount = 0;
            T2_TypeBCount = 0;
            return;
        }

        var y = byValue / b;

        if (x < 0 || y < n)
        {
            T2_SolutionText = "해가 없습니다. (조건 불만족)";
            T2_TypeACount = 0;
            T2_TypeBCount = 0;
            return;
        }

        T2_TypeACount = x;
        T2_TypeBCount = y;

        var verifyFinal = a2 * x + b * (y - n) + b2 * n;

        T2_SolutionText = $"""
            [풀이]
            초기: {a}다리 괴물 = x, {b}다리 괴물 = y

            ▶ {a}x + {b}y = {initLegs} (초기 다리 합계)

            변환 후:
            - {a}다리 괴물 x마리 → {a2}다리가 됨
            - {b}다리 괴물 중 {n}마리 → {b2}다리가 됨

            ▶ {a2}x + {b}(y-{n}) + {b2}×{n} = {finalLegs}
            → {a2}x + {b}y - {b * n} + {b2 * n} = {finalLegs}
            → {a2}x + {b}y = {finalLegs + b * n - b2 * n}

            두 식을 빼면:
            ({a}x + {b}y) - ({a2}x + {b}y) = {initLegs} - {finalLegs + b * n - b2 * n}
            → {a - a2}x = {numerator}
            → x = {x}

            y = ({initLegs} - {a}×{x}) / {b} = {y}

            [검증]
            초기: {a}×{x} + {b}×{y} = {a * x + b * y} ✓
            변환 후: {a2}×{x} + {b}×{y - n} + {b2}×{n} = {verifyFinal} ✓

            [정답]
            처음 {a}다리 괴물: {x}마리
            처음 {b}다리 괴물: {y}마리
            """;
    }

    private void CalculateType3()
    {
        var a = T3_InitialLegsA;
        var b = T3_InitialLegsB;
        var a2 = T3_AfterLegsA;
        var n = T3_TransformCountB;
        var b2 = T3_AfterLegsB;
        var mult = T3_NewCreatureMultiplier;
        var c = T3_NewCreatureLegs;
        var initLegs = T3_InitialTotalLegs;
        var finalLegs = T3_FinalTotalLegs;

        var denominator = a - a2 - c * mult;
        if (denominator == 0)
        {
            T3_SolutionText = "계수가 0이 되어 계산할 수 없습니다.";
            T3_TypeACount = 0;
            T3_TypeBCount = 0;
            return;
        }

        var numerator = initLegs - finalLegs + n * (b2 - b);

        if (numerator % denominator != 0)
        {
            T3_SolutionText = "해가 없습니다. 입력값을 확인하세요.";
            T3_TypeACount = 0;
            T3_TypeBCount = 0;
            return;
        }

        var x = numerator / denominator;

        var byValue = initLegs - a * x;
        if (byValue % b != 0 || byValue < 0)
        {
            T3_SolutionText = "해가 없습니다. 입력값을 확인하세요.";
            T3_TypeACount = 0;
            T3_TypeBCount = 0;
            return;
        }

        var y = byValue / b;

        if (x < 0 || y < n)
        {
            T3_SolutionText = "해가 없습니다. (조건 불만족)";
            T3_TypeACount = 0;
            T3_TypeBCount = 0;
            return;
        }

        T3_TypeACount = x;
        T3_TypeBCount = y;

        var newCount = mult * x;
        var verifyFinal = a2 * x + b * (y - n) + b2 * n + c * newCount;

        T3_SolutionText = $"""
            [풀이]
            초기: {a}다리 괴물 = x, {b}다리 괴물 = y

            ▶ {a}x + {b}y = {initLegs} (초기 다리 합계)

            변환 후:
            - {a}다리 괴물 x마리 → {a2}다리가 됨: {a2}x
            - {b}다리 괴물 중 {n}마리 → {b2}다리가 됨: {b2}×{n}
            - {b}다리 괴물 나머지 (y-{n})마리: {b}(y-{n})
            - 신규 탄생: {mult}x마리의 {c}다리 괴물: {c}×{mult}x

            ▶ {a2}x + {b}(y-{n}) + {b2}×{n} + {c}×{mult}x = {finalLegs}
            → ({a2} + {c * mult})x + {b}y - {b * n} + {b2 * n} = {finalLegs}
            → {a2 + c * mult}x + {b}y = {finalLegs + b * n - b2 * n}

            두 식을 빼면:
            ({a} - {a2 + c * mult})x = {initLegs} - {finalLegs + b * n - b2 * n}
            → {denominator}x = {numerator}
            → x = {x}

            y = ({initLegs} - {a}×{x}) / {b} = {y}

            [검증]
            초기: {a}×{x} + {b}×{y} = {a * x + b * y} ✓
            변환 후: {a2}×{x} + {b}×{y - n} + {b2}×{n} + {c}×{newCount} = {verifyFinal} ✓

            [정답]
            처음 {a}다리 괴물: {x}마리
            처음 {b}다리 괴물: {y}마리
            """;
    }
}
