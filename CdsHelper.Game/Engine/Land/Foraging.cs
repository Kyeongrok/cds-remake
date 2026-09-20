using CdsHelper.Game.Engine;
using CdsHelper.Support.Local.Models;

namespace CdsHelper.Game.Engine.Land;

/// <summary>
/// 상륙 차림표 「보급」의 <b>물·식량 찾기</b>(<c>0x0048DEC6</c>~<c>0x0048E0C7</c>).
/// </summary>
/// <remarks>
/// 동굴(<see cref="LandEvents.CaveOdds"/>)을 지나면 <b>언제나</b> 이 되돌이가 돈다 — 동굴이
/// 안 나와도, 보물을 얻어도, 소굴에 당해도 마찬가지다.
/// <code>
///   0048dec6  선원 수로 보정값을 잡는다     30 이하 −10 · 100 위 +10 · 그 사이 0
///   0048def2  지금 선 칸의 지형 부류로 물·식량 등급을 꺼낸다(0x004CCFE0 · 0x004CD014)
///   0048df08  rand(100) &lt; 등급 x 25 + 보정  이면 그것이 나온다
///   0048df51  나온 통 수 = rand(3) + 등급 x 2
///   0048df9a  실을 수 있는 데까지 번갈아 담는다(모자란 쪽부터)
///   0048e08d  하나도 못 찾으면 피로 +10 · 규율 −10, 찾았으면 +5 · −5
/// </code>
/// 지형 부류는 <c>ShipMapHost.TerrainClass</c> 가 주는 0~6 과 같은 값이다(표는 열셋 칸이지만
/// 뒤 여섯은 바다 쪽이라 뭍에서는 안 걸린다).
/// </remarks>
public static class Foraging
{
    /// <summary>지형 부류별 <b>물</b> 등급(<c>0x004CCFE0</c>).</summary>
    public static readonly int[] WaterLevels = [4, 2, 1, 0, 1, 4, 3, 2, 2, 1, 1, 0, 1];

    /// <summary>지형 부류별 <b>식량</b> 등급(<c>0x004CD014</c>).</summary>
    public static readonly int[] FoodLevels = [4, 2, 1, 0, 2, 3, 3, 2, 1, 1, 0, 0, 1];

    /// <summary>
    /// 선원 수 보정(<c>0x0048DEC6</c>) — 손이 많으면 잘 찾는다.
    /// </summary>
    /// <remarks>
    /// 30 이하면 −10, 100 위면 +10, 그 사이는 0 이다. 게임은 8척의 선원을 다 더한 값을 쓴다.
    /// </remarks>
    public static int CrewBonus(int crew) => crew <= 30 ? -10 : crew >= 100 ? 10 : 0;

    /// <summary>등급을 꺼낸다. 부류를 모르면(-1) 0 으로 본다.</summary>
    public static int LevelOf(int[] table, int ground) =>
        ground >= 0 && ground < table.Length ? table[ground] : 0;

    /// <summary>그것이 나왔는가 — <c>rand(100) &lt; 등급 x 25 + 보정</c>(<c>0x0048DF21</c>).</summary>
    public static bool Found(int level, int bonus, GameRandom dice) =>
        dice.Next(100) < level * 25 + bonus;

    /// <summary>나온 통 수 — <c>rand(3) + 등급 x 2</c>(<c>0x0048DF5F</c>).</summary>
    public static int Amount(int level, GameRandom dice) => dice.Next(3) + level * 2;

    /// <summary>
    /// 찾은 것을 실을 수 있는 만큼만 담는다(<c>0x0048DF9A</c>).
    /// </summary>
    /// <remarks>
    /// 한 통씩 번갈아 담는데, <b>지금 실린 통 수가 적은 쪽</b>을 먼저 담아 물과 식량을 맞춘다.
    /// 칸(<c>0x00474490</c>)이나 무게(<c>0x004743D0</c>)가 모자라면 거기서 멈추고,
    /// <b>무게를 넘긴 마지막 한 통은 도로 무른다</b>(<c>0x0048E010</c>).
    /// </remarks>
    /// <param name="water">찾은 물 통 수.</param>
    /// <param name="food">찾은 식량 통 수.</param>
    /// <param name="haveWater">지금 실린 물 통 수.</param>
    /// <param name="haveFood">지금 실린 식량 통 수.</param>
    /// <param name="slots">남은 짐 칸 수.</param>
    /// <param name="room">남은 적재 중량.</param>
    /// <returns>실제로 실은 (물, 식량) 통 수.</returns>
    public static (int Water, int Food) Stow(int water, int food, int haveWater, int haveFood,
                                             int slots, int room)
    {
        int waterWeight = Supply.Of(SupplyKind.Water).UnitWeight;   // 10
        int foodWeight = Supply.Of(SupplyKind.Food).UnitWeight;     //  5
        int tookWater = 0, tookFood = 0;

        while (true)
        {
            if (water <= 0 && food <= 0) break;
            if (slots <= 0 || room <= 0) break;

            int keptWater = tookWater, keptFood = tookFood;

            // 식량이 없거나, 실린 식량이 물보다 많으면 물을 담는다.
            if (food == 0 || (water != 0 && haveFood + tookFood > haveWater + tookWater))
            {
                water--;
                tookWater++;
                room -= waterWeight;
            }
            else
            {
                food--;
                tookFood++;
                room -= foodWeight;
            }

            slots--;
            if (room < 0) { tookWater = keptWater; tookFood = keptFood; break; }
        }

        return (tookWater, tookFood);
    }

    /// <summary>하나도 못 찾았을 때 오르는 피로(<c>0x0048E09A</c>).</summary>
    public const int EmptyFatigue = 10;

    /// <summary>하나도 못 찾았을 때 깎이는 규율(<c>0x0048E0A6</c>).</summary>
    public const int EmptyMorale = 10;

    /// <summary>뭐라도 찾았을 때 오르는 피로(<c>0x0048E0AF</c>).</summary>
    public const int TiredFatigue = 5;

    /// <summary>뭐라도 찾았을 때 깎이는 규율(<c>0x0048E0BB</c>).</summary>
    public const int TiredMorale = 5;
}
