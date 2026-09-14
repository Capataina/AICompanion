extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Hud = live::AICompanion.Companion.HeadsUpDisplay.CompanionHealthBar;
using Describe = live::AICompanion.Companion.HeadsUpDisplay.DescribeCompanionHud;
using Symbol = live::AICompanion.Companion.HeadsUpDisplay.HudSymbol;
using Snapshot = live::AICompanion.Companion.Brain.Infrastructure.Grants.ActivitySnapshot;
using Phase = live::AICompanion.Companion.Brain.Infrastructure.Selection.ActivityPhase;
using Family = live::AICompanion.Companion.Brain.Infrastructure.Selection.PurposeFamily;

internal static class VerifyCompanionHud
{
    private static readonly (Family Family, string Activity)[] Activities =
    {
        (Family.Gathering, "mine"), (Family.Gathering, "chop"),
        (Family.Combat, "guard"), (Family.Combat, "hunt"),
        (Family.NearbyAssistance, "place-torches"), (Family.NearbyAssistance, "collect"),
        (Family.NearbyAssistance, "keep-company"),
    };

    private static Snapshot Active(Family family, string activity)
        => new(1, 1, family, activity, Phase.Executing, false, false, false, false);

    public static void Verify()
    {
        var registered = new live::AICompanion.Companion.Brain.Infrastructure.Selection.Chooser().Actions;
        Require(registered.Count == Activities.Length && Activities.All(pair =>
            registered.Any(action => action.Family == pair.Family && action.Name == pair.Activity)),
            "HUD coverage must match every registered ordinary activity");
        var icons = Activities.Select(pair => Describe.Activity(Active(pair.Family, pair.Activity))).ToArray();
        Require(icons.All(icon => icon.Symbol != Symbol.None && !icon.Subdued) && icons.Select(icon => icon.Symbol).Distinct().Count() == Activities.Length,
            "each ordinary activity needs a distinct visible symbol");
        Require(Enum.GetValues<Family>().Select(family => Describe.Family(Active(family, "mine")).Symbol).Distinct().Count() == 3,
            "the three families need distinct symbols");
        var mine = Active(Family.Gathering, "mine");
        foreach (var paused in new[] { mine with { SafetyActive = true }, mine with { Recovering = true }, mine with { Phase = Phase.Suspended } })
            Require(Describe.Activity(paused).Subdued && Describe.Activity(paused).Symbol == Describe.Activity(mine).Symbol
                && Describe.Family(paused) == Describe.Family(mine), "interruption must retain family and dim the same activity");
        Require(Describe.Activity(default).Symbol == Symbol.None && Describe.Family(default).Symbol == Symbol.None
            && Describe.Activity(mine with { Downed = true }).Symbol == Symbol.None && Describe.Family(mine with { Downed = true }).Symbol == Symbol.None,
            "absence and downing must not invent ordinary work");
    }

