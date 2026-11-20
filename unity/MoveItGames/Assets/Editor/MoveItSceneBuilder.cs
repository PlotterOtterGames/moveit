using System.Linq;
using MoveIt.Games;
using MoveIt.Games.Arcade;
using MoveIt.Games.Bowling;
using MoveIt.Games.Dress;
using MoveIt.Games.Fairy;
using MoveIt.Games.Boxing;
using MoveIt.Games.Kart;
using MoveIt.Games.Paint;
using MoveIt.Games.Runner;
using MoveIt.Games.Space;
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
        const string BoxingScenePath = "Assets/Games/Boxing/Boxing.unity";
        const string BoxingMaterialsDir = "Assets/Games/Boxing/Materials";
        const string TennisScenePath = "Assets/Games/Tennis/Tennis.unity";
        const string TennisMaterialsDir = "Assets/Games/Tennis/Materials";
        const string SportsScenePath = "Assets/Games/Sports/Sports.unity";
        const string VolleyScenePath = "Assets/Games/Volley/Volley.unity";
        const string VolleyMaterialsDir = "Assets/Games/Volley/Materials";
        const string KartScenePath = "Assets/Games/Kart/Kart.unity";
        const string KartMaterialsDir = "Assets/Games/Kart/Materials";
        const string KartArtDir = "Assets/Games/Kart/Art";
        const string ArcadeScenePath = "Assets/Games/Arcade/Arcade.unity";
        const string ArcadeMaterialsDir = "Assets/Games/Arcade/Materials";
        const string ArcadeArtDir = "Assets/Games/Arcade/Art";
        const string SpaceScenePath = "Assets/Games/Space/Space.unity";
        const string SpaceMaterialsDir = "Assets/Games/Space/Materials";
        const string SpaceArtDir = "Assets/Games/Space/Art";
        const string FairyScenePath = "Assets/Games/Fairy/Fairy.unity";
        const string FairyMaterialsDir = "Assets/Games/Fairy/Materials";
        const string FairyArtDir = "Assets/Games/Fairy/Art";
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
            BuildBoxingScene();
            BuildTennisScene();
            BuildVolleyScene();
            BuildKartScene();
            BuildArcadeScene();
            BuildSpaceScene();
            BuildFairyScene();
            BuildSportsScene();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(MenuScenePath, true),
                new EditorBuildSettingsScene(RunnerScenePath, true),
                new EditorBuildSettingsScene(DressScenePath, true),
                new EditorBuildSettingsScene(BowlingScenePath, true),
                new EditorBuildSettingsScene(PaintScenePath, true),
                new EditorBuildSettingsScene(SwordScenePath, true),
                new EditorBuildSettingsScene(BoxingScenePath, true),
                new EditorBuildSettingsScene(TennisScenePath, true),
                new EditorBuildSettingsScene(VolleyScenePath, true),
                new EditorBuildSettingsScene(KartScenePath, true),
                new EditorBuildSettingsScene(ArcadeScenePath, true),
                new EditorBuildSettingsScene(SpaceScenePath, true),
                new EditorBuildSettingsScene(FairyScenePath, true),
                new EditorBuildSettingsScene(SportsScenePath, true),
                new EditorBuildSettingsScene(MotionDebugScenePath, true),
            };
            PlayerSettings.runInBackground = true;  // keep receiving motion data when another window has focus
            BoxBuild.ApplyBranding();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MenuScenePath);
            Debug.Log("[MoveIt] Built Menu, Runner, Dress, Bowling, Paint, Sword, Boxing, Tennis, Volley, Kart, Arcade, Space, Fairy, Sports and MotionDebug scenes.");
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
            Set(game, "mirrorMaterial", ShaderMat(DressMaterialsDir, "Mirror", "Tundu/Mirror"));
            Set(game, "mirrorPrepMaterial", ShaderMat(DressMaterialsDir, "MirrorPrep", "Hidden/Tundu/MirrorPrep"));
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

        /// <summary>
        /// Lunapark!: an orthographic camera in HUD pixels (1080 high), like Vishu!; the game draws the camera picture, the
        /// sprites (three batches from one sheet: alpha, additive, the HUD's) and Muri!'s wall itself. Art from tools/arcade.
        /// </summary>
        [MenuItem("MoveIt/Build Arcade Scene")]
        public static void BuildArcadeScene()
        {
            EnsureFolder(ArcadeMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = CreateCamera(Hex("#141938"));
            cam.orthographic = true;
            cam.orthographicSize = 540f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;
            cam.transform.position = new Vector3(960f, 540f, -10f);
            CreateMotionRig(fullScreenOverlay: false);
            var game = new GameObject("Arcade").AddComponent<ArcadeGame>();
            Set(game, "cam", cam);
            var sprites = ShaderMat(ArcadeMaterialsDir, "Sprites", "Tundu/Fx");
            sprites.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            sprites.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            sprites.SetFloat("_ZTest", (float)CompareFunction.Always);
            var glow = ShaderMat(ArcadeMaterialsDir, "Glow", "Tundu/Fx");
            glow.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            glow.SetFloat("_DstBlend", (float)BlendMode.One);
            glow.SetFloat("_ZTest", (float)CompareFunction.Always);
            Set(game, "spriteMaterial", sprites);
            Set(game, "glowMaterial", glow);
            Set(game, "pictureMaterial", ShaderMat(ArcadeMaterialsDir, "Picture", "Tundu/Fx"));
            Set(game, "wallMaterial", ShaderMat(ArcadeMaterialsDir, "Wall", "Tundu/Wall"));
            Set(game, "sheetJson", AssetDatabase.LoadAssetAtPath<TextAsset>(ArcadeArtDir + "/arcade_sprites.json"));
            foreach (var (field, file) in new[] { ("sheet", "arcade_sprites"), ("wallBoard", "arcade_wall") })
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ArcadeArtDir}/{file}.png");
                if (tex == null) Debug.LogWarning($"[MoveIt] {ArcadeArtDir}/{file}.png is missing: run tools/arcade/art.py");
                Set(game, field, tex);
            }
            EditorSceneManager.SaveScene(scene, ArcadeScenePath);
        }

        /// <summary>
        /// Yjet!: the world is built by the game from code (SpaceWorld: the planets from tools/space's maps, art/space's
        /// props, the star field, the far pictures); a night sky (Tundu/Sky in the deep blues, no fog: space is clear), the
        /// sun from behind the camera on the left (the planets lit with a crescent of night on their right), no real-time
        /// shadows, gentle post-processing (the box has none), the chase camera placed every frame.
        /// </summary>
        [MenuItem("MoveIt/Build Space Scene")]
        public static void BuildSpaceScene()
        {
            EnsureFolder(SpaceMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // tools/space/sky.py's painted galaxy round the whole trip (the game turns and tints a copy for each system)
            var sky = ShaderMat(SpaceMaterialsDir, "SpaceSky", "Tundu/SkyPanorama");
            sky.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{SpaceArtDir}/space_sky.png"));
            sky.SetFloat("_Exposure", 1f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            // the nebulae's light all round: violet and lilac (the toys' shade sides are never grey)
            RenderSettings.ambientSkyColor = Hex("#6A6AC8");
            RenderSettings.ambientEquatorColor = Hex("#4A3F96");
            RenderSettings.ambientGroundColor = Hex("#2C2266");
            // the system's sun lights the astronauts (the game turns it from the star every frame)
            CreateLight(Quaternion.LookRotation(new Vector3(0.45f, -0.45f, 0.77f)), 1.25f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFF2DC");

            var cam = CreateCamera(Hex("#090C29"));
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 9000f;
            cam.fieldOfView = 60f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 2.4f, -7f), Quaternion.Euler(8f, 0f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var post = new GameObject("Post Processing").AddComponent<Volume>();
            post.isGlobal = true;
            post.sharedProfile = PostProfile("Assets/Games/Space/SpacePost.asset", contrast: 6f, saturation: 8f, exposure: 0f, vignette: 0.18f);

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Space").AddComponent<SpaceGame>();
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, additive: true, folder: SpaceMaterialsDir));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, additive: true, folder: SpaceMaterialsDir));
            T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
            var palette = Load<Texture2D>($"{SpaceArtDir}/tundu_palette.png");
            var heights = Load<Texture2DArray>($"{SpaceArtDir}/planet_heights.png");
            if (heights == null) Debug.LogWarning($"[MoveIt] {SpaceArtDir}/planet_heights.png is missing or not an array: run tools/space/heights.py");
            Set(game, "art.props", Load<GameObject>($"{SpaceArtDir}/SpaceProps.fbx"));
            // every planet and moon: one material, each one's look in a property block
            var planet = ShaderMat(SpaceMaterialsDir, "ProcPlanet", "Tundu/ProcPlanet");
            planet.SetTexture("_Heights", heights);
            planet.SetColor("_ShadeColor", Hex("#6E6CB4"));
            planet.SetFloat("_Ambient", 0.3f);
            planet.SetFloat("_Softness", 0.26f);
            planet.SetColor("_EdgeColor", new Color(1f, 0.6f, 0.45f, 0.3f));
            EditorUtility.SetDirty(planet);
            Set(game, "art.planet", planet);
            Set(game, "art.halo", ShaderMat(SpaceMaterialsDir, "Halo", "Tundu/Halo"));
            var star = ShaderMat(SpaceMaterialsDir, "Star", "Tundu/Star");
            star.SetTexture("_Heights", heights);
            EditorUtility.SetDirty(star);
            Set(game, "art.star", star);
            Set(game, "art.layer", ShaderMat(SpaceMaterialsDir, "PlanetLayer", "Tundu/PlanetLayer"));
            var dust = ShaderMat(SpaceMaterialsDir, "Dust", "Tundu/Dust");
            dust.SetColor("_Color", new Color(0.5f, 0.58f, 0.78f, 1f));
            dust.SetFloat("_Size", 0.07f);
            dust.SetFloat("_Box", 70f);
            EditorUtility.SetDirty(dust);
            Set(game, "art.dust", dust);
            Set(game, "art.sky", sky);
            Set(game, "art.glass", ShaderMat(SpaceMaterialsDir, "Glass", "Tundu/Glass"));
            Set(game, "art.flame", FxMat("Flame", null, Color.white, additive: true, folder: SpaceMaterialsDir));
            Set(game, "art.glow", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: SpaceMaterialsDir));
            // the belts' rocks: glossy toys, shade tinted violet, a cool rim of light from the nebulae
            var toon = ToonIn(SpaceMaterialsDir, "Props", gloss: 0.45f, rim: 0.5f, vertex: false);
            toon.SetTexture("_BaseMap", palette);
            toon.SetFloat("_Ambient", 0.5f);
            toon.SetFloat("_GlossPower", 26f);
            toon.SetColor("_ShadeColor", Hex("#7568D6"));
            toon.SetColor("_RimColor", new Color(0.62f, 0.72f, 1f, 0.55f));
            toon.SetFloat("_RimPower", 2.6f);
            toon.enableInstancing = true;
            EditorUtility.SetDirty(toon);
            Set(game, "art.toon", toon);
            Set(game, "art.heights", heights);
            var ui = Load<Texture2D>($"{SpaceArtDir}/space_ui.png");
            Set(game, "art.clouds", Load<Texture2D>($"{SpaceArtDir}/clouds.png"));
            Set(game, "art.rings", Load<Texture2D>($"{SpaceArtDir}/rings.png"));
            Set(game, "art.ui", ui);
            Set(game, "art.uiJson", Load<TextAsset>($"{SpaceArtDir}/space_ui.json"));
            if (ui == null) Debug.LogWarning($"[MoveIt] {SpaceArtDir}/space_ui.png is missing: run tools/space/art.py");
            EditorSceneManager.SaveScene(scene, SpaceScenePath);
        }

        /// <summary>
        /// Zana!'s garden defence: the garden is built by the game from code (GardenWorld: the sky dome, the meadow and its
        /// cobbled paths, art/fairy's garden kit instanced, the gloom as the tree is hurt; GloomViews: the creatures put
        /// together from art/fairy's kit), the fairies are art/fairy's tall heroine with wings made in code. Here: the
        /// materials (the look's overhaul of 2026-11-17: everything Tundu/Realm, lit per vertex, its colours and light set by
        /// GardenWorld at runtime), the late-afternoon sun, no real-time shadows and no post-processing (the box has none:
        /// the editor shows what the box shows), the camera placed every frame.
        /// </summary>
        [MenuItem("MoveIt/Build Fairy Scene")]
        public static void BuildFairyScene()
        {
            EnsureFolder(FairyMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#FFE6DA");
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 260f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("#B4CCFF");
            RenderSettings.ambientEquatorColor = Hex("#FFD8D4");
            RenderSettings.ambientGroundColor = Hex("#84C278");
            CreateLight(Quaternion.LookRotation(GardenWorld.SunTravel), 1.12f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFE6C2");

            var cam = CreateCamera(Hex("#FFE6D2"));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.nearClipPlane = 0.2f;
            cam.farClipPlane = 300f;
            cam.fieldOfView = 50f;
            cam.transform.SetPositionAndRotation(new Vector3(0f, 2.6f, -6.5f), Quaternion.Euler(0f, 0f, 0f));
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Fairy").AddComponent<FairyGame>();
            T Load<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path);
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, additive: true, folder: FairyMaterialsDir));
            var dot = FxMat("Dot", "fx_dot.png", Color.white, additive: true, folder: FairyMaterialsDir);
            Set(game, "dotMaterial", dot);
            Set(game, "wingMaterial", ShaderMat(FairyMaterialsDir, "Wing", "Tundu/Wing"));
            // the garden (art/fairy/props.py: vertex colours, no textures) in Tundu/Realm's variants
            var props = Load<GameObject>($"{FairyArtDir}/FairyProps.fbx");
            if (props == null) Debug.LogWarning($"[MoveIt] {FairyArtDir}/FairyProps.fbx is missing: run art/fairy/build.py -- --props --ao --export");
            Set(game, "art.props", props);
            Material Realm(string name, System.Action<Material> setup)
            {
                var m = ShaderMat(FairyMaterialsDir, name, "Tundu/Realm");
                foreach (var k in new[] { "_GROUND", "_SKY" }) m.DisableKeyword(k);
                m.SetFloat("_Cull", (float)CullMode.Back);
                m.SetFloat("_FogScale", 1f);
                m.SetFloat("_Sway", 1f);
                m.renderQueue = -1;
                m.enableInstancing = true;
                setup(m);
                EditorUtility.SetDirty(m);
                return m;
            }
            var detail = Load<Texture2D>($"{FairyArtDir}/garden_detail.png");
            if (detail == null) Debug.LogWarning($"[MoveIt] {FairyArtDir}/garden_detail.png is missing: run tools/fairy/ground.py");
            Set(game, "art.ground", Realm("Ground", m =>
            {
                m.EnableKeyword("_GROUND");
                m.enableInstancing = false;
                m.SetTexture("_DetailMap", detail);
                m.SetFloat("_DetailScale", 0.16f);
                m.SetFloat("_DetailStrength", 0.5f);
                m.SetColor("_PathColor", Hex("#F4D9B0"));
                m.SetColor("_PathShade", Hex("#B0857E"));
                m.SetFloat("_AOStrength", 1f);
                m.SetColor("_RimColor", new Color(1f, 0.93f, 0.78f, 0.12f));
            }));
            Set(game, "art.prop", Realm("Props", m =>
            {
                m.SetFloat("_AOStrength", 0.62f);
                m.SetFloat("_Ambient", 0.48f);
            }));
            Set(game, "art.grass", Realm("Grass", m =>
            {
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.SetFloat("_AOStrength", 0.9f);
                m.SetColor("_RimColor", new Color(1f, 0.95f, 0.75f, 0.2f));
            }));
            Set(game, "art.cloud", Realm("Clouds", m =>
            {
                m.SetFloat("_FogScale", 0.32f);
                m.SetFloat("_Sway", 0f);
                m.SetFloat("_Ambient", 0.62f);
                m.SetFloat("_Softness", 0.3f);
                m.SetColor("_ShadeColor", Hex("#C9C2F2"));
                m.SetColor("_RimColor", new Color(1f, 0.97f, 0.9f, 0.5f));
            }));
            Set(game, "art.sky", Realm("SkyDome", m =>
            {
                m.EnableKeyword("_SKY");
                m.enableInstancing = false;
                m.SetFloat("_Cull", (float)CullMode.Off);
                m.renderQueue = 2499;   // after the garden: only where nothing else is drawn
            }));
            Set(game, "art.glow", dot);
            Set(game, "art.shadow", FxMat("Shadow", "fx_dot.png", Color.white, additive: false, folder: FairyMaterialsDir));
            // the creatures: the kit, where its parts go, the gloom shader, the pixie's bubble, the shapes' bubbles
            var ui = Load<Texture2D>($"{FairyArtDir}/fairy_ui.png");
            if (ui == null) Debug.LogWarning($"[MoveIt] {FairyArtDir}/fairy_ui.png is missing: run tools/fairy/art.py");
            Set(game, "gloom.kit", Load<GameObject>($"{FairyArtDir}/GloomKit.fbx"));
            Set(game, "gloom.anchors", Load<TextAsset>($"{FairyArtDir}/gloom_kit.json"));
            Set(game, "gloom.gloom", ShaderMat(FairyMaterialsDir, "Gloom", "Tundu/Gloom"));
            Set(game, "gloom.bubble", ShaderMat(FairyMaterialsDir, "Bubble", "Tundu/Bubble"));
            var runes = FxMat("Runes", null, Color.white, additive: false, folder: FairyMaterialsDir);
            runes.SetTexture("_BaseMap", ui);
            EditorUtility.SetDirty(runes);
            Set(game, "gloom.runes", runes);
            Set(game, "gloom.glow", dot);
            Set(game, "ui", ui);
            Set(game, "uiJson", Load<TextAsset>($"{FairyArtDir}/fairy_ui.json"));
            EditorSceneManager.SaveScene(scene, FairyScenePath);
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
            var cam = CreateCamera(Hex("#141938"));   // the brand's Night around the paper (PaintGame sets it per mode)
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
        /// Superboks! (the overhaul of 2026-11-19): the four arenas and their rings (art/boxing/arena.py's Arena.fbx, lit at
        /// the vertices by Tundu/Arena; <see cref="BoxingWorld"/> shows one stage at a time), the boxers' gloves and the
        /// belt (Boxing.fbx), the HUD's sheet (tools/boxing/art.py), the see-through hero's materials, the camera behind the
        /// hero (<see cref="BoxingGame"/> moves it). No shadows and no post-processing: the box draws neither, so the editor
        /// shows what the box shows.
        /// </summary>
        [MenuItem("MoveIt/Build Boxing Scene")]
        public static void BuildBoxingScene()
        {
            EnsureFolder(BoxingMaterialsDir);
            var background = Hex("#120E1E");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#2A2238");
            RenderSettings.fogStartDistance = 12f;
            RenderSettings.fogEndDistance = 54f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Hex("#8A7AA8");
            CreateLight(Quaternion.Euler(58f, 18f, 0f), 1.15f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFE6C8");

            var cam = CreateCamera(background);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.08f;
            cam.farClipPlane = 420f;
            cam.fieldOfView = BoxingGame.FightFov;
            cam.transform.position = BoxingGame.FightCamera + Vector3.up * BoxingWorld.RingHeight;
            cam.transform.LookAt(BoxingGame.FightLook + Vector3.up * BoxingWorld.RingHeight);
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            CreateMotionRig(fullScreenOverlay: false);

            var world = new GameObject("World").AddComponent<BoxingWorld>();
            var game = new GameObject("Boxing").AddComponent<BoxingGame>();
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "world", world);
            // the kit (art/boxing/build.py --kit): the gloves and the belt, coloured by region with Tundu/Athlete
            const string kitFbx = "Assets/Games/Boxing/Art/Boxing.fbx";
            var kit = AssetDatabase.LoadAllAssetsAtPath(kitFbx).OfType<Mesh>().ToArray();
            foreach (var (field, name) in new[] { ("glove", "Glove"), ("belt", "Belt") })
            {
                var mesh = kit.FirstOrDefault(m => m.name == name);
                if (mesh == null) Debug.LogError($"[MoveIt] No {name} in {kitFbx}: run art/boxing/build.py -- --kit --ao --export");
                Set(game, "art." + field, mesh);
            }
            // the arenas (art/boxing/build.py --arena): every stage's pieces and rings, the crowd's seats
            const string arenaFbx = "Assets/Games/Boxing/Art/Arena.fbx";
            var arena = AssetDatabase.LoadAllAssetsAtPath(arenaFbx).OfType<Mesh>().OrderBy(m => m.name).ToArray();
            if (arena.Length == 0) Debug.LogError($"[MoveIt] No meshes in {arenaFbx}: run art/boxing/build.py -- --arena --ao --export");
            SetArray(game, "art.kit", arena);
            var seats = AssetDatabase.FindAssets("arena_seats_ t:TextAsset", new[] { "Assets/Games/Boxing/Art" })
                .Select(g => AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(g))).Where(t => t != null).ToArray();
            SetArray(game, "art.seats", seats);
            var athlete = AthleteMat(BoxingMaterialsDir);
            Set(game, "art.athlete", athlete);
            Set(game, "art.cape", athlete);
            var arenaMat = ShaderMat(BoxingMaterialsDir, "Arena", "Tundu/Arena");
            arenaMat.DisableKeyword("_SKY");
            arenaMat.DisableKeyword("_LITE");
            EditorUtility.SetDirty(arenaMat);
            var arenaLite = ShaderMat(BoxingMaterialsDir, "ArenaLite", "Tundu/Arena");
            arenaLite.DisableKeyword("_SKY");
            arenaLite.EnableKeyword("_LITE");
            EditorUtility.SetDirty(arenaLite);
            // the sky after everything opaque: only where nothing else is
            var arenaSky = ShaderMat(BoxingMaterialsDir, "ArenaSky", "Tundu/Arena");
            arenaSky.EnableKeyword("_SKY");
            arenaSky.DisableKeyword("_LITE");
            arenaSky.SetFloat("_Cull", 0f);
            arenaSky.renderQueue = 2499;
            EditorUtility.SetDirty(arenaSky);
            var arenaGlow = ShaderMat(BoxingMaterialsDir, "ArenaGlow", "Tundu/ArenaGlow");
            Set(game, "art.arena", arenaMat);
            Set(game, "art.arenaLite", arenaLite);
            Set(game, "art.arenaSky", arenaSky);
            Set(game, "art.arenaGlow", arenaGlow);
            Set(game, "art.glow", FxMat("Glow", "fx_dot.png", Color.white, additive: true, folder: BoxingMaterialsDir));
            var crowd = ShaderMat(BoxingMaterialsDir, "Crowd", "Tundu/Crowd");
            crowd.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Games/Sports/Art/crowd_atlas.png"));
            Set(game, "art.crowd", crowd);
            // the see-through hero: the depth first (queue 2999), then its colour once (3000), like Shpata!'s ghost
            var ghostDepth = ShaderMat(BoxingMaterialsDir, "GhostDepth", "Tundu/AthleteGhost");
            ghostDepth.SetFloat("_ColorMask", 0f);
            ghostDepth.SetFloat("_ZWrite", 1f);
            ghostDepth.renderQueue = 2999;
            EditorUtility.SetDirty(ghostDepth);
            var ghostColour = ShaderMat(BoxingMaterialsDir, "GhostColour", "Tundu/AthleteGhost");
            ghostColour.SetFloat("_ColorMask", 15f);
            ghostColour.SetFloat("_ZWrite", 0f);
            ghostColour.renderQueue = 3000;
            EditorUtility.SetDirty(ghostColour);
            Set(game, "art.ghostDepth", ghostDepth);
            Set(game, "art.ghostColour", ghostColour);
            // the HUD's sheet, and the comic bursts drawn from it in the world (alpha, over the boxers)
            var sheet = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Games/Boxing/Art/boxing_ui.png");
            if (sheet == null) Debug.LogError("[MoveIt] No boxing_ui.png: run tools/boxing/art.py");
            Set(game, "art.sheet", sheet);
            Set(game, "art.sheetJson", AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Games/Boxing/Art/boxing_ui.json"));
            var burst = ShaderMat(BoxingMaterialsDir, "Burst", "Tundu/Fx");
            burst.SetTexture("_BaseMap", sheet);
            burst.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            burst.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            burst.renderQueue = 3100;
            EditorUtility.SetDirty(burst);
            Set(game, "art.burst", burst);
            Set(game, "portraitMaterial", ShaderMat(BoxingMaterialsDir, "Portrait", "Tundu/Portrait"));
            Set(game, "sparkMaterial", FxMat("Spark", "fx_star.png", Color.white, folder: BoxingMaterialsDir));
            Set(game, "dotMaterial", FxMat("Dot", "fx_dot.png", Color.white, folder: BoxingMaterialsDir));

            EditorSceneManager.SaveScene(scene, BoxingScenePath);
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
            Set(game, "world.dinoMaterial", HeroMat());
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
            // Vrapo!, the console polish of 2026-11-18: art/runner's valley kit and Dino, lit per vertex (Tundu/Valley) and
            // Dino per pixel (Tundu/Hero); the world, the camera's shots, the time of day and the HUD are built at runtime
            EnsureFolder(RunnerMaterialsDir);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;
            RenderSettings.ambientMode = AmbientMode.Trilight;

            CreateLight(Quaternion.Euler(40f, 30f, 0f), 1.1f, shadows: false);
            var sun = Object.FindAnyObjectByType<Light>();
            sun.color = Hex("#FFE8C6");

            var cam = CreateCamera(Hex("#CFE8FA"));
            cam.transform.position = new Vector3(0f, 3.4f, -6.5f);
            cam.transform.rotation = Quaternion.Euler(14f, 0f, 0f);
            cam.farClipPlane = 620f;
            cam.fieldOfView = 60f;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            cam.GetUniversalAdditionalCameraData().renderShadows = false;

            CreateMotionRig(fullScreenOverlay: false);

            var game = new GameObject("Runner").AddComponent<RunnerGame>();
            var fx = game.gameObject.AddComponent<RunnerFx>();
            var playerGo = new GameObject("Player");
            var player = playerGo.AddComponent<RunnerPlayer>();
            var dinoModel = AssetDatabase.LoadAssetAtPath<GameObject>(DinoModelPath);
            if (dinoModel != null)
            {
                // Dino from art/runner (Blender): his own vertex colours, lit per pixel
                var dino = (GameObject)PrefabUtility.InstantiatePrefab(dinoModel);
                dino.name = "Dino";
                dino.transform.SetParent(playerGo.transform, false);
                var hero = HeroMat();
                foreach (var r in dino.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial = hero;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                foreach (var smr in dino.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    smr.updateWhenOffscreen = true;    // the curved world moves him off his bounds
                    smr.quality = SkinQuality.Bone4;
                }
                var animator = dino.GetComponent<Animator>();
                if (animator == null) animator = dino.AddComponent<Animator>();   // (not ??: Unity's fake null)
                animator.avatar = AssetDatabase.LoadAllAssetsAtPath(DinoModelPath).OfType<Avatar>().FirstOrDefault();
                animator.runtimeAnimatorController = DinoController();
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                Set(player, "visual", dino.transform);
                Set(player, "animator", animator);
            }

            Set(game, "player", player);
            Set(game, "cam", cam);
            Set(game, "sun", sun);
            Set(game, "fx", fx);
            Set(game, "art.kit", Art("Valley"));
            Set(game, "art.prop", ValleyMat("Valley"));
            Set(game, "art.ground", ValleyMat("ValleyGround", ground: true));
            Set(game, "art.sky", ValleyMat("ValleySky", sky: true));
            Set(game, "art.shadow", ValleyFxMat("ValleyShadow", "fx_dot.png", new Color(0.12f, 0.1f, 0.28f, 1f), additive: false));
            Set(game, "art.glow", ValleyFxMat("ValleyGlow", "fx_dot.png", Color.white, additive: true));
            Set(game, "hudSheet", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RunnerArtDir}/runner_ui.png"));
            Set(game, "hudJson", AssetDatabase.LoadAssetAtPath<TextAsset>($"{RunnerArtDir}/runner_ui.json"));

            Set(fx, "dustMaterial", ValleyFxMat("ValleyDust", "fx_dot.png", Color.white, additive: false));
            Set(fx, "starMaterial", ValleyFxMat("ValleyStar", "fx_sparkle.png", Color.white, additive: true));
            Set(fx, "streakMaterial", ValleyFxMat("ValleyStreak", "fx_streak.png", Color.white, additive: true));
            Set(fx, "leafMaterial", ValleyFxMat("ValleyLeaf", "fx_leaf.png", Color.white, additive: false));
            Set(fx, "glowMaterial", ValleyFxMat("ValleyFirefly", "fx_dot.png", Color.white, additive: true));

            EditorSceneManager.SaveScene(scene, RunnerScenePath);
        }

        /// <summary>Tundu/Valley for Vrapo!'s valley (instanced; the world makes its variants at runtime): the props, or the
        /// floor (_GROUND, with tools/runner/ground.py's detail), or the sky dome (_SKY).</summary>
        static Material ValleyMat(string name, bool ground = false, bool sky = false)
        {
            string path = $"{RunnerMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/Valley");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.enableInstancing = true;
            mat.shaderKeywords = new string[0];
            if (ground)
            {
                mat.EnableKeyword("_GROUND");
                mat.SetTexture("_DetailMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{RunnerArtDir}/ground_detail.png"));
                mat.SetVector("_DetailScale", new Vector4(1f / 4.4f, 1f / 5f, 0f, 0f));
            }
            if (sky) mat.EnableKeyword("_SKY");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/ValleyFx: Vrapo!'s particles and soft shadows, on the curved world.</summary>
        static Material ValleyFxMat(string name, string texture, Color tint, bool additive)
        {
            string path = $"{RunnerMaterialsDir}/{name}.mat";
            var shader = Shader.Find("Tundu/ValleyFx");
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
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Tundu/Hero: Dino's material (his vertex colours, AO, gloss and glow), Vrapo!'s and Gara!'s dino.</summary>
        static Material HeroMat()
        {
            string path = $"{RunnerMaterialsDir}/Hero.mat";
            var shader = Shader.Find("Tundu/Hero");
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
            foreach (var name in new[] { "Idle", "LookAround", "Wave", "Ready", "Run", "Jump", "Flip", "Slide", "Hit", "Dizzy", "Cheer", "Dance" })
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
