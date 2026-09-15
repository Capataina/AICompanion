extern alias live;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Terraria;
using Hud = live::AICompanion.Companion.HeadsUpDisplay.CompanionHealthBar;
using Primitives = live::AICompanion.Companion.ProfileCard.DrawCardPrimitives;

/// <summary>
/// The HUD notch through its own drawing: three bars with even padding and nothing else inside or under the
/// notch, each bar's fill read back off the pixels against a pinned fraction, a downed notch that no longer
/// looks healthy, and the click, drag and right-click contract.
/// </summary>
internal static class VerifyCompanionHud
{
    private static readonly Color Background = new(18, 27, 40);
    private static readonly Color Body = new(18, 18, 21);
    private static readonly Color Track = new(44, 44, 50);

    public static void Render(GraphicsDevice graphics, SpriteBatch batch, Point size, float scale, string output, string suffix)
    {
        var companion = (live::AICompanion.Companion.CharacterBody.CompanionNPC)Main.npc[0].ModNPC;
        var save = Main.LocalPlayer.GetModPlayer<live::AICompanion.Companion.PlayerIntegration.CompanionPlayer>();
        bool wasDowned = companion.IsDowned;
        int originalLife = companion.NPC.life;
        Vector2? savedPosition = save.HealthBarPosition;
        bool menu = Main.gameMenu;
        var downed = companion.GetType().GetProperty("IsDowned")!;
        // Mana, experience and life are pinned at fractions the pixels can be counted against: a half pool, a
        // quarter of the second level, three quarters of life. Mana and experience live on private setters, so
        // the fixture writes them the way it writes the downed flag, and restores them.
        var mana = companion.Mana;
        var manaCurrent = mana.GetType().GetProperty("Current")!;
        float originalMana = (float)manaCurrent.GetValue(mana)!;
        var experience = save.Experience;
        var experienceTotal = experience.GetType().GetProperty("Total")!;
        int originalExperience = (int)experienceTotal.GetValue(experience)!;
        manaCurrent.SetValue(mana, mana.Max / 2f);
        experienceTotal.SetValue(experience, 150);
        companion.NPC.life = companion.NPC.lifeMax * 3 / 4;
        // Downed, the health bar fills with revival progress. At none it draws no fill at all, and "no coloured fill"
        // then holds whatever colour the fill would have had, so the downed scene is pinned half revived.
        var reviveField = companion.GetType().GetField("reviveProgress", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int originalRevive = (int)reviveField.GetValue(companion)!;
        int reviveTicks = (int)companion.GetType().GetField("ReviveTicks", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(null)!;
        reviveField.SetValue(companion, reviveTicks / 2);
        Require(Math.Abs(mana.Fraction - 0.5f) < 0.001f && experience.Level == 2 && Math.Abs(experience.Fraction - 0.25f) < 0.001f,
            "the pinned mana and experience fractions must be what the bars are asked to draw");
        var hud = new Hud();
        var draw = typeof(Hud).GetMethod("Draw", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Main.screenWidth = size.X; Main.screenHeight = size.Y;
        Main.gameMenu = false; Main.mouseX = Main.mouseY = -100;
        Main.mouseLeft = Main.mouseRight = false;
        using var target = new RenderTarget2D(graphics, size.X, size.Y);
        try
        {
            // Three notches on one sheet: docked and healthy at the top edge, free and healthy down the left, free
            // and downed down the right. A free position past any edge must be clamped onto the screen.
            foreach (Vector2 position in new[] { new Vector2(-500, -500), new Vector2(size.X * 2, size.Y * 2) })
            {
                save.HealthBarPosition = position;
                Require(new Rectangle(0, 0, size.X, size.Y).Contains(Hud.Bounds(save)), $"a free notch dragged to {position} must stay on screen");
            }
            var scenes = new (Vector2? Position, bool Downed)[]
            {
                (null, false),
                (new Vector2(0, 90 * scale), false),
                (new Vector2(size.X, 90 * scale), true),
            };
            var boxes = new Rectangle[scenes.Length];
            graphics.SetRenderTarget(target); graphics.Clear(Background);
            batch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp,
                DepthStencilState.None, RasterizerState.CullNone, null, Matrix.Identity);
            for (int i = 0; i < scenes.Length; i++)
            {
                save.HealthBarPosition = scenes[i].Position;
                downed.SetValue(companion, scenes[i].Downed);
                boxes[i] = Hud.Bounds(save);
                RequireEvenGeometry(boxes[i], scale, i);
                draw.Invoke(hud, null);
            }
            batch.End(); graphics.SetRenderTarget(null);
            var pixels = new Color[size.X * size.Y]; target.GetData(pixels);
            Color At(int x, int y) => pixels[y * size.X + x];

            // Every bar's fill, counted along its centre row as the pixels that are no longer the track: a fill at the
            // wrong fraction, or not drawn, counts wrong. A rounded fill's two end pixels blend into the track and may
            // fail the test, which is the slack allowed.
            bool NotTrack(Color c) => Math.Abs(c.R - Track.R) > 30 || Math.Abs(c.G - Track.G) > 30 || Math.Abs(c.B - Track.B) > 30;
            int Filled(Rectangle bar) => Enumerable.Range(bar.Left, bar.Width).Count(x => NotTrack(At(x, bar.Center.Y)));
            var docked = Hud.Bars(boxes[0], scale);
            foreach (var (bar, fraction, name) in new[] { (docked.Health, .75f, "health"), (docked.Mana, .5f, "mana"), (docked.Experience, .25f, "experience") })
            {
                int painted = Filled(bar), expected = (int)(bar.Width * fraction);
                Require(Math.Abs(painted - expected) <= 3, $"the {name} bar painted {painted} fill pixels where {fraction:P0} of {bar.Width} is {expected}");
            }
            var healthBlue = At(docked.Mana.X + 2, docked.Mana.Center.Y);
            Require(healthBlue.B > 200 && healthBlue.R < 160, $"the mana fill must be the game's mana blue; read {healthBlue}");
            var gold = At(docked.Experience.X + 2, docked.Experience.Center.Y);
            Require(gold.R > 200 && gold.G > 160 && gold.B < 130, $"the experience fill must be gold; read {gold}");

            // The notch carries nothing but its three bars. Inside the body, away from its rounded corners, every
            // pixel outside the bars must be the body's own colour; and the band under each notch must be untouched
            // background. A name, a level, a reading, an icon or a status label drawn anywhere there fails this.
            for (int i = 0; i < scenes.Length; i++)
            {
                Rectangle box = boxes[i];
                var bars = Hud.Bars(box, scale);
                int inset = (int)(12 * scale) + 2, strays = 0;
                for (int y = box.Top + 2; y < box.Bottom - inset; y++)
                    for (int x = box.Left + inset; x < box.Right - inset; x++)
                    {
                        var p = new Point(x, y);
                        if (Inflate(bars.Health).Contains(p) || Inflate(bars.Mana).Contains(p) || Inflate(bars.Experience).Contains(p)) continue;
                        Color c = At(x, y);
                        if (Math.Abs(c.R - Body.R) > 12 || Math.Abs(c.G - Body.G) > 12 || Math.Abs(c.B - Body.B) > 12) strays++;
                    }
                int below = 0;
                for (int y = box.Bottom + 2; y < Math.Min(size.Y, box.Bottom + (int)(24 * scale)); y++)
                    for (int x = Math.Max(0, box.Left - 20); x < Math.Min(size.X, box.Right + 20); x++)
                        if (At(x, y) != Background) below++;
                Require(strays == 0 && below == 0, $"notch {i} drew something besides its bars: {strays} pixels inside the body, {below} under it");
            }
            // Downed, the health bar is grey and fills with revival: the fill runs the revival fraction of the bar, and none
            // of it is green or red.
            var down = Hud.Bars(boxes[2], scale).Health;
            int percent = companion.RevivePercent;
            Require(percent >= 45 && percent <= 55, $"premise: the downed scene must be about half revived; it reads {percent}%");
            int greyFill = Filled(down), revived = (int)(down.Width * percent / 100f);
            int coloured = Enumerable.Range(down.Left, down.Width).Count(x => At(x, down.Center.Y) is var c && Math.Abs(c.R - c.G) > 40);
            Require(Math.Abs(greyFill - revived) <= 3 && coloured == 0,
                $"a downed notch must fill its health bar in grey to the revival fraction; {greyFill} fill pixels where {percent}% of {down.Width} is {revived}, {coloured} coloured");
            Console.WriteLine($"native HUD notch {suffix}: {boxes[0].Width}x{boxes[0].Height}, three bars with even padding, fills read back at 75/50/25%, nothing else drawn in or under the notch, downed health grey");
            string file = Path.Combine(output, "Hud-" + suffix + ".png");
            using (var stream = File.Create(file)) target.SaveAsPng(stream, size.X, size.Y);
            Console.WriteLine("RENDER " + file);
            graphics.SetRenderTarget(target);
            batch.Begin();
            downed.SetValue(companion, false);
            VerifyInput(hud, draw, save, size, scale);
            batch.End(); graphics.SetRenderTarget(null);
        }
        finally
        {
            save.HealthBarPosition = savedPosition; Main.gameMenu = menu;
            downed.SetValue(companion, wasDowned);
            companion.NPC.life = originalLife;
            reviveField.SetValue(companion, originalRevive);
            manaCurrent.SetValue(mana, originalMana);
            experienceTotal.SetValue(experience, originalExperience);
            // The fixture owns the hidden graphics device. Release the shared mask cache before that device is
            // disposed; the live mod releases it through its main-thread unload queue.
            var masks = (System.Collections.IDictionary)typeof(Primitives).GetField("masks", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            foreach (Texture2D texture in masks.Values) texture.Dispose();
            masks.Clear();
            hud.OnWorldUnload();
        }
    }

    private static Rectangle Inflate(Rectangle r) { r.Inflate(1, 1); return r; }

    /// <summary>The notch is its bars plus one padding on every side, and the two gaps between bars are equal.</summary>
    private static void RequireEvenGeometry(Rectangle box, float scale, int scene)
    {
        Require(box.Width == (int)(Hud.BaseWidth * scale) && box.Height == (int)(Hud.BaseHeight * scale),
            $"notch {scene} is {box.Width}x{box.Height}, not the base size {Hud.BaseWidth}x{Hud.BaseHeight} at scale {scale}");
        var (health, mana, experience) = Hud.Bars(box, scale);
        int left = health.Left - box.Left, right = box.Right - health.Right, top = health.Top - box.Top, bottom = box.Bottom - experience.Bottom;
        int gap1 = mana.Top - health.Bottom, gap2 = experience.Top - mana.Bottom;
        Require(health.Width == mana.Width && mana.Width == experience.Width && health.Left == mana.Left && mana.Left == experience.Left,
            $"notch {scene}: the three bars must share one column");
        Require(left == right && Math.Abs(top - bottom) <= 1 && Math.Abs(top - left) <= 1 && gap1 == gap2 && gap1 > 0,
            $"notch {scene}: padding must be even and the gaps equal; left {left} right {right} top {top} bottom {bottom} gaps {gap1}/{gap2}");
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
        save.HealthBarPosition = null;
        Rectangle box = Hud.Bounds(save);
        Main.mouseX = box.Center.X; Main.mouseY = box.Center.Y;
        Main.mouseLeft = Main.mouseLeftRelease = true;
        draw.Invoke(hud, null);
        Main.mouseLeft = false; draw.Invoke(hud, null);
        Require(live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem.IsOpen, "clicking the notch must open the real profile card");
        card.OnWorldUnload();
        Main.mouseX = box.Center.X; Main.mouseY = box.Center.Y;
        Main.mouseLeft = Main.mouseLeftRelease = true; draw.Invoke(hud, null);
        Main.mouseX = size.X + 100; Main.mouseY = size.Y + 100; draw.Invoke(hud, null);
        Require(save.HealthBarPosition != null && new Rectangle(0, 0, size.X, size.Y).Contains(Hud.Bounds(save)),
            "dragging the notch past the screen corner must free it and keep it on screen");
        Main.mouseLeft = false; draw.Invoke(hud, null);
        Require(!live::AICompanion.Companion.ProfileCard.CompanionProfileCardSystem.IsOpen, "releasing a drag must not open the profile card");
        box = Hud.Bounds(save);
        Main.mouseX = box.Center.X; Main.mouseY = box.Center.Y;
        Main.mouseRight = Main.mouseRightRelease = true; draw.Invoke(hud, null);
        Require(save.HealthBarPosition == null, "right-click must restore docking");
        Main.mouseRight = false; Main.mouseX = Main.mouseY = -100;
        Console.WriteLine("native HUD input: click opens the card, a drag frees the notch on screen without opening it, right-click docks");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
