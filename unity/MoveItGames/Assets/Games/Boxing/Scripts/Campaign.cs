using System;

namespace MoveIt.Games.Boxing
{
    /// <summary>How hard the climb is, chosen before it like ARMS's Grand Prix levels (the other Tundu games' three).</summary>
    public enum BoxingDifficulty { Easy, Medium, Hard }

    /// <summary>
    /// What an opponent throws. The first five are ordinary punches; the rest are a champion's <b>power punches</b> (they
    /// glow and take longer to come; the guard doesn't stop them). Each has one way to survive it, the same every time:
    /// <list type="bullet">
    /// <item>a jab, a cross, a hook: <b>block</b> (gloves up) or <b>dodge</b> (lean; a hook: away from its side or under
    /// it);</item>
    /// <item>an uppercut: comes up between the gloves: <b>dodge</b> it (lean either way);</item>
    /// <item>a body blow: at the belly, a lean doesn't take it away: <b>block</b> it;</item>
    /// <item>the power punches go through the gloves: dodge them, each the way its swing shows.</item>
    /// </list>
    /// </summary>
    public enum Attack
    {
        /// <summary>A quick straight punch at the head: short tell, little damage.</summary>
        Jab,
        /// <summary>The rear hand's straight punch: more power.</summary>
        Cross,
        /// <summary>A swing from a side (<see cref="PlannedAttack.Side"/>): block, or lean away from it, or duck.</summary>
        Hook,
        /// <summary>From below, between the gloves: lean either way.</summary>
        Uppercut,
        /// <summary>At the belly: block it.</summary>
        Body,
        /// <summary>Power punch from a side, a huge hook: lean away or duck.</summary>
        Haymaker,
        /// <summary>A fist launched like a rocket, straight: lean or duck.</summary>
        Rocket,
        /// <summary>Both arms swinging across at head height: duck.</summary>
        Sweep,
        /// <summary>A punch charged with lightning on a side: lean away from it.</summary>
        Lightning,
        /// <summary>A ball of fire thrown at the hero: punch it back (it hits the thrower), or dodge it.</summary>
        Fireball,
    }

    /// <summary>Where an opponent holds its gloves while waiting: nothing covered, the head (hit the body, or uppercut
    /// through it), or the body (hit the head).</summary>
    public enum Guard { Open, High, Low }

    /// <summary>
    /// One opponent: who it is and the one thing it teaches, on top of what its place in the climb makes it
    /// (<see cref="BoxingLevel"/>: the multipliers here are 1 for "as strong as its place says").
    /// </summary>
    public sealed class OpponentProfile
    {
        public string Id = "", Name = "", Title = "";
        /// <summary>Its stage (0 = the gym .. 3 = the stars) and its place there (0, 1, 2 = the stage's champion).</summary>
        public int Stage, Index;
        public bool Champion => Index == Campaign.FightsPerStage - 1;
        public bool Final => Stage == Campaign.Stages.Length - 1 && Champion;
        /// <summary>Its tells' length (more = slower, easier to read).</summary>
        public float Tell = 1f;
        /// <summary>How much its tells' length varies from punch to punch (0 = always the same; the alien's odd rhythm).</summary>
        public float TellJitter = 0.12f;
        /// <summary>How often it attacks.</summary>
        public float Aggression = 1f;
        /// <summary>Its health.</summary>
        public float Toughness = 1f;
        /// <summary>How much its punches hurt.</summary>
        public float Power = 1f;
        /// <summary>Which guard it holds while it waits, as weights.</summary>
        public float OpenGuard = 1f, HighGuard = 1f, LowGuard = 1f;
        /// <summary>How often a punch is followed by another.</summary>
        public float Combo = 1f;
        /// <summary>How often it blocks or slips the hero's punches, how often it punches back after that.</summary>
        public float Defense = 1f, Counter = 1f;
        /// <summary>Of its defences, the share that are slips (the head aside) rather than blocks.</summary>
        public float Slip = 0.3f;
        /// <summary>Its ordinary punches and how often each (relative).</summary>
        public (Attack attack, float weight)[] Moves = { (Attack.Jab, 3f), (Attack.Cross, 1f) };
        /// <summary>Its power punches (a champion's): one of them instead of an ordinary punch now and then.</summary>
        public Attack[] Specials = Array.Empty<Attack>();
        /// <summary>A machine's fixed rhythm: <see cref="Moves"/> in order, again and again, on a steady beat.</summary>
        public bool Rhythm;
        /// <summary>It learns the hero's favourite punch and guards against it (the hero's shadow).</summary>
        public bool Mirror;
        /// <summary>It shows off after landing a punch (open for a moment: a free hit).</summary>
        public float Taunt = 0.25f;
        /// <summary>Drawn this big (1 = the hero's size).</summary>
        public float Scale = 1f;

