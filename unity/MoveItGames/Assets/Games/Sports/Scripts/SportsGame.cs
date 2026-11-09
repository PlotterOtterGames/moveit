using System.Collections.Generic;
using UnityEngine;

namespace MoveIt.Games.Sports
{
    /// <summary>
    /// "Sporte!": Kinect Sports events this box didn't have yet (it has Bowling, Tenis! and Volejboll! as games of their
    /// own): the 100 metres (<see cref="TrackActivity"/>) and football (<see cref="FootballActivity"/>). Beach volleyball
    /// was a third until 2026-11-07, when it became its own game with Tenis!'s menu and two players (Volejboll!; it still
    /// plays on the beach venue built here). The flow (the choice of sport, how-to, countdown, medals) is
    /// <see cref="ActivityGame"/>'s. The choice: the chosen sport's venue fills the screen with its athlete waiting in
    /// front (the venues are all loaded, far apart; the camera cuts between them with a quick flash), a card each along
    /// the bottom.
    /// </summary>
    public sealed class SportsGame : ActivityGame
    {
        static readonly Color Sun = SportHud.Sun, Coral = SportHud.Coral, Sky = SportHud.Sky, Night = SportHud.Night;

        [Header("Athletes")]
        [SerializeField, Tooltip("Assets/Games/Sports/Art/Athlete.fbx (art/sports/athlete.py).")] GameObject athletePrefab;
        [SerializeField, Tooltip("Tundu/Athlete.")] Material athleteMaterial;
        [SerializeField, Tooltip("The clips in Athlete.fbx.")] AnimationClip[] athleteClips;
        [Header("Venues")]
        [SerializeField] SportsArt art = new SportsArt();

        protected override void Start()
        {
            Athlete.Register(athletePrefab, athleteMaterial, athleteClips);
            SportsArt.Register(art);
            base.Start();
        }

        /// <summary>The player's own athlete: the same person in every sport, in that sport's kit.</summary>
        public static AthleteLook MyLook(Wear wear)
        {
            var look = AthleteLook.Person(PlayerPrefs.GetInt("sports.me", 2), wear);
            if (look.HairStyle == "Buzz") look.HairStyle = "Crop";
            return look;
        }

        protected override string Title => SportsStrings.Title;
        protected override string ChooseText => SportsStrings.Choose;
        protected override string Prefix => "sports";
        protected override string ChooseMusic => "sports";

        protected override void AddActivities(List<Activity> list)
        {
            list.Add(new TrackActivity());
            list.Add(new FootballActivity());
        }

        // ---------------------------------------------------------------- the choice

        /// <summary>Where each sport's corner of the choice is: the venue's offset, where its athlete stands and faces,
        /// where the camera looks from.</summary>
        static readonly (Venue.Kind kind, Vector3 offset, Vector3 athlete, float yaw, Vector3 cam, Vector3 look)[] Corners =
        {
            (Venue.Kind.Track, new Vector3(3000f, 0f, 0f), new Vector3(-0.61f, 0f, -1.4f), 200f, new Vector3(-2.4f, 1.45f, 2.6f), new Vector3(0.3f, 1.15f, -1.4f)),
            (Venue.Kind.Football, Vector3.zero, new Vector3(1.8f, 0f, 44.5f), 195f, new Vector3(0.2f, 1.5f, 39.8f), new Vector3(2.4f, 1.2f, 44.5f)),
        };

        readonly List<Athlete> _menuAthletes = new List<Athlete>();
        readonly List<Venue> _menuVenues = new List<Venue>();
        ActivityKit _kit;
        int _menuSelected = -1;
        float _switchedAt = -10f;
        float _drift;

