using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CdsHelper.Game.Engine;
using CdsHelper.Game.Local.Settings;
using CdsHelper.Game.UI.Views;
using CdsHelper.Support.Local.Models;

static class Program
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly List<string> Results = [];
    static string Root = "";
    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private)!.GetValue(obj)!;
    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException(message);
        Results.Add("PASS " + message);
        Console.WriteLine(Results[^1]);
        File.WriteAllLines(Path.Combine(Root, "checks.log"), Results);
    }
    static void Same(IEnumerable<int> actual, int[] expected, string message) => Check(actual.SequenceEqual(expected), message);
    static PlayerAbilityEditDialog Dialog(Player player) => (PlayerAbilityEditDialog)Activator.CreateInstance(
        typeof(PlayerAbilityEditDialog), Private, null, [player], null)!;
    static void Fill(PlayerAbilityEditDialog d, params string[] values)
    {
        var fields = Field<TextBox[]>(d, "_fields");
        for (int i = 0; i < values.Length; i++) fields[i].Text = values[i];
    }
    static void Apply(PlayerAbilityEditDialog d) => d.GetType().GetMethod("Apply", Private)!.Invoke(d, null);
    static void Levels(PlayerAbilityEditDialog d, string field, int[] values)
    {
        var fields = Field<ComboBox[]>(d, field);
        for (int i = 0; i < values.Length; i++) fields[i].SelectedIndex = values[i];
    }
    static void SameLevels(Player player, int[] skills, int[] tongues, string message)
    {
        Same(Skill.Names.Select(player.LevelOf), skills, message + " (13 skills)");
        Same(Skill.Languages.Select(player.TongueOf), tongues, message + " (14 languages)");
    }
    static WorldMapWindow LoadWorld(string path)
    {
        var world = new WorldMapWindow();
        var startup = (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), world,
            typeof(WorldMapWindow).GetMethod("OnLoaded", Private | BindingFlags.DeclaredOnly)!);
        world.Loaded -= startup;
        var game = Field<Game>(world, "_game");
        game.SetDirectory(AppContext.BaseDirectory);
        game.Bgm.Enabled = false;
        world.Show();
        world.LoadGame(path);
        Field<WorldMapHost>(world, "_host").Paused = true;
        Check(Field<bool>(world, "_started"), "Actual game map starts successfully");
        string version = Assembly.GetEntryAssembly()!.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        Check(world.Title.Contains(version), "Game title matches entry assembly version");
        return world;
    }
    static void CheckLoaded(WorldMapWindow world, int[] abilities, int[] skills, int[] tongues)
    {
        var player = Field<Game>(world, "_game").Player;
        Same(player.Abilities, abilities, "Actual WorldMapWindow.LoadGame restores edited abilities");
        SameLevels(player, skills, tongues, "Actual game load restores edited levels");
        var dialog = Dialog(player);
        Check(Field<TextBox[]>(dialog, "_fields").Select(x => x.Text).SequenceEqual(abilities.Take(Ability.Shown).Select(x => Ability.Display(x).ToString())), "Reopened editor displays persisted abilities");
        Same(Field<ComboBox[]>(dialog, "_skillFields").Select(x => x.SelectedIndex), skills, "Reopened editor displays 13 persisted skills");
        Same(Field<ComboBox[]>(dialog, "_tongueFields").Select(x => x.SelectedIndex), tongues, "Reopened editor displays 14 persisted languages");
        dialog.Close();
    }
    static void CloseWorld(WorldMapWindow w)
    {
        // Shutdown disposes the real window without waiting for its optional farewell animation.
        Application.Current.Shutdown();
    }

    // Exercise the real callback with the same persistent windows as a normal town visit.
    // Close only windows created by this invocation, so a rejected editor cannot hang the harness.
    static bool TryEditor(WorldMapWindow world, Action<PlayerAbilityEditDialog>? edit = null)
    {
        var before = Application.Current.Windows.Cast<Window>().ToHashSet();
        bool opened = false;
        Exception? error = null;
        var pending = world.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var editor = Application.Current.Windows.OfType<PlayerAbilityEditDialog>().SingleOrDefault();
                opened = editor != null;
                if (editor != null) edit?.Invoke(editor);
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                foreach (var window in Application.Current.Windows.Cast<Window>().Where(w => !before.Contains(w)).ToArray())
                    window.Close();
            }
        }, DispatcherPriority.ApplicationIdle);
        try { typeof(WorldMapWindow).GetMethod("EditPlayerAbilities", Private)!.Invoke(world, null); }
        finally { pending.Abort(); }
        if (error != null) throw error;
        return opened;
    }

    static void CheckEditorAvailability(WorldMapWindow world, bool reproduce = false)
    {
        var game = Field<Game>(world, "_game");
        var host = Field<WorldMapHost>(world, "_host");
        var asking = typeof(WorldMapWindow).GetField("_asking", Private)!;
        host.Paused = true;
        var city = CityPicView.Open(world, game, 0, game.CityName(0))
            ?? throw new InvalidOperationException("Town fixture needs the game's city assets");
        try
        {
            asking.SetValue(world, false);
            Check(TryEditor(world), "Editor opens in the starting/resumed town state");
            asking.SetValue(world, true); // EnterCity/CheckPort retain this until the city closes.
            bool townOpens = TryEditor(world);
            asking.SetValue(world, false);
            var menu = MenuWindow.ShowCentered(city, new TextBlock { Text = "시설 메뉴" });
            var photo = BuildingPhotoWindow.Show(city, new uint[320 * 240], [], 1, new Point(100, 100))!;
            bool menuOpens = TryEditor(world);
            if (reproduce)
            {
                Console.WriteLine($"REPRODUCTION ordinary-arrival editor opened={townOpens}; building-menu editor opened={menuOpens}");
                return;
            }
            Check(townOpens, "Editor opens after arrival while the town's pause flag remains set");
            Check(menuOpens, "Building menu and photo do not block the editor");
            asking.SetValue(world, true);
            Check(TryEditor(world, editor => Check(editor.Owner == city, "Town editor is owned by the active city")),
                "Editor opens with town pause, building menu and photo together");
            Check(Field<bool>(world, "_asking") && host.Paused, "Cancel preserves town pause and encounter suppression");
            int[] before = game.Player.Abilities.ToArray();
            Check(TryEditor(world, editor => { Fill(editor, "2"); Apply(editor); }), "Apply works during a normal town visit");
            Check(game.Player.Abilities[0] == 1 && game.Unsaved, "Town edit changes the current player and marks it unsaved");
            Check(Field<bool>(world, "_asking") && host.Paused, "Apply preserves town pause and encounter suppression");
            game.Player.SetAbilities(before);

            var talk = ConfirmDialog.Hold(menu, "검증용 대화");
            Check(!TryEditor(world), "A real dialogue nested under a building menu still blocks editing");
            talk.Close();
            Check(TryEditor(world), "Editor becomes available immediately after the dialogue closes");
            var battle = new GameWindow { Owner = city, Width = 200, Height = 100, Content = new TextBlock { Text = "Busy game window" } };
            battle.Show();
            Check(!TryEditor(world), "Other active game windows remain blocked, including nonmodal scenes");
            battle.Close();
            Check(TryEditor(world), "Closing a blocking game window restores editor access");
            photo.Close();
            menu.Close();
        }
        finally { city.Close(); asking.SetValue(world, false); }
        asking.SetValue(world, true);
        Check(!TryEditor(world), "Busy map without a city still blocks editing");
        asking.SetValue(world, false);
        Check(TryEditor(world), "Map editor is available again after the busy state ends");
    }

    [STAThread]
    static int Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Root);
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            // Read settings normally, but configure this test process in memory without calling settings writers.
            _ = GameSettings.BgmEnabled;
            var settings = (GameSettingsData)typeof(GameSettings).GetField("_data", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            settings.BgmEnabled = false;
            settings.SfxEnabled = false;
            settings.PlayOpeningMovie = false;
            settings.Language = "ko";
            int[] finalSkills = Enumerable.Range(0, Skill.Names.Length).Select(i => i % 4).ToArray();
            int[] finalTongues = Enumerable.Range(0, Skill.Languages.Length).Select(i => (i + 2) % 4).ToArray();
            finalSkills[Skill.Sailing] = 3;
            int[] finalAbilities = [84, 99, 72, 87, 91, 41];
            string savePath = Path.Combine(Root, "ability-roundtrip.CDS");
            if (args.Contains("--verify-only"))
            {
                Check(File.Exists(savePath), "Separate process opens the earlier test save");
                var verifyWorld = LoadWorld(savePath);
                CheckLoaded(verifyWorld, finalAbilities, finalSkills, finalTongues);
                CloseWorld(verifyWorld);
                return 0;
            }
            var player = new Player { Name = "에디터 검증", Age = 25 };
            int[] original = [50, 50, 50, 50, 50, 41];
            int[] originalSkills = Enumerable.Repeat(2, Skill.Names.Length).ToArray();
            int[] originalTongues = Enumerable.Repeat(1, Skill.Languages.Length).ToArray();
            player.SetAbilities(original);
            foreach (string name in Skill.Names) player.SetSkill(name, 2);
            foreach (string name in Skill.Languages) player.SetTongue(name, 1);
            player.SetSkill("custom-skill", 2);
            player.SetTongue("custom-language", 1);

            var current = Dialog(player);
            Check(Field<TextBox[]>(current, "_fields").All(x => x.Text == "51"), "Editor starts with current abilities, not a hardcoded value of 1");
            Same(Field<ComboBox[]>(current, "_skillFields").Select(x => x.SelectedIndex), originalSkills, "Editor starts with current skills");
            Same(Field<ComboBox[]>(current, "_tongueFields").Select(x => x.SelectedIndex), originalTongues, "Editor starts with current languages");
            current.Close();

            var cancel = Dialog(player);
            Fill(cancel, "100", "99", "98", "97", "96");
            Levels(cancel, "_skillFields", finalSkills);
            Levels(cancel, "_tongueFields", finalTongues);
            cancel.DialogResult = false;
            Same(player.Abilities, original, "Cancel preserves all six stored abilities");
            SameLevels(player, originalSkills, originalTongues, "Cancel preserves all levels");

            foreach (string invalid in new[] { "0", "101", "-1", "abc", "", "1.5", "999999999999" })
            {
                var bad = Dialog(player);
                Fill(bad, "100", "99", invalid, "98", "97");
                Levels(bad, "_skillFields", finalSkills);
                Levels(bad, "_tongueFields", finalTongues);
                Apply(bad);
                Same(player.Abilities, original, "Atomic rejection: '" + invalid + "'");
                SameLevels(player, originalSkills, originalTongues, "Invalid ability leaves all levels unchanged");
                Check(Field<TextBlock>(bad, "_error").Text.Contains("무력"), "Error identifies invalid ability");
                bad.Close();
            }

            foreach (string group in new[] { "_skillFields", "_tongueFields" })
            {
                var bad = Dialog(player);
                Fill(bad, "100");
                Levels(bad, "_skillFields", finalSkills);
                Levels(bad, "_tongueFields", finalTongues);
                Field<ComboBox[]>(bad, group)[^1].SelectedIndex = -1;
                Apply(bad);
                Same(player.Abilities, original, "Invalid level leaves abilities unchanged: " + group);
                SameLevels(player, originalSkills, originalTongues, "Invalid level leaves every tab unchanged");
                Check(Field<TabControl>(bad, "_tabs").SelectedIndex == (group == "_skillFields" ? 1 : 2), "Invalid hidden tab is selected");
                bad.Close();
            }

            Player.AbilityBonus = (_, i) => i == 0 ? 15 : 0;
            var bonus = Dialog(player);
            Fill(bonus, "95");
            Check(Field<TextBlock[]>(bonus, "_effective")[0].Text == "100", "Bonus preview is capped at 100");
            Apply(bonus);
            Check(player.Abilities[0] == 94 && Ability.Display(player.AbilityOf(0)) == 100, "Base value excludes bonus");
            player.SetAbilities(original);
            Player.AbilityBonus = (_, _) => 0;

            var changed = Dialog(player);
            Fill(changed, "1", "100", "73", "88", "92");
            int[] expectedSkills = Enumerable.Range(0, Skill.Names.Length).Select(i => i % 4).ToArray();
            Levels(changed, "_skillFields", expectedSkills);
            Levels(changed, "_tongueFields", finalTongues);
            Apply(changed);
            int[] expected = [0, 99, 72, 87, 91, 41];
            Same(player.Abilities, expected, "Apply stores display minus one and preserves hidden faith");
            SameLevels(player, expectedSkills, finalTongues, "Apply commits levels including zero and three");
            Check(player.LevelOf("custom-skill") == 2 && player.TongueOf("custom-language") == 1, "Unknown dictionary entries are preserved");
            Check(changed.DialogResult == true, "Changed dialog reports a committed edit");
            var unchanged = Dialog(player);
            Apply(unchanged);
            Check(unchanged.DialogResult == false, "Unchanged dialog reports no edit");

            Check(GameSave.Save(player, path: savePath).Length == 0, "Production GameSave.Save writes test save");
            using (var json = JsonDocument.Parse(File.ReadAllText(savePath)))
                Same(json.RootElement.GetProperty("Abilities").EnumerateArray().Select(x => x.GetInt32()), expected, "On-disk JSON retains all six exact stored values");
            var saved = GameSave.Load(savePath)!;
            Check(saved != null, "Production GameSave.Load reads test save");
            Same(saved!.Abilities!, expected, "Deserialized abilities match edit");
            Same(Skill.Names.Select(x => saved.Skills.GetValueOrDefault(x)), expectedSkills, "Serialized skills match all 13 edits");
            Same(Skill.Languages.Select(x => saved.Tongues!.GetValueOrDefault(x)), finalTongues, "Serialized languages match all 14 edits");

            // Use the actual game's LoadGame/StartMap restoration, renderer and existing assets.
            var world = LoadWorld(savePath);
            var game = Field<Game>(world, "_game");
            CheckLoaded(world, expected, expectedSkills, finalTongues);

            CheckEditorAvailability(world, args.Contains("--reproduce-guard"));
            if (args.Contains("--reproduce-guard")) { CloseWorld(world); return 0; }

            // Exercise the actual development-menu callback and its modal pause / unsaved handling.
            game.Unsaved = false;
            var host = Field<WorldMapHost>(world, "_host");
            host.Paused = false;
            Check(TryEditor(world, dialog =>
            {
                Check(host.Paused, "Game time is paused while editing");
                Fill(dialog, "85");
                Levels(dialog, "_skillFields", finalSkills);
                Apply(dialog);
            }), "Development-menu callback opens the editor on the map");
            Check(!host.Paused, "Game pause state is restored after applying");
            host.Paused = true;
            Check(game.Unsaved, "Development-menu callback marks edited game unsaved");
            Same(game.Player.Abilities, finalAbilities, "Menu callback commits current player's abilities");
            SameLevels(game.Player, finalSkills, finalTongues, "Menu callback commits current player's levels");
            // Use the same pre-save callbacks as Game.Save, then redirect only the serializer's destination.
            game.BeforeSave?.Invoke();
            Check(!Aging.OnSave(game.Player, game.Random), "Normal pre-save callbacks preserve the 25-year-old fixture's edits");
            Check(GameSave.Save(game.Player, path: savePath).Length == 0, "Menu edits saved through production serializer to isolated file");
            world.LoadGame(savePath);
            host.Paused = true;
            CheckLoaded(world, finalAbilities, finalSkills, finalTongues);

            // Keep the production world available for direct visual and menu interaction checks.
            if (args.Contains("--interactive"))
            {
                Field<WorldMapHost>(world, "_host").Paused = true;
                world.Closed += (_, _) => app.Shutdown();
                File.WriteAllText(Path.Combine(Root, "interactive-ready.txt"), savePath);
                app.Run();
            }
            else CloseWorld(world);
            File.WriteAllLines(Path.Combine(Root, "checks.log"), Results);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            File.WriteAllText(Path.Combine(Root, "failure.txt"), ex.ToString());
            return 1;
        }
        finally { app.Shutdown(); }
    }
}