    public static void Render(GraphicsDevice graphics, SpriteBatch batch,
        Point size, float scale, string output, string suffix)
    {
        Verify();
        var companion = (live::AICompanion.Companion.CharacterBody.CompanionNPC)Main.npc[0].ModNPC;
        var save = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>();
        var original = companion.Brain.Presentation;
        bool wasDowned = companion.IsDowned;
        int originalLife = companion.NPC.life;
        Vector2? savedPosition = save.HealthBarPosition;
        bool menu = Main.gameMenu;
        var property = companion.Brain.GetType().GetProperty("Presentation")!;
        var hud = new Hud();
        var draw = typeof(Hud).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var states = Activities.Select(pair => Active(pair.Family, pair.Activity)).ToList();
        states.Add(states[0] with { Phase = Phase.Suspended, SafetyActive = true });
        states.Add(states[6] with { Phase = Phase.Suspended, Recovering = true });
        states.Add(default);
        states.Add(states[0] with { Downed = true, Phase = Phase.Suspended });
        Main.screenWidth = size.X; Main.screenHeight = size.Y;
        Main.gameMenu = false; Main.mouseX = Main.mouseY = -100;
        Main.mouseLeft = Main.mouseRight = false;
        using var target = new RenderTarget2D(graphics, size.X, size.Y);
        var paintedSlots = new List<Rectangle>();
        try
        {
            foreach (Vector2 position in new[] { Vector2.Zero, new Vector2(size.X, size.Y) })
            {
                save.HealthBarPosition = position;
                Require(new Rectangle(0, 0, size.X, size.Y).Contains(Hud.Bounds(save)), "dragged HUD wings must remain on screen");
            }
            graphics.SetRenderTarget(target); graphics.Clear(new Color(18, 27, 40));
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
            for (int i = 0; i < states.Count; i++)
            {
                property.SetValue(companion.Brain, states[i]);
                companion.GetType().GetProperty("IsDowned")!.SetValue(companion, states[i].Downed);
                companion.NPC.life = states[i].Downed ? 1 : i == 2 ? 37 : originalLife;
                save.HealthBarPosition = i == 0 ? null : new Vector2((size.X - 232 * scale) / 2, i * 39 * scale);
                Rectangle health = Hud.HealthBounds(save), left = Hud.IconBounds(health, true, scale), right = Hud.IconBounds(health, false, scale);
                Require(left.Right < health.Left && right.Left > health.Right && left.Center.Y == right.Center.Y
                    && Hud.Bounds(save).Left < left.Left && Hud.Bounds(save).Right > right.Right
                    && Hud.Bounds(save).Contains(left) && Hud.Bounds(save).Contains(right), "family/health/activity geometry must be ordered, padded and captured");
                paintedSlots.Add(left); paintedSlots.Add(right);
                draw.Invoke(hud, null);
            }
            batch.End(); graphics.SetRenderTarget(null);
            var pixels = new Color[size.X * size.Y]; target.GetData(pixels);
            foreach (Rectangle slot in paintedSlots)
                Require(Enumerable.Range(slot.Top, slot.Height).Any(y => Enumerable.Range(slot.Left, slot.Width)
                    .Any(x => pixels[y * size.X + x].R > 60 && pixels[y * size.X + x].G > 60
                        && pixels[y * size.X + x].B > 60)), "a HUD symbol was not actually painted above the dark notch body");
            string file = Path.Combine(output, "Hud-" + suffix + ".png");
            using (var stream = File.Create(file)) target.SaveAsPng(stream, size.X, size.Y);
            Console.WriteLine("RENDER " + file);
            graphics.SetRenderTarget(target);
            batch.Begin();
            VerifyInput(hud, draw, save, size, scale);
            batch.End(); graphics.SetRenderTarget(null);
        }
        finally
        {
            property.SetValue(companion.Brain, original); save.HealthBarPosition = savedPosition; Main.gameMenu = menu;
            companion.GetType().GetProperty("IsDowned")!.SetValue(companion, wasDowned);
            companion.NPC.life = originalLife;
            // The fixture owns the hidden graphics device. Release production's cached masks
            // before that device is disposed; the live mod uses its main-thread unload queue.
            var masks = (System.Collections.IDictionary)typeof(Hud).GetField("masks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (Texture2D texture in masks.Values) texture.Dispose();
            masks.Clear();
            hud.OnWorldUnload();
        }
    }

    private static void VerifyInput(Hud hud, MethodInfo draw,
        live::AICompanion.Companion.PlayerIntegration.CompanionPlayer save, Point size, float scale)
    {
        var card = Terraria.ModLoader.ModContent.GetInstance<live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem>();
        if (card == null)
        {
            card = new live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem();
            Terraria.ModLoader.ContentInstance.Register(card);
            card.Load();
        }
        save.HealthBarPosition = new Vector2((size.X - 232 * scale) / 2, 40 * scale);
        Rectangle left = Hud.IconBounds(Hud.HealthBounds(save), true, scale);
        Main.mouseX = left.Center.X; Main.mouseY = left.Center.Y;
        Main.mouseLeft = Main.mouseLeftRelease = true;
        draw.Invoke(hud, null);
        Main.mouseLeft = false; draw.Invoke(hud, null);
        Require(live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem.IsOpen,
            "clicking the family icon must open the real profile card");
        card.OnWorldUnload();
        Rectangle right = Hud.IconBounds(Hud.HealthBounds(save), false, scale);
        Main.mouseX = right.Center.X; Main.mouseY = right.Center.Y;
        Main.mouseLeft = Main.mouseLeftRelease = true; draw.Invoke(hud, null);
        Main.mouseX = size.X + 100; Main.mouseY = size.Y + 100; draw.Invoke(hud, null);
        Require(new Rectangle(0, 0, size.X, size.Y).Contains(Hud.Bounds(save)), "dragging by the activity icon must keep both wings visible");
        Main.mouseLeft = false; draw.Invoke(hud, null);
        Require(!live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem.IsOpen,
            "releasing a drag must not open the profile card");
        Rectangle box = Hud.Bounds(save);
        Main.mouseX = box.Center.X; Main.mouseY = box.Center.Y;
        Main.mouseRight = Main.mouseRightRelease = true; draw.Invoke(hud, null);
        Require(save.HealthBarPosition == null, "right-click must restore docking");
        Main.mouseRight = false; Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("native HUD input: icon click, drag containment and redocking");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