        protected override void BuildChooseWorld(Transform root, ActivityKit kit)
        {
            _kit = kit;
            _menuAthletes.Clear();
            _menuVenues.Clear();
            _menuSelected = -1;
            var looks = new[] { MyLook(Wear.Track).Team(Coral, Night, Color.white, Sun), MyLook(Wear.Football).Team(Sky, Color.white, Sky) };
            looks[0].WearsHeadband = true;
            looks[0].Headband = Sun;
            for (int i = 0; i < Corners.Length; i++)
            {
                var c = Corners[i];
                var place = new GameObject($"Corner {c.kind}").transform;
                place.SetParent(root, false);
                place.localPosition = c.offset;
                var venue = Venue.Load(c.kind, place, seed: 3 + i, occupancy: 0.8f, water: kit.Water);
                _menuVenues.Add(venue);
                var a = Athlete.Create($"Menu athlete {c.kind}", looks[i], place);
                a.transform.localPosition = c.athlete;
                a.transform.localRotation = Quaternion.Euler(0f, c.yaw, 0f);
                _menuAthletes.Add(a);
                if (c.kind == Venue.Kind.Football)
                {
                    var ball = SportsBalls.Create("Menu ball", SportsBalls.Football, kit.Actor, null, place, Color.white);
                    ball.localPosition = c.athlete + Quaternion.Euler(0f, c.yaw, 0f) * new Vector3(0.25f, 0.11f, 0.45f);
                }
            }
        }

        /// <summary>The chosen sport: its light, its athlete getting ready (the others wait), a flash as the camera
        /// cuts there.</summary>
        protected override void UpdateChooseWorld(int selected, float dt)
        {
            if (_menuAthletes.Count < Corners.Length || selected == _menuSelected) return;
            bool first = _menuSelected < 0;
            _menuSelected = selected;
            if (!first) _switchedAt = Time.unscaledTime;
            switch (Corners[selected].kind)
            {
                case Venue.Kind.Track: Daylight.Afternoon.Apply(_kit); break;
                default: Daylight.Stadium.Apply(_kit); break;
            }
            string[] ready = { "SetPosition", "Ready" };
            for (int i = 0; i < _menuAthletes.Count; i++)
            {
                if (i == selected) _menuAthletes[i].PlayThen("Wave", ready[i] == "SetPosition" ? "Idle" : ready[i], 0.2f, 1f, 0.4f);
                else _menuAthletes[i].Play("Idle", 0.2f);
            }
            _menuVenues[selected].Fans?.Cheer(0.5f, 1.5f);
            _drift = 0f;
        }

        protected override bool PlaceChooseCamera(Camera camera, float dt)
        {
            if (_menuSelected < 0 || _menuSelected >= Corners.Length) return false;
            var c = Corners[_menuSelected];
            _drift += dt;
            // a slow push in, like a TV intro
            float k = 1f - Mathf.Exp(-_drift * 0.25f);
            var from = c.offset + c.cam + new Vector3(-0.4f, 0.15f, -0.6f);
            var to = c.offset + c.cam;
            camera.transform.position = Vector3.Lerp(from, to, k);
            camera.transform.LookAt(c.offset + c.look);
            camera.fieldOfView = 42f;
            return true;
        }

        protected override void DrawChooseScreen(float w)
        {
            // the title in the corner, the cards along the bottom, a flash when the camera cuts to another sport
            SportHud.Text(new Rect(70f, 36f, 900f, 130f), Title, 112, Sun, TextAnchor.MiddleLeft, 6f);
            SportHud.Pill(70f + SportHud.Width(ChooseText, 36) / 2f + 22f, 168f, ChooseText, Color.white, 36);
            if (Selected < ActivityList.Count)
            {
                var a = ActivityList[Selected];
                var body = SportHud.Style(34, false);
                body.wordWrap = true;
                body.alignment = TextAnchor.UpperLeft;
                var r = new Rect(70f, 250f, 640f, 220f);
                // no shadow: it didn't show under a dark panel, and each layer is a draw of the whole panel
                SportHud.Panel(new Rect(r.x - 4f, r.y - 4f, r.width + 8f, 190f), new Color(Night.r, Night.g, Night.b, 0.82f), 22f,
                               null, 3f, false);
                body.normal.textColor = Color.white;
                GUI.Label(new Rect(r.x + 24f, r.y + 18f, r.width - 48f, 160f), a.HowTo, body);
            }
            DrawCards(w, 640f);
            DrawChooseHint(w);
            float flash = 1f - Mathf.Clamp01((Time.unscaledTime - _switchedAt) / 0.35f);
            if (flash > 0f) GameHud.Box(new Rect(0f, 0f, w, GameHud.Height), new Color(1f, 1f, 1f, flash * 0.85f));
        }
    }
}
