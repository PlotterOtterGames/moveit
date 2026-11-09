using System.Linq;
using MoveIt.Games;
using MoveIt.Games.Bowling;
using MoveIt.Games.Dress;
using MoveIt.Games.Fly;
using MoveIt.Games.Kart;
using MoveIt.Games.Paint;
using MoveIt.Games.Runner;
using MoveIt.Games.Sports;
using MoveIt.Games.Sword;
using MoveIt.Games.Tennis;
using MoveIt.Games.Volley;
using MoveIt.Motion;
using MoveIt.Shell;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MoveIt.EditorTools
{
    /// <summary>
    /// Generates the gray-box scenes from code (menu: MoveIt / Build All Scenes, or batch mode
    /// -executeMethod MoveIt.EditorTools.MoveItSceneBuilder.BuildAll). Re-running overwrites the scenes.
    /// </summary>
    public static class MoveItSceneBuilder
    {
        const string MenuScenePath = "Assets/Scenes/Menu.unity";
        const string MotionDebugScenePath = "Assets/Scenes/MotionDebug.unity";
        const string RunnerScenePath = "Assets/Games/Runner/Runner.unity";
        const string DressScenePath = "Assets/Games/Dress/Dress.unity";
        const string DressMaterialsDir = "Assets/Games/Dress/Materials";
        const string DressArtDir = "Assets/Games/Dress/Art";
        const string BowlingScenePath = "Assets/Games/Bowling/Bowling.unity";
        const string PaintScenePath = "Assets/Games/Paint/Paint.unity";
        const string PaintMaterialsDir = "Assets/Games/Paint/Materials";
        const string SwordScenePath = "Assets/Games/Sword/Sword.unity";
        const string SwordMaterialsDir = "Assets/Games/Sword/Materials";
        const string FlyScenePath = "Assets/Games/Fly/Fly.unity";
        const string FlyMaterialsDir = "Assets/Games/Fly/Materials";
        const string TennisScenePath = "Assets/Games/Tennis/Tennis.unity";
        const string TennisMaterialsDir = "Assets/Games/Tennis/Materials";
        const string SportsScenePath = "Assets/Games/Sports/Sports.unity";
        const string VolleyScenePath = "Assets/Games/Volley/Volley.unity";
        const string VolleyMaterialsDir = "Assets/Games/Volley/Materials";
        const string KartScenePath = "Assets/Games/Kart/Kart.unity";
        const string KartMaterialsDir = "Assets/Games/Kart/Materials";
        const string KartArtDir = "Assets/Games/Kart/Art";
        const string SportsMaterialsDir = "Assets/Games/Sports/Materials";
        const string RunnerMaterialsDir = "Assets/Games/Runner/Materials";
        const string RunnerArtDir = "Assets/Games/Runner/Art";
        const string DinoModelPath = RunnerArtDir + "/Dino.fbx";
        const string DinoControllerPath = RunnerArtDir + "/Dino.controller";
        const string BowlingMaterialsDir = "Assets/Games/Bowling/Materials";
        const float LaneWidth = 2.2f;

        static readonly Color SkyColor = new Color(0.55f, 0.75f, 0.95f);

        [MenuItem("MoveIt/Build All Scenes")]
        public static void BuildAll()
        {
            BuildMenuScene();
            BuildMotionDebugScene();
            BuildRunnerScene();
            BuildDressScene();
            BuildBowlingScene();
            BuildPaintScene();
            BuildSwordScene();
            BuildFlyScene();
            BuildTennisScene();
            BuildVolleyScene();
            BuildKartScene();
            BuildSportsScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(RunnerScenePath, true),
                new EditorBuildSettingsScene(DressScenePath, true),
                new EditorBuildSettingsScene(BowlingScenePath, true),
                new EditorBuildSettingsScene(PaintScenePath, true),
                new EditorBuildSettingsScene(SwordScenePath, true),
                new EditorBuildSettingsScene(FlyScenePath, true),
                new EditorBuildSettingsScene(TennisScenePath, true),
                new EditorBuildSettingsScene(VolleyScenePath, true),
                new EditorBuildSettingsScene(KartScenePath, true),
                new EditorBuildSettingsScene(SportsScenePath, true),
                new EditorBuildSettingsScene(MotionDebugScenePath, true),
            };
            PlayerSettings.runInBackground = true;  // keep receiving motion data when another window has focus
            BoxBuild.ApplyBranding();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MenuScenePath);
            Debug.Log("[MoveIt] Built Menu, Runner, Dress, Bowling, Paint, Sword, Fly, Tennis, Volley, Kart, Sports and MotionDebug scenes.");
        }

        [MenuItem("MoveIt/Build Menu Scene")]
        public static void BuildMenuScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = CreateCamera(new Color(0.08f, 0.09f, 0.14f));
            cam.transform.position = new Vector3(0f, 1f, -10f);
            CreateMotionRig(fullScreenOverlay: false);
            new GameObject("Menu").AddComponent<MainMenu>();
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MenuScenePath);
        }

        /// <summary>
        /// Vishu!: an orthographic camera in HUD pixels (1080 high), the game draws the camera picture, the outfit (one
        /// mesh, Tundu/Outfit), the body mask (Tundu/BodyMask) and the particles itself. Art from tools/dress.
        /// </summary>
        [MenuItem("MoveIt/Build Dress Scene")]
        public static void BuildDressScene()
        {
            EnsureFolder(DressMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = CreateCamera(Hex("#141938"));
            cam.orthographic = true;
            cam.orthographicSize = 540f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.position = new Vector3(960f, 540f, -10f);
            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Dress").AddComponent<DressGame>();
            Set(game, "cam", cam);
            Set(game, "outfitMaterial", ShaderMat(DressMaterialsDir, "Outfit", "Tundu/Outfit"));
            Set(game, "maskMaterial", ShaderMat(DressMaterialsDir, "BodyMask", "Tundu/BodyMask"));
            var fx = ShaderMat(DressMaterialsDir, "DressFx", "Tundu/Fx");
            fx.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            fx.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set(game, "fxMaterial", fx);
            Set(game, "atlasJson", AssetDatabase.LoadAssetAtPath<TextAsset>(DressArtDir + "/outfit_atlas.json"));
            Set(game, "uiJson", AssetDatabase.LoadAssetAtPath<TextAsset>(DressArtDir + "/dress_ui.json"));
            foreach (var (field, file) in new[] { ("atlas", "outfit_atlas"), ("atlasMask", "outfit_mask"), ("thumbs", "outfit_thumbs"),
                                                  ("thumbsMask", "outfit_thumbs_mask"), ("uiSheet", "dress_ui"), ("fxSheet", "dress_fx") })
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{DressArtDir}/{file}.png");
                if (tex == null) Debug.LogWarning($"[MoveIt] {DressArtDir}/{file}.png is missing: run tools/dress/outfits.py and ui_art.py");
                Set(game, field, tex);
            }
            EditorSceneManager.SaveScene(scene, DressScenePath);
        }

        static Material ShaderMat(string folder, string name, string shaderName)
        {
            string path = $"{folder}/{name}.mat";
            var shader = Shader.Find(shaderName);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("MoveIt/Build Bowling Scene")]
        public static void BuildBowlingScene()
        {
            EnsureFolder(BowlingMaterialsDir);
            var lane = Mat(BowlingMaterialsDir, "Lane", new Color(0.85f, 0.65f, 0.4f), smoothness: 0.7f);
            var gutter = Mat(BowlingMaterialsDir, "Gutter", new Color(0.25f, 0.25f, 0.28f));
            var wall = Mat(BowlingMaterialsDir, "Wall", new Color(0.15f, 0.15f, 0.2f));
            var pin = Mat(BowlingMaterialsDir, "Pin", new Color(0.97f, 0.97f, 0.95f), smoothness: 0.6f);
            var ball = Mat(BowlingMaterialsDir, "Ball", new Color(0.1f, 0.3f, 0.9f), smoothness: 0.8f);
            var aim = Mat(BowlingMaterialsDir, "Aim", new Color(1f, 0.85f, 0.2f), emission: 1f);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            CreateLight(Quaternion.Euler(55f, 20f, 0f), 1.1f, shadows: true);

            var cam = CreateCamera(new Color(0.06f, 0.06f, 0.1f));
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 50f;
            cam.transform.position = new Vector3(0f, 2.2f, -3f);
            cam.transform.LookAt(new Vector3(0f, 0f, 10f));

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Bowling").AddComponent<BowlingGame>();
            Set(game, "cam", cam);
            Set(game, "laneMaterial", lane);
            Set(game, "gutterMaterial", gutter);
            Set(game, "wallMaterial", wall);
            Set(game, "pinMaterial", pin);
            Set(game, "ballMaterial", ball);
            Set(game, "aimMaterial", aim);

            EditorSceneManager.SaveScene(scene, BowlingScenePath);
        }

        [MenuItem("MoveIt/Build Paint Scene")]
        public static void BuildPaintScene()
        {
            EnsureFolder(PaintMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = CreateCamera(new Color(1f, 0.973f, 0.933f));
            cam.orthographic = true;
            cam.orthographicSize = 5f;
            cam.transform.position = new Vector3(0f, 0f, -10f);
            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Paint").AddComponent<PaintGame>();
            Set(game, "cam", cam);
            // Tundu/Fx quads: the paper or camera picture (opaque), the painting (premultiplied), the page lines (alpha)
            Set(game, "backgroundMaterial", PaintFxMat("PaintBackground", BlendMode.One, BlendMode.Zero));
            Set(game, "canvasMaterial", PaintFxMat("PaintCanvas", BlendMode.One, BlendMode.OneMinusSrcAlpha));
            Set(game, "lineArtMaterial", PaintFxMat("PaintLineArt", BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, Hex("#1E1E24")));
            Set(game, "brushMaterial", PaintBrushMat());
            EditorSceneManager.SaveScene(scene, PaintScenePath);
        }

        static Material PaintFxMat(string name, BlendMode src, BlendMode dst, Color? tint = null)
        {
            string path = $"{PaintMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Fx");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", tint ?? Color.white);
            mat.SetFloat("_SrcBlend", (float)src);
            mat.SetFloat("_DstBlend", (float)dst);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material PaintBrushMat()
        {
            string path = $"{PaintMaterialsDir}/PaintBrush.mat";
            var shader = Shader.Find("Tundu/Brush");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// Shpata!: a sunny castle courtyard (sand arena, stone walls with battlements, towers, a gate the knights come
        /// through, palette banners), the camera centred just behind and above the player's see-through figure.
        /// </summary>
        [MenuItem("MoveIt/Build Sword Scene")]
        public static void BuildSwordScene()
        {
            EnsureFolder(SwordMaterialsDir);
            var horizon = Hex("#D2EEF6");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = SkyMat(horizon, SwordMaterialsDir);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 30f;
            RenderSettings.fogEndDistance = 90f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);
            CreateLight(Quaternion.Euler(48f, -35f, 0f), 1.25f, shadows: true);
            Object.FindAnyObjectByType<Light>().color = new Color(1f, 0.96f, 0.88f);

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 120f;
            cam.fieldOfView = 58f;
            // centred, above and behind the figure: its head covers only the knight's legs, the knight shows above it
            cam.transform.position = new Vector3(0f, 2.55f, -1.35f);
            cam.transform.LookAt(new Vector3(0f, 1.1f, 2.6f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile("Assets/Games/Sword/SwordPost.asset");

            CreateMotionRig(fullScreenOverlay: false);

            // the courtyard
            var world = new GameObject("Courtyard").transform;
            void Block(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material mat, Vector3 euler = default, bool shadows = true)
            {
                var go = GameObject.CreatePrimitive(type);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(world, false);
                go.transform.SetPositionAndRotation(pos, Quaternion.Euler(euler));
                go.transform.localScale = scale;
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                go.isStatic = true;
            }
            var sand = SwordToon("Sand", Hex("#EBD3A6"));
            var arena = SwordToon("Arena", Hex("#DEBB86"));
            var path = SwordToon("Path", Hex("#D9B47F"));
            var stone = SwordToon("Stone", Hex("#D2C6B2"));
            var stoneDark = SwordToon("StoneDark", Hex("#B4A68F"));
            var gate = SwordToon("Gate", Hex("#5A4A6E"));
            var wood = SwordToon("Wood", Hex("#B98250"));
            var hay = SwordToon("Hay", Hex("#F2D27A"));
            Block(PrimitiveType.Plane, "Ground", new Vector3(0f, 0f, 10f), new Vector3(8f, 1f, 8f), sand, shadows: false);
            Block(PrimitiveType.Cylinder, "Arena", new Vector3(0f, 0.005f, 1.6f), new Vector3(6f, 0.005f, 6f), arena, shadows: false);
            Block(PrimitiveType.Cube, "Path", new Vector3(0f, 0.004f, 8.5f), new Vector3(2.4f, 0.01f, 10f), path, shadows: false);
            // walls with battlements
            Block(PrimitiveType.Cube, "Back wall", new Vector3(0f, 2.2f, 14f), new Vector3(28f, 4.4f, 1f), stone);
            for (float x = -13.2f; x <= 13.3f; x += 1.6f)
                Block(PrimitiveType.Cube, "Merlon", new Vector3(x, 4.7f, 14f), new Vector3(0.8f, 0.6f, 1f), stone);
            foreach (float side in new[] { -1f, 1f })
            {
                Block(PrimitiveType.Cube, "Side wall", new Vector3(13.5f * side, 2.2f, 6f), new Vector3(1f, 4.4f, 17f), stone);
                for (float z = -2.4f; z <= 14.1f; z += 1.6f)
                    Block(PrimitiveType.Cube, "Merlon", new Vector3(13.5f * side, 4.7f, z), new Vector3(1f, 0.6f, 0.8f), stone);
                Block(PrimitiveType.Cylinder, "Tower", new Vector3(10.5f * side, 3.4f, 13.6f), new Vector3(3.2f, 3.4f, 3.2f), stoneDark);
                Block(PrimitiveType.Cylinder, "Tower top", new Vector3(10.5f * side, 6.95f, 13.6f), new Vector3(3.7f, 0.18f, 3.7f), stone);
                Block(PrimitiveType.Sphere, "Tower roof", new Vector3(10.5f * side, 7.2f, 13.6f), new Vector3(3.4f, 2.6f, 3.4f),
                      SwordToon(side < 0 ? "RoofCoral" : "RoofGrape", side < 0 ? Hex("#FF5A4E") : Hex("#7B61FF")));
                Block(PrimitiveType.Cylinder, "Flag pole", new Vector3(10.5f * side, 9.3f, 13.6f), new Vector3(0.08f, 1.0f, 0.08f), wood);
                Block(PrimitiveType.Cube, "Flag", new Vector3(10.5f * side + 0.55f, 9.9f, 13.6f), new Vector3(1.0f, 0.6f, 0.04f),
                      SwordToon("FlagSun", Hex("#FFC93C")), shadows: false);
                // hay bales and a straw training dummy at the sides of the arena
                Block(PrimitiveType.Cylinder, "Hay", new Vector3(6.2f * side, 0.45f, 4.5f), new Vector3(1.1f, 0.45f, 1.1f), hay, new Vector3(0f, 0f, 90f));
                Block(PrimitiveType.Cylinder, "Hay", new Vector3(6.9f * side, 0.45f, 6.2f), new Vector3(1.1f, 0.45f, 1.1f), hay, new Vector3(0f, 30f, 90f));
                Block(PrimitiveType.Cylinder, "Dummy post", new Vector3(5.4f * side, 0.9f, 8.5f), new Vector3(0.12f, 0.9f, 0.12f), wood);
                Block(PrimitiveType.Capsule, "Dummy", new Vector3(5.4f * side, 1.5f, 8.5f), new Vector3(0.55f, 0.5f, 0.55f), hay);
            }
            Block(PrimitiveType.Cube, "Gate", new Vector3(0f, 1.5f, 13.45f), new Vector3(3f, 3f, 0.2f), gate);
            Block(PrimitiveType.Cylinder, "Gate arch", new Vector3(0f, 3f, 13.45f), new Vector3(3f, 0.1f, 3f), gate, new Vector3(90f, 0f, 0f));
            string[] banners = { "#FF5A4E", "#FFC93C", "#31C6F4", "#6FD66A" };
            float[] bannerX = { -7.5f, -4f, 4f, 7.5f };
            for (int i = 0; i < banners.Length; i++)
                Block(PrimitiveType.Cube, "Banner", new Vector3(bannerX[i], 2.6f, 13.45f), new Vector3(1.1f, 2.4f, 0.05f),
                      SwordToon("Banner" + i, Hex(banners[i])), shadows: false);

            // the player's see-through figure and its sword, and the game
            var avatar = new GameObject("Player").AddComponent<GhostAvatar>();
            Set(avatar, "ghostMaterial", GhostMat("Ghost", depth: false));
            Set(avatar, "depthMaterial", GhostMat("GhostDepth", depth: true));
            Set(avatar, "bladeMaterial", SwordToon("Blade", Hex("#F4F7FF"), rim: 0.6f));
            Set(avatar, "guardMaterial", SwordToon("Guard", Hex("#FFC93C"), rim: 0.3f));
            Set(avatar, "gripMaterial", SwordToon("Grip", Hex("#7B61FF")));
            Set(avatar, "trailMaterial", FxMat("SwordTrail", null, Color.white, additive: true, folder: SwordMaterialsDir));
            // the player's shield: silver with a coral rim and a sun emblem, unlike any knight's (theirs are coloured)
            Set(avatar, "shieldMaterial", SwordToon("Shield", Hex("#E8ECF8"), rim: 0.25f));
            Set(avatar, "shieldRimMaterial", SwordToon("ShieldRim", Hex("#FF5A4E"), rim: 0.2f));

            var game = new GameObject("Sword").AddComponent<SwordGame>();
            Set(game, "cam", cam);
            Set(game, "avatar", avatar);
            Set(game, "toonMaterial", SwordToon("Knight", Color.white, rim: 0.15f));
            Set(game, "starMaterial", FxMat("Star", "fx_star.png", Color.white, folder: SwordMaterialsDir));
            Set(game, "markerMaterial", FxMat("Marker", null, Color.white, folder: SwordMaterialsDir, onTop: true));

            EditorSceneManager.SaveScene(scene, SwordScenePath);
        }

        /// <summary>
        /// Fluturo!: flying over a toy city under a bright sky (<see cref="FlyWorld"/> builds the city in code), the camera
        /// behind and above the hero (<see cref="FlyGame"/> moves it). No shadows: everything is in the air, and it's
        /// cheaper on the box.
        /// </summary>
        [MenuItem("MoveIt/Build Fly Scene")]
        public static void BuildFlyScene()
        {
            EnsureFolder(FlyMaterialsDir);
            var horizon = Hex("#D2EEF6");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = SkyMat(horizon, FlyMaterialsDir);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 70f;
            RenderSettings.fogEndDistance = 185f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);
            // a little softer than the other games: the city's pale walls would wash out in a 1.2 sun
            CreateLight(Quaternion.Euler(50f, -30f, 0f), 1f, shadows: false);
            Object.FindAnyObjectByType<Light>().color = new Color(1f, 0.96f, 0.88f);

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.fieldOfView = 60f;
            cam.transform.position = new Vector3(0f, FlyWorld.FlyHeight + FlyGame.CameraHeight, -FlyGame.CameraBack);
            cam.transform.LookAt(new Vector3(0f, FlyWorld.FlyHeight + FlyGame.LookHeight, FlyGame.LookAhead));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            var profile = PostProfile("Assets/Games/Fly/FlyPost.asset");
            if (profile.TryGet<ColorAdjustments>(out var grade))
            {
                grade.postExposure.Override(-0.05f);   // bright pastel facades: no extra exposure, a bit more colour
                grade.saturation.Override(32f);
                grade.contrast.Override(18f);
                EditorUtility.SetDirty(profile);
            }
            post.sharedProfile = profile;

            CreateMotionRig(fullScreenOverlay: false);

            var world = new GameObject("City").AddComponent<FlyWorld>();
            Set(world, "cityMaterial", FlyCityMat(horizon));
            Set(world, "cloudMaterial", FlyToon("Cloud", rim: 0.35f));

            var game = new GameObject("Fly").AddComponent<FlyGame>();
            Set(game, "cam", cam);
            Set(game, "world", world);
            Set(game, "toonMaterial", FlyToon("Toon", rim: 0.2f));
            Set(game, "actorMaterial", FlyActorMat());
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: FlyMaterialsDir));
            Set(game, "glowMaterial", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: FlyMaterialsDir));
            Set(game, "markerMaterial", FxMat("Marker", null, Color.white, folder: FlyMaterialsDir, onTop: true));
            Set(game, "shadowMaterial", FxMat("Shadow", "fx_dot.png", new Color(0.1f, 0.12f, 0.3f, 0.42f), folder: FlyMaterialsDir));
            Set(game, "streakMaterial", FxMat("Streak", "fx_streak.png", Color.white, additive: true, folder: FlyMaterialsDir));

            EditorSceneManager.SaveScene(scene, FlyScenePath);
        }

        /// <summary>
        /// Tenis!: a stadium in the sun (<see cref="TennisStadium"/> builds the court, stands and crowd in code), the camera
        /// high behind the player's baseline like Wii Sports Tennis, real-time shadows under the players.
        /// </summary>
        [MenuItem("MoveIt/Build Tennis Scene")]
        public static void BuildTennisScene()
        {
            EnsureFolder(TennisMaterialsDir);
            // Wii Sports Tennis's look: a calm sunny day, soft haze, a sun that doesn't burn the colours out (the stadium's
            // colours count on a sunlit surface being ~15 % brighter than its colour: TennisStadium.LightGain)
            var horizon = Hex("#DCE9EC");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = SkyMat(horizon, TennisMaterialsDir, top: Hex("#8EC3E6"), bottom: Hex("#CBDDC2"));
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 160f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.47f, 0.5f, 0.55f);
            // no real-time shadows (the box can't afford them: TennisFx and the stadium draw the sun's shadows as soft
            // shapes on the grass, the same in the editor)
            CreateLight(Quaternion.Euler(50f, -30f, 0f), 1f, shadows: false);
            Object.FindAnyObjectByType<Light>().color = new Color(1f, 0.97f, 0.92f);

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 220f;
            // the classic view (TennisGame.CameraViews[0]; the game places the camera every frame): a little higher and
            // steeper than Wii's camera (measured from a match: 6.5 m up, 12.3 m behind the baseline, 17 degrees down, 32
            // degrees of view), the back player low in the picture, the whole court and the far pair above them
            var view = TennisGame.CameraViews[0];
            cam.fieldOfView = view.Fov;
            cam.transform.SetPositionAndRotation(new Vector3(0f, view.Height, -(Court.HalfLength + view.Behind)), Quaternion.Euler(view.Pitch, 0f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile("Assets/Games/Tennis/TennisPost.asset", contrast: 6f, saturation: 0f, exposure: 0f, vignette: 0.16f);

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Tennis").AddComponent<TennisGame>();
            Set(game, "cam", cam);
            Set(game, "actorMaterial", TennisToon("Actor", gloss: 0.6f, rim: 0.18f));
            // the court fills the picture: Tundu/Toon's simple path (sky light per vertex, no rim or gloss) costs the TV
            // box's GPU far less per pixel and looks the same on flat grass; the grass's detail is tiled over the world
            // (_WORLD_DETAIL: the one texture sample the palette took anyway)
            var stadium = TennisToon("Stadium", gloss: 0f, rim: 0f, simple: true);
            stadium.EnableKeyword("_WORLD_DETAIL");
            stadium.SetTexture("_BaseMap", TennisGrassDetail());
            stadium.SetFloat("_DetailScale", 0.22f);
            stadium.SetFloat("_DetailStrength", 0.14f);
            EditorUtility.SetDirty(stadium);
            Set(game, "stadiumMaterial", stadium);
            Set(game, "netMaterial", FxMat("Net", null, Color.white, folder: TennisMaterialsDir));
            var ball = TennisToon("Ball", gloss: 0f, rim: 0.45f, vertex: false);
            ball.SetColor("_BaseColor", Hex("#DDF23A"));
            ball.SetColor("_EmissionColor", Hex("#DDF23A") * 0.35f);
            EditorUtility.SetDirty(ball);
            Set(game, "ballMaterial", ball);
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: TennisMaterialsDir));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, folder: TennisMaterialsDir));
            Set(game, "trailMaterial", FxMat("Trail", null, Color.white, additive: true, folder: TennisMaterialsDir));
            Set(game, "shadowMaterial", FxMat("Shadow", null, Color.white, folder: TennisMaterialsDir));
            Set(game, "glowMaterial", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: TennisMaterialsDir));

            EditorSceneManager.SaveScene(scene, TennisScenePath);
        }

        /// <summary>The grass's detail texture (<see cref="TennisArt.GrassDetail"/>, drawn in code) as an asset the stadium's
        /// material can keep (one channel, mipmapped, tiling).</summary>
        static Texture2D TennisGrassDetail()
        {
            string path = $"{TennisMaterialsDir}/GrassDetail.asset";
            var tex = TennisArt.GrassDetail();
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old == null) AssetDatabase.CreateAsset(tex, path);
            else
            {
                EditorUtility.CopySerialized(tex, old);   // the same asset (and the material's reference) with new pixels
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(old);
            }
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Volejboll!: Sporte!'s beach (the venue, its crowd and the sea, a children's net) with Tenis!'s look and settings:
        /// no real-time shadows (VolleyFx draws the sun's as soft shapes on the sand), gentle post-processing (the box has
        /// none), the camera high behind the near team (the game places it every frame). The game sets the beach's daylight
        /// at runtime (Daylight.Beach).
        /// </summary>
        [MenuItem("MoveIt/Build Volley Scene")]
        public static void BuildVolleyScene()
        {
            EnsureFolder(VolleyMaterialsDir);
            var horizon = Hex("#CBEFFA");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = SkyMat(horizon, VolleyMaterialsDir, top: Hex("#1F7FE6"), bottom: Hex("#7FC9E0"));
            RenderSettings.skybox = sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#D7F1FA");
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = 1300f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);
            CreateLight(Quaternion.Euler(40f, 205f, 0f), 1.4f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFEBC2");

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1500f;
            var view = VolleyGame.CameraViews[0];
            cam.fieldOfView = view.Fov;
            cam.transform.SetPositionAndRotation(new Vector3(0f, view.Height, -(VolleyMatch.HalfLength + view.Behind)), Quaternion.Euler(view.Pitch, 0f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile("Assets/Games/Volley/VolleyPost.asset", contrast: 6f, saturation: 4f, exposure: 0f, vignette: 0.16f);

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Volley").AddComponent<VolleyGame>();
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "actorMaterial", ToonIn(VolleyMaterialsDir, "Actor", gloss: 0.6f, rim: 0.18f, vertex: true));
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: VolleyMaterialsDir));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, folder: VolleyMaterialsDir));
            Set(game, "trailMaterial", FxMat("Trail", null, Color.white, additive: true, folder: VolleyMaterialsDir));
            Set(game, "shadowMaterial", FxMat("Shadow", null, Color.white, folder: VolleyMaterialsDir));
            Set(game, "glowMaterial", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: VolleyMaterialsDir));
            Set(game, "skyMaterial", sky);
            Set(game, "waterMaterial", ShaderMat(VolleyMaterialsDir, "Water", "Tundu/Water"));
            SetSportsArt(game, VolleyMaterialsDir);
            EditorSceneManager.SaveScene(scene, VolleyScenePath);
        }

        /// <summary>
        /// Gara!: the track is built by the game from code (KartWorld: the road, the ground, the scenery from art/kart's
        /// props and Vrapo!'s dino valley), its daylight set per track. Tenis!'s look and settings: no real-time shadows
        /// (KartFx draws soft ones), gentle post-processing (the box has none), the chase camera placed every frame.
        /// </summary>
        [MenuItem("MoveIt/Build Kart Scene")]
        public static void BuildKartScene()
        {
            EnsureFolder(KartMaterialsDir);
            var horizon = Hex("#CBEFFA");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = SkyMat(horizon, KartMaterialsDir, top: Hex("#1F7FE6"), bottom: Hex("#7FC9E0"));
            RenderSettings.skybox = sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#D7F1FA");
            RenderSettings.fogStartDistance = 140f;
            RenderSettings.fogEndDistance = 700f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);
            CreateLight(Quaternion.Euler(42f, 205f, 0f), 1.35f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFEBC2");

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 900f;
            cam.fieldOfView = KartGame.CameraViews[0].Fov;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 3f, -10f), Quaternion.Euler(10f, 90f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile("Assets/Games/Kart/KartPost.asset", contrast: 6f, saturation: 6f, exposure: 0f, vignette: 0.14f);

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Kart").AddComponent<KartGame>();
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "skyMaterial", sky);
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: KartMaterialsDir));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, folder: KartMaterialsDir));
            Set(game, "shadowMaterial", FxMat("Shadow", null, Color.white, folder: KartMaterialsDir));
            Set(game, "actorMaterial", ToonIn(KartMaterialsDir, "Actor", gloss: 0.6f, rim: 0.18f, vertex: true));

            // the track: the road and the ground in vertex colours on Tundu/Toon's simple path (they fill the screen), the
            // ground with the grass's detail tiled over the world
            var road = ToonIn(KartMaterialsDir, "Road", gloss: 0f, rim: 0f, vertex: true);
            road.SetFloat("_Ambient", 0.26f);
            road.SetColor("_ShadeColor", new Color(0.62f, 0.64f, 0.82f));
            road.EnableKeyword("_TOON_SIMPLE");
            EditorUtility.SetDirty(road);
            Set(game, "world.road", road);
            var ground = ToonIn(KartMaterialsDir, "Ground", gloss: 0f, rim: 0f, vertex: true);
            ground.SetFloat("_Ambient", 0.26f);
            ground.EnableKeyword("_TOON_SIMPLE");
            ground.EnableKeyword("_WORLD_DETAIL");
            ground.SetTexture("_BaseMap", KartGrassDetail());
            ground.SetFloat("_DetailScale", 0.12f);
            ground.SetFloat("_DetailStrength", 0.2f);
            EditorUtility.SetDirty(ground);
            Set(game, "world.ground", ground);
            var props = ToonIn(KartMaterialsDir, "Props", gloss: 0f, rim: 0.08f, vertex: false);
            props.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{KartArtDir}/tundu_palette.png"));
            props.SetFloat("_Ambient", 0.3f);
            props.EnableKeyword("_TOON_SIMPLE");
            EditorUtility.SetDirty(props);
            Set(game, "world.props", props);
            var stars = ToonIn(KartMaterialsDir, "Stars", gloss: 0.9f, rim: 0.35f, vertex: false);
            stars.SetColor("_BaseColor", Hex("#FFC93C"));
            stars.SetColor("_EmissionColor", Hex("#FFC93C") * 0.35f);
            EditorUtility.SetDirty(stars);
            Set(game, "world.stars", stars);
            Set(game, "world.pads", FxMat("Pads", null, Color.white, additive: true, folder: KartMaterialsDir));
            Set(game, "world.water", ShaderMat(KartMaterialsDir, "Water", "Tundu/Water"));
            var crowd = ShaderMat(KartMaterialsDir, "Crowd", "Tundu/Crowd");
            crowd.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Games/Sports/Art/crowd_atlas.png"));
            Set(game, "world.crowd", crowd);
            Set(game, "world.kartProps", AssetDatabase.LoadAssetAtPath<GameObject>($"{KartArtDir}/KartProps.fbx"));
            SetArray(game, "world.dinoProps", new[] { "Fern1", "Fern2", "Cycad1", "Cycad2", "Rock1", "Rock2", "Boulders", "Volcano" }
                .Select(n => (Object)AssetDatabase.LoadAssetAtPath<GameObject>($"{RunnerArtDir}/{n}.fbx")).ToArray());
            Set(game, "world.dino", AssetDatabase.LoadAssetAtPath<GameObject>(DinoModelPath));
            Set(game, "world.ptero", AssetDatabase.LoadAssetAtPath<GameObject>($"{RunnerArtDir}/Ptero.fbx"));
            Set(game, "world.dinoController", AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(DinoControllerPath));
            Set(game, "world.pteroController", AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>($"{RunnerArtDir}/Ptero.controller"));

            // the kart: art/kart's model, coloured by region with the chibis' shader (closed pieces: back faces culled)
            Set(game, "models.kart", AssetDatabase.LoadAssetAtPath<GameObject>($"{KartArtDir}/Kart.fbx"));
            var paint = AthleteMat(KartMaterialsDir);
            paint.SetFloat("_Cull", (float)CullMode.Back);
            EditorUtility.SetDirty(paint);
            Set(game, "models.paint", paint);
            Set(game, "models.glow", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: KartMaterialsDir));
            EditorSceneManager.SaveScene(scene, KartScenePath);
        }

        /// <summary>The ground's grass detail (Tenis!'s, drawn in code) as an asset its material keeps.</summary>
        static Texture2D KartGrassDetail()
        {
            string path = $"{KartMaterialsDir}/GrassDetail.asset";
            var tex = TennisArt.GrassDetail();
            var old = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (old == null) AssetDatabase.CreateAsset(tex, path);
            else
            {
                EditorUtility.CopySerialized(tex, old);
                Object.DestroyImmediate(tex);
                EditorUtility.SetDirty(old);
            }
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Sporte!'s venues and their materials on a game's serialized <c>art</c> (SportsArt): Sporte! and
        /// Volejboll! (the beach).</summary>
        static void SetSportsArt(Object game, string materials)
        {
            const string art = "Assets/Games/Sports/Art", tex = art + "/Textures", venues = art + "/Venues";
            T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
            Set(game, "art.football", Load<GameObject>(venues + "/Football.fbx"));
            Set(game, "art.track", Load<GameObject>(venues + "/Track.fbx"));
            Set(game, "art.beach", Load<GameObject>(venues + "/Beach.fbx"));
            Set(game, "art.footballSeats", Load<TextAsset>(venues + "/Football_crowd.txt"));
            Set(game, "art.trackSeats", Load<TextAsset>(venues + "/Track_crowd.txt"));
            Set(game, "art.beachSeats", Load<TextAsset>(venues + "/Beach_crowd.txt"));
            Set(game, "art.grassDetail", Load<Texture2D>(tex + "/grass_detail.png"));
            Set(game, "art.trackDetail", Load<Texture2D>(tex + "/track_detail.png"));
            Set(game, "art.sandDetail", Load<Texture2D>(tex + "/sand_detail.png"));
            // the venues fill most of the screen: Tundu/Toon's simple path (no rim or gloss, sky light per vertex) saves the
            // TV box's GPU a lot per pixel
            var world = ToonIn(materials, "VenueWorld", gloss: 0f, rim: 0f, vertex: true);
            world.SetFloat("_Ambient", 0.42f);
            world.SetFloat("_ShadowStrength", 0.62f);
            world.EnableKeyword("_TOON_SIMPLE");
            Set(game, "art.world", world);
            var ground = ToonIn(materials, "VenueGround", gloss: 0f, rim: 0f, vertex: true);
            ground.SetTexture("_BaseMap", Load<Texture2D>(tex + "/grass_detail.png"));
            ground.SetFloat("_Ambient", 0.42f);
            ground.SetFloat("_ShadowStrength", 0.6f);
            ground.EnableKeyword("_TOON_SIMPLE");
            Set(game, "art.ground", ground);
            var boards = ShaderMat(materials, "Boards", "Tundu/Screen");
            boards.SetTexture("_BaseMap", Load<Texture2D>(tex + "/ads.png"));
            boards.SetTextureScale("_BaseMap", new Vector2(1f / 24f, 1f));
            boards.SetFloat("_Scroll", 0.012f);
            boards.SetVector("_LedDensity", new Vector4(2400f, 100f, 0f, 0f));
            Set(game, "art.boards", boards);
            var screen = ShaderMat(materials, "BigScreen", "Tundu/Screen");
            screen.SetTexture("_BaseMap", Load<Texture2D>(tex + "/screen.png"));
            screen.SetVector("_LedDensity", new Vector4(330f, 150f, 0f, 0f));
            Set(game, "art.screen", screen);
            var net = ShaderMat(materials, "Net", "Tundu/Net");
            net.SetTexture("_BaseMap", Load<Texture2D>(tex + "/net.png"));
            Set(game, "art.net", net);
            var crowd = ShaderMat(materials, "Crowd", "Tundu/Crowd");
            crowd.SetTexture("_BaseMap", Load<Texture2D>(art + "/crowd_atlas.png"));
            Set(game, "art.crowd", crowd);
        }

        /// <summary>Sporte!: one scene for its events (each builds its own world in code and sets the sky, fog and
        /// sun).</summary>
        [MenuItem("MoveIt/Build Sports Scene")]
        public static void BuildSportsScene()
        {
            BuildActivityScene<SportsGame>(SportsScenePath, SportsMaterialsDir, "Assets/Games/Sports/SportsPost.asset", game =>
            {
                // the athletes: the model of art/sports/athlete.py, its clips, Tundu/Athlete
                const string fbx = "Assets/Games/Sports/Art/Athlete.fbx";
                Set(game, "athletePrefab", AssetDatabase.LoadAssetAtPath<GameObject>(fbx));
                Set(game, "athleteMaterial", AthleteMat(SportsMaterialsDir));
                SetArray(game, "athleteClips", AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>()
                    .Where(c => !c.name.StartsWith("__preview__")).Cast<Object>().ToArray());
                // the venues (art/sports/venues.py), their textures (tools/sports/textures.py) and materials
                SetSportsArt(game, SportsMaterialsDir);
            });
        }

        /// <summary>Tundu/Athlete: the athletes' material (their colours come per character).</summary>
        static Material AthleteMat(string folder)
        {
            string path = $"{folder}/Athlete.mat";
            var shader = Shader.Find("Tundu/Athlete");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>
        /// A game of activities (<see cref="ActivityGame"/>): a sunlit sky by default (the activities set their own), the
        /// camera they move, shadows, a little post-processing, the materials they build with.
        /// </summary>
        static void BuildActivityScene<T>(string scenePath, string materials, string postAsset, System.Action<T> extra = null) where T : ActivityGame
        {
            EnsureFolder(materials);
            var horizon = Hex("#D2EEF6");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sky = SkyMat(horizon, materials);
            RenderSettings.skybox = sky;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 150f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);
            CreateLight(Quaternion.Euler(50f, -30f, 0f), 1.2f, shadows: true);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = new Color(1f, 0.96f, 0.88f);

            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 260f;
            cam.fieldOfView = 58f;
            cam.transform.position = new Vector3(0f, 2.5f, -5f);
            cam.transform.LookAt(new Vector3(0f, 1.3f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile(postAsset);

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject(typeof(T).Name.Replace("Game", "")).AddComponent<T>();
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "actorMaterial", ToonIn(materials, "Actor", gloss: 0.9f, rim: 0.25f, vertex: true));
            Set(game, "worldMaterial", ToonIn(materials, "World", gloss: 0.25f, rim: 0f, vertex: true));
            Set(game, "toonMaterial", ToonIn(materials, "Toon", gloss: 0.3f, rim: 0.15f, vertex: false));
            Set(game, "ghostMaterial", GhostMat("Ghost", depth: false, folder: materials));
            Set(game, "ghostDepthMaterial", GhostMat("GhostDepth", depth: true, folder: materials));
            Set(game, "fxMaterial", FxMat("Fx", null, Color.white, folder: materials));
            Set(game, "markerMaterial", FxMat("Marker", null, Color.white, folder: materials, onTop: true));
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: materials));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, folder: materials));
            Set(game, "waterMaterial", ShaderMat(materials, "Water", "Tundu/Water"));
            Set(game, "skyMaterial", sky);
            extra?.Invoke(game);

            EditorSceneManager.SaveScene(scene, scenePath);
        }

        /// <summary>Tundu/Toon in <paramref name="folder"/> for a flat world: vertex colours (alpha = gloss) for merged parts
        /// and worlds, or plain white for pieces coloured one by one.</summary>
        static Material ToonIn(string folder, string name, float gloss, float rim, bool vertex)
        {
            string path = $"{folder}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Curve", 0f);
            mat.SetFloat("_UseVertexColor", vertex ? 1f : 0f);
            mat.SetFloat("_Gloss", gloss);
            mat.SetFloat("_GlossPower", 36f);
            mat.SetFloat("_Ambient", 0.35f);
            mat.SetFloat("_ShadeSoftness", 0.2f);
            mat.SetFloat("_ShadowStrength", 0.55f);
            mat.SetColor("_RimColor", new Color(1f, 1f, 1f, rim));
            mat.SetColor("_EmissionColor", Color.black);
            mat.enableInstancing = !vertex;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Toon for Tenis! (flat world): with vertex colours (alpha = gloss) for the merged players and
        /// stadium, or a plain colour.</summary>
        static Material TennisToon(string name, float gloss, float rim, bool vertex = true, bool simple = false)
        {
            string path = $"{TennisMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Curve", 0f);
            mat.SetFloat("_UseVertexColor", vertex ? 1f : 0f);
            mat.SetFloat("_Gloss", gloss);
            mat.SetFloat("_GlossPower", 36f);
            mat.SetFloat("_Ambient", 0.35f);
            mat.SetFloat("_ShadeSoftness", 0.2f);
            mat.SetFloat("_ShadowStrength", 0.55f);
            mat.SetColor("_RimColor", new Color(1f, 1f, 1f, rim));
            mat.SetColor("_EmissionColor", Color.black);
            if (simple) mat.EnableKeyword("_TOON_SIMPLE");
            else mat.DisableKeyword("_TOON_SIMPLE");
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/City with the city's texture array (tools/fly/city_textures.py, imported by FlyArtImport):
        /// one material for every block. Its glass mirrors the same sky as the skybox.</summary>
        static Material FlyCityMat(Color horizon)
        {
            string path = $"{FlyMaterialsDir}/City.mat";
            var shader = Shader.Find("Tundu/City");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            AssetDatabase.ImportAsset(FlyArtImport.CityTexture);
            var tiles = AssetDatabase.LoadAssetAtPath<Texture2DArray>(FlyArtImport.CityTexture);
            if (tiles == null) Debug.LogError($"[MoveIt] {FlyArtImport.CityTexture} is not a texture array: run tools/fly/city_textures.py, reimport");
            mat.SetTexture("_BaseMap", tiles);
            mat.SetColor("_SkyTop", Hex("#5BB8F2"));
            mat.SetColor("_SkyHorizon", horizon);
            mat.SetFloat("_Ambient", 0.14f);
            mat.SetColor("_ShadeColor", new Color(0.64f, 0.66f, 0.88f));
            mat.enableInstancing = false;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Toon for characters merged from parts: vertex colours (alpha = gloss), a shiny highlight, a soft
        /// rim, flat (they fly; the city bends below them).</summary>
        static Material FlyActorMat()
        {
            var mat = FlyToon("Actor", rim: 0.28f);
            mat.SetFloat("_UseVertexColor", 1f);
            mat.SetFloat("_Gloss", 0.9f);
            mat.SetFloat("_GlossPower", 36f);
            mat.SetFloat("_Ambient", 0.35f);
            mat.SetFloat("_ShadeSoftness", 0.2f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Toon, white (the game colours each part): flat, or bending with the world (the city).</summary>
        static Material FlyToon(string name, float rim = 0f, bool curve = false)
        {
            string path = $"{FlyMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Curve", curve ? 1f : 0f);
            mat.SetFloat("_Ambient", curve ? 0.3f : 0.4f);
            mat.SetColor("_RimColor", new Color(1f, 1f, 1f, rim));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Toon in a plain colour for Shpata! (flat: the courtyard doesn't bend like the runner's world).</summary>
        static Material SwordToon(string name, Color color, float rim = 0f)
        {
            string path = $"{SwordMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_Curve", 0f);
            mat.SetFloat("_ShadowStrength", 0.5f);
            mat.SetColor("_RimColor", new Color(1f, 1f, 1f, rim));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Ghost: the colour material, or the depth one drawn just before it (see the shader).</summary>
        static Material GhostMat(string name, bool depth, string folder = SwordMaterialsDir)
        {
            string path = $"{folder}/{name}.mat";
            var shader = Shader.Find("Tundu/Ghost");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetFloat("_ColorMask", depth ? 0f : 15f);
            mat.SetFloat("_ZWrite", depth ? 1f : 0f);
            mat.SetColor("_BaseColor", new Color(0.19f, 0.78f, 0.96f, 0.42f));   // brand sky, a little more solid than glass
            mat.SetFloat("_RimPower", 1.6f);
            mat.renderQueue = depth ? 2999 : 3000;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("MoveIt/Build Motion Debug Scene")]
        public static void BuildMotionDebugScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = CreateCamera(new Color(0.05f, 0.05f, 0.08f));
            cam.transform.position = new Vector3(0f, 1f, -10f);
            CreateMotionRig(fullScreenOverlay: true);
            var lab = new GameObject("Recognition Lab");
            lab.AddComponent<BackToMenu>();  // camera test is opened from Settings
            lab.AddComponent<RecognitionLab>();
            EnsureFolder("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, MotionDebugScenePath);
        }

        [MenuItem("MoveIt/Build Runner Scene")]
        public static void BuildRunnerScene()
        {
            EnsureFolder(RunnerMaterialsDir);
            var playerMat = Mat(RunnerMaterialsDir, "Player", new Color(0.2f, 0.5f, 1f));
            var lowMat = Mat(RunnerMaterialsDir, "LowBarrier", new Color(1f, 0.55f, 0.1f));
            var highMat = Mat(RunnerMaterialsDir, "HighBarrier", new Color(0.9f, 0.2f, 0.25f));
            var blockMat = Mat(RunnerMaterialsDir, "Block", new Color(0.3f, 0.32f, 0.4f));
            var coinMat = Mat(RunnerMaterialsDir, "Coin", new Color(1f, 0.85f, 0.1f), emission: 0.6f);
            // Dino world: a dirt path through grass under a cartoon sky, everything in the toon / curved-world shader
            var groundA = ToonMat("GroundA", Hex("#E2A96C"));
            var groundB = ToonMat("GroundB", Hex("#D69C60"));
            var laneMat = ToonMat("LaneLine", Hex("#B98250"));
            var grassMat = ToonMat("Grass", Hex("#8CD05C"));
            var horizon = Hex("#D2EEF6");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = SkyMat(horizon);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = horizon;
            RenderSettings.fogStartDistance = 35f;
            RenderSettings.fogEndDistance = 125f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.62f, 0.66f, 0.74f);

            CreateLight(Quaternion.Euler(50f, -30f, 0f), 1.25f, shadows: true);
            Object.FindAnyObjectByType<Light>().color = new Color(1f, 0.96f, 0.88f);   // warm sun

            var rig = new GameObject("Camera Rig").transform;
            var cam = CreateCamera(horizon);
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.farClipPlane = 220f;
            cam.transform.SetParent(rig, false);
            cam.transform.localPosition = new Vector3(0f, 3.4f, -6.5f);
            cam.transform.localRotation = Quaternion.Euler(14f, 0f, 0f);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = RunnerPostProfile();

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Runner").AddComponent<RunnerGame>();
            var fx = game.gameObject.AddComponent<RunnerFx>();
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<RunnerPlayer>();
            Set(player, "fx", fx);
            var dinoModel = AssetDatabase.LoadAssetAtPath<GameObject>(DinoModelPath);
            if (dinoModel != null)
            {
                // Dino from art/runner (Blender): the palette in the toon shader with a soft rim light
                var dino = (GameObject)PrefabUtility.InstantiatePrefab(dinoModel);
                dino.name = "Dino";
                dino.transform.SetParent(playerGo.transform, false);
                var dinoMat = PaletteMat("TunduPaletteDino", rim: 0.35f);
                foreach (var r in dino.GetComponentsInChildren<Renderer>()) r.sharedMaterial = dinoMat;
                var animator = dino.GetComponent<Animator>();
                if (animator == null) animator = dino.AddComponent<Animator>();   // (not ??: Unity's fake null)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(DinoModelPath).OfType<Avatar>().FirstOrDefault();
                animator.runtimeAnimatorController = DinoController();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Set(player, "visual", dino.transform);
                Set(player, "animator", animator);
            }
            else
            {
                var visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.DestroyImmediate(visual.GetComponent<Collider>());
                visual.name = "Body";
                visual.GetComponent<Renderer>().sharedMaterial = playerMat;
                visual.transform.SetParent(playerGo.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                visual.transform.localScale = new Vector3(0.8f, 0.9f, 0.8f);
                Set(player, "visual", visual.transform);
            }

            Set(game, "player", player);
            Set(game, "cameraRig", rig);
            Set(game, "fx", fx);
            Set(game, "lowBarrierMaterial", lowMat);
            Set(game, "highBarrierMaterial", highMat);
            Set(game, "blockMaterial", blockMat);
            Set(game, "coinMaterial", coinMat);
            Set(game, "groundMaterialA", groundA);
            Set(game, "groundMaterialB", groundB);
            Set(game, "sideGroundMaterial", grassMat);
            Set(game, "laneMaterial", laneMat);

            Set(fx, "dustMaterial", FxMat("FxDust", "fx_dot.png", Color.white));
            Set(fx, "starMaterial", FxMat("FxStar", "fx_star.png", Color.white));
            Set(fx, "streakMaterial", FxMat("FxStreak", "fx_streak.png", Color.white, additive: true));
            Set(fx, "shadowMaterial", FxMat("FxShadow", "fx_dot.png", new Color(0.12f, 0.12f, 0.3f, 0.4f)));
            if (dinoModel != null)
            {
                var palette = PaletteMat();
                Set(game, "worldMaterial", palette);
                Set(game, "lowBarrierPrefab", Art("Log"));
                Set(game, "highBarrierPrefab", Art("VineBridge"));
                Set(game, "blockPrefab", Art("Boulders"));
                Set(game, "coinPrefab", Art("Egg"));
                SetArray(game, "sceneryPrefabs", Art("Fern1"), Art("Fern2"), Art("Rock1"), Art("Rock2"), Art("Fern1"));
                SetArray(game, "treePrefabs", Art("Cycad1"), Art("Cycad2"));
                Set(fx, "cloudPrefab", Art("Cloud"));
                Set(fx, "pteroPrefab", Art("Ptero"));
                Set(fx, "pteroController", SimpleController("Ptero", "Fly"));
                Set(fx, "skyMaterial", PaletteMat("TunduPaletteSky", curve: false));
                // volcanoes on the horizon (static: far things don't move); smoke rises from the bent crater
                var anchors = new System.Collections.Generic.List<Object>();
                foreach (var (pos, scale, yaw) in new[] { (new Vector3(-48f, 0f, 90f), 0.7f, 20f), (new Vector3(60f, 0f, 96f), 0.5f, 140f) })
                {
                    var volcano = (GameObject)PrefabUtility.InstantiatePrefab(Art("Volcano"));
                    volcano.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
                    volcano.transform.localScale = Vector3.one * scale;
                    foreach (var r in volcano.GetComponentsInChildren<Renderer>())
                    {
                        r.sharedMaterial = palette;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                    }
                    float drop = WorldCurve * Mathf.Pow(pos.z + 6.5f, 2f);
                    var anchor = new GameObject("Smoke").transform;
                    anchor.SetParent(volcano.transform, false);
                    anchor.position = pos + Vector3.up * (33f * scale - drop);
                    anchors.Add(anchor);
                }
                SetArray(fx, "smokeAnchors", anchors.ToArray());
            }

            EditorSceneManager.SaveScene(scene, RunnerScenePath);
        }

        const float WorldCurve = 0.0009f;   // RunnerGame.worldCurve default: keep in sync

        /// <summary>Tundu/Toon with a plain colour (ground, lane lines): bends with the curved world.</summary>
        static Material ToonMat(string name, Color color)
        {
            string path = $"{RunnerMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.SetTexture("_BaseMap", null);
            mat.SetFloat("_ShadowStrength", 0.55f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Fx (unlit, soft alpha) with one of the fx_* textures (null = plain). onTop: drawn over
        /// everything (markers).</summary>
        static Material FxMat(string name, string texture, Color tint, bool additive = false, string folder = RunnerMaterialsDir,
                              bool onTop = false)
        {
            string path = $"{folder}/{name}.mat";
            var shader = Shader.Find("Tundu/Fx");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", texture != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{RunnerArtDir}/{texture}") : null);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZTest", onTop ? (float)CompareFunction.Always : (float)CompareFunction.LessEqual);
            mat.renderQueue = onTop ? 3100 : -1;
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material SkyMat(Color horizon, string folder = RunnerMaterialsDir, Color? top = null, Color? bottom = null)
        {
            string path = $"{folder}/Sky.mat";
            var shader = Shader.Find("Tundu/Sky");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_TopColor", top ?? Hex("#5BB8F2"));
            mat.SetColor("_HorizonColor", horizon);
            mat.SetColor("_BottomColor", bottom ?? Hex("#BDE3DC"));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Colour grading, a soft vignette and a gentle bloom on the brightest bits (eggs, sparkles).</summary>
        static VolumeProfile RunnerPostProfile() => PostProfile("Assets/Games/Runner/RunnerPost.asset");

        static VolumeProfile PostProfile(string path, float contrast = 14f, float saturation = 18f, float exposure = 0.1f, float vignette = 0.22f)
        {
            AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(exposure);
            color.contrast.Override(contrast);
            color.saturation.Override(saturation);
            var vignetteFx = profile.Add<Vignette>(true);
            vignetteFx.intensity.Override(vignette);
            vignetteFx.smoothness.Override(0.45f);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            bloom.highQualityFiltering.Override(false);
            bloom.downscale.Override(BloomDownscaleMode.Quarter);
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return profile;
        }

        static RuntimeAnimatorController SimpleController(string model, string state)
        {
            string modelPath = $"{RunnerArtDir}/{model}.fbx";
            string path = $"{RunnerArtDir}/{model}.controller";
            var clip = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            AssetDatabase.DeleteAsset(path);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
            var s = controller.layers[0].stateMachine.AddState(state);
            s.motion = clip;
            return controller;
        }

        static void CreateMotionRig(bool fullScreenOverlay)
        {
            var go = new GameObject("Motion");
            go.AddComponent<MotionSystem>();
            var gestures = go.AddComponent<BodyGestures>();
            var input = go.AddComponent<BodyInput>();
            Set(input, "gestures", gestures);
            go.AddComponent<BodyHands>();
            var overlay = go.AddComponent<MotionDebugOverlay>();
            Set(overlay, "gestures", gestures);
            var so = new SerializedObject(overlay);
            so.FindProperty("fullScreen").boolValue = fullScreenOverlay;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static Camera CreateCamera(Color background)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            go.AddComponent<AudioListener>();
            return cam;
        }

        static void CreateLight(Quaternion rotation, float intensity, bool shadows)
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            go.transform.rotation = rotation;
        }

        /// <summary>Unlit, vertex-colored, transparent material for LineRenderer trails.</summary>
        static Material SpriteMat(string folder, string name)
        {
            string path = $"{folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
            mat = new Material(Shader.Find("Sprites/Default"));
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        static GameObject Art(string name) => AssetDatabase.LoadAssetAtPath<GameObject>($"{RunnerArtDir}/{name}.fbx");

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

        static void SetArray(Object target, string field, params Object[] values)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>The runner world's one material: Tundu/Toon with the 64x64 Tundu palette (art/tools/tundu_art.py).
        /// Variants: the dino gets a soft rim light, the sky things (clouds, pterodactyls) don't bend with the world.</summary>
        static Material PaletteMat(string name = "TunduPalette", float rim = 0f, bool curve = true)
        {
            string path = $"{RunnerMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Toon");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(RunnerArtDir + "/tundu_palette.png"));
            mat.SetColor("_BaseColor", Color.white);
            mat.SetColor("_RimColor", new Color(1f, 1f, 1f, rim));
            mat.SetFloat("_Curve", curve ? 1f : 0f);
            mat.SetFloat("_Ambient", curve ? 0.25f : 0.45f);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Animator states named like the dino's clips, no transitions: RunnerPlayer cross-fades by name.</summary>
        static RuntimeAnimatorController DinoController()
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(DinoModelPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            AssetDatabase.DeleteAsset(DinoControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(DinoControllerPath);
            var machine = controller.layers[0].stateMachine;
            foreach (var name in new[] { "Idle", "Run", "Jump", "Slide", "Hit", "Cheer" })
            {
                var state = machine.AddState(name);
                if (clips.TryGetValue(name, out var clip)) state.motion = clip;
                else Debug.LogWarning($"[MoveIt] Dino.fbx has no '{name}' clip");
                if (name == "Idle") machine.defaultState = state;
            }
            return controller;
        }

        static Material Mat(string folder, string name, Color color, float emission = 0f, float smoothness = 0.2f)
        {
            string path = $"{folder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            if (emission > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", color * emission);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
