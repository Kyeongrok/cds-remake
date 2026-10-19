using CdsHelper.Game.Local.Helpers;
using CdsHelper.Support.Local.Models;
using MoonSharp.Interpreter;

namespace CdsHelper.Game.UI.AddOns;

/// <summary>
/// 애드온이 부를 수 있는 전역 함수들 — <b>읽기만</b> 한다.
/// </summary>
/// <remarks>
/// 화면에서 볼 수 있는 값만 내준다(금화 · 날짜 · 함대 · 짐 · 보급 · 배 자리). 못 찾은 발견물 자리나 딴 도시 시세처럼
/// 원본 화면에 안 나오는 것은 안 준다 — 하드코어 랭킹에서 남보다 더 알게 되면 안 된다.
/// 게임이 아직 안 섰으면(제목 화면) 값을 묻는 함수는 nil 을 낸다.
/// </remarks>
internal static class AddOnApi
{
    static AddOnApi()
    {
        UserData.RegisterType<AddOnFrame>();
        UserData.RegisterType<AddOnText>();
        UserData.RegisterType<AddOnButton>();
    }

    public static void Install(AddOn addon)
    {
        var lua = addon.Lua!;
        var g = lua.Globals;

        g["CreateFrame"] = DynValue.NewCallback((_, a) =>
            UserData.Create(addon.CreateFrame(a.Count > 0 && a[0].Type == DataType.String ? a[0].String : null)));

        g["GetTime"] = DynValue.NewCallback((_, _) => DynValue.NewNumber(AddOnHost.Now));
        g["GetLocale"] = DynValue.NewCallback((_, _) => DynValue.NewString(Lang.Current.Code));
        g["GetAddOnName"] = DynValue.NewCallback((_, _) => DynValue.NewString(addon.Name));
        g["ReloadUI"] = DynValue.NewCallback((_, _) =>
        {
            // 부르는 판이 도는 가운데 걷으면 안 된다 — 틱이 끝난 뒤에 다시 읽는다.
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(AddOnHost.Reload);
            return DynValue.Nil;
        });

        // OpenWindow(이름) — 단축키로 여는 게임 창을 연다. 열 수 있는 이름이면 true.
        // 창은 모달이라 Lua 호출이 끝난 뒤에 띄운다 — 호출 가운데 띄우면 그 창이 떠 있는 동안 같은 Lua 판에 타이머가 또 들어온다.
        g["OpenWindow"] = DynValue.NewCallback((_, a) =>
        {
            if (a.Count == 0 || a[0].Type != DataType.String || AddOnHost.Stage?.Open(a[0].String.ToLowerInvariant()) is not { } open)
                return DynValue.False;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(open);
            return DynValue.True;
        });

        Player? P() => AddOnHost.Stage is { } s && s.Running() ? s.Game().Player : null;
        DynValue Num(Func<Player, double> get) => P() is { } p ? DynValue.NewNumber(get(p)) : DynValue.Nil;

        g["GetPlayerName"] = DynValue.NewCallback((_, _) => P() is { } p ? DynValue.NewString(p.Name) : DynValue.Nil);
        g["GetGold"] = DynValue.NewCallback((_, _) => Num(p => p.Gold));
        g["GetFame"] = DynValue.NewCallback((_, _) => Num(p => p.Fame));
        g["GetAge"] = DynValue.NewCallback((_, _) => Num(p => p.Age));
        g["GetCondition"] = DynValue.NewCallback((_, _) => Num(p => p.Condition));
        g["GetFatigue"] = DynValue.NewCallback((_, _) => Num(p => p.Fatigue));
        g["GetMorale"] = DynValue.NewCallback((_, _) => Num(p => p.Morale));
        g["GetCrew"] = DynValue.NewCallback((_, _) => Num(p => p.Crew));
        g["GetSupplyDays"] = DynValue.NewCallback((_, _) => Num(p => p.SupplyDaysLeft));
        g["GetDiscoveryCount"] = DynValue.NewCallback((_, _) => Num(p => p.Discoveries.Count));

        g["GetDate"] = DynValue.NewCallback((_, _) => P() is { } p
            ? DynValue.NewTuple(DynValue.NewNumber(p.Date.Year), DynValue.NewNumber(p.Date.Month), DynValue.NewNumber(p.Date.Day))
            : DynValue.Nil);

        g["GetCity"] = DynValue.NewCallback((_, _) => P() is { CityId: >= 0 } p
            ? DynValue.NewTuple(DynValue.NewNumber(p.CityId), DynValue.NewString(p.CityName))
            : DynValue.Nil);

        g["GetPosition"] = DynValue.NewCallback((_, _) =>
            P() != null && AddOnHost.Stage?.Position() is { } at
                ? DynValue.NewTuple(DynValue.NewNumber(at.Lat), DynValue.NewNumber(at.Lon))
                : DynValue.Nil);

        g["IsOnLand"] = DynValue.NewCallback((_, _) =>
            P() != null ? DynValue.NewBoolean(AddOnHost.Stage!.OnLand()) : DynValue.Nil);

        // 보급품 — 열쇠는 food · water 따위 영어 이름(SupplyKind), 값은 통 수.
        g["GetSupply"] = DynValue.NewCallback((_, _) =>
        {
            if (P() is not { } p) return DynValue.Nil;
            var t = new Table(lua);
            foreach (var kind in Enum.GetValues<SupplyKind>())
                t[kind.ToString().ToLowerInvariant()] = p.SupplyOf(kind);
            return DynValue.NewTable(t);
        });

        g["GetShips"] = DynValue.NewCallback((_, _) =>
        {
            if (P() is not { } p) return DynValue.Nil;
            var list = new Table(lua);
            foreach (var ship in p.Ships)
            {
                var t = new Table(lua)
                {
                    ["name"] = ship.Name,
                    ["hull"] = ship.Hull.Name,
                    ["hp"] = ship.Hp,
                    ["maxHp"] = ship.MaxHp,
                    ["speed"] = ship.Speed,
                    ["maxSpeed"] = ship.MaxSpeed,
                    ["capacity"] = ship.Capacity,
                    ["crewNeeded"] = ship.Crew,
                    ["guns"] = ship.Guns,
                    ["lent"] = ship.Lent,
                };
                list.Append(DynValue.NewTable(t));
            }
            return DynValue.NewTable(list);
        });

        g["GetCargo"] = DynValue.NewCallback((_, _) =>
        {
            if (P() is not { } p) return DynValue.Nil;
            var goods = AddOnHost.Stage!.Game().Goods;
            var list = new Table(lua);
            foreach (var cargo in p.CargoHold)
            {
                var t = new Table(lua)
                {
                    ["name"] = goods?.Find(cargo.Kind)?.Name ?? cargo.Kind.ToString(),
                    ["count"] = cargo.Count,
                    ["months"] = cargo.Months,
                    ["spoiled"] = cargo.Spoiled,
                };
                list.Append(DynValue.NewTable(t));
            }
            return DynValue.NewTable(list);
        });

        // C_Timer.After(초, 함수) · C_Timer.NewTicker(초, 함수) — 틱커는 :Cancel() 로 멈춘다.
        var timers = new Table(lua);
        timers["After"] = DynValue.NewCallback((_, a) =>
        {
            addon.AddTimer(a[0].CastToNumber() ?? 0, a[1], repeat: false);
            return DynValue.Nil;
        });
        timers["NewTicker"] = DynValue.NewCallback((_, a) =>
        {
            var timer = addon.AddTimer(a[0].CastToNumber() ?? 1, a[1], repeat: true);
            var handle = new Table(lua);
            handle["Cancel"] = DynValue.NewCallback((_, _) => { timer.Cancelled = true; return DynValue.Nil; });
            return DynValue.NewTable(handle);
        });
        g["C_Timer"] = timers;
    }
}
