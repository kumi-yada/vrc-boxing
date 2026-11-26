#if UNITY_EDITOR
using System;
using System.IO;
using TMPro;
using UdonSharp;
using UdonSharp.Compiler;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// Generates the placeholder Combo Cutter booth prefab (Assets/Scenes/ComboStation.prefab)
// and drops one instance into the world scene if it is missing. Rerunnable: the
// prefab is rebuilt from scratch and the scene instance keeps its overrides.
//
// Headless:
// Unity -batchmode -nographics -quit -projectPath <project> \
//   -executeMethod ComboStationBuilder.BuildFromCommandLine -logFile /tmp/combo-build.log
public static class ComboStationBuilder
{
    private const string PrefabPath = "Assets/Scenes/ComboStation.prefab";
    private const string MaterialDir = "Assets/Materials/ComboStation";
    private const string ScenePath = "Assets/VRCDefaultWorldScene.unity";
    private static readonly Vector3 ScenePosition = new Vector3(3f, 0f, 1.02f);

    private static readonly Color Cyan = new Color(0.1f, 0.95f, 1f, 1f);

    [MenuItem("Tools/Combo Station/Build Prefab")]
    public static void BuildMenu()
    {
        Build(true);
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            EnsureProgramAssets();
            UdonSharpCompilerV1.CompileSync();
            Build(true);
            AssetDatabase.SaveAssets();
            Debug.Log("[ComboStationBuilder] Build finished.");
            EditorApplication.Exit(0);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ComboStationBuilder] Build failed.\n{ex}");
            EditorApplication.Exit(1);
        }
    }

    private static void EnsureProgramAssets()
    {
        Type[] types =
        {
            typeof(ComboStationManager), typeof(ComboStationTarget), typeof(ComboHoloCoach),
            typeof(ComboMarquee), typeof(ComboCuePlayer),
        };
        bool created = false;
        foreach (Type t in types)
        {
            if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(t) != null) continue;
            string scriptPath = $"Assets/Scripts/ComboStation/{t.Name}.cs";
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (script == null) throw new Exception("Missing script " + scriptPath);
            UdonSharpProgramAsset asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
            asset.sourceCsScript = script;
            AssetDatabase.CreateAsset(asset, Path.ChangeExtension(scriptPath, ".asset"));
            created = true;
        }
        if (created) AssetDatabase.Refresh();
    }

    private static void Build(bool placeInScene)
    {
        Directory.CreateDirectory(MaterialDir);
        Material holo = FadeMaterial("Holo", new Color(0.2f, 0.85f, 1f, 0.35f), 0.8f);
        Material zoneMat = FadeMaterial("Zone", new Color(0.1f, 0.3f, 0.35f, 0.15f), 1f);
        Material beam = FadeMaterial("Beam", new Color(0.2f, 0.85f, 1f, 0.06f), 0.3f);
        Material padMat = FadeMaterial("RestartPad", new Color(0.1f, 0.3f, 0.35f, 0.15f), 1f);
        Material projector = OpaqueMaterial("Projector", new Color(0.05f, 0.1f, 0.12f), Cyan * 1.5f);
        Material matMat = OpaqueMaterial("StanceMat", new Color(0.06f, 0.06f, 0.1f), new Color(0.05f, 0.25f, 0.3f));
        Material board = OpaqueMaterial("Board", new Color(0.03f, 0.03f, 0.05f), Color.black);
        Material card = OpaqueMaterial("Card", new Color(0.15f, 0.15f, 0.2f), new Color(0.03f, 0.03f, 0.04f));

        GameObject root = new GameObject("ComboStation");
        try
        {
            ComboStationManager manager = UdonSharpUndo.AddComponent<ComboStationManager>(root);
            ComboCuePlayer cues = UdonSharpUndo.AddComponent<ComboCuePlayer>(root);
            AudioSource audio = root.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 1.5f;
            audio.maxDistance = 10f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            WireCues(cues, audio);

            GameObject mat = Primitive(PrimitiveType.Cylinder, "StanceMat", root.transform,
                new Vector3(0f, 0.005f, 0f), Vector3.zero, new Vector3(1.2f, 0.005f, 1.2f), matMat, false);

            Primitive(PrimitiveType.Cylinder, "ProjectorDisc", root.transform,
                new Vector3(0f, 0.02f, 0.9f), Vector3.zero, new Vector3(0.7f, 0.02f, 0.7f), projector, false);
            Primitive(PrimitiveType.Cylinder, "ProjectorBeam", root.transform,
                new Vector3(0f, 0.5f, 0.9f), Vector3.zero, new Vector3(0.55f, 0.48f, 0.55f), beam, false);

            // the coach faces -z, toward the player standing on the mat at the origin
            GameObject coachGo = new GameObject("HoloCoach");
            coachGo.transform.SetParent(root.transform, false);
            coachGo.transform.localPosition = new Vector3(0f, 0f, 0.9f);
            GameObject body = new GameObject("Body");
            body.transform.SetParent(coachGo.transform, false);
            Transform b = body.transform;
            GameObject torso = Primitive(PrimitiveType.Capsule, "Torso", b, new Vector3(0f, 1.25f, 0f), Vector3.zero, new Vector3(0.42f, 0.32f, 0.24f), holo, false);
            GameObject shoulders = Primitive(PrimitiveType.Capsule, "Shoulders", b, new Vector3(0f, 1.46f, 0f), new Vector3(0f, 0f, 90f), new Vector3(0.15f, 0.27f, 0.15f), holo, false);
            GameObject neck = new GameObject("Neck");
            neck.transform.SetParent(b, false);
            neck.transform.localPosition = new Vector3(0f, 1.55f, 0f);
            GameObject headGo = Primitive(PrimitiveType.Sphere, "Head", neck.transform, new Vector3(0f, 0.14f, 0f), Vector3.zero, new Vector3(0.2f, 0.24f, 0.21f), holo, false);
            GameObject armL = Primitive(PrimitiveType.Capsule, "ForearmL", b, new Vector3(-0.2f, 1.42f, -0.14f), new Vector3(-60f, 0f, -12f), new Vector3(0.09f, 0.14f, 0.09f), holo, false);
            GameObject armR = Primitive(PrimitiveType.Capsule, "ForearmR", b, new Vector3(0.2f, 1.42f, -0.14f), new Vector3(-60f, 0f, 12f), new Vector3(0.09f, 0.14f, 0.09f), holo, false);
            GameObject gloveL = Primitive(PrimitiveType.Sphere, "GloveL", b, new Vector3(-0.15f, 1.56f, -0.22f), Vector3.zero, new Vector3(0.12f, 0.13f, 0.13f), holo, false);
            GameObject gloveR = Primitive(PrimitiveType.Sphere, "GloveR", b, new Vector3(0.15f, 1.56f, -0.22f), Vector3.zero, new Vector3(0.12f, 0.13f, 0.13f), holo, false);

            ComboHoloCoach coach = UdonSharpUndo.AddComponent<ComboHoloCoach>(coachGo);
            coach.body = b;
            coach.head = neck.transform;
            coach.bodyRenderers = new Renderer[]
            {
                torso.GetComponent<Renderer>(), shoulders.GetComponent<Renderer>(), headGo.GetComponent<Renderer>(),
                armL.GetComponent<Renderer>(), armR.GetComponent<Renderer>(),
                gloveL.GetComponent<Renderer>(), gloveR.GetComponent<Renderer>(),
            };

            GameObject zoneGo = Primitive(PrimitiveType.Cube, "TargetZone", b, new Vector3(0f, 1.3f, -0.17f), Vector3.zero, new Vector3(0.34f, 0.3f, 0.06f), zoneMat, true);
            ComboStationTarget zone = PunchableTarget(zoneGo, manager);

            GameObject padGo = Primitive(PrimitiveType.Cylinder, "RestartPad", root.transform, new Vector3(0.85f, 1.1f, 0.45f), new Vector3(90f, 62f, 0f), new Vector3(0.28f, 0.03f, 0.28f), padMat, true);
            ComboStationTarget pad = PunchableTarget(padGo, manager);
            pad.isRestartPad = true;
            Text("RestartLabel", root.transform, new Vector3(0.85f, 1.33f, 0.45f), new Vector3(0f, 62f, 0f), "RESTART", 0.6f, new Vector2(0.6f, 0.15f));

            GameObject marqueeGo = new GameObject("Marquee");
            marqueeGo.transform.SetParent(root.transform, false);
            marqueeGo.transform.localPosition = new Vector3(0f, 2.3f, 1.15f);
            Primitive(PrimitiveType.Cube, "Backboard", marqueeGo.transform, new Vector3(0f, 0f, 0.03f), Vector3.zero, new Vector3(1.5f, 0.75f, 0.03f), board, false);
            TMP_Text title = Text("Title", marqueeGo.transform, new Vector3(0f, 0.25f, 0f), Vector3.zero, "", 0.8f, new Vector2(1.4f, 0.22f));
            ComboMarquee marquee = UdonSharpUndo.AddComponent<ComboMarquee>(marqueeGo);
            marquee.title = title;
            marquee.cards = new Transform[3];
            marquee.cardFaces = new Renderer[3];
            marquee.cardIcons = new TMP_Text[3];
            marquee.cardLabels = new TMP_Text[3];
            for (int i = 0; i < 3; i++)
            {
                GameObject cardRoot = new GameObject("Card" + (i + 1));
                cardRoot.transform.SetParent(marqueeGo.transform, false);
                cardRoot.transform.localPosition = new Vector3((i - 1) * 0.42f, -0.1f, -0.01f);
                GameObject face = Primitive(PrimitiveType.Quad, "Face", cardRoot.transform, Vector3.zero, Vector3.zero, new Vector3(0.34f, 0.4f, 1f), card, false);
                marquee.cards[i] = cardRoot.transform;
                marquee.cardFaces[i] = face.GetComponent<Renderer>();
                marquee.cardIcons[i] = Text("Icon", cardRoot.transform, new Vector3(0f, 0.04f, -0.005f), Vector3.zero, "J", 2.2f, new Vector2(0.3f, 0.26f));
                marquee.cardLabels[i] = Text("Label", cardRoot.transform, new Vector3(0f, -0.14f, -0.005f), Vector3.zero, "JAB", 0.45f, new Vector2(0.32f, 0.08f));
            }

            GameObject boardGo = new GameObject("Scoreboard");
            boardGo.transform.SetParent(root.transform, false);
            boardGo.transform.localPosition = new Vector3(-1.05f, 1.55f, 0.75f);
            boardGo.transform.localRotation = Quaternion.Euler(0f, -54f, 0f);
            Primitive(PrimitiveType.Cube, "Backboard", boardGo.transform, new Vector3(0f, 0f, 0.03f), Vector3.zero, new Vector3(1.0f, 0.55f, 0.03f), board, false);
            TMP_Text scoreboard = Text("Text", boardGo.transform, Vector3.zero, Vector3.zero, "COMBO CUTTER", 0.55f, new Vector2(0.92f, 0.5f));

            manager.zone = zone;
            manager.restartPad = pad;
            manager.coach = coach;
            manager.marquee = marquee;
            manager.cues = cues;
            manager.scoreboard = scoreboard;
            manager.matCenter = mat.transform;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        if (placeInScene) PlaceInScene();
    }

    private static ComboStationTarget PunchableTarget(GameObject go, ComboStationManager manager)
    {
        PunchTarget punch = UdonSharpUndo.AddComponent<PunchTarget>(go);
        ComboStationTarget target = UdonSharpUndo.AddComponent<ComboStationTarget>(go);
        target.manager = manager;
        punch.hitEventReceiver = target;
        punch.hitEventName = "OnTargetHit";
        return target;
    }

    private static void WireCues(ComboCuePlayer cues, AudioSource audio)
    {
        AudioClip hit = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/hit.wav");
        AudioClip startGong = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/試合開始のゴング.mp3");
        AudioClip endGong = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Sounds/試合終了のゴング.mp3");
        cues.source = audio;
        cues.clips = new AudioClip[ComboCuePlayer.CUE_COUNT];
        cues.pitches = new float[ComboCuePlayer.CUE_COUNT];
        cues.volumes = new float[ComboCuePlayer.CUE_COUNT];
        SetCue(cues, ComboCuePlayer.CUE_CALIBRATE_START, hit, 1.6f, 0.4f);
        SetCue(cues, ComboCuePlayer.CUE_CALIBRATE_RETRY, hit, 0.7f, 0.5f);
        SetCue(cues, ComboCuePlayer.CUE_CALIBRATE_DONE, startGong, 1.4f, 0.6f);
        SetCue(cues, ComboCuePlayer.CUE_ANNOUNCE, hit, 2.0f, 0.35f);
        SetCue(cues, ComboCuePlayer.CUE_STEP_LIT, hit, 2.6f, 0.15f);
        SetCue(cues, ComboCuePlayer.CUE_PERFECT, hit, 1.25f, 1f);
        SetCue(cues, ComboCuePlayer.CUE_GOOD, hit, 0.95f, 0.8f);
        SetCue(cues, ComboCuePlayer.CUE_MISS, endGong, 0.6f, 0.7f);
        SetCue(cues, ComboCuePlayer.CUE_COMBO_LANDED, startGong, 1.2f, 0.8f);
        SetCue(cues, ComboCuePlayer.CUE_GUARD_READY, hit, 3f, 0.12f);
        SetCue(cues, ComboCuePlayer.CUE_RUN_COMPLETE, endGong, 1f, 1f);
        SetCue(cues, ComboCuePlayer.CUE_RESTART, startGong, 1f, 0.8f);
        SetCue(cues, ComboCuePlayer.CUE_VR_ONLY, hit, 0.5f, 0.5f);
    }

    private static void SetCue(ComboCuePlayer cues, int id, AudioClip clip, float pitch, float volume)
    {
        cues.clips[id] = clip;
        cues.pitches[id] = pitch;
        cues.volumes[id] = volume;
    }

    private static void PlaceInScene()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (GameObject go in scene.GetRootGameObjects())
            if (go.name == "ComboStation") return;
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.transform.position = ScenePosition;
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat, bool triggerBox)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        // holograms and props must never block the player; only punch targets keep a (trigger) collider
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        if (triggerBox)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (type == PrimitiveType.Cylinder) box.size = new Vector3(1f, 2f, 1f);
        }
        Renderer r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return go;
    }

    private static TMP_Text Text(string name, Transform parent, Vector3 pos, Vector3 euler, string text, float size, Vector2 rect)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = Color.white;
        tmp.rectTransform.sizeDelta = rect;
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(euler);
        return tmp;
    }

    private static Material FadeMaterial(string name, Color color, float emission)
    {
        Material m = LoadOrCreate(name);
        m.SetFloat("_Mode", 2f);
        m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        m.SetInt("_ZWrite", 0);
        m.DisableKeyword("_ALPHATEST_ON");
        m.EnableKeyword("_ALPHABLEND_ON");
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.renderQueue = (int)RenderQueue.Transparent;
        m.color = color;
        SetEmission(m, new Color(color.r, color.g, color.b) * emission);
        return m;
    }

    private static Material OpaqueMaterial(string name, Color color, Color emission)
    {
        Material m = LoadOrCreate(name);
        m.color = color;
        SetEmission(m, emission);
        return m;
    }

    private static void SetEmission(Material m, Color emission)
    {
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        m.SetColor("_EmissionColor", emission);
        EditorUtility.SetDirty(m);
    }

    private static Material LoadOrCreate(string name)
    {
        string path = $"{MaterialDir}/Combo{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Standard"));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
#endif