        /// <summary>Its number in the climb (0 .. <see cref="Campaign.Fights"/> - 1).</summary>
        public int Number => Stage * Campaign.FightsPerStage + Index;
    }

    /// <summary>A place in the climb and the three who fight there.</summary>
    public sealed class Stage
    {
        public string Id = "";
        public OpponentProfile[] Opponents = Array.Empty<OpponentProfile>();
    }

    /// <summary>
    /// The climb (2026-11-18, user: "the superhero goes stage by stage punching stronger and stronger enemies: first in a
    /// box ring, then a roof, then a mountain, then outer space"; 2026-11-19 overhaul, user: "make it fun like the Wii
    /// boxing game, don't over-AI it, keep it well designed"): exactly those four places, from the neighbourhood's gym to
    /// the villain's arena among the stars, three fights each, the third the stage's champion with a power punch.
    /// Each opponent teaches one thing, so the climb is a course a child can follow:
    /// <list type="number">
    /// <item><b>the gym</b>: Beni (punch, block), Rina (a high guard: go to the body, or uppercut), Leka (the Haymaker:
    /// dodge);</item>
    /// <item><b>the roof</b> at sunset: Robo-Tik (a fixed rhythm to read), Magneta (a low guard and body blows: block
    /// them, punch the head), Robo-Bosi (the Rocket Fist);</item>
    /// <item><b>the mountain</b>: Ujku (combos of hooks), Shqiponja (slips and counters: wait, dodge, then hit), Jeti (the
    /// Sweep: duck);</item>
    /// <item><b>the stars</b>: Gugu (an odd rhythm: watch the arm, not the clock), Hija (the hero's shadow: guards against
    /// the hero's favourite punch), Doktor Errësira (fireballs to punch back, lightning).</item>
    /// </list>
    /// How strong each one is comes from its place and the difficulty (<see cref="BoxingLevel.StrengthOf"/>).
    /// </summary>
    public static class Campaign
    {
        public const int FightsPerStage = 3;

        static (Attack, float)[] M(params (Attack, float)[] moves) => moves;

