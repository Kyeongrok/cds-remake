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

            // Exercise the actual development-menu callback and its modal pause / unsaved handling.
            game.Unsaved = false;
            var host = Field<WorldMapHost>(world, "_host");
            host.Paused = false;
            Exception? callbackError = null;
            world.Dispatcher.BeginInvoke(() =>
            {
                var dialog = world.OwnedWindows.OfType<PlayerAbilityEditDialog>().Single();
                try
                {
                    Check(host.Paused, "Game time is paused while editing");
                    Fill(dialog, "85");
                    Levels(dialog, "_skillFields", finalSkills);
                    Apply(dialog);
                }
                catch (Exception ex) { callbackError = ex; dialog.Close(); }
            }, DispatcherPriority.ApplicationIdle);
            typeof(WorldMapWindow).GetMethod("EditPlayerAbilities", Private)!.Invoke(world, null);
            if (callbackError != null) throw callbackError;
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
