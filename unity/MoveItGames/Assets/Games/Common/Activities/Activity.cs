using MoveIt.Motion;
using UnityEngine;

namespace MoveIt.Games
{
    /// <summary>The medals' colours on the screen.</summary>
    public static class MedalLook
    {
        public static Color Colour(Medal m) => m switch
        {
            Medal.Bronze => new Color(0.86f, 0.55f, 0.3f),
            Medal.Silver => new Color(0.82f, 0.86f, 0.94f),
            Medal.Gold => new Color(1f, 0.79f, 0.24f),
            Medal.Platinum => new Color(0.72f, 0.93f, 1f),
            _ => new Color(1f, 1f, 1f, 0.25f),
        };
    }

    /// <summary>What an activity builds and plays with: the scene's camera and materials, the effects, the player's
    /// pose and the player's figure.</summary>
    public sealed class ActivityKit
    {
        public Camera Cam;
        public Light Sun;
        /// <summary>Tundu/Toon, vertex colours with gloss: characters, coins, props that move.</summary>
        public Material Actor;
        /// <summary>Tundu/Toon, vertex colours, matte: the worlds (one mesh per piece of world).</summary>
        public Material World;
        /// <summary>Tundu/Toon, plain white (coloured by property blocks).</summary>
        public Material Toon;
        public Material Ghost, GhostDepth;
        /// <summary>Tundu/Fx alpha, plain (glass, light shafts, rings).</summary>
        public Material Fx;
        /// <summary>Tundu/Fx alpha, drawn on top of everything (markers).</summary>
        public Material Marker;
        /// <summary>Tundu/Water: the river.</summary>
        public Material Water;
        /// <summary>The sky material (Tundu/Sky), its colours set per activity.</summary>
        public Material Sky;
        public GameFx Fx3;
        public BodyPuppet Puppet;
        public BodyInput Input;
        public BodyGestures Gestures => Input != null ? Input.Gestures : null;
        /// <summary>Where every activity puts its world (destroyed when it ends).</summary>
        public Transform Root;

        /// <summary>The keyboard (or no camera) is in charge.</summary>
        public bool UsingKeys => Input == null || Input.UsingKeyboard || PlayerGate.State(Input) == GateState.NoService;

        /// <summary>Sets the sky, fog and sun for an activity.</summary>
        public void Weather(Color top, Color horizon, Color bottom, Color fog, float fogStart, float fogEnd, Color ambient,
                            Vector3 sunEuler, Color sunColour, float sunIntensity, bool shadows = true)
        {
            if (Sky != null)
            {
                Sky.SetColor("_TopColor", top);
                Sky.SetColor("_HorizonColor", horizon);
                Sky.SetColor("_BottomColor", bottom);
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = fog;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            RenderSettings.ambientLight = ambient;
            if (Cam != null) Cam.backgroundColor = horizon;
            if (Sun != null)
            {
                Sun.transform.rotation = Quaternion.Euler(sunEuler);
                Sun.color = sunColour;
                Sun.intensity = sunIntensity;
                Sun.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            }
        }
    }

    /// <summary>
    /// One activity of an <see cref="ActivityGame"/> (Aventura!'s river, Sporte!'s sprint...). The game builds it (its world under <see cref="ActivityKit.Root"/>), shows its
    /// how-to card and a countdown, then calls <see cref="Step"/> every frame until it is <see cref="Finished"/>. Points are
    /// the activity's own (coins, blocks, leaks); medals come from <see cref="MedalNeeds"/>.
    /// </summary>
    public abstract class Activity
    {
        protected ActivityKit Kit;

        /// <summary>A short name for saving bests and the log ("river").</summary>
        public abstract string Id { get; }
        public abstract string Title { get; }
        /// <summary>One or two lines: what to do with the body.</summary>
        public abstract string HowTo { get; }
        public abstract string KeyboardHelp { get; }
        /// <summary>Where the player must stand (<see cref="PlayerGate.Use"/>).</summary>
        public abstract PlayArea Area { get; }
        public abstract string Music { get; }
        /// <summary>The colour of its tile on the activity screen.</summary>
        public abstract Color Colour { get; }
        /// <summary>Points for bronze, silver, gold and platinum.</summary>
        public abstract int[] MedalNeeds { get; }

        public int Points { get; protected set; }
        public bool Finished { get; protected set; }
        /// <summary>Seconds played (paused time not counted).</summary>
        public float Time { get; protected set; }
        /// <summary>A line for the results screen ("Coins: 54 of 80").</summary>
        public virtual string ResultLine => "";

        public void Build(ActivityKit kit)
        {
            Kit = kit;
            OnBuild();
        }

        protected abstract void OnBuild();

        /// <summary>The countdown is over: play.</summary>
        public virtual void Begin() { }

        /// <summary>Every frame. <paramref name="playing"/> false = the how-to card, the countdown or a pause (keep the
        /// world alive, don't advance the rules).</summary>
        public abstract void Step(float dt, bool playing);

        /// <summary>The activity's own HUD while playing (the game draws the points and the frame around it).</summary>
        public virtual void DrawHud(float width) { }

        /// <summary>Anything that isn't under the root (materials made in code, global shader values).</summary>
        public virtual void Dispose() { }

        /// <summary>-1, 0 or 1 from two held keys (keyboard play).</summary>
        protected static float KeyAxis(UnityEngine.InputSystem.Key negative, UnityEngine.InputSystem.Key positive)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return 0f;
            return (kb[positive].isPressed ? 1f : 0f) - (kb[negative].isPressed ? 1f : 0f);
        }

        protected static bool KeyDown(UnityEngine.InputSystem.Key key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        protected static bool KeyHeld(UnityEngine.InputSystem.Key key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[key].isPressed;
        }
    }
}