        public static readonly Stage[] Stages =
        {
            new Stage
            {
                Id = "gym",
                Opponents = new[]
                {
                    // the friendly sparring partner: slow jabs, a guard that's mostly down
                    new OpponentProfile { Id = "beni", Name = "Beni", Title = "Topi", Tell = 1.3f, Aggression = 0.7f, Toughness = 0.8f,
                        OpenGuard = 2f, HighGuard = 1f, LowGuard = 0f, Combo = 0f, Defense = 0.3f, Counter = 0.2f,
                        Moves = M((Attack.Jab, 3f), (Attack.Cross, 1f)), Taunt = 0.1f },
                    // quick jabs behind a high guard: hit the belly, or uppercut
                    new OpponentProfile { Id = "rina", Name = "Rina", Title = "e Shpejta", Tell = 1.0f, Aggression = 1.1f, Toughness = 0.9f,
                        OpenGuard = 0.4f, HighGuard = 3f, LowGuard = 0f, Defense = 0.9f, Counter = 0.8f,
                        Moves = M((Attack.Jab, 4f), (Attack.Cross, 1f), (Attack.Body, 1f)) },
                    // the gym's champion: hooks from both sides and the big swing
                    new OpponentProfile { Id = "leka", Name = "Leka", Title = "Kampioni", Toughness = 1.1f, Power = 1.1f,
                        Moves = M((Attack.Jab, 2f), (Attack.Cross, 1f), (Attack.Hook, 2f)), Specials = new[] { Attack.Haymaker },
                        Taunt = 0.5f, Scale = 1.05f },
                },
            },
            new Stage
            {
                Id = "roof",
                Opponents = new[]
                {
                    // a little robot that ticks: jab, jab, hook, jab, jab, uppercut, always the same, on a beat
                    new OpponentProfile { Id = "robotik", Name = "Robo-Tik", Title = "Ora", Rhythm = true, Taunt = 0f, Combo = 0f,
                        Defense = 0.4f, Counter = 0.3f, TellJitter = 0f, OpenGuard = 0.6f, HighGuard = 1.5f, LowGuard = 0.5f,
                        Moves = M((Attack.Jab, 1f), (Attack.Jab, 1f), (Attack.Hook, 1f), (Attack.Jab, 1f), (Attack.Jab, 1f), (Attack.Uppercut, 1f)) },
                    // a magnet robot with a low guard: body blows (block them), its head open
                    new OpponentProfile { Id = "magneta", Name = "Magneta", Title = "Magneti", HighGuard = 0.3f, LowGuard = 3f, OpenGuard = 0.6f,
                        Moves = M((Attack.Body, 3f), (Attack.Cross, 1f), (Attack.Hook, 1f)) },
                    // the robots' boss: rocket fists
                    new OpponentProfile { Id = "robobos", Name = "Robo-Bosi", Title = "i Hekurt", Toughness = 1.2f, Power = 1.1f, Tell = 1.05f,
                        Defense = 0.6f, Moves = M((Attack.Cross, 2f), (Attack.Hook, 2f), (Attack.Body, 1f)), Specials = new[] { Attack.Rocket },
                        Taunt = 0.2f, Scale = 1.15f },
                },
            },
            new Stage
            {
                Id = "mountain",
                Opponents = new[]
                {
                    // the wolf: combos of hooks from both sides (block, or lean away from them, or duck)
                    new OpponentProfile { Id = "ujku", Name = "Ujku", Title = "i Malit", Aggression = 1.15f, Combo = 1.6f,
                        Moves = M((Attack.Hook, 3f), (Attack.Jab, 2f)) },
                    // the eagle: slips punches and hits back (wait for its punch, dodge it, then hit)
                    new OpponentProfile { Id = "shqiponja", Name = "Shqiponja", Title = "e Bjeshkëve", Defense = 1.7f, Counter = 1.5f,
                        Slip = 0.75f, OpenGuard = 1.4f, Moves = M((Attack.Jab, 2f), (Attack.Cross, 2f), (Attack.Uppercut, 1f)) },
                    // the yeti: big, tough, its arms swing across: duck
                    new OpponentProfile { Id = "jeti", Name = "Jeti", Title = "Gjigandi i Borës", Toughness = 1.35f, Power = 1.2f, Tell = 1.1f,
                        Aggression = 0.9f, Defense = 0.6f, Moves = M((Attack.Cross, 2f), (Attack.Hook, 2f), (Attack.Uppercut, 1f)),
                        Specials = new[] { Attack.Sweep }, Scale = 1.3f },
                },
            },
            new Stage
            {
                Id = "stars",
                Opponents = new[]
                {
                    // a little alien with an odd rhythm: its tells come short or long (watch the arm, not the clock)
                    new OpponentProfile { Id = "gugu", Name = "Gugu", Title = "nga Hëna", TellJitter = 0.38f, Aggression = 1.1f,
                        Moves = M((Attack.Jab, 2f), (Attack.Hook, 1f), (Attack.Uppercut, 2f), (Attack.Body, 1f)) },
                    // the hero's shadow: guards against the hero's favourite punch (mix them up)
                    new OpponentProfile { Id = "hija", Name = "Hija", Title = "e Heroit", Mirror = true, Defense = 1.3f, Counter = 1.4f,
                        Moves = M((Attack.Jab, 2f), (Attack.Cross, 2f), (Attack.Hook, 2f), (Attack.Uppercut, 1f), (Attack.Body, 1f)) },
                    // the villain at the top: fireballs to punch back, lightning
                    new OpponentProfile { Id = "erresira", Name = "Doktor Errësira", Title = "", Toughness = 1.4f, Power = 1.2f, Defense = 1.1f,
                        Moves = M((Attack.Jab, 1f), (Attack.Cross, 2f), (Attack.Hook, 2f), (Attack.Uppercut, 1f)),
                        Specials = new[] { Attack.Fireball, Attack.Lightning }, Taunt = 0.5f, Scale = 1.2f },
                },
            },
        };

        public static int Fights => Stages.Length * FightsPerStage;

        static Campaign()
        {
            for (int s = 0; s < Stages.Length; s++)
                for (int i = 0; i < Stages[s].Opponents.Length; i++)
                {
                    Stages[s].Opponents[i].Stage = s;
                    Stages[s].Opponents[i].Index = i;
                }
        }

        /// <summary>The opponent of fight <paramref name="number"/> (0 = the gym's first).</summary>
        public static OpponentProfile Opponent(int number)
        {
            number = Math.Max(0, Math.Min(Fights - 1, number));
            return Stages[number / FightsPerStage].Opponents[number % FightsPerStage];
        }

        /// <summary>The opponent with id <paramref name="id"/> (null when there's none).</summary>
        public static OpponentProfile Find(string id)
        {
            foreach (var s in Stages)
                foreach (var o in s.Opponents)
                    if (o.Id == id) return o;
            return null;
        }

        public static bool IsSpecial(Attack a) => a >= Attack.Haymaker;

        /// <summary>Attacks that come from a side (<see cref="PlannedAttack.Side"/> matters).</summary>
        public static bool FromSide(Attack a) => a is Attack.Hook or Attack.Haymaker or Attack.Lightning;
    }
}
